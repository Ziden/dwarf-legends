using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.WorldGen.Generation;

namespace DwarfFortress.WorldGen.History;

public static class GeneratedWorldHistoryProjector
{
    public static GeneratedWorldHistory FromSnapshot(
        HistoryYearSnapshot snapshot,
        int seed,
        GeneratedWorldHistory? baseline = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var baselineCivilizations = baseline?.Civilizations.ToDictionary(civilization => civilization.Id, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, CivilizationRecord>(StringComparer.OrdinalIgnoreCase);
        var baselineHouseholds = baseline?.Households.ToDictionary(household => household.Id, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, HouseholdRecord>(StringComparer.OrdinalIgnoreCase);
        var baselineFigures = baseline?.Figures.ToDictionary(figure => figure.Id, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, HistoricalFigureRecord>(StringComparer.OrdinalIgnoreCase);

        var territoryByTile = new Dictionary<WorldCoord, string>(snapshot.TerritoryByTile);
        var territoriesByCivilization = territoryByTile
            .GroupBy(entry => entry.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.Key).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var sites = snapshot.Sites
            .Select(site => new SiteRecord
            {
                Id = site.SiteId,
                Name = site.Name,
                Kind = site.Kind,
                OwnerCivilizationId = site.OwnerCivilizationId,
                Location = site.Location,
                Development = site.Development,
                Security = site.Security,
            })
            .ToArray();
        var sitesById = sites.ToDictionary(site => site.Id, StringComparer.OrdinalIgnoreCase);

        var figures = snapshot.Figures
            .Select(figure => ProjectFigure(figure, snapshot.Year, baselineFigures, baselineHouseholds, sitesById))
            .ToArray();
        var memberFigureIdsByHousehold = figures
            .Where(figure => !string.IsNullOrWhiteSpace(figure.HouseholdId))
            .GroupBy(figure => figure.HouseholdId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(figure => figure.Id).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var households = snapshot.Households
            .Select(household => ProjectHousehold(household, baselineHouseholds, memberFigureIdsByHousehold))
            .ToArray();

        var civilizations = snapshot.Civilizations
            .Select(civilization => ProjectCivilization(civilization, baselineCivilizations, territoriesByCivilization, sitesById))
            .ToArray();

        return new GeneratedWorldHistory
        {
            Seed = seed,
            SimulatedYears = snapshot.Year,
            Civilizations = civilizations,
            Sites = sites,
            SitePopulations = snapshot.SitePopulations.ToArray(),
            Households = households,
            Figures = figures,
            Roads = snapshot.Roads.ToArray(),
            Events = snapshot.Events.ToArray(),
            TerritoryByTile = territoryByTile,
        };
    }

    private static CivilizationRecord ProjectCivilization(
        CivilizationYearRecord civilization,
        IReadOnlyDictionary<string, CivilizationRecord> baselineCivilizations,
        IReadOnlyDictionary<string, WorldCoord[]> territoriesByCivilization,
        IReadOnlyDictionary<string, SiteRecord> sites)
    {
        baselineCivilizations.TryGetValue(civilization.CivilizationId, out var baselineCivilization);
        territoriesByCivilization.TryGetValue(civilization.CivilizationId, out var territory);
        territory ??= Array.Empty<WorldCoord>();

        var capital = ResolveCapital(civilization.CivilizationId, territory, sites, baselineCivilization?.Capital);

        return new CivilizationRecord
        {
            Id = civilization.CivilizationId,
            Name = civilization.Name,
            IsHostile = baselineCivilization?.IsHostile ?? false,
            PrimaryUnitDefId = baselineCivilization?.PrimaryUnitDefId ?? string.Empty,
            Influence = baselineCivilization?.Influence ?? 0f,
            Militarism = baselineCivilization?.Militarism ?? 0f,
            TradeFocus = baselineCivilization?.TradeFocus ?? 0f,
            Capital = capital,
            Territory = territory,
        };
    }

    private static HouseholdRecord ProjectHousehold(
        HouseholdYearRecord household,
        IReadOnlyDictionary<string, HouseholdRecord> baselineHouseholds,
        IReadOnlyDictionary<string, string[]> memberFigureIdsByHousehold)
    {
        baselineHouseholds.TryGetValue(household.HouseholdId, out var baselineHousehold);
        memberFigureIdsByHousehold.TryGetValue(household.HouseholdId, out var memberFigureIds);
        memberFigureIds ??= baselineHousehold?.MemberFigureIds.ToArray() ?? Array.Empty<string>();

        return new HouseholdRecord
        {
            Id = household.HouseholdId,
            Name = household.Name,
            CivilizationId = ChooseFirstNonEmpty(household.CivilizationId, baselineHousehold?.CivilizationId),
            HomeSiteId = ChooseFirstNonEmpty(household.HomeSiteId, baselineHousehold?.HomeSiteId),
            MemberFigureIds = memberFigureIds,
        };
    }

    private static HistoricalFigureRecord ProjectFigure(
        HistoricalFigureYearRecord figure,
        int snapshotYear,
        IReadOnlyDictionary<string, HistoricalFigureRecord> baselineFigures,
        IReadOnlyDictionary<string, HouseholdRecord> baselineHouseholds,
        IReadOnlyDictionary<string, SiteRecord> sites)
    {
        baselineFigures.TryGetValue(figure.FigureId, out var baselineFigure);
        baselineHouseholds.TryGetValue(figure.HouseholdId, out var baselineHousehold);

        var birthSiteId = ResolveBirthSiteId(figure, baselineFigure, baselineHousehold, sites);
        var currentSiteId = ChooseFirstNonEmpty(figure.CurrentSiteId, baselineFigure?.CurrentSiteId, birthSiteId);
        int? deathYear = figure.IsAlive ? null : baselineFigure?.DeathYear ?? snapshotYear;

        return new HistoricalFigureRecord
        {
            Id = figure.FigureId,
            Name = figure.Name,
            SpeciesDefId = ChooseFirstNonEmpty(figure.SpeciesDefId, baselineFigure?.SpeciesDefId),
            CivilizationId = ChooseFirstNonEmpty(figure.CivilizationId, baselineFigure?.CivilizationId),
            BirthSiteId = birthSiteId,
            CurrentSiteId = currentSiteId,
            HouseholdId = ChooseFirstNonEmpty(figure.HouseholdId, baselineFigure?.HouseholdId),
            BirthYear = baselineFigure?.BirthYear ?? figure.BirthYear,
            DeathYear = deathYear,
            IsAlive = figure.IsAlive,
            IsFounder = figure.IsFounder || baselineFigure?.IsFounder == true,
            ProfessionId = ChooseFirstNonEmpty(figure.ProfessionId, baselineFigure?.ProfessionId, "peasant"),
            LaborIds = baselineFigure?.LaborIds.ToArray() ?? Array.Empty<string>(),
            SkillLevels = baselineFigure is null
                ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(baselineFigure.SkillLevels, StringComparer.OrdinalIgnoreCase),
            AttributeLevels = baselineFigure is null
                ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(baselineFigure.AttributeLevels, StringComparer.OrdinalIgnoreCase),
            LikedFoodId = baselineFigure?.LikedFoodId,
            DislikedFoodId = baselineFigure?.DislikedFoodId,
        };
    }

    private static WorldCoord ResolveCapital(
        string civilizationId,
        IReadOnlyList<WorldCoord> territory,
        IReadOnlyDictionary<string, SiteRecord> sites,
        WorldCoord? baselineCapital)
    {
        if (baselineCapital is { } baselineCapitalValue && territory.Contains(baselineCapitalValue))
            return baselineCapitalValue;

        var ownedSite = sites.Values
            .Where(site => string.Equals(site.OwnerCivilizationId, civilizationId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(site => site.Development)
            .ThenByDescending(site => site.Security)
            .ThenBy(site => site.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (ownedSite is not null)
            return ownedSite.Location;

        return territory.FirstOrDefault();
    }

    private static string ResolveBirthSiteId(
        HistoricalFigureYearRecord figure,
        HistoricalFigureRecord? baselineFigure,
        HouseholdRecord? baselineHousehold,
        IReadOnlyDictionary<string, SiteRecord> sites)
    {
        var birthSiteId = ChooseFirstNonEmpty(baselineFigure?.BirthSiteId, baselineHousehold?.HomeSiteId);
        if (!string.IsNullOrWhiteSpace(birthSiteId))
            return birthSiteId;

        if (!string.IsNullOrWhiteSpace(figure.CurrentSiteId) && sites.ContainsKey(figure.CurrentSiteId))
            return figure.CurrentSiteId;

        return string.Empty;
    }

    private static string ChooseFirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }
}
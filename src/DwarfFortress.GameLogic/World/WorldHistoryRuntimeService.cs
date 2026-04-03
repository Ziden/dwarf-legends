using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.History;

namespace DwarfFortress.GameLogic.World;

public sealed class WorldHistoryRuntimeService : IGameSystem
{
    private const string SaveKey = "world_history_runtime_snapshot";
    private static readonly string[] FallbackFounderNames = ["Urist", "Bomrek", "Domas"];

    private readonly Dictionary<string, RuntimeHistoryCivilizationSnapshot> _civilizationsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuntimeHistorySiteSnapshot> _sitesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuntimeHistoryHouseholdSnapshot> _householdsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuntimeHistoryFigureSnapshot> _figuresById = new(StringComparer.Ordinal);

    private GameContext? _ctx;

    public string SystemId => SystemIds.WorldHistoryRuntimeService;
    public int UpdateOrder => 3;
    public bool IsEnabled { get; set; } = true;

    public WorldHistoryRuntimeSnapshot? Snapshot { get; private set; }
    public WorldHistoryEmbarkSummary? CurrentSummary => Snapshot?.EmbarkSummary;

    public void Initialize(GameContext ctx) => _ctx = ctx;
    public void Tick(float delta) { }

    public void OnSave(SaveWriter writer)
    {
        if (Snapshot is not null)
            writer.Write(SaveKey, Snapshot);
    }

    public void OnLoad(SaveReader reader)
    {
        Snapshot = reader.TryRead<WorldHistoryRuntimeSnapshot>(SaveKey);
        RebuildIndexes();
    }

    public void RefreshFromLatestGeneration()
    {
        var mapGeneration = _ctx!.TryGet<MapGenerationService>();
        if (mapGeneration?.LastGeneratedEmbark is not { } embarkContext)
        {
            Snapshot = null;
            RebuildIndexes();
            return;
        }

        Snapshot = BuildSnapshot(
            embarkContext,
            mapGeneration.LastGeneratedHistory);
        RebuildIndexes();
    }

    public RuntimeHistoryCivilizationSnapshot? GetCivilization(string? civilizationId)
    {
        if (string.IsNullOrWhiteSpace(civilizationId))
            return null;

        return _civilizationsById.TryGetValue(civilizationId, out var civilization) ? civilization : null;
    }

    public RuntimeHistorySiteSnapshot? GetSite(string? siteId)
    {
        if (string.IsNullOrWhiteSpace(siteId))
            return null;

        return _sitesById.TryGetValue(siteId, out var site) ? site : null;
    }

    public RuntimeHistoryHouseholdSnapshot? GetHousehold(string? householdId)
    {
        if (string.IsNullOrWhiteSpace(householdId))
            return null;

        return _householdsById.TryGetValue(householdId, out var household) ? household : null;
    }

    public RuntimeHistoryFigureSnapshot? GetFigure(string? figureId)
    {
        if (string.IsNullOrWhiteSpace(figureId))
            return null;

        return _figuresById.TryGetValue(figureId, out var figure) ? figure : null;
    }

    public IReadOnlyList<RuntimeHistorySiteSnapshot> GetCandidateOriginSites(int maxCount = 8)
    {
        if (Snapshot is null || Snapshot.Sites.Count == 0)
            return Array.Empty<RuntimeHistorySiteSnapshot>();

        var summary = Snapshot.EmbarkSummary;
        var embark = Snapshot.EmbarkContext.WorldCoord;

        return Snapshot.Sites
            .OrderBy(site => string.Equals(site.Id, summary.PrimarySiteId, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(site => string.Equals(site.OwnerCivilizationId, summary.OwnerCivilizationId, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(site => ManhattanDistance(site.WorldX, site.WorldY, embark.X, embark.Y))
            .ThenByDescending(site => site.Population)
            .ThenByDescending(site => site.Development)
            .Take(Math.Max(1, maxCount))
            .ToArray();
    }

    public DwarfProvenanceComponent CreateStartingDwarfProvenance(int dwarfIndex)
    {
        return GetStartingDwarfProfiles(dwarfIndex + 1).ElementAtOrDefault(dwarfIndex)?.Provenance
            ?? CreateFallbackProfile(dwarfIndex).Provenance;
    }

    public IReadOnlyList<RuntimeStartingDwarfProfile> GetStartingDwarfProfiles(int count = 3)
    {
        if (Snapshot is null || count <= 0)
            return Array.Empty<RuntimeStartingDwarfProfile>();

        var siteRanks = BuildPlayableSiteRanks();
        var playableCivilizations = Snapshot.Civilizations
            .Where(civilization => IsPlayableCivilization(civilization.PrimaryUnitDefId))
            .Select(civilization => civilization.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selected = Snapshot.Figures
            .Where(figure => figure.IsAlive)
            .Where(figure => IsPlayableCivilization(figure.SpeciesDefId))
            .Where(figure => playableCivilizations.Contains(figure.CivilizationId))
            .Select(figure => new
            {
                Figure = figure,
                Rank = ResolveFigureSiteRank(figure, siteRanks),
                Household = GetHousehold(figure.HouseholdId),
            })
            .OrderBy(entry => entry.Rank)
            .ThenBy(entry => entry.Figure.IsFounder ? 0 : 1)
            .ThenByDescending(entry => entry.Figure.SkillLevels.Values.DefaultIfEmpty(0).Max())
            .ThenBy(entry => entry.Figure.BirthYear)
            .ThenBy(entry => entry.Figure.Name, StringComparer.Ordinal)
            .Take(count)
            .Select(entry => CreateStartingProfile(entry.Figure, entry.Household))
            .ToList();

        for (var i = selected.Count; i < count; i++)
            selected.Add(CreateFallbackProfile(i));

        return selected;
    }

    private static WorldHistoryRuntimeSnapshot BuildSnapshot(
        GeneratedEmbarkContext embarkContext,
        GeneratedWorldHistory? history)
    {
        var latestSitePopulations = BuildLatestSitePopulationLookup(history);
        var civilizations = history?.Civilizations.Select(civilization => new RuntimeHistoryCivilizationSnapshot
        {
            Id = civilization.Id,
            Name = civilization.Name,
            IsHostile = civilization.IsHostile,
            PrimaryUnitDefId = civilization.PrimaryUnitDefId,
            Influence = civilization.Influence,
            Militarism = civilization.Militarism,
            TradeFocus = civilization.TradeFocus,
        }).ToList() ?? [];

        var sites = history?.Sites.Select(site =>
        {
            latestSitePopulations.TryGetValue(site.Id, out var populationRecord);
            return new RuntimeHistorySiteSnapshot
            {
                Id = site.Id,
                Name = site.Name,
                Kind = site.Kind,
                OwnerCivilizationId = site.OwnerCivilizationId,
                WorldX = site.Location.X,
                WorldY = site.Location.Y,
                Development = populationRecord?.Prosperity ?? site.Development,
                Security = populationRecord?.Security ?? site.Security,
                Population = populationRecord?.Population ?? 0,
                HouseholdCount = populationRecord?.HouseholdCount ?? 0,
                MilitaryCount = populationRecord?.MilitaryCount ?? 0,
                CraftCount = populationRecord?.CraftCount ?? 0,
                AgrarianCount = populationRecord?.AgrarianCount ?? 0,
                MiningCount = populationRecord?.MiningCount ?? 0,
            };
        }).ToList() ?? [];
        var households = history?.Households.Select(household => new RuntimeHistoryHouseholdSnapshot
        {
            Id = household.Id,
            Name = household.Name,
            CivilizationId = household.CivilizationId,
            HomeSiteId = household.HomeSiteId,
            MemberFigureIds = household.MemberFigureIds.ToList(),
        }).ToList() ?? [];
        var figures = history?.Figures.Select(figure => new RuntimeHistoryFigureSnapshot
        {
            Id = figure.Id,
            Name = figure.Name,
            SpeciesDefId = figure.SpeciesDefId,
            CivilizationId = figure.CivilizationId,
            BirthSiteId = figure.BirthSiteId,
            CurrentSiteId = figure.CurrentSiteId,
            HouseholdId = figure.HouseholdId,
            BirthYear = figure.BirthYear,
            IsAlive = figure.IsAlive,
            IsFounder = figure.IsFounder,
            ProfessionId = figure.ProfessionId,
            LaborIds = figure.LaborIds.ToList(),
            SkillLevels = new Dictionary<string, int>(figure.SkillLevels, StringComparer.OrdinalIgnoreCase),
            AttributeLevels = new Dictionary<string, int>(figure.AttributeLevels, StringComparer.OrdinalIgnoreCase),
            LikedFoodId = figure.LikedFoodId,
            DislikedFoodId = figure.DislikedFoodId,
        }).ToList() ?? [];

        var localHistory = embarkContext.LocalHistory;
        var localPrimarySite = localHistory?.PrimarySite;
        string? territoryOwnerCivilizationId = localHistory?.TerritoryOwnerCivilizationId;
        if (string.IsNullOrWhiteSpace(territoryOwnerCivilizationId) && history?.TryGetOwner(embarkContext.WorldCoord, out var territoryOwner) == true)
            territoryOwnerCivilizationId = territoryOwner;

        var primarySite = localPrimarySite is { } localPrimarySiteValue
            ? sites.FirstOrDefault(site => string.Equals(site.Id, localPrimarySiteValue.Id, StringComparison.Ordinal))
            : null;

        var ownerCivilizationId = localHistory?.OwnerCivilizationId ?? territoryOwnerCivilizationId;
        primarySite ??= SelectPrimarySite(sites, embarkContext.WorldCoord, ownerCivilizationId);
        ownerCivilizationId ??= primarySite?.OwnerCivilizationId;

        var ownerCivilization = ownerCivilizationId is null
            ? null
            : civilizations.FirstOrDefault(civilization => string.Equals(civilization.Id, ownerCivilizationId, StringComparison.Ordinal));

        return new WorldHistoryRuntimeSnapshot
        {
            EmbarkContext = embarkContext,
            Civilizations = civilizations,
            Sites = sites,
            Households = households,
            Figures = figures,
            EmbarkSummary = new WorldHistoryEmbarkSummary
            {
                RegionName = primarySite?.Name ?? localPrimarySite?.Name ?? $"Region {embarkContext.WorldCoord.X},{embarkContext.WorldCoord.Y}",
                BiomeId = embarkContext.EffectiveBiomeId,
                SimulatedYears = history?.SimulatedYears ?? 0,
                OwnerCivilizationId = ownerCivilizationId,
                OwnerCivilizationName = ownerCivilization?.Name,
                PrimarySiteId = primarySite?.Id ?? localPrimarySite?.Id,
                PrimarySiteName = primarySite?.Name ?? localPrimarySite?.Name,
                PrimarySiteKind = primarySite?.Kind ?? localPrimarySite?.Kind,
                PrimarySitePopulation = primarySite?.Population ?? 0,
                PrimarySiteHouseholdCount = primarySite?.HouseholdCount ?? 0,
                PrimarySiteMilitaryCount = primarySite?.MilitaryCount ?? 0,
                RecentEvents = BuildRecentEvents(history, ownerCivilizationId, primarySite?.Id),
            },
        };
    }

    private static Dictionary<string, SitePopulationRecord> BuildLatestSitePopulationLookup(GeneratedWorldHistory? history)
    {
        if (history?.SitePopulations.Count > 0)
        {
            return history.SitePopulations
                .GroupBy(record => record.SiteId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(record => record.Year).First(),
                    StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, SitePopulationRecord>(StringComparer.OrdinalIgnoreCase);
    }

    private void RebuildIndexes()
    {
        _civilizationsById.Clear();
        _sitesById.Clear();
        _householdsById.Clear();
        _figuresById.Clear();

        if (Snapshot is null)
            return;

        foreach (var civilization in Snapshot.Civilizations)
            _civilizationsById[civilization.Id] = civilization;

        foreach (var site in Snapshot.Sites)
            _sitesById[site.Id] = site;

        foreach (var household in Snapshot.Households)
            _householdsById[household.Id] = household;

        foreach (var figure in Snapshot.Figures)
            _figuresById[figure.Id] = figure;
    }

    private Dictionary<string, int> BuildPlayableSiteRanks()
    {
        var ranks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var site in GetPlayableOriginSites(maxCount: Math.Max(8, Snapshot?.Sites.Count ?? 0)).Select((site, index) => (site, index)))
            ranks[site.site.Id] = site.index;

        return ranks;
    }

    private IReadOnlyList<RuntimeHistorySiteSnapshot> GetPlayableOriginSites(int maxCount)
    {
        if (Snapshot is null)
            return Array.Empty<RuntimeHistorySiteSnapshot>();

        var embark = Snapshot.EmbarkContext.WorldCoord;
        var ownerIsPlayable = IsPlayableCivilization(GetCivilization(Snapshot.EmbarkSummary.OwnerCivilizationId)?.PrimaryUnitDefId);

        return Snapshot.Sites
            .Where(site => IsPlayableCivilization(GetCivilization(site.OwnerCivilizationId)?.PrimaryUnitDefId))
            .OrderBy(site => ownerIsPlayable && string.Equals(site.OwnerCivilizationId, Snapshot.EmbarkSummary.OwnerCivilizationId, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(site => string.Equals(site.Id, Snapshot.EmbarkSummary.PrimarySiteId, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(site => ManhattanDistance(site.WorldX, site.WorldY, embark.X, embark.Y))
            .ThenByDescending(site => site.Population)
            .ThenByDescending(site => site.Development)
            .Take(Math.Max(1, maxCount))
            .ToArray();
    }

    private bool IsPlayableCivilization(string? speciesDefId)
    {
        if (string.IsNullOrWhiteSpace(speciesDefId))
            return false;

        var creature = _ctx?.TryGet<DataManager>()?.Creatures.GetOrNull(speciesDefId);
        return creature?.IsPlayable ?? false;
    }

    private static int ResolveFigureSiteRank(RuntimeHistoryFigureSnapshot figure, IReadOnlyDictionary<string, int> siteRanks)
    {
        if (!string.IsNullOrWhiteSpace(figure.CurrentSiteId) && siteRanks.TryGetValue(figure.CurrentSiteId, out var currentRank))
            return currentRank;
        if (!string.IsNullOrWhiteSpace(figure.BirthSiteId) && siteRanks.TryGetValue(figure.BirthSiteId, out var birthRank))
            return birthRank;

        return int.MaxValue;
    }

    private RuntimeStartingDwarfProfile CreateStartingProfile(RuntimeHistoryFigureSnapshot figure, RuntimeHistoryHouseholdSnapshot? household)
    {
        var originSite = GetSite(figure.CurrentSiteId) ?? GetSite(figure.BirthSiteId);
        return new RuntimeStartingDwarfProfile
        {
            Name = figure.Name,
            ProfessionId = figure.ProfessionId,
            LaborIds = figure.LaborIds.Count == 0 ? [LaborIds.Hauling, LaborIds.Misc] : figure.LaborIds.ToArray(),
            SkillLevels = new Dictionary<string, int>(figure.SkillLevels, StringComparer.OrdinalIgnoreCase),
            AttributeLevels = new Dictionary<string, int>(figure.AttributeLevels, StringComparer.OrdinalIgnoreCase),
            LikedFoodId = figure.LikedFoodId,
            DislikedFoodId = figure.DislikedFoodId,
            Provenance = new DwarfProvenanceComponent
            {
                WorldSeed = Snapshot!.EmbarkContext.Seed,
                FigureId = figure.Id,
                HouseholdId = household?.Id ?? figure.HouseholdId,
                CivilizationId = figure.CivilizationId,
                OriginSiteId = originSite?.Id ?? figure.CurrentSiteId,
                BirthSiteId = figure.BirthSiteId,
                MigrationWaveId = BuildMigrationWaveId(Snapshot.EmbarkContext),
                WorldX = Snapshot.EmbarkContext.WorldCoord.X,
                WorldY = Snapshot.EmbarkContext.WorldCoord.Y,
                RegionX = Snapshot.EmbarkContext.RegionCoord.RegionX,
                RegionY = Snapshot.EmbarkContext.RegionCoord.RegionY,
            },
        };
    }

    private RuntimeStartingDwarfProfile CreateFallbackProfile(int dwarfIndex)
    {
        var name = FallbackFounderNames[dwarfIndex % FallbackFounderNames.Length];
        var laborIds = dwarfIndex switch
        {
            0 => new[] { LaborIds.Mining, LaborIds.Hauling },
            1 => new[] { LaborIds.WoodCutting, LaborIds.Hauling },
            _ => new[] { LaborIds.Crafting, LaborIds.Hauling },
        };

        return new RuntimeStartingDwarfProfile
        {
            Name = name,
            ProfessionId = ProfessionIds.Peasant,
            LaborIds = laborIds,
            SkillLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [laborIds[0]] = 2,
            },
            Provenance = Snapshot is null
                ? new DwarfProvenanceComponent()
                : new DwarfProvenanceComponent
                {
                    WorldSeed = Snapshot.EmbarkContext.Seed,
                    CivilizationId = Snapshot.EmbarkSummary.OwnerCivilizationId,
                    OriginSiteId = Snapshot.EmbarkSummary.PrimarySiteId,
                    BirthSiteId = Snapshot.EmbarkSummary.PrimarySiteId,
                    MigrationWaveId = BuildMigrationWaveId(Snapshot.EmbarkContext),
                    WorldX = Snapshot.EmbarkContext.WorldCoord.X,
                    WorldY = Snapshot.EmbarkContext.WorldCoord.Y,
                    RegionX = Snapshot.EmbarkContext.RegionCoord.RegionX,
                    RegionY = Snapshot.EmbarkContext.RegionCoord.RegionY,
                },
        };
    }

    private static RuntimeHistorySiteSnapshot? SelectPrimarySite(
        IReadOnlyList<RuntimeHistorySiteSnapshot> sites,
        WorldCoord embarkCoord,
        string? ownerCivilizationId)
    {
        return sites
            .OrderBy(site => site.WorldX == embarkCoord.X && site.WorldY == embarkCoord.Y ? 0 : 1)
            .ThenBy(site => string.Equals(site.OwnerCivilizationId, ownerCivilizationId, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(site => ManhattanDistance(site.WorldX, site.WorldY, embarkCoord.X, embarkCoord.Y))
            .ThenByDescending(site => site.Population)
            .ThenByDescending(site => site.Development)
            .FirstOrDefault();
    }

    private static string[] BuildRecentEvents(
        GeneratedWorldHistory? history,
        string? ownerCivilizationId,
        string? primarySiteId)
    {
        if (history is null || history.Events.Count == 0)
            return Array.Empty<string>();

        var relevantEvents = history.Events
            .Where(evt =>
                string.Equals(evt.PrimaryCivilizationId, ownerCivilizationId, StringComparison.Ordinal) ||
                string.Equals(evt.SecondaryCivilizationId, ownerCivilizationId, StringComparison.Ordinal) ||
                string.Equals(evt.SiteId, primarySiteId, StringComparison.Ordinal))
            .OrderByDescending(evt => evt.Year)
            .Take(8)
            .Select(evt => $"Y{evt.Year}: {evt.Summary}")
            .ToList();

        if (relevantEvents.Count >= 8)
            return relevantEvents.ToArray();

        foreach (var evt in history.Events.OrderByDescending(evt => evt.Year))
        {
            var text = $"Y{evt.Year}: {evt.Summary}";
            if (relevantEvents.Contains(text, StringComparer.Ordinal))
                continue;

            relevantEvents.Add(text);
            if (relevantEvents.Count >= 8)
                break;
        }

        return relevantEvents.ToArray();
    }

    private static int ManhattanDistance(int ax, int ay, int bx, int by)
        => Math.Abs(ax - bx) + Math.Abs(ay - by);

    private static string BuildMigrationWaveId(GeneratedEmbarkContext embarkContext)
        => $"embark-{embarkContext.Seed}-{embarkContext.WorldCoord.X}-{embarkContext.WorldCoord.Y}";
}

using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Story;
using DwarfFortress.WorldGen.World;

namespace DwarfFortress.WorldGen.History;

/// <summary>
/// Deterministic world-history simulation tied to generated terrain.
/// V2 adds year-by-year stepping so history can be inspected as it unfolds.
/// </summary>
public sealed class HistorySimulator : IHistorySimulator
{
    private const int MaxCivilizations = 6;
    private const int MinCivilizations = 2;
    private const int MaxRoadsPerCivilization = 6;

    private static readonly (int Dx, int Dy)[] CardinalOffsets =
    [
        (0, -1),
        (1, 0),
        (0, 1),
        (-1, 0),
    ];

    public GeneratedWorldHistory Simulate(
        GeneratedWorldMap world,
        int seed,
        WorldLoreConfig? config = null,
        int? simulatedYearsOverride = null)
    {
        return SimulateTimeline(world, seed, config, simulatedYearsOverride).FinalHistory;
    }

    public GeneratedWorldHistoryTimeline SimulateTimeline(
        GeneratedWorldMap world,
        int seed,
        WorldLoreConfig? config = null,
        int? simulatedYearsOverride = null)
    {
        return CreateSession(world, seed, config, simulatedYearsOverride).Complete();
    }

    public HistorySimulationSession CreateSession(
        GeneratedWorldMap world,
        int seed,
        WorldLoreConfig? config = null,
        int? simulatedYearsOverride = null)
    {
        if (world is null)
            throw new ArgumentNullException(nameof(world));

        var cfg = WorldLoreConfig.WithDefaults(config);
        return new HistorySimulationSession(world, seed, cfg, simulatedYearsOverride);
    }

    /// <summary>
    /// Incremental history generation session that advances one simulated year at a time.
    /// Use this to visualize world history as it is generated.
    /// </summary>
    public sealed class HistorySimulationSession
    {
        private readonly GeneratedWorldMap _world;
        private readonly int _seed;
        private readonly WorldLoreConfig _config;
        private readonly Random _rng;
        private readonly List<WorldCoord> _landTiles;
        private readonly List<CivilizationDraft> _civilizations;
        private readonly Dictionary<string, CivilizationDraft> _civById;
        private readonly Dictionary<WorldCoord, string> _territoryByTile;
        private readonly List<SiteDraft> _siteDrafts;
        private readonly Dictionary<string, CivilizationDynamicState> _dynamicState;
        private readonly List<HistoryYearSnapshot> _yearlySnapshots;
        private readonly List<HistoricalEventRecord> _allEvents;
        private int _nextSiteOrdinal;
        private GeneratedWorldHistory? _finalHistory;

        internal HistorySimulationSession(
            GeneratedWorldMap world,
            int seed,
            WorldLoreConfig config,
            int? simulatedYearsOverride)
        {
            _world = world;
            _seed = seed;
            _config = config;
            _rng = new Random(SeedHash.Hash(seed, world.Seed, world.Width, world.Height));

            _landTiles = CollectLandTiles(world);
            _civilizations = _landTiles.Count == 0
                ? []
                : BuildCivilizationDrafts(world, _landTiles, _rng, _config);
            _civById = _civilizations.ToDictionary(civ => civ.Id, StringComparer.OrdinalIgnoreCase);

            _territoryByTile = _civilizations.Count == 0
                ? new Dictionary<WorldCoord, string>()
                : AssignTerritories(world, _landTiles, _civilizations);
            RebuildTerritoryLists(_civilizations, _territoryByTile);

            _siteDrafts = _civilizations.Count == 0
                ? []
                : BuildSiteDrafts(world, _civilizations, _config, _rng);
            _nextSiteOrdinal = Math.Max(1, _siteDrafts.Count + 1);

            _dynamicState = _civilizations.ToDictionary(
                civ => civ.Id,
                civ => new CivilizationDynamicState
                {
                    Prosperity = Clamp01(0.36f + (civ.TradeFocus * 0.45f) + ((float)_rng.NextDouble() * 0.12f)),
                    Threat = Clamp01(0.24f + (civ.Militarism * 0.46f) + (civ.IsHostile ? 0.12f : 0f) + ((float)_rng.NextDouble() * 0.10f)),
                },
                StringComparer.OrdinalIgnoreCase);

            if (_civilizations.Count == 0)
            {
                TargetYears = 0;
            }
            else
            {
                var years = simulatedYearsOverride ?? NextInclusive(
                    _rng,
                    _config.History!.SimulatedYearsMin,
                    _config.History.SimulatedYearsMax);
                TargetYears = Math.Max(0, years);
            }

            _yearlySnapshots = new List<HistoryYearSnapshot>(TargetYears);
            _allEvents = new List<HistoricalEventRecord>(Math.Max(8, TargetYears));

            if (TargetYears == 0)
                _finalHistory = BuildGeneratedHistory(_world, _seed, 0, _civilizations, _siteDrafts, _territoryByTile, _allEvents);
        }

        public int CurrentYear { get; private set; }
        public int TargetYears { get; }
        public bool IsCompleted => CurrentYear >= TargetYears;
        public IReadOnlyList<HistoryYearSnapshot> Years => _yearlySnapshots;
        public GeneratedWorldHistory? FinalHistory => _finalHistory;

        public bool TryAdvance(out HistoryYearSnapshot? snapshot)
        {
            snapshot = null;
            if (IsCompleted)
                return false;

            var year = CurrentYear + 1;
            var yearEvents = SimulateYearEvents(
                year,
                _civilizations,
                _dynamicState,
                _siteDrafts,
                _config,
                _rng,
                ref _nextSiteOrdinal,
                _world);
            _allEvents.AddRange(yearEvents);

            AdvanceTerritories(
                _world,
                _landTiles,
                _territoryByTile,
                _civilizations,
                _civById,
                _dynamicState,
                _rng);
            RebuildTerritoryLists(_civilizations, _territoryByTile);

            AdvanceSites(_world, _siteDrafts, _civById, _dynamicState, _rng);
            snapshot = BuildYearSnapshot(
                year,
                yearEvents,
                _civilizations,
                _dynamicState,
                _siteDrafts,
                _territoryByTile,
                _world);
            _yearlySnapshots.Add(snapshot);
            CurrentYear = year;

            if (IsCompleted)
            {
                _finalHistory = BuildGeneratedHistory(
                    _world,
                    _seed,
                    CurrentYear,
                    _civilizations,
                    _siteDrafts,
                    _territoryByTile,
                    _allEvents);
            }

            return true;
        }

        public GeneratedWorldHistoryTimeline Complete()
        {
            while (TryAdvance(out _))
            {
                // Step until complete.
            }

            return new GeneratedWorldHistoryTimeline
            {
                FinalHistory = _finalHistory ?? BuildGeneratedHistory(
                    _world,
                    _seed,
                    CurrentYear,
                    _civilizations,
                    _siteDrafts,
                    _territoryByTile,
                    _allEvents),
                Years = _yearlySnapshots.ToArray(),
            };
        }
    }

    private static List<WorldCoord> CollectLandTiles(GeneratedWorldMap world)
    {
        var land = new List<WorldCoord>(world.Width * world.Height);
        for (var y = 0; y < world.Height; y++)
        for (var x = 0; x < world.Width; x++)
        {
            var tile = world.GetTile(x, y);
            if (MacroBiomeIds.IsOcean(tile.MacroBiomeId))
                continue;

            land.Add(new WorldCoord(x, y));
        }

        return land;
    }

    private static List<CivilizationDraft> BuildCivilizationDrafts(
        GeneratedWorldMap world,
        List<WorldCoord> landTiles,
        Random rng,
        WorldLoreConfig cfg)
    {
        var candidates = ScoreCapitalCandidates(world, landTiles, rng);
        if (candidates.Count == 0)
            return [];

        var targetCount = Math.Clamp(
            2 + ((world.Width * world.Height) / 1800),
            MinCivilizations,
            MaxCivilizations);

        var templates = ResolveFactionTemplates(cfg, rng);
        var drafts = new List<CivilizationDraft>(targetCount);
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedCapitals = new HashSet<WorldCoord>();
        var spacing = Math.Max(5, Math.Min(world.Width, world.Height) / 4);

        foreach (var template in templates)
        {
            if (drafts.Count >= targetCount)
                break;

            if (!TryPickCapital(candidates, usedCapitals, spacing, out var capital))
                continue;

            var id = MakeUniqueCivilizationId(template.Id, usedIds, drafts.Count);
            var name = ResolveName(rng, template.NamePattern, cfg);
            drafts.Add(new CivilizationDraft
            {
                Id = id,
                Name = name,
                IsHostile = template.IsHostile,
                PrimaryUnitDefId = ResolvePrimaryUnit(template, rng),
                Influence = RandomInRange(rng, template.InfluenceMin, template.InfluenceMax),
                Militarism = RandomInRange(rng, template.MilitarismMin, template.MilitarismMax),
                TradeFocus = RandomInRange(rng, template.TradeFocusMin, template.TradeFocusMax),
                Capital = capital,
            });
            usedCapitals.Add(capital);
        }

        while (drafts.Count < Math.Min(targetCount, candidates.Count) && drafts.Count < MaxCivilizations)
        {
            if (!TryPickCapital(candidates, usedCapitals, spacing, out var capital))
                break;

            var id = MakeUniqueCivilizationId($"civ_{drafts.Count + 1:00}", usedIds, drafts.Count);
            var hostile = rng.NextDouble() < 0.35;
            drafts.Add(new CivilizationDraft
            {
                Id = id,
                Name = $"{Pick(rng, cfg.NameLeft)} {Pick(rng, cfg.NameRight)} Dominion",
                IsHostile = hostile,
                PrimaryUnitDefId = hostile ? "goblin" : "dwarf",
                Influence = (float)(0.30 + (rng.NextDouble() * 0.45)),
                Militarism = (float)(0.20 + (rng.NextDouble() * 0.60)),
                TradeFocus = (float)(0.10 + (rng.NextDouble() * 0.70)),
                Capital = capital,
            });
            usedCapitals.Add(capital);
        }

        return drafts;
    }

    private static List<FactionTemplateConfig> ResolveFactionTemplates(WorldLoreConfig cfg, Random rng)
    {
        var templates = new List<FactionTemplateConfig>(cfg.FactionTemplates.Count);
        foreach (var template in cfg.FactionTemplates)
        {
            var spawnChance = Clamp01(template.SpawnChance);
            if (spawnChance < 1f && rng.NextDouble() > spawnChance)
                continue;

            templates.Add(template);
        }

        if (templates.Count == 0 && cfg.FactionTemplates.Count > 0)
            templates.Add(cfg.FactionTemplates[0]);

        return templates
            .Select(template => (Template: template, Key: rng.Next()))
            .OrderBy(entry => entry.Key)
            .Select(entry => entry.Template)
            .ToList();
    }

    private static List<CapitalCandidate> ScoreCapitalCandidates(
        GeneratedWorldMap world,
        List<WorldCoord> landTiles,
        Random rng)
    {
        var candidates = new List<CapitalCandidate>(landTiles.Count);
        foreach (var coord in landTiles)
        {
            var tile = world.GetTile(coord.X, coord.Y);
            var elevationBand = 1f - MathF.Min(1f, MathF.Abs(tile.ElevationBand - 0.56f) * 2f);
            var score =
                (tile.MoistureBand * 0.24f) +
                (tile.DrainageBand * 0.20f) +
                (tile.ForestCover * 0.14f) +
                (elevationBand * 0.20f) +
                ((1f - tile.Relief) * 0.10f) +
                ((1f - tile.MountainCover) * 0.06f) +
                (tile.HasRiver ? 0.14f : 0f) +
                (((float)rng.NextDouble() - 0.5f) * 0.06f);

            candidates.Add(new CapitalCandidate(coord, score));
        }

        candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
        return candidates;
    }

    private static bool TryPickCapital(
        List<CapitalCandidate> candidates,
        HashSet<WorldCoord> usedCapitals,
        int minSpacing,
        out WorldCoord chosen)
    {
        foreach (var candidate in candidates)
        {
            if (usedCapitals.Contains(candidate.Coord))
                continue;

            var tooClose = false;
            foreach (var existing in usedCapitals)
            {
                if (Manhattan(existing, candidate.Coord) < minSpacing)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
                continue;

            chosen = candidate.Coord;
            return true;
        }

        foreach (var candidate in candidates)
        {
            if (usedCapitals.Contains(candidate.Coord))
                continue;

            chosen = candidate.Coord;
            return true;
        }

        chosen = default;
        return false;
    }

    private static string MakeUniqueCivilizationId(string preferred, HashSet<string> used, int suffixSeed)
    {
        var baseId = string.IsNullOrWhiteSpace(preferred)
            ? $"civ_{suffixSeed + 1:00}"
            : preferred.Trim();

        if (used.Add(baseId))
            return baseId;

        for (var i = 2; i < 64; i++)
        {
            var candidate = $"{baseId}_{i:00}";
            if (used.Add(candidate))
                return candidate;
        }

        var fallback = $"civ_{suffixSeed + 1:00}_{Guid.NewGuid():N}";
        used.Add(fallback);
        return fallback;
    }

    private static string ResolvePrimaryUnit(FactionTemplateConfig template, Random rng)
    {
        if (!string.IsNullOrWhiteSpace(template.AlternatePrimaryUnitDefId) &&
            rng.NextDouble() < Clamp01(template.AlternatePrimaryChance))
        {
            return template.AlternatePrimaryUnitDefId!;
        }

        return string.IsNullOrWhiteSpace(template.PrimaryUnitDefId)
            ? "goblin"
            : template.PrimaryUnitDefId;
    }
    private static Dictionary<WorldCoord, string> AssignTerritories(
        GeneratedWorldMap world,
        List<WorldCoord> landTiles,
        List<CivilizationDraft> civilizations)
    {
        var ownership = new Dictionary<WorldCoord, string>(landTiles.Count);

        foreach (var coord in landTiles)
        {
            var tile = world.GetTile(coord.X, coord.Y);
            if (tile.ElevationBand >= 0.96f && tile.Relief >= 0.82f && !tile.HasRiver)
                continue;

            CivilizationDraft? bestOwner = null;
            var bestScore = float.MaxValue;
            foreach (var civilization in civilizations)
            {
                var score = TerritoryCost(civilization, coord, tile);
                if (score >= bestScore)
                    continue;

                bestScore = score;
                bestOwner = civilization;
            }

            if (bestOwner is null)
                continue;

            ownership[coord] = bestOwner.Id;
        }

        foreach (var civilization in civilizations)
        {
            if (ownership.TryGetValue(civilization.Capital, out var ownerId) &&
                string.Equals(ownerId, civilization.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ownership[civilization.Capital] = civilization.Id;
        }

        RebuildTerritoryLists(civilizations, ownership);
        return ownership;
    }

    private static void RebuildTerritoryLists(
        List<CivilizationDraft> civilizations,
        IReadOnlyDictionary<WorldCoord, string> ownership)
    {
        foreach (var civilization in civilizations)
            civilization.Territory.Clear();

        var civById = civilizations.ToDictionary(civ => civ.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var (coord, ownerId) in ownership)
        {
            if (!civById.TryGetValue(ownerId, out var civilization))
                continue;

            civilization.Territory.Add(coord);
        }
    }

    private static float TerritoryCost(CivilizationDraft civilization, WorldCoord coord, GeneratedWorldTile tile)
    {
        var distance = Manhattan(civilization.Capital, coord);
        var baseCost = distance * 1.05f;
        baseCost += tile.Relief * 3.0f;
        baseCost += tile.MountainCover * 2.2f;
        baseCost += MathF.Max(0f, tile.ElevationBand - 0.78f) * 7.5f;
        baseCost -= tile.HasRiver ? (0.75f + (civilization.TradeFocus * 0.55f)) : 0f;
        baseCost -= civilization.IsHostile ? civilization.Militarism * 0.20f : civilization.TradeFocus * 0.12f;
        return baseCost;
    }

    private static List<SiteDraft> BuildSiteDrafts(
        GeneratedWorldMap world,
        List<CivilizationDraft> civilizations,
        WorldLoreConfig cfg,
        Random rng)
    {
        var sites = new List<SiteDraft>(civilizations.Count * 4);
        var occupied = new HashSet<WorldCoord>();
        var nextSiteId = 1;

        foreach (var civilization in civilizations)
        {
            var capitalTile = world.GetTile(civilization.Capital.X, civilization.Capital.Y);
            var capitalSite = new SiteDraft
            {
                Id = $"site_{nextSiteId++:0000}",
                Name = $"{Pick(rng, cfg.NameLeft)} {Pick(rng, cfg.NameRight)} Hold",
                Kind = ResolveCapitalSiteKind(cfg),
                OwnerCivilizationId = civilization.Id,
                Location = civilization.Capital,
                Development = Clamp01(0.58f + (civilization.TradeFocus * 0.24f) + (capitalTile.HasRiver ? 0.08f : 0f)),
                Security = Clamp01(0.55f + (civilization.Militarism * 0.30f) + (capitalTile.MountainCover * 0.10f)),
            };
            sites.Add(capitalSite);
            occupied.Add(capitalSite.Location);

            var additionalSites = Math.Clamp(
                1 + (civilization.Territory.Count / 220) + (civilization.Influence >= 0.62f ? 1 : 0),
                1,
                7);

            var candidates = civilization.Territory
                .Where(coord => coord != civilization.Capital)
                .OrderByDescending(coord => ScoreSiteCandidate(world, coord, civilization, rng))
                .ToList();

            for (var i = 0; i < candidates.Count && additionalSites > 0; i++)
            {
                var location = candidates[i];
                if (occupied.Any(existing => Manhattan(existing, location) < 4))
                    continue;

                var tile = world.GetTile(location.X, location.Y);
                var kind = ResolveSiteKind(cfg, tile, civilization, rng);
                var development = Clamp01(
                    0.35f +
                    (civilization.TradeFocus * 0.34f) +
                    (tile.MoistureBand * 0.14f) +
                    (tile.HasRiver ? 0.12f : 0f) -
                    (tile.Relief * 0.10f) +
                    (((float)rng.NextDouble() - 0.5f) * 0.08f));
                var security = Clamp01(
                    0.30f +
                    (civilization.Militarism * 0.34f) +
                    (tile.MountainCover * 0.12f) +
                    (tile.Relief * 0.08f) +
                    (civilization.IsHostile ? 0.07f : 0f) +
                    (((float)rng.NextDouble() - 0.5f) * 0.08f));

                sites.Add(new SiteDraft
                {
                    Id = $"site_{nextSiteId++:0000}",
                    Name = $"{Pick(rng, cfg.NameLeft)} {kind}",
                    Kind = kind,
                    OwnerCivilizationId = civilization.Id,
                    Location = location,
                    Development = development,
                    Security = security,
                });
                occupied.Add(location);
                additionalSites--;
            }
        }

        return sites;
    }

    private static string ResolveCapitalSiteKind(WorldLoreConfig cfg)
    {
        if (cfg.SiteKinds.Any(site => string.Equals(site.Id, "fortress", StringComparison.OrdinalIgnoreCase)))
            return "fortress";
        if (cfg.SiteKinds.Count > 0)
            return cfg.SiteKinds[0].Id;
        return "capital";
    }

    private static float ScoreSiteCandidate(
        GeneratedWorldMap world,
        WorldCoord coord,
        CivilizationDraft civilization,
        Random rng)
    {
        var tile = world.GetTile(coord.X, coord.Y);
        var distancePenalty = Manhattan(civilization.Capital, coord) * 0.02f;
        return
            (tile.DrainageBand * 0.26f) +
            (tile.MoistureBand * 0.18f) +
            ((1f - tile.Relief) * 0.12f) +
            (tile.HasRiver ? 0.35f : 0f) +
            (tile.ForestCover * 0.10f) -
            distancePenalty +
            (((float)rng.NextDouble() - 0.5f) * 0.08f);
    }

    private static string ResolveSiteKind(
        WorldLoreConfig cfg,
        GeneratedWorldTile tile,
        CivilizationDraft civilization,
        Random rng)
    {
        if (cfg.SiteKinds.Count == 0)
            return "settlement";

        var kindIds = cfg.SiteKinds.Select(site => site.Id).ToArray();
        if (tile.MountainCover >= 0.65f || tile.Relief >= 0.70f)
        {
            var rocky = kindIds.FirstOrDefault(id =>
                id.Contains("watch", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("cave", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("ruin", StringComparison.OrdinalIgnoreCase));
            if (rocky is not null)
                return rocky;
        }

        if (tile.HasRiver || tile.DrainageBand >= 0.66f)
        {
            var riparian = kindIds.FirstOrDefault(id =>
                id.Contains("fortress", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("hamlet", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("shrine", StringComparison.OrdinalIgnoreCase));
            if (riparian is not null)
                return riparian;
        }

        if (civilization.IsHostile)
        {
            var hostile = kindIds.FirstOrDefault(id =>
                id.Contains("cave", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("watch", StringComparison.OrdinalIgnoreCase));
            if (hostile is not null)
                return hostile;
        }

        return kindIds[rng.Next(kindIds.Length)];
    }
    private static List<HistoricalEventRecord> SimulateYearEvents(
        int year,
        List<CivilizationDraft> civilizations,
        Dictionary<string, CivilizationDynamicState> dynamicState,
        List<SiteDraft> sites,
        WorldLoreConfig cfg,
        Random rng,
        ref int nextSiteOrdinal,
        GeneratedWorldMap world)
    {
        var history = cfg.History!;
        var typeWeights = new[]
        {
            ("treaty", Math.Max(0f, history.EventWeightTreaty)),
            ("raid", Math.Max(0f, history.EventWeightRaid)),
            ("founding", Math.Max(0f, history.EventWeightFounding)),
            ("skirmish", Math.Max(0f, history.EventWeightSkirmish)),
            ("crisis", Math.Max(0f, history.EventWeightCrisis)),
        };
        var totalWeight = typeWeights.Sum(entry => entry.Item2);
        if (totalWeight <= 0f)
            totalWeight = 1f;

        var yearEvents = new List<HistoricalEventRecord>();
        var eventsThisYear = NextInclusive(rng, history.EventsPerYearMin, history.EventsPerYearMax);
        for (var i = 0; i < eventsThisYear; i++)
        {
            var type = PickWeightedType(typeWeights, totalWeight, rng);
            var primary = civilizations[rng.Next(civilizations.Count)];
            var secondary = civilizations.Count > 1
                ? civilizations.Where(c => c.Id != primary.Id).OrderBy(_ => rng.Next()).First()
                : null;
            var site = ResolveEventSite(type, primary.Id, sites, rng);
            var createdSiteId = default(string);

            ApplyEventEffects(type, primary, secondary, dynamicState);
            if (type == "founding" &&
                TryCreateFoundingSite(primary, sites, cfg, rng, world, ref nextSiteOrdinal, out var newSite))
            {
                sites.Add(newSite);
                createdSiteId = newSite.Id;
            }

            yearEvents.Add(new HistoricalEventRecord
            {
                Year = year,
                Type = type,
                PrimaryCivilizationId = primary.Id,
                SecondaryCivilizationId = secondary?.Id,
                SiteId = createdSiteId ?? site?.Id,
                Summary = BuildEventSummary(type, primary, secondary, site, createdSiteId),
            });
        }

        foreach (var state in dynamicState.Values)
        {
            state.Prosperity = Clamp01(state.Prosperity + (((float)rng.NextDouble() - 0.5f) * 0.010f));
            state.Threat = Clamp01(state.Threat + (((float)rng.NextDouble() - 0.5f) * 0.010f));
        }

        return yearEvents;
    }

    private static SiteDraft? ResolveEventSite(
        string type,
        string primaryCivilizationId,
        List<SiteDraft> sites,
        Random rng)
    {
        if (sites.Count == 0)
            return null;

        var byOwner = sites.Where(site =>
            string.Equals(site.OwnerCivilizationId, primaryCivilizationId, StringComparison.OrdinalIgnoreCase)).ToList();

        if (type is "founding")
            return byOwner.Count > 0 ? byOwner[rng.Next(byOwner.Count)] : sites[rng.Next(sites.Count)];

        if (type is "raid" or "skirmish")
        {
            var foreign = sites.Where(site =>
                !string.Equals(site.OwnerCivilizationId, primaryCivilizationId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (foreign.Count > 0)
                return foreign[rng.Next(foreign.Count)];
        }

        return byOwner.Count > 0 ? byOwner[rng.Next(byOwner.Count)] : sites[rng.Next(sites.Count)];
    }

    private static void ApplyEventEffects(
        string type,
        CivilizationDraft primary,
        CivilizationDraft? secondary,
        IDictionary<string, CivilizationDynamicState> dynamicState)
    {
        var primaryState = dynamicState[primary.Id];
        switch (type)
        {
            case "treaty":
                primaryState.Prosperity = Clamp01(primaryState.Prosperity + 0.05f);
                primaryState.Threat = Clamp01(primaryState.Threat - 0.03f);
                if (secondary is not null)
                {
                    var secondaryState = dynamicState[secondary.Id];
                    secondaryState.Prosperity = Clamp01(secondaryState.Prosperity + 0.05f);
                    secondaryState.Threat = Clamp01(secondaryState.Threat - 0.03f);
                }
                break;

            case "raid":
                primaryState.Threat = Clamp01(primaryState.Threat + 0.08f);
                primaryState.Prosperity = Clamp01(primaryState.Prosperity + 0.01f);
                if (secondary is not null)
                {
                    var secondaryState = dynamicState[secondary.Id];
                    secondaryState.Threat = Clamp01(secondaryState.Threat + 0.06f);
                    secondaryState.Prosperity = Clamp01(secondaryState.Prosperity - 0.04f);
                }
                break;

            case "founding":
                primaryState.Prosperity = Clamp01(primaryState.Prosperity + 0.03f);
                primaryState.Threat = Clamp01(primaryState.Threat - 0.01f);
                break;

            case "skirmish":
                primaryState.Threat = Clamp01(primaryState.Threat + 0.05f);
                primaryState.Prosperity = Clamp01(primaryState.Prosperity - 0.02f);
                if (secondary is not null)
                {
                    var secondaryState = dynamicState[secondary.Id];
                    secondaryState.Threat = Clamp01(secondaryState.Threat + 0.05f);
                    secondaryState.Prosperity = Clamp01(secondaryState.Prosperity - 0.02f);
                }
                break;

            case "crisis":
                primaryState.Threat = Clamp01(primaryState.Threat + 0.05f);
                primaryState.Prosperity = Clamp01(primaryState.Prosperity - 0.05f);
                break;
        }
    }

    private static bool TryCreateFoundingSite(
        CivilizationDraft civilization,
        List<SiteDraft> sites,
        WorldLoreConfig cfg,
        Random rng,
        GeneratedWorldMap world,
        ref int nextSiteOrdinal,
        out SiteDraft site)
    {
        site = default!;
        if (civilization.Territory.Count == 0)
            return false;

        var occupied = sites.Select(existing => existing.Location).ToList();
        var candidates = civilization.Territory
            .Where(coord => occupied.All(existing => Manhattan(existing, coord) >= 4))
            .OrderByDescending(coord => ScoreSiteCandidate(world, coord, civilization, rng))
            .ToList();
        if (candidates.Count == 0)
            return false;

        var location = candidates[0];
        var tile = world.GetTile(location.X, location.Y);
        var kind = ResolveSiteKind(cfg, tile, civilization, rng);
        site = new SiteDraft
        {
            Id = $"site_{nextSiteOrdinal++:0000}",
            Name = $"{Pick(rng, cfg.NameLeft)} {kind}",
            Kind = kind,
            OwnerCivilizationId = civilization.Id,
            Location = location,
            Development = Clamp01(0.42f + (civilization.TradeFocus * 0.22f)),
            Security = Clamp01(0.38f + (civilization.Militarism * 0.18f)),
        };
        return true;
    }

    private static string PickWeightedType((string Type, float Weight)[] weights, float totalWeight, Random rng)
    {
        var roll = (float)rng.NextDouble() * totalWeight;
        for (var i = 0; i < weights.Length; i++)
        {
            var (type, weight) = weights[i];
            if (weight <= 0f)
                continue;

            if (roll < weight)
                return type;
            roll -= weight;
        }

        return "treaty";
    }

    private static void AdvanceTerritories(
        GeneratedWorldMap world,
        IReadOnlyList<WorldCoord> landTiles,
        Dictionary<WorldCoord, string> territoryByTile,
        IReadOnlyList<CivilizationDraft> civilizations,
        IReadOnlyDictionary<string, CivilizationDraft> civById,
        IReadOnlyDictionary<string, CivilizationDynamicState> dynamicState,
        Random rng)
    {
        var claims = new List<TerritoryClaim>(landTiles.Count / 6);
        foreach (var coord in landTiles)
        {
            var tile = world.GetTile(coord.X, coord.Y);
            territoryByTile.TryGetValue(coord, out var currentOwnerId);
            var bestOwnerId = currentOwnerId;
            var bestPressure = currentOwnerId is null
                ? 0.52f
                : ComputeTerritoryPressure(civById[currentOwnerId], dynamicState[currentOwnerId], coord, tile, rng);

            foreach (var (dx, dy) in CardinalOffsets)
            {
                var nx = coord.X + dx;
                var ny = coord.Y + dy;
                var neighbor = new WorldCoord(nx, ny);
                if (!territoryByTile.TryGetValue(neighbor, out var candidateOwnerId))
                    continue;
                if (candidateOwnerId == currentOwnerId)
                    continue;
                if (!civById.TryGetValue(candidateOwnerId, out var candidateCiv))
                    continue;

                var pressure = ComputeTerritoryPressure(candidateCiv, dynamicState[candidateOwnerId], coord, tile, rng);
                var captureMargin = currentOwnerId is null
                    ? 0.08f
                    : (candidateCiv.IsHostile ? 0.12f : 0.16f);

                if (pressure <= bestPressure + captureMargin)
                    continue;

                bestPressure = pressure;
                bestOwnerId = candidateOwnerId;
            }

            if (bestOwnerId is null || bestOwnerId == currentOwnerId)
                continue;

            claims.Add(new TerritoryClaim(coord, bestOwnerId, bestPressure));
        }

        if (claims.Count == 0)
            return;

        claims.Sort((left, right) => right.Pressure.CompareTo(left.Pressure));
        var yearlyLimit = Math.Clamp(landTiles.Count / 12, 6, 120);
        var applied = 0;
        foreach (var claim in claims)
        {
            territoryByTile[claim.Coord] = claim.OwnerCivilizationId;
            applied++;
            if (applied >= yearlyLimit)
                break;
        }

        foreach (var civilization in civilizations)
        {
            if (territoryByTile.TryGetValue(civilization.Capital, out var ownerId) &&
                string.Equals(ownerId, civilization.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            territoryByTile[civilization.Capital] = civilization.Id;
        }
    }

    private static float ComputeTerritoryPressure(
        CivilizationDraft civilization,
        CivilizationDynamicState state,
        WorldCoord coord,
        GeneratedWorldTile tile,
        Random rng)
    {
        var distancePenalty = Manhattan(civilization.Capital, coord) * 0.015f;
        var pressure =
            (civilization.Influence * 0.38f) +
            (civilization.Militarism * 0.24f) +
            (state.Prosperity * 0.18f) +
            (state.Threat * (civilization.IsHostile ? 0.16f : 0.06f)) +
            (tile.HasRiver ? 0.11f : 0f) +
            ((1f - tile.Relief) * 0.08f) +
            ((1f - tile.MountainCover) * 0.06f) -
            distancePenalty +
            (((float)rng.NextDouble() - 0.5f) * 0.05f);
        return pressure;
    }
    private static void AdvanceSites(
        GeneratedWorldMap world,
        IList<SiteDraft> sites,
        IReadOnlyDictionary<string, CivilizationDraft> civById,
        IReadOnlyDictionary<string, CivilizationDynamicState> dynamicState,
        Random rng)
    {
        foreach (var site in sites)
        {
            if (!civById.TryGetValue(site.OwnerCivilizationId, out var owner))
                continue;
            if (!dynamicState.TryGetValue(owner.Id, out var state))
                continue;

            var tile = world.GetTile(site.Location.X, site.Location.Y);
            var developmentDelta =
                ((state.Prosperity - 0.5f) * 0.08f) +
                ((owner.TradeFocus - 0.5f) * 0.05f) +
                (tile.HasRiver ? 0.02f : 0f) -
                ((state.Threat - 0.5f) * 0.05f) -
                (tile.Relief * 0.02f) +
                (((float)rng.NextDouble() - 0.5f) * 0.02f);

            var securityDelta =
                ((owner.Militarism - 0.5f) * 0.06f) +
                ((state.Threat - 0.5f) * 0.04f) +
                (tile.MountainCover * 0.02f) +
                (((float)rng.NextDouble() - 0.5f) * 0.02f);

            site.Development = Clamp01(site.Development + developmentDelta);
            site.Security = Clamp01(site.Security + securityDelta);
        }
    }

    private static HistoryYearSnapshot BuildYearSnapshot(
        int year,
        IReadOnlyList<HistoricalEventRecord> yearEvents,
        IReadOnlyList<CivilizationDraft> civilizations,
        IReadOnlyDictionary<string, CivilizationDynamicState> dynamicState,
        IReadOnlyList<SiteDraft> sites,
        IReadOnlyDictionary<WorldCoord, string> territoryByTile,
        GeneratedWorldMap world)
    {
        var civilizationRecords = civilizations
            .Select(civ =>
            {
                var state = dynamicState[civ.Id];
                return new CivilizationYearRecord
                {
                    CivilizationId = civ.Id,
                    Name = civ.Name,
                    TerritoryTiles = civ.Territory.Count,
                    Prosperity = state.Prosperity,
                    Threat = state.Threat,
                };
            })
            .OrderByDescending(record => record.TerritoryTiles)
            .ThenBy(record => record.CivilizationId, StringComparer.Ordinal)
            .ToArray();

        var siteRecords = sites
            .Select(site => new SiteYearRecord
            {
                SiteId = site.Id,
                Name = site.Name,
                Kind = site.Kind,
                OwnerCivilizationId = site.OwnerCivilizationId,
                Location = site.Location,
                Development = site.Development,
                Security = site.Security,
            })
            .ToArray();

        var avgProsperity = civilizationRecords.Length == 0
            ? 0f
            : civilizationRecords.Average(record => record.Prosperity);
        var avgThreat = civilizationRecords.Length == 0
            ? 0f
            : civilizationRecords.Average(record => record.Threat);
        var roads = BuildRoads(world, civilizations, sites, territoryByTile);

        return new HistoryYearSnapshot
        {
            Year = year,
            AverageProsperity = avgProsperity,
            AverageThreat = avgThreat,
            Events = yearEvents.ToArray(),
            Civilizations = civilizationRecords,
            Sites = siteRecords,
            Roads = roads,
            TerritoryByTile = new Dictionary<WorldCoord, string>(territoryByTile),
        };
    }

    private static GeneratedWorldHistory BuildGeneratedHistory(
        GeneratedWorldMap world,
        int seed,
        int simulatedYears,
        IReadOnlyList<CivilizationDraft> civilizations,
        IReadOnlyList<SiteDraft> siteDrafts,
        IReadOnlyDictionary<WorldCoord, string> territoryByTile,
        IReadOnlyList<HistoricalEventRecord> allEvents)
    {
        if (civilizations.Count == 0)
        {
            return new GeneratedWorldHistory
            {
                Seed = seed,
                SimulatedYears = 0,
            };
        }

        var roadRecords = BuildRoads(world, civilizations, siteDrafts, territoryByTile);
        var civilizationRecords = civilizations
            .Select(civ => new CivilizationRecord
            {
                Id = civ.Id,
                Name = civ.Name,
                IsHostile = civ.IsHostile,
                PrimaryUnitDefId = civ.PrimaryUnitDefId,
                Influence = civ.Influence,
                Militarism = civ.Militarism,
                TradeFocus = civ.TradeFocus,
                Capital = civ.Capital,
                Territory = civ.Territory.ToArray(),
            })
            .ToList();
        var siteRecords = siteDrafts
            .Select(ToSiteRecord)
            .ToList();

        return new GeneratedWorldHistory
        {
            Seed = seed,
            SimulatedYears = Math.Max(0, simulatedYears),
            Civilizations = civilizationRecords,
            Sites = siteRecords,
            Roads = roadRecords,
            Events = allEvents.ToArray(),
            TerritoryByTile = new Dictionary<WorldCoord, string>(territoryByTile),
        };
    }

    private static List<RoadRecord> BuildRoads(
        GeneratedWorldMap world,
        IReadOnlyList<CivilizationDraft> civilizations,
        IReadOnlyList<SiteDraft> sites,
        IReadOnlyDictionary<WorldCoord, string> territoryByTile)
    {
        if (!WorldGenFeatureFlags.EnableRoadGeneration)
            return [];

        var roads = new List<RoadRecord>(civilizations.Count * 3);
        var roadId = 1;

        foreach (var civilization in civilizations)
        {
            var civSites = sites
                .Where(site => string.Equals(site.OwnerCivilizationId, civilization.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (civSites.Count <= 1)
                continue;

            var capital = civSites.FirstOrDefault(site => site.Location == civilization.Capital) ?? civSites[0];
            var targets = civSites
                .Where(site => site.Id != capital.Id)
                .OrderBy(site => Manhattan(capital.Location, site.Location))
                .Take(MaxRoadsPerCivilization)
                .ToList();

            foreach (var target in targets)
            {
                var path = FindRoadPath(world, territoryByTile, civilization.Id, capital.Location, target.Location);
                if (path.Count < 2)
                    continue;

                roads.Add(new RoadRecord
                {
                    Id = $"road_{roadId++:0000}",
                    OwnerCivilizationId = civilization.Id,
                    FromSiteId = capital.Id,
                    ToSiteId = target.Id,
                    Path = path,
                });
            }
        }

        return roads;
    }

    private static List<WorldCoord> FindRoadPath(
        GeneratedWorldMap world,
        IReadOnlyDictionary<WorldCoord, string> territoryByTile,
        string civilizationId,
        WorldCoord start,
        WorldCoord goal)
    {
        var cameFrom = new Dictionary<WorldCoord, WorldCoord>();
        var gScore = new Dictionary<WorldCoord, float> { [start] = 0f };
        var visited = new HashSet<WorldCoord>();
        var queue = new PriorityQueue<WorldCoord, float>();
        queue.Enqueue(start, Heuristic(start, goal));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
                continue;

            if (current == goal)
                return ReconstructPath(cameFrom, current);

            foreach (var (dx, dy) in CardinalOffsets)
            {
                var nx = current.X + dx;
                var ny = current.Y + dy;
                if (nx < 0 || ny < 0 || nx >= world.Width || ny >= world.Height)
                    continue;

                var neighbor = new WorldCoord(nx, ny);
                var tile = world.GetTile(nx, ny);
                if (MacroBiomeIds.IsOcean(tile.MacroBiomeId))
                    continue;

                var currentCost = gScore[current];
                var tentative = currentCost + RoadStepCost(tile, territoryByTile, civilizationId, neighbor);
                if (gScore.TryGetValue(neighbor, out var knownCost) && tentative >= knownCost)
                    continue;

                cameFrom[neighbor] = current;
                gScore[neighbor] = tentative;
                var estimated = tentative + Heuristic(neighbor, goal);
                queue.Enqueue(neighbor, estimated);
            }
        }

        return [];
    }

    private static float RoadStepCost(
        GeneratedWorldTile tile,
        IReadOnlyDictionary<WorldCoord, string> territoryByTile,
        string civilizationId,
        WorldCoord coord)
    {
        var cost = 1f;
        cost += tile.Relief * 2.20f;
        cost += tile.MountainCover * 2.10f;
        cost += MathF.Max(0f, tile.ElevationBand - 0.74f) * 3.40f;
        cost -= tile.HasRiver ? 0.35f : 0f;

        if (territoryByTile.TryGetValue(coord, out var ownerId) &&
            !string.Equals(ownerId, civilizationId, StringComparison.OrdinalIgnoreCase))
        {
            cost += 1.20f;
        }

        return Math.Max(0.25f, cost);
    }

    private static float Heuristic(WorldCoord from, WorldCoord to)
        => Manhattan(from, to);

    private static List<WorldCoord> ReconstructPath(
        IReadOnlyDictionary<WorldCoord, WorldCoord> cameFrom,
        WorldCoord current)
    {
        var path = new List<WorldCoord> { current };
        while (cameFrom.TryGetValue(current, out var previous))
        {
            current = previous;
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    private static SiteRecord ToSiteRecord(SiteDraft site)
        => new()
        {
            Id = site.Id,
            Name = site.Name,
            Kind = site.Kind,
            OwnerCivilizationId = site.OwnerCivilizationId,
            Location = site.Location,
            Development = site.Development,
            Security = site.Security,
        };

    private static string BuildEventSummary(
        string type,
        CivilizationDraft primary,
        CivilizationDraft? secondary,
        SiteDraft? site,
        string? createdSiteId)
    {
        return type switch
        {
            "raid" => secondary is null
                ? $"{primary.Name} launched frontier raids."
                : $"{primary.Name} raided border holdings of {secondary.Name}.",
            "founding" => createdSiteId is not null
                ? $"{primary.Name} founded {createdSiteId}."
                : $"{primary.Name} established a new outpost.",
            "skirmish" => secondary is null
                ? $"{primary.Name} reported scattered skirmishes."
                : $"{primary.Name} and {secondary.Name} fought a border skirmish.",
            "crisis" => site is null
                ? $"{primary.Name} weathered a regional crisis."
                : $"{primary.Name} faced unrest near {site.Name}.",
            _ => secondary is null
                ? $"{primary.Name} negotiated a local accord."
                : $"{primary.Name} and {secondary.Name} signed a treaty.",
        };
    }

    private static string ResolveName(Random rng, string pattern, WorldLoreConfig config)
    {
        var effectivePattern = string.IsNullOrWhiteSpace(pattern) ? "{left} Compact" : pattern;
        return effectivePattern
            .Replace("{left}", Pick(rng, config.NameLeft), StringComparison.Ordinal)
            .Replace("{right}", Pick(rng, config.NameRight), StringComparison.Ordinal);
    }

    private static T Pick<T>(Random rng, IReadOnlyList<T> values)
        => values[rng.Next(values.Count)];

    private static float Clamp01(float value)
        => Math.Clamp(value, 0f, 1f);

    private static int Manhattan(WorldCoord a, WorldCoord b)
        => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static int NextInclusive(Random rng, int min, int max)
    {
        if (max < min)
            (min, max) = (max, min);

        return rng.Next(min, max + 1);
    }

    private static float RandomInRange(Random rng, float min, float max)
    {
        if (max < min)
            (min, max) = (max, min);

        return (float)(min + ((max - min) * rng.NextDouble()));
    }

    private sealed class CivilizationDraft
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public bool IsHostile { get; init; }
        public string PrimaryUnitDefId { get; init; } = "goblin";
        public float Influence { get; init; }
        public float Militarism { get; init; }
        public float TradeFocus { get; init; }
        public WorldCoord Capital { get; init; }
        public List<WorldCoord> Territory { get; } = [];
    }

    private sealed class CivilizationDynamicState
    {
        public float Prosperity { get; set; }
        public float Threat { get; set; }
    }

    private sealed class SiteDraft
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Kind { get; init; } = "";
        public string OwnerCivilizationId { get; init; } = "";
        public WorldCoord Location { get; init; }
        public float Development { get; set; }
        public float Security { get; set; }
    }

    private readonly record struct CapitalCandidate(WorldCoord Coord, float Score);
    private readonly record struct TerritoryClaim(WorldCoord Coord, string OwnerCivilizationId, float Pressure);
}

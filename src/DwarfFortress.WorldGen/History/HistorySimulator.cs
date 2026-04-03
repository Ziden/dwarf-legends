using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.WorldGen.Config;
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
    private readonly WorldGenContentCatalog _contentCatalog;

    private static readonly (int Dx, int Dy)[] CardinalOffsets =
    [
        (0, -1),
        (1, 0),
        (0, 1),
        (-1, 0),
    ];

    public HistorySimulator(WorldGenContentCatalog? contentCatalog = null)
    {
        _contentCatalog = contentCatalog ?? WorldGenContentRegistry.Current;
    }

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
        return new HistorySimulationSession(world, seed, cfg, simulatedYearsOverride, _contentCatalog);
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
        private readonly WorldGenContentCatalog _contentCatalog;
        private readonly Random _rng;
        private readonly List<WorldCoord> _landTiles;
        private readonly List<CivilizationDraft> _civilizations;
        private readonly Dictionary<string, CivilizationDraft> _civById;
        private readonly Dictionary<WorldCoord, string> _territoryByTile;
        private readonly List<SiteDraft> _siteDrafts;
        private readonly List<HouseholdDraft> _householdDrafts;
        private readonly List<HistoricalFigureDraft> _figureDrafts;
        private readonly Dictionary<string, SitePopulationState> _sitePopulationStateById;
        private readonly Dictionary<string, CivilizationDynamicState> _dynamicState;
        private readonly List<HistoryYearSnapshot> _yearlySnapshots;
        private readonly List<HistoricalEventRecord> _allEvents;
        private readonly List<SitePopulationRecord> _sitePopulationHistory;
        private int _nextSiteOrdinal;
        private int _nextHouseholdOrdinal;
        private int _nextFigureOrdinal;
        private GeneratedWorldHistory? _finalHistory;

        internal HistorySimulationSession(
            GeneratedWorldMap world,
            int seed,
            WorldLoreConfig config,
            int? simulatedYearsOverride,
            WorldGenContentCatalog contentCatalog)
        {
            _world = world;
            _seed = seed;
            _config = config;
            _contentCatalog = contentCatalog;
            _rng = new Random(SeedHash.Hash(seed, world.Seed, world.Width, world.Height));

            _landTiles = CollectLandTiles(world);
            _civilizations = _landTiles.Count == 0
                ? []
                : BuildCivilizationDrafts(world, _landTiles, _rng, _config, _contentCatalog);
            _civById = _civilizations.ToDictionary(civ => civ.Id, StringComparer.OrdinalIgnoreCase);

            _territoryByTile = _civilizations.Count == 0
                ? new Dictionary<WorldCoord, string>()
                : AssignTerritories(world, _landTiles, _civilizations);
            RebuildTerritoryLists(_civilizations, _territoryByTile);

            _siteDrafts = _civilizations.Count == 0
                ? []
                : BuildSiteDrafts(world, _civilizations, _config, _rng);
            _nextSiteOrdinal = Math.Max(1, _siteDrafts.Count + 1);

            _householdDrafts = [];
            _figureDrafts = [];
            _nextHouseholdOrdinal = 1;
            _nextFigureOrdinal = 1;
            if (_siteDrafts.Count > 0)
                SeedHistoricalPopulation(_contentCatalog, _civilizations, _siteDrafts, _householdDrafts, _figureDrafts, _rng, ref _nextHouseholdOrdinal, ref _nextFigureOrdinal, year: 0);

            _dynamicState = _civilizations.ToDictionary(
                civ => civ.Id,
                civ => new CivilizationDynamicState
                {
                    Prosperity = Clamp01(0.36f + (civ.TradeFocus * 0.45f) + ((float)_rng.NextDouble() * 0.12f)),
                    Threat = Clamp01(0.24f + (civ.Militarism * 0.46f) + (civ.IsHostile ? 0.12f : 0f) + ((float)_rng.NextDouble() * 0.10f)),
                },
                StringComparer.OrdinalIgnoreCase);
            _sitePopulationStateById = new Dictionary<string, SitePopulationState>(StringComparer.OrdinalIgnoreCase);
            EnsureSitePopulationStates(_world, _siteDrafts, _civById, _householdDrafts, _figureDrafts, _sitePopulationStateById);
            _sitePopulationHistory = BuildSitePopulationRecordsForYear(0, _siteDrafts, _sitePopulationStateById).ToList();

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
                _finalHistory = BuildGeneratedHistory(_world, _seed, 0, _civilizations, _siteDrafts, _householdDrafts, _figureDrafts, _territoryByTile, _allEvents, _sitePopulationHistory);
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
                _householdDrafts,
                _figureDrafts,
                _config,
                _contentCatalog,
                _rng,
                ref _nextSiteOrdinal,
                ref _nextHouseholdOrdinal,
                ref _nextFigureOrdinal,
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

            EnsureSitePopulationStates(_world, _siteDrafts, _civById, _householdDrafts, _figureDrafts, _sitePopulationStateById);
            AdvanceSites(_world, _siteDrafts, _civById, _dynamicState, _sitePopulationStateById, _rng);
            AdvanceSitePopulations(year, _world, _siteDrafts, _civById, _dynamicState, _sitePopulationStateById, _sitePopulationHistory, _rng);
            snapshot = BuildYearSnapshot(
                year,
                yearEvents,
                _civilizations,
                _dynamicState,
                _siteDrafts,
                _sitePopulationStateById,
                _householdDrafts,
                _figureDrafts,
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
                    _householdDrafts,
                    _figureDrafts,
                    _territoryByTile,
                        _allEvents,
                        _sitePopulationHistory);
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
                    _householdDrafts,
                    _figureDrafts,
                    _territoryByTile,
                    _allEvents,
                    _sitePopulationHistory),
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
        WorldLoreConfig cfg,
        WorldGenContentCatalog contentCatalog)
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
                PrimaryUnitDefId = contentCatalog.ResolveFactionPrimaryUnit(template, rng),
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
                PrimaryUnitDefId = contentCatalog.ResolveDefaultCivilizationPrimaryUnit(hostile, rng),
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
        List<HouseholdDraft> households,
        List<HistoricalFigureDraft> figures,
        WorldLoreConfig cfg,
        WorldGenContentCatalog contentCatalog,
        Random rng,
        ref int nextSiteOrdinal,
        ref int nextHouseholdOrdinal,
        ref int nextFigureOrdinal,
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
                SeedSitePopulation(contentCatalog, primary, newSite, households, figures, rng, ref nextHouseholdOrdinal, ref nextFigureOrdinal, year, founderBias: true);
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
        IReadOnlyDictionary<string, SitePopulationState> sitePopulationById,
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

            if (sitePopulationById.TryGetValue(site.Id, out var populationState) && populationState.Population > 0)
            {
                var population = populationState.Population;
                var militaryShare = populationState.MilitaryCount / (float)population;
                var craftShare = populationState.CraftCount / (float)population;
                var agrarianShare = populationState.AgrarianCount / (float)population;
                var miningShare = populationState.MiningCount / (float)population;

                developmentDelta +=
                    (agrarianShare * (0.04f + (tile.MoistureBand * 0.02f) + (tile.HasRiver ? 0.02f : 0f))) +
                    (craftShare * (0.03f + (owner.TradeFocus * 0.02f))) +
                    (miningShare * ((tile.MountainCover * 0.02f) + (tile.Relief * 0.01f))) -
                    (militaryShare * 0.01f);

                securityDelta +=
                    (militaryShare * 0.06f) +
                    (populationState.Population >= 20 ? 0.01f : 0f) -
                    (MathF.Max(0f, 0.18f - militaryShare) * 0.03f);
            }

            site.Development = Clamp01(site.Development + developmentDelta);
            site.Security = Clamp01(site.Security + securityDelta);
        }
    }

    private static void EnsureSitePopulationStates(
        GeneratedWorldMap world,
        IReadOnlyList<SiteDraft> sites,
        IReadOnlyDictionary<string, CivilizationDraft> civById,
        IReadOnlyList<HouseholdDraft> households,
        IReadOnlyList<HistoricalFigureDraft> figures,
        Dictionary<string, SitePopulationState> sitePopulationById)
    {
        foreach (var site in sites.OrderBy(site => site.Id, StringComparer.Ordinal))
        {
            if (sitePopulationById.ContainsKey(site.Id))
                continue;
            if (!civById.TryGetValue(site.OwnerCivilizationId, out var owner))
                continue;

            sitePopulationById[site.Id] = BuildInitialSitePopulationState(world, site, owner, households, figures);
        }
    }

    private static SitePopulationState BuildInitialSitePopulationState(
        GeneratedWorldMap world,
        SiteDraft site,
        CivilizationDraft owner,
        IReadOnlyList<HouseholdDraft> households,
        IReadOnlyList<HistoricalFigureDraft> figures)
    {
        var tile = world.GetTile(site.Location.X, site.Location.Y);
        var livingFigures = figures
            .Where(figure => figure.IsAlive && string.Equals(figure.CurrentSiteId, site.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var householdCount = households.Count(household => string.Equals(household.HomeSiteId, site.Id, StringComparison.OrdinalIgnoreCase));

        var militarySeed = 0;
        var craftSeed = 0;
        var agrarianSeed = 0;
        var miningSeed = 0;
        foreach (var figure in livingFigures)
        {
            switch (ResolvePopulationCategory(figure.ProfessionId))
            {
                case PopulationCategory.Military:
                    militarySeed++;
                    break;
                case PopulationCategory.Craft:
                    craftSeed++;
                    break;
                case PopulationCategory.Agrarian:
                    agrarianSeed++;
                    break;
                case PopulationCategory.Mining:
                    miningSeed++;
                    break;
            }
        }

        var population = Math.Max(
            livingFigures.Length,
            6 +
            (householdCount * 3) +
            (int)MathF.Round(site.Development * 12f) +
            (int)MathF.Round(site.Security * 4f) +
            (tile.HasRiver ? 2 : 0) +
            (HasGarrisonSiteKind(site.Kind) ? 3 : 0) +
            (tile.MountainCover >= 0.5f ? 2 : 0));
        householdCount = Math.Max(householdCount, Math.Max(1, (int)MathF.Round(population / 4.2f)));

        var workforce = Math.Max(livingFigures.Length, Math.Max(1, (int)MathF.Round(population * 0.60f)));
        var workforceAllocation = AllocateSiteWorkforce(
            workforce,
            militarySeed + 1f + (owner.Militarism * 2.0f) + (HasGarrisonSiteKind(site.Kind) ? 1.2f : 0f) + (owner.IsHostile ? 0.6f : 0f),
            craftSeed + 1f + (owner.TradeFocus * 2.0f) + (site.Development * 1.2f),
            agrarianSeed + 1f + (tile.MoistureBand * 1.8f) + (tile.HasRiver ? 0.8f : 0f) + (HasAgrarianSiteKind(site.Kind) ? 0.8f : 0f),
            miningSeed + 1f + (tile.MountainCover * 1.8f) + (tile.Relief * 1.0f) + (HasMiningSiteKind(site.Kind) ? 0.9f : 0f));

        return new SitePopulationState
        {
            Population = population,
            HouseholdCount = householdCount,
            MilitaryCount = workforceAllocation.Military,
            CraftCount = workforceAllocation.Craft,
            AgrarianCount = workforceAllocation.Agrarian,
            MiningCount = workforceAllocation.Mining,
            Prosperity = site.Development,
            Security = site.Security,
        };
    }

    private static void AdvanceSitePopulations(
        int year,
        GeneratedWorldMap world,
        IReadOnlyList<SiteDraft> sites,
        IReadOnlyDictionary<string, CivilizationDraft> civById,
        IReadOnlyDictionary<string, CivilizationDynamicState> dynamicState,
        Dictionary<string, SitePopulationState> sitePopulationById,
        IList<SitePopulationRecord> sitePopulationHistory,
        Random rng)
    {
        foreach (var site in sites.OrderBy(site => site.Id, StringComparer.Ordinal))
        {
            if (!civById.TryGetValue(site.OwnerCivilizationId, out var owner))
                continue;
            if (!dynamicState.TryGetValue(owner.Id, out var civilizationState))
                continue;
            if (!sitePopulationById.TryGetValue(site.Id, out var populationState))
            {
                populationState = BuildInitialSitePopulationState(world, site, owner, Array.Empty<HouseholdDraft>(), Array.Empty<HistoricalFigureDraft>());
                sitePopulationById[site.Id] = populationState;
            }

            var tile = world.GetTile(site.Location.X, site.Location.Y);
            var population = Math.Max(1, populationState.Population);
            var militaryShare = populationState.MilitaryCount / (float)population;
            var craftShare = populationState.CraftCount / (float)population;
            var agrarianShare = populationState.AgrarianCount / (float)population;
            var miningShare = populationState.MiningCount / (float)population;
            var crowding = populationState.HouseholdCount <= 0
                ? 1.1f
                : population / (float)(populationState.HouseholdCount * 4);

            var growthScore =
                ((site.Development - 0.5f) * 1.1f) +
                ((site.Security - 0.5f) * 0.7f) +
                ((civilizationState.Prosperity - 0.5f) * 0.9f) -
                ((civilizationState.Threat - 0.5f) * 0.8f) +
                (agrarianShare * (0.7f + tile.MoistureBand + (tile.HasRiver ? 0.4f : 0f))) +
                (craftShare * (0.5f + (owner.TradeFocus * 0.7f))) +
                (miningShare * (0.2f + (tile.MountainCover * 0.9f) + (tile.Relief * 0.5f))) +
                (militaryShare * (site.Security < 0.5f ? 0.8f : 0.2f)) -
                (MathF.Max(0f, crowding - 1.05f) * 0.75f) +
                (((float)rng.NextDouble() - 0.5f) * 0.5f);
            var deltaScale = 1f + MathF.Min(3f, populationState.Population / 18f);
            var delta = (int)MathF.Round(growthScore * deltaScale);

            populationState.Population = Math.Max(MinimumPopulationForSite(site.Kind), populationState.Population + delta);
            populationState.HouseholdCount = Math.Clamp(
                Math.Max(1, (int)MathF.Round(populationState.Population / (3.8f + (site.Development * 0.6f)))),
                1,
                populationState.Population);

            var workforce = Math.Clamp(
                Math.Max(1, (int)MathF.Round(populationState.Population * (0.48f + (site.Development * 0.20f)))),
                1,
                populationState.Population);
            var workforceAllocation = AllocateSiteWorkforce(
                workforce,
                0.6f + (owner.Militarism * 2.0f) + (civilizationState.Threat * 1.2f) + (HasGarrisonSiteKind(site.Kind) ? 1.1f : 0f) + (owner.IsHostile ? 0.6f : 0f) + (militaryShare * 1.4f),
                0.8f + (owner.TradeFocus * 2.1f) + (site.Development * 1.5f) + (craftShare * 1.1f),
                0.8f + (tile.MoistureBand * 1.9f) + (tile.HasRiver ? 0.8f : 0f) + (HasAgrarianSiteKind(site.Kind) ? 0.7f : 0f) + (agrarianShare * 1.2f),
                0.5f + (tile.MountainCover * 2.1f) + (tile.Relief * 1.2f) + (HasMiningSiteKind(site.Kind) ? 0.9f : 0f) + (miningShare * 1.0f));
            populationState.MilitaryCount = workforceAllocation.Military;
            populationState.CraftCount = workforceAllocation.Craft;
            populationState.AgrarianCount = workforceAllocation.Agrarian;
            populationState.MiningCount = workforceAllocation.Mining;
            populationState.Prosperity = site.Development;
            populationState.Security = site.Security;
        }

        foreach (var record in BuildSitePopulationRecordsForYear(year, sites, sitePopulationById))
            sitePopulationHistory.Add(record);
    }

    private static void SeedHistoricalPopulation(
        WorldGenContentCatalog contentCatalog,
        IReadOnlyList<CivilizationDraft> civilizations,
        IReadOnlyList<SiteDraft> sites,
        IList<HouseholdDraft> households,
        IList<HistoricalFigureDraft> figures,
        Random rng,
        ref int nextHouseholdOrdinal,
        ref int nextFigureOrdinal,
        int year)
    {
        foreach (var civilization in civilizations)
        {
            foreach (var site in sites.Where(site => string.Equals(site.OwnerCivilizationId, civilization.Id, StringComparison.OrdinalIgnoreCase)))
                SeedSitePopulation(contentCatalog, civilization, site, households, figures, rng, ref nextHouseholdOrdinal, ref nextFigureOrdinal, year, founderBias: site.Location == civilization.Capital);
        }
    }

    private static void SeedSitePopulation(
        WorldGenContentCatalog contentCatalog,
        CivilizationDraft civilization,
        SiteDraft site,
        IList<HouseholdDraft> households,
        IList<HistoricalFigureDraft> figures,
        Random rng,
        ref int nextHouseholdOrdinal,
        ref int nextFigureOrdinal,
        int year,
        bool founderBias)
    {
        var householdCount = Math.Clamp(1 + (int)MathF.Round(site.Development * 3.5f) + (founderBias ? 1 : 0), 1, 5);
        for (var householdIndex = 0; householdIndex < householdCount; householdIndex++)
        {
            var householdId = $"household_{nextHouseholdOrdinal++:0000}";
            var memberIds = new List<string>();
            var memberCount = Math.Clamp(2 + (int)MathF.Round(site.Development * 2.5f) + (householdIndex == 0 && founderBias ? 1 : 0), 2, 5);

            for (var memberIndex = 0; memberIndex < memberCount; memberIndex++)
            {
                var profession = contentCatalog.ResolveHistoryFigureProfession(
                    civilization.PrimaryUnitDefId,
                    site.Kind,
                    memberIndex,
                    founderBias,
                    rng);
                var figureId = $"figure_{nextFigureOrdinal++:0000}";
                memberIds.Add(figureId);
                figures.Add(new HistoricalFigureDraft
                {
                    Id = figureId,
                    Name = contentCatalog.ResolveHistoryFigureName(civilization.PrimaryUnitDefId, rng),
                    SpeciesDefId = civilization.PrimaryUnitDefId,
                    CivilizationId = civilization.Id,
                    BirthSiteId = site.Id,
                    CurrentSiteId = site.Id,
                    HouseholdId = householdId,
                    BirthYear = year - NextInclusive(rng, 18, founderBias ? 90 : 70),
                    IsAlive = true,
                    IsFounder = founderBias && householdIndex == 0,
                    ProfessionId = profession.ProfessionId,
                    LaborIds = profession.LaborIds.ToArray(),
                    SkillLevels = new Dictionary<string, int>(profession.SkillLevels, StringComparer.OrdinalIgnoreCase),
                    AttributeLevels = new Dictionary<string, int>(profession.AttributeLevels, StringComparer.OrdinalIgnoreCase),
                    LikedFoodId = profession.LikedFoodId,
                    DislikedFoodId = profession.DislikedFoodId,
                });
            }

            households.Add(new HouseholdDraft
            {
                Id = householdId,
                Name = $"House of {figures[^memberIds.Count].Name}",
                CivilizationId = civilization.Id,
                HomeSiteId = site.Id,
                MemberFigureIds = memberIds,
            });
        }
    }

    private static HistoryYearSnapshot BuildYearSnapshot(
        int year,
        IReadOnlyList<HistoricalEventRecord> yearEvents,
        IReadOnlyList<CivilizationDraft> civilizations,
        IReadOnlyDictionary<string, CivilizationDynamicState> dynamicState,
        IReadOnlyList<SiteDraft> sites,
        IReadOnlyDictionary<string, SitePopulationState> sitePopulationById,
        IReadOnlyList<HouseholdDraft> households,
        IReadOnlyList<HistoricalFigureDraft> figures,
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
        var sitePopulationRecords = BuildSitePopulationRecordsForYear(year, sites, sitePopulationById);
        var householdRecords = households
            .Select(household => new HouseholdYearRecord
            {
                HouseholdId = household.Id,
                Name = household.Name,
                CivilizationId = household.CivilizationId,
                HomeSiteId = household.HomeSiteId,
                MemberCount = household.MemberFigureIds.Count,
            })
            .ToArray();
        var figureRecords = figures
            .Select(figure => new HistoricalFigureYearRecord
            {
                FigureId = figure.Id,
                Name = figure.Name,
                SpeciesDefId = figure.SpeciesDefId,
                CivilizationId = figure.CivilizationId,
                CurrentSiteId = figure.CurrentSiteId,
                HouseholdId = figure.HouseholdId,
                BirthYear = figure.BirthYear,
                IsAlive = figure.IsAlive,
                IsFounder = figure.IsFounder,
                ProfessionId = figure.ProfessionId,
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
            SitePopulations = sitePopulationRecords,
            Households = householdRecords,
            Figures = figureRecords,
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
        IReadOnlyList<HouseholdDraft> householdDrafts,
        IReadOnlyList<HistoricalFigureDraft> figureDrafts,
        IReadOnlyDictionary<WorldCoord, string> territoryByTile,
        IReadOnlyList<HistoricalEventRecord> allEvents,
        IReadOnlyList<SitePopulationRecord> sitePopulationHistory)
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
        var householdRecords = householdDrafts
            .Select(household => new HouseholdRecord
            {
                Id = household.Id,
                Name = household.Name,
                CivilizationId = household.CivilizationId,
                HomeSiteId = household.HomeSiteId,
                MemberFigureIds = household.MemberFigureIds.ToArray(),
            })
            .ToList();
        var figureRecords = figureDrafts
            .Select(figure => new HistoricalFigureRecord
            {
                Id = figure.Id,
                Name = figure.Name,
                SpeciesDefId = figure.SpeciesDefId,
                CivilizationId = figure.CivilizationId,
                BirthSiteId = figure.BirthSiteId,
                CurrentSiteId = figure.CurrentSiteId,
                HouseholdId = figure.HouseholdId,
                BirthYear = figure.BirthYear,
                DeathYear = figure.DeathYear,
                IsAlive = figure.IsAlive,
                IsFounder = figure.IsFounder,
                ProfessionId = figure.ProfessionId,
                LaborIds = figure.LaborIds.ToArray(),
                SkillLevels = new Dictionary<string, int>(figure.SkillLevels, StringComparer.OrdinalIgnoreCase),
                AttributeLevels = new Dictionary<string, int>(figure.AttributeLevels, StringComparer.OrdinalIgnoreCase),
                LikedFoodId = figure.LikedFoodId,
                DislikedFoodId = figure.DislikedFoodId,
            })
            .ToList();

        return new GeneratedWorldHistory
        {
            Seed = seed,
            SimulatedYears = Math.Max(0, simulatedYears),
            Civilizations = civilizationRecords,
            Sites = siteRecords,
            SitePopulations = sitePopulationHistory.ToArray(),
            Households = householdRecords,
            Figures = figureRecords,
            Roads = roadRecords,
            Events = allEvents.ToArray(),
            TerritoryByTile = new Dictionary<WorldCoord, string>(territoryByTile),
        };
    }

    private static SitePopulationRecord[] BuildSitePopulationRecordsForYear(
        int year,
        IReadOnlyList<SiteDraft> sites,
        IReadOnlyDictionary<string, SitePopulationState> sitePopulationById)
    {
        return sites
            .OrderBy(site => site.Id, StringComparer.Ordinal)
            .Where(site => sitePopulationById.ContainsKey(site.Id))
            .Select(site => ToSitePopulationRecord(site.Id, year, sitePopulationById[site.Id]))
            .ToArray();
    }

    private static SitePopulationRecord ToSitePopulationRecord(string siteId, int year, SitePopulationState state)
        => new()
        {
            SiteId = siteId,
            Year = year,
            Population = state.Population,
            HouseholdCount = state.HouseholdCount,
            MilitaryCount = state.MilitaryCount,
            CraftCount = state.CraftCount,
            AgrarianCount = state.AgrarianCount,
            MiningCount = state.MiningCount,
            Prosperity = state.Prosperity,
            Security = state.Security,
        };

    private static SiteWorkforceAllocation AllocateSiteWorkforce(
        int workforce,
        float militaryWeight,
        float craftWeight,
        float agrarianWeight,
        float miningWeight)
    {
        if (workforce <= 0)
            return new SiteWorkforceAllocation(0, 0, 0, 0);

        var weights = new[]
        {
            Math.Max(0.01f, militaryWeight),
            Math.Max(0.01f, craftWeight),
            Math.Max(0.01f, agrarianWeight),
            Math.Max(0.01f, miningWeight),
        };
        var counts = new int[weights.Length];
        var fractions = new float[weights.Length];
        var totalWeight = weights.Sum();
        var assigned = 0;

        for (var i = 0; i < weights.Length; i++)
        {
            var rawCount = workforce * (weights[i] / totalWeight);
            counts[i] = (int)MathF.Floor(rawCount);
            fractions[i] = rawCount - counts[i];
            assigned += counts[i];
        }

        for (var remaining = workforce - assigned; remaining > 0; remaining--)
        {
            var bestIndex = 0;
            for (var i = 1; i < fractions.Length; i++)
            {
                if (fractions[i] <= fractions[bestIndex])
                    continue;

                bestIndex = i;
            }

            counts[bestIndex]++;
            fractions[bestIndex] = -1f;
        }

        return new SiteWorkforceAllocation(
            counts[0],
            counts[1],
            counts[2],
            counts[3]);
    }

    private static PopulationCategory ResolvePopulationCategory(string professionId)
    {
        return professionId switch
        {
            "militia" => PopulationCategory.Military,
            "crafter" or "mason" or "woodworker" or "brewer" or "cook" => PopulationCategory.Craft,
            "farmer" => PopulationCategory.Agrarian,
            "miner" => PopulationCategory.Mining,
            _ => PopulationCategory.None,
        };
    }

    private static bool HasGarrisonSiteKind(string siteKind)
        => siteKind.Contains("watch", StringComparison.OrdinalIgnoreCase) ||
           siteKind.Contains("fortress", StringComparison.OrdinalIgnoreCase) ||
           siteKind.Contains("cave", StringComparison.OrdinalIgnoreCase);

    private static bool HasAgrarianSiteKind(string siteKind)
        => siteKind.Contains("hamlet", StringComparison.OrdinalIgnoreCase) ||
           siteKind.Contains("village", StringComparison.OrdinalIgnoreCase) ||
           siteKind.Contains("shrine", StringComparison.OrdinalIgnoreCase);

    private static bool HasMiningSiteKind(string siteKind)
        => siteKind.Contains("cave", StringComparison.OrdinalIgnoreCase) ||
           siteKind.Contains("mine", StringComparison.OrdinalIgnoreCase) ||
           siteKind.Contains("ruin", StringComparison.OrdinalIgnoreCase);

    private static int MinimumPopulationForSite(string siteKind)
        => HasGarrisonSiteKind(siteKind) ? 4 : 6;

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
        public string PrimaryUnitDefId { get; init; } = "";
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

    private sealed class SitePopulationState
    {
        public int Population { get; set; }
        public int HouseholdCount { get; set; }
        public int MilitaryCount { get; set; }
        public int CraftCount { get; set; }
        public int AgrarianCount { get; set; }
        public int MiningCount { get; set; }
        public float Prosperity { get; set; }
        public float Security { get; set; }
    }

    private sealed class HouseholdDraft
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string CivilizationId { get; init; } = "";
        public string HomeSiteId { get; set; } = "";
        public List<string> MemberFigureIds { get; init; } = [];
    }

    private sealed class HistoricalFigureDraft
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string SpeciesDefId { get; init; } = "";
        public string CivilizationId { get; init; } = "";
        public string BirthSiteId { get; init; } = "";
        public string CurrentSiteId { get; set; } = "";
        public string HouseholdId { get; init; } = "";
        public int BirthYear { get; init; }
        public int? DeathYear { get; set; }
        public bool IsAlive { get; set; } = true;
        public bool IsFounder { get; init; }
        public string ProfessionId { get; init; } = "peasant";
        public string[] LaborIds { get; init; } = [];
        public Dictionary<string, int> SkillLevels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> AttributeLevels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public string? LikedFoodId { get; init; }
        public string? DislikedFoodId { get; init; }
    }

    private enum PopulationCategory
    {
        None,
        Military,
        Craft,
        Agrarian,
        Mining,
    }

    private readonly record struct CapitalCandidate(WorldCoord Coord, float Score);
    private readonly record struct SiteWorkforceAllocation(int Military, int Craft, int Agrarian, int Mining);
    private readonly record struct TerritoryClaim(WorldCoord Coord, string OwnerCivilizationId, float Pressure);
}

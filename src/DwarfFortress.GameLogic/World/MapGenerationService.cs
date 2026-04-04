using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.History;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Local;
using DwarfFortress.WorldGen.Maps;
using DwarfFortress.WorldGen.Regions;
using DwarfFortress.WorldGen.World;

namespace DwarfFortress.GameLogic.World;

public readonly record struct MapGenerationSettings(
    int WorldWidth,
    int WorldHeight,
    int RegionWidth,
    int RegionHeight,
    bool EnableHistory = true,
    int SimulatedHistoryYears = 125)
{
    public static MapGenerationSettings Default => new(
        WorldWidth: 64,
        WorldHeight: 64,
        RegionWidth: 32,
        RegionHeight: 32,
        EnableHistory: true,
        SimulatedHistoryYears: 125);
}

public readonly record struct GeneratedEmbarkContext(
    int Seed,
    WorldCoord WorldCoord,
    RegionCoord RegionCoord,
    string MacroBiomeId,
    string RegionBiomeVariantId,
    string EffectiveBiomeId,
    LocalHistoryContext? LocalHistory = null);

public interface IMapGenerationService
{
    GeneratedWorldMap GetOrCreateWorld(int seed, MapGenerationSettings settings);
    GeneratedWorldHistory? GetOrCreateHistory(int seed, MapGenerationSettings settings);
    GeneratedRegionMap GetOrCreateRegion(int seed, WorldCoord worldCoord, MapGenerationSettings settings);
    GeneratedEmbarkMap GetOrCreateLocal(int seed, RegionCoord regionCoord, LocalGenerationSettings settings, MapGenerationSettings generationSettings);
    RegionCoord ResolveDefaultRegionCoord(int seed, MapGenerationSettings settings);
    GeneratedEmbarkContext GenerateAndApplyEmbark(
        WorldMap targetMap,
        int seed,
        LocalGenerationSettings settings,
        string? biomeOverrideId = null,
        MapGenerationSettings? generationSettings = null);
}

/// <summary>
/// Caches and orchestrates hierarchical world -> region -> local generation.
/// </summary>
public sealed class MapGenerationService : IGameSystem, IMapGenerationService
{
    private const string LastContextSaveKey = "map_generation_last_context";
    private const int HistorySeedSalt = 74119;
    private const int MaxWorldEmbarkCandidates = 12;
    private const int MaxRegionCandidatesPerWorld = 4;
    private const int MaxLocalEmbarkEvaluations = 18;
    private const int MaxNearbyWaterDistance = 12;
    private const int MinNearbyFoodSources = 2;
    private const int MinReachableSurfaceTiles = 48;

    private static readonly (int X, int Y)[] CardinalDirections =
    [
        (0, -1),
        (1, 0),
        (0, 1),
        (-1, 0),
    ];

    private readonly IWorldLayerGenerator _worldGenerator;
    private readonly IHistorySimulator _historySimulator;
    private readonly IRegionLayerGenerator _regionGenerator;
    private readonly ILocalLayerGenerator _localGenerator;

    private readonly Dictionary<WorldCacheKey, GeneratedWorldMap> _worldCache = new();
    private readonly Dictionary<HistoryCacheKey, GeneratedWorldHistory> _historyCache = new();
    private readonly Dictionary<RegionCacheKey, GeneratedRegionMap> _regionCache = new();
    private readonly Dictionary<LocalCacheKey, GeneratedEmbarkMap> _localCache = new();

    public string SystemId => SystemIds.MapGenerationService;
    public int UpdateOrder => 2;
    public bool IsEnabled { get; set; } = true;

    public GeneratedEmbarkContext? LastGeneratedEmbark { get; private set; }
    public GeneratedEmbarkMap? LastGeneratedLocalMap { get; private set; }
    public GeneratedWorldHistory? LastGeneratedHistory { get; private set; }

    public MapGenerationService(
        IWorldLayerGenerator? worldGenerator = null,
        IHistorySimulator? historySimulator = null,
        IRegionLayerGenerator? regionGenerator = null,
        ILocalLayerGenerator? localGenerator = null)
    {
        _worldGenerator = worldGenerator ?? new WorldLayerGenerator();
        _historySimulator = historySimulator ?? new HistorySimulator();
        _regionGenerator = regionGenerator ?? new RegionLayerGenerator();
        _localGenerator = localGenerator ?? new LocalLayerGenerator();
    }

    public void Initialize(GameContext ctx) { }
    public void Tick(float delta) { }

    public void OnSave(SaveWriter writer)
    {
        if (LastGeneratedEmbark is { } context)
            writer.Write(LastContextSaveKey, context);
    }

    public void OnLoad(SaveReader reader)
    {
        ClearCaches();
        LastGeneratedEmbark = reader.TryRead<GeneratedEmbarkContext>(LastContextSaveKey);
        LastGeneratedLocalMap = null;
        LastGeneratedHistory = null;
    }

    public GeneratedWorldMap GetOrCreateWorld(int seed, MapGenerationSettings settings)
    {
        ValidateSettings(settings);
        var key = new WorldCacheKey(seed, settings.WorldWidth, settings.WorldHeight);

        if (_worldCache.TryGetValue(key, out var cached))
            return cached;

        var generated = _worldGenerator.Generate(seed, settings.WorldWidth, settings.WorldHeight);
        _worldCache[key] = generated;
        return generated;
    }

    public GeneratedWorldHistory? GetOrCreateHistory(int seed, MapGenerationSettings settings)
    {
        ValidateSettings(settings);
        if (!settings.EnableHistory || settings.SimulatedHistoryYears <= 0)
            return null;

        var key = new HistoryCacheKey(
            seed,
            settings.WorldWidth,
            settings.WorldHeight,
            settings.SimulatedHistoryYears);
        if (_historyCache.TryGetValue(key, out var cached))
            return cached;

        var world = GetOrCreateWorld(seed, settings);
        var historySeed = MixSeed(seed, settings.WorldWidth, settings.WorldHeight, HistorySeedSalt);
        var generated = _historySimulator.Simulate(
            world,
            historySeed,
            simulatedYearsOverride: settings.SimulatedHistoryYears);

        _historyCache[key] = generated;
        return generated;
    }

    public GeneratedRegionMap GetOrCreateRegion(int seed, WorldCoord worldCoord, MapGenerationSettings settings)
    {
        ValidateSettings(settings);
        var world = GetOrCreateWorld(seed, settings);
        if (worldCoord.X < 0 || worldCoord.X >= world.Width || worldCoord.Y < 0 || worldCoord.Y >= world.Height)
            throw new ArgumentOutOfRangeException(nameof(worldCoord), "World coordinate is outside world bounds.");

        var key = new RegionCacheKey(
            seed,
            settings.WorldWidth,
            settings.WorldHeight,
            worldCoord.X,
            worldCoord.Y,
            settings.RegionWidth,
            settings.RegionHeight,
            settings.EnableHistory,
            settings.EnableHistory ? settings.SimulatedHistoryYears : 0);

        if (_regionCache.TryGetValue(key, out var cached))
            return cached;

        var history = GetOrCreateHistory(seed, settings);
        var generated = _regionGenerator.Generate(world, worldCoord, settings.RegionWidth, settings.RegionHeight, history);
        _regionCache[key] = generated;
        return generated;
    }

    public GeneratedEmbarkMap GetOrCreateLocal(
        int seed,
        RegionCoord regionCoord,
        LocalGenerationSettings settings,
        MapGenerationSettings generationSettings)
    {
        ValidateSettings(generationSettings);
        if (settings.Width <= 0) throw new ArgumentOutOfRangeException(nameof(settings.Width));
        if (settings.Height <= 0) throw new ArgumentOutOfRangeException(nameof(settings.Height));
        if (settings.Depth <= 0) throw new ArgumentOutOfRangeException(nameof(settings.Depth));

        var worldCoord = new WorldCoord(regionCoord.WorldX, regionCoord.WorldY);
        var region = GetOrCreateRegion(seed, worldCoord, generationSettings);
        if (regionCoord.RegionX < 0 || regionCoord.RegionX >= region.Width ||
            regionCoord.RegionY < 0 || regionCoord.RegionY >= region.Height)
            throw new ArgumentOutOfRangeException(nameof(regionCoord), "Region coordinate is outside region bounds.");

        var key = LocalCacheKey.From(seed, generationSettings, regionCoord, settings);
        if (_localCache.TryGetValue(key, out var cached))
            return cached;

        var history = GetOrCreateHistory(seed, generationSettings);
        var localHistory = BuildLocalHistoryContext(history, worldCoord);
        var generated = _localGenerator.Generate(region, regionCoord, settings, localHistory);
        _localCache[key] = generated;
        return generated;
    }

    public RegionCoord ResolveDefaultRegionCoord(int seed, MapGenerationSettings settings)
        => ResolveEmbarkRegionCoord(seed, new LocalGenerationSettings(48, 48, 8), settings);

    public GeneratedEmbarkContext GenerateAndApplyEmbark(
        WorldMap targetMap,
        int seed,
        LocalGenerationSettings settings,
        string? biomeOverrideId = null,
        MapGenerationSettings? generationSettings = null)
    {
        if (targetMap is null) throw new ArgumentNullException(nameof(targetMap));
        var resolvedGenerationSettings = generationSettings ?? MapGenerationSettings.Default;
        var regionCoord = ResolveEmbarkRegionCoord(seed, settings, resolvedGenerationSettings);

        var effectiveSettings = settings with
        {
            BiomeOverrideId = biomeOverrideId ?? settings.BiomeOverrideId,
        };

        var localMap = GetOrCreateLocal(seed, regionCoord, effectiveSettings, resolvedGenerationSettings);
        WorldGenerator.ApplyGeneratedEmbark(targetMap, localMap);

        var history = GetOrCreateHistory(seed, resolvedGenerationSettings);
        var localHistory = BuildLocalHistoryContext(history, new WorldCoord(regionCoord.WorldX, regionCoord.WorldY));
        var world = GetOrCreateWorld(seed, resolvedGenerationSettings);
        var region = GetOrCreateRegion(seed, new WorldCoord(regionCoord.WorldX, regionCoord.WorldY), resolvedGenerationSettings);
        var worldTile = world.GetTile(regionCoord.WorldX, regionCoord.WorldY);
        var regionTile = region.GetTile(regionCoord.RegionX, regionCoord.RegionY);

        var context = new GeneratedEmbarkContext(
            Seed: seed,
            WorldCoord: new WorldCoord(regionCoord.WorldX, regionCoord.WorldY),
            RegionCoord: regionCoord,
            MacroBiomeId: worldTile.MacroBiomeId,
            RegionBiomeVariantId: regionTile.BiomeVariantId,
            EffectiveBiomeId: effectiveSettings.BiomeOverrideId ?? worldTile.MacroBiomeId,
            LocalHistory: localHistory);

        LastGeneratedEmbark = context;
        LastGeneratedLocalMap = localMap;
        LastGeneratedHistory = history;
        return context;
    }

    private static LocalHistoryContext? BuildLocalHistoryContext(GeneratedWorldHistory? history, WorldCoord embarkCoord)
    {
        if (history is null)
            return null;

        string? territoryOwnerCivilizationId = null;
        if (history.TryGetOwner(embarkCoord, out var territoryOwner))
            territoryOwnerCivilizationId = territoryOwner;

        var nearbySites = history.Sites
            .Where(site => ManhattanDistance(site.Location, embarkCoord) <= 1)
            .Select(site => new LocalHistorySite(
                site.Id,
                site.Name,
                site.Kind,
                site.OwnerCivilizationId,
                site.Location.X,
                site.Location.Y,
                site.Development,
                site.Security))
            .OrderBy(site => site.WorldX == embarkCoord.X && site.WorldY == embarkCoord.Y ? 0 : 1)
            .ThenBy(site => string.Equals(site.OwnerCivilizationId, territoryOwnerCivilizationId, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(site => ManhattanDistance(site.WorldX, site.WorldY, embarkCoord.X, embarkCoord.Y))
            .ThenByDescending(site => site.Development)
            .ThenBy(site => site.Name, StringComparer.Ordinal)
            .ToList();

        var nearbyRoads = history.Roads
            .Select(road => ToLocalHistoryRoad(road, embarkCoord))
            .Where(road => road.DistanceFromEmbark <= 1)
            .ToList();

        if (nearbySites.Count == 0 && nearbyRoads.Count == 0 && string.IsNullOrWhiteSpace(territoryOwnerCivilizationId))
            return null;

        var primarySite = SelectPrimarySite(nearbySites, embarkCoord, territoryOwnerCivilizationId);
        var ownerCivilizationId = territoryOwnerCivilizationId ?? primarySite?.OwnerCivilizationId;
        if (string.IsNullOrWhiteSpace(ownerCivilizationId))
        {
            ownerCivilizationId = nearbySites
                .OrderBy(site => site.WorldX == embarkCoord.X && site.WorldY == embarkCoord.Y ? 0 : 1)
                .ThenByDescending(site => site.Development)
                .Select(site => site.OwnerCivilizationId)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        }

        if (string.IsNullOrWhiteSpace(ownerCivilizationId))
        {
            ownerCivilizationId = nearbyRoads
                .Select(road => road.OwnerCivilizationId)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        }

        var supportingSites = nearbySites
            .Where(site => primarySite is null || !string.Equals(site.Id, primarySite.Value.Id, StringComparison.Ordinal))
            .Take(8)
            .ToArray();

        var supportingRoads = nearbyRoads
            .OrderBy(road => road.DistanceFromEmbark)
            .ThenBy(road => road.PortalEdges.Length == 0 ? 1 : 0)
            .ThenBy(road => string.Equals(road.OwnerCivilizationId, ownerCivilizationId, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(road => road.Id, StringComparer.Ordinal)
            .Take(8)
            .ToArray();

        return new LocalHistoryContext(
            ownerCivilizationId,
            territoryOwnerCivilizationId,
            primarySite,
            supportingSites,
            supportingRoads);
    }

    private static LocalHistorySite? SelectPrimarySite(
        IReadOnlyList<LocalHistorySite> sites,
        WorldCoord embarkCoord,
        string? territoryOwnerCivilizationId)
    {
        if (sites.Count == 0)
            return null;

        var selected = sites
            .OrderBy(site => site.WorldX == embarkCoord.X && site.WorldY == embarkCoord.Y ? 0 : 1)
            .ThenBy(site => string.Equals(site.OwnerCivilizationId, territoryOwnerCivilizationId, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(site => ManhattanDistance(site.WorldX, site.WorldY, embarkCoord.X, embarkCoord.Y))
            .ThenByDescending(site => site.Development)
            .ThenByDescending(site => site.Security)
            .ThenBy(site => site.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(selected.Id) ? null : selected;
    }

    private static LocalHistoryRoad ToLocalHistoryRoad(RoadRecord road, WorldCoord embarkCoord)
    {
        var minDistance = int.MaxValue;
        foreach (var coord in road.Path)
            minDistance = Math.Min(minDistance, ManhattanDistance(coord, embarkCoord));

        return new LocalHistoryRoad(
            road.Id,
            road.OwnerCivilizationId,
            string.IsNullOrWhiteSpace(road.FromSiteId) ? null : road.FromSiteId,
            string.IsNullOrWhiteSpace(road.ToSiteId) ? null : road.ToSiteId,
            minDistance,
            ResolveRoadPortalEdges(road.Path, embarkCoord));
    }

    private static LocalMapEdge[] ResolveRoadPortalEdges(IReadOnlyList<WorldCoord> path, WorldCoord embarkCoord)
    {
        var edges = new List<LocalMapEdge>(4);
        for (var i = 0; i < path.Count; i++)
        {
            if (!path[i].Equals(embarkCoord))
                continue;

            if (i > 0)
                AddPortalEdge(edges, embarkCoord, path[i - 1]);
            if (i + 1 < path.Count)
                AddPortalEdge(edges, embarkCoord, path[i + 1]);
        }

        return edges.ToArray();
    }

    private static void AddPortalEdge(List<LocalMapEdge> edges, WorldCoord center, WorldCoord neighbor)
    {
        var dx = neighbor.X - center.X;
        var dy = neighbor.Y - center.Y;
        if (Math.Abs(dx) + Math.Abs(dy) != 1)
            return;

        var edge = dx switch
        {
            < 0 => LocalMapEdge.West,
            > 0 => LocalMapEdge.East,
            _ => dy < 0 ? LocalMapEdge.North : LocalMapEdge.South,
        };

        if (!edges.Contains(edge))
            edges.Add(edge);
    }

    private static int ManhattanDistance(WorldCoord a, WorldCoord b)
        => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static int ManhattanDistance(int ax, int ay, int bx, int by)
        => Math.Abs(ax - bx) + Math.Abs(ay - by);

    private void ClearCaches()
    {
        _worldCache.Clear();
        _historyCache.Clear();
        _regionCache.Clear();
        _localCache.Clear();
        LastGeneratedLocalMap = null;
        LastGeneratedHistory = null;
    }

    private static void ValidateSettings(MapGenerationSettings settings)
    {
        if (settings.WorldWidth <= 0) throw new ArgumentOutOfRangeException(nameof(settings.WorldWidth));
        if (settings.WorldHeight <= 0) throw new ArgumentOutOfRangeException(nameof(settings.WorldHeight));
        if (settings.RegionWidth <= 0) throw new ArgumentOutOfRangeException(nameof(settings.RegionWidth));
        if (settings.RegionHeight <= 0) throw new ArgumentOutOfRangeException(nameof(settings.RegionHeight));
        if (settings.SimulatedHistoryYears < 0) throw new ArgumentOutOfRangeException(nameof(settings.SimulatedHistoryYears));
    }

    private static int PositiveMod(int value, int modulus)
    {
        if (modulus <= 0) throw new ArgumentOutOfRangeException(nameof(modulus));
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static int MixSeed(int a, int b, int c, int d)
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + a;
            hash = (hash * 31) + b;
            hash = (hash * 31) + c;
            hash = (hash * 31) + d;
            return hash;
        }
    }

    private RegionCoord ResolveEmbarkRegionCoord(int seed, LocalGenerationSettings localSettings, MapGenerationSettings settings)
    {
        ValidateSettings(settings);
        if (localSettings.Width <= 0) throw new ArgumentOutOfRangeException(nameof(localSettings.Width));
        if (localSettings.Height <= 0) throw new ArgumentOutOfRangeException(nameof(localSettings.Height));
        if (localSettings.Depth <= 0) throw new ArgumentOutOfRangeException(nameof(localSettings.Depth));

        var history = GetOrCreateHistory(seed, settings);
        var world = GetOrCreateWorld(seed, settings);
        var worldCandidates = BuildWorldEmbarkCandidates(seed, world, history)
            .Take(MaxWorldEmbarkCandidates)
            .ToArray();
        if (worldCandidates.Length == 0)
            return ResolveLegacyRegionCoord(seed, settings);

        var regionCandidates = new List<EmbarkRegionCandidate>(worldCandidates.Length * MaxRegionCandidatesPerWorld);
        foreach (var worldCandidate in worldCandidates)
        {
            var region = GetOrCreateRegion(seed, worldCandidate.WorldCoord, settings);
            regionCandidates.AddRange(BuildRegionEmbarkCandidates(seed, region, worldCandidate)
                .Take(MaxRegionCandidatesPerWorld));
        }

        if (regionCandidates.Count == 0)
            return ResolveLegacyRegionCoord(seed, settings);

        var orderedCandidates = regionCandidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Coord.WorldX)
            .ThenBy(candidate => candidate.Coord.WorldY)
            .ThenBy(candidate => candidate.Coord.RegionX)
            .ThenBy(candidate => candidate.Coord.RegionY)
            .Take(MaxLocalEmbarkEvaluations);

        RegionCoord? bestFallback = null;
        var bestFallbackScore = float.MinValue;

        foreach (var candidate in orderedCandidates)
        {
            var local = GetOrCreateLocal(seed, candidate.Coord, localSettings, settings);
            var evaluation = EvaluateEmbarkSite(local);
            var totalScore = candidate.Score + evaluation.Score;

            if (totalScore > bestFallbackScore)
            {
                bestFallback = candidate.Coord;
                bestFallbackScore = totalScore;
            }

            if (evaluation.IsSuitable)
                return candidate.Coord;
        }

        return bestFallback ?? ResolveLegacyRegionCoord(seed, settings);
    }

    private IEnumerable<EmbarkWorldCandidate> BuildWorldEmbarkCandidates(
        int seed,
        GeneratedWorldMap world,
        GeneratedWorldHistory? history)
    {
        var candidates = new List<EmbarkWorldCandidate>(world.Width * world.Height);
        for (var x = 0; x < world.Width; x++)
        for (var y = 0; y < world.Height; y++)
        {
            var coord = new WorldCoord(x, y);
            var tile = world.GetTile(x, y);
            var score = ScoreWorldEmbarkCandidate(seed, coord, tile, history);
            candidates.Add(new EmbarkWorldCandidate(coord, score));
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.WorldCoord.X)
            .ThenBy(candidate => candidate.WorldCoord.Y);
    }

    private IEnumerable<EmbarkRegionCandidate> BuildRegionEmbarkCandidates(
        int seed,
        GeneratedRegionMap region,
        EmbarkWorldCandidate worldCandidate)
    {
        var candidates = new List<EmbarkRegionCandidate>(region.Width * region.Height);
        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
        {
            var coord = new RegionCoord(region.WorldCoord.X, region.WorldCoord.Y, x, y);
            var tile = region.GetTile(x, y);
            var score = worldCandidate.Score + ScoreRegionEmbarkCandidate(seed, coord, tile);
            candidates.Add(new EmbarkRegionCandidate(coord, score));
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Coord.RegionX)
            .ThenBy(candidate => candidate.Coord.RegionY);
    }

    private static float ScoreWorldEmbarkCandidate(
        int seed,
        WorldCoord coord,
        GeneratedWorldTile tile,
        GeneratedWorldHistory? history)
    {
        var score = 0f;

        if (MacroBiomeIds.IsOcean(tile.MacroBiomeId))
            score -= 2f;

        score += tile.HasRiver ? 0.90f : 0f;
        score += Math.Clamp(tile.FlowAccumulation, 0f, 1f) * 0.25f;
        score += tile.MoistureBand * 0.35f;
        score += tile.ForestCover * 0.35f;
        score += (1f - Math.Abs((tile.TemperatureBand * 2f) - 1f)) * 0.20f;
        score += tile.HasRoad ? 0.05f : 0f;
        score -= tile.MountainCover * 0.35f;
        score -= tile.Relief * 0.20f;

        if (history is not null)
        {
            score += ResolveNearbySiteScore(history, coord) * 0.20f;
            score += ResolveNearbyRoadScore(history, coord) * 0.08f;
            if (history.TryGetOwner(coord, out _))
                score += 0.10f;
        }

        score += StableTieBreak(seed, coord.X, coord.Y, 13091) * 0.02f;
        return score;
    }

    private static float ScoreRegionEmbarkCandidate(
        int seed,
        RegionCoord coord,
        GeneratedRegionTile tile)
    {
        var score = 0f;

        if (RegionBiomeVariantIds.IsOceanVariant(tile.BiomeVariantId))
            score -= 2f;

        score += tile.HasRiver ? 1.05f : 0f;
        score += tile.HasLake ? 0.90f : 0f;
        score += tile.Groundwater * 0.60f;
        score += tile.FlowAccumulationBand * 0.30f;
        score += tile.VegetationDensity * 0.55f;
        score += tile.VegetationSuitability * 0.45f;
        score += tile.SoilDepth * 0.18f;
        score += tile.HasRoad ? 0.05f : 0f;
        score += tile.ResourceRichness * 0.10f;
        score -= (tile.Slope / 255f) * 0.45f;

        if (tile.SurfaceClassId == RegionSurfaceClassIds.Snow)
            score -= 0.12f;
        else if (tile.SurfaceClassId == RegionSurfaceClassIds.Mud)
            score += 0.06f;

        score += StableTieBreak(seed, coord.RegionX, coord.RegionY, 21121) * 0.02f;
        return score;
    }

    private static float ResolveNearbySiteScore(GeneratedWorldHistory history, WorldCoord coord)
    {
        var best = 0f;
        foreach (var site in history.Sites)
        {
            var distance = ManhattanDistance(site.Location, coord);
            if (distance > 1)
                continue;

            var distanceMultiplier = distance == 0 ? 1f : 0.55f;
            var score =
                ((Math.Clamp(site.Development, 0f, 1f) * 0.70f) +
                 (Math.Clamp(site.Security, 0f, 1f) * 0.30f)) *
                distanceMultiplier;
            if (score > best)
                best = score;
        }

        return best;
    }

    private static float ResolveNearbyRoadScore(GeneratedWorldHistory history, WorldCoord coord)
    {
        foreach (var road in history.Roads)
        {
            foreach (var point in road.Path)
            {
                if (ManhattanDistance(point, coord) <= 1)
                    return 1f;
            }
        }

        return 0f;
    }

    private static EmbarkSiteEvaluation EvaluateEmbarkSite(GeneratedEmbarkMap map)
    {
        if (map.Width <= 0 || map.Height <= 0 || map.Depth <= 0)
            return new EmbarkSiteEvaluation(false, 0, int.MaxValue, int.MaxValue, 0, -10f);

        var originX = map.Width / 2;
        var originY = map.Height / 2;
        if (!IsWalkableSurfaceTile(map, originX, originY))
            return new EmbarkSiteEvaluation(false, 0, int.MaxValue, int.MaxValue, 0, -10f);

        var searchRadius = Math.Max(10, (Math.Min(map.Width, map.Height) / 2) - 4);
        var visited = new bool[map.Width, map.Height];
        var queue = new Queue<(int X, int Y, int Distance)>();
        var nearbyFoodSources = new HashSet<int>();
        var reachableTiles = 0;
        var closestWaterDistance = int.MaxValue;
        var closestFoodDistance = int.MaxValue;

        visited[originX, originY] = true;
        queue.Enqueue((originX, originY, 0));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Distance > searchRadius)
                continue;

            reachableTiles++;
            ScanEmbarkResources(
                map,
                current.X,
                current.Y,
                current.Distance,
                nearbyFoodSources,
                ref closestWaterDistance,
                ref closestFoodDistance);

            foreach (var (dx, dy) in CardinalDirections)
            {
                var nx = current.X + dx;
                var ny = current.Y + dy;
                if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height || visited[nx, ny])
                    continue;
                if (!IsWalkableSurfaceTile(map, nx, ny))
                    continue;

                visited[nx, ny] = true;
                queue.Enqueue((nx, ny, current.Distance + 1));
            }
        }

        var hasNearbyWater = closestWaterDistance != int.MaxValue && closestWaterDistance <= MaxNearbyWaterDistance;
        var score =
            (hasNearbyWater ? 1.9f : 0f) +
            (closestWaterDistance == int.MaxValue ? 0f : Math.Max(0f, 1.2f - (closestWaterDistance * 0.08f))) +
            Math.Min(nearbyFoodSources.Count, 6) * 0.28f +
            (closestFoodDistance == int.MaxValue ? 0f : Math.Max(0f, 0.9f - (closestFoodDistance * 0.05f))) +
            Math.Min(reachableTiles, 160) * 0.004f;

        return new EmbarkSiteEvaluation(
            hasNearbyWater,
            nearbyFoodSources.Count,
            closestWaterDistance,
            closestFoodDistance,
            reachableTiles,
            score);
    }

    private static void ScanEmbarkResources(
        GeneratedEmbarkMap map,
        int x,
        int y,
        int distance,
        ISet<int> nearbyFoodSources,
        ref int closestWaterDistance,
        ref int closestFoodDistance)
    {
        ScanEmbarkResourceTile(map, x, y, distance, nearbyFoodSources, ref closestWaterDistance, ref closestFoodDistance);
        foreach (var (dx, dy) in CardinalDirections)
            ScanEmbarkResourceTile(map, x + dx, y + dy, distance + 1, nearbyFoodSources, ref closestWaterDistance, ref closestFoodDistance);
    }

    private static void ScanEmbarkResourceTile(
        GeneratedEmbarkMap map,
        int x,
        int y,
        int distance,
        ISet<int> nearbyFoodSources,
        ref int closestWaterDistance,
        ref int closestFoodDistance)
    {
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            return;

        var tile = map.GetTile(x, y, 0);
        if (IsDrinkableWaterTile(tile))
            closestWaterDistance = Math.Min(closestWaterDistance, distance);

        if (!IsHarvestableFoodTile(tile))
            return;

        nearbyFoodSources.Add((y * map.Width) + x);
        closestFoodDistance = Math.Min(closestFoodDistance, distance);
    }

    private static bool IsWalkableSurfaceTile(GeneratedEmbarkMap map, int x, int y)
    {
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            return false;

        var tile = map.GetTile(x, y, 0);
        if (!tile.IsPassable)
            return false;
        if (tile.FluidType == GeneratedFluidType.Magma || tile.TileDefId == GeneratedTileDefIds.Magma)
            return false;
        if (tile.FluidType == GeneratedFluidType.Water || tile.TileDefId == GeneratedTileDefIds.Water)
            return tile.FluidLevel <= WorldMap.MaxWadeableWaterLevel;

        return true;
    }

    private static bool IsDrinkableWaterTile(GeneratedTile tile)
    {
        if (tile.FluidType == GeneratedFluidType.Magma || tile.TileDefId == GeneratedTileDefIds.Magma)
            return false;

        return (tile.FluidType == GeneratedFluidType.Water || tile.TileDefId == GeneratedTileDefIds.Water)
               && tile.FluidLevel > 0;
    }

    private static bool IsHarvestableFoodTile(GeneratedTile tile)
        => !string.IsNullOrWhiteSpace(tile.PlantDefId)
           && tile.PlantYieldLevel > 0
           && tile.PlantGrowthStage >= GeneratedPlantGrowthStages.Mature;

    private static RegionCoord ResolveLegacyRegionCoord(int seed, MapGenerationSettings settings)
    {
        if (settings.WorldWidth <= 0 || settings.WorldHeight <= 0 || settings.RegionWidth <= 0 || settings.RegionHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(settings));

        var wx = PositiveMod(seed * 31 + 17, settings.WorldWidth);
        var wy = PositiveMod(seed * 17 + 31, settings.WorldHeight);
        var rx = PositiveMod(seed * 13 + wx * 7, settings.RegionWidth);
        var ry = PositiveMod(seed * 19 + wy * 11, settings.RegionHeight);
        return new RegionCoord(wx, wy, rx, ry);
    }

    private static float StableTieBreak(int a, int b, int c, int d)
    {
        var hash = MixSeed(a, b, c, d) & int.MaxValue;
        return hash / (float)int.MaxValue;
    }

    private readonly record struct EmbarkWorldCandidate(WorldCoord WorldCoord, float Score);
    private readonly record struct EmbarkRegionCandidate(RegionCoord Coord, float Score);
    private readonly record struct EmbarkSiteEvaluation(
        bool HasNearbyWater,
        int NearbyFoodSourceCount,
        int ClosestWaterDistance,
        int ClosestFoodDistance,
        int ReachableSurfaceTiles,
        float Score)
    {
        public bool IsSuitable
            => HasNearbyWater
               && NearbyFoodSourceCount >= MinNearbyFoodSources
               && ReachableSurfaceTiles >= MinReachableSurfaceTiles;
    }

    private readonly record struct WorldCacheKey(int Seed, int Width, int Height);
    private readonly record struct HistoryCacheKey(int Seed, int WorldWidth, int WorldHeight, int SimulatedYears);

    private readonly record struct RegionCacheKey(
        int Seed,
        int WorldWidth,
        int WorldHeight,
        int WorldX,
        int WorldY,
        int RegionWidth,
        int RegionHeight,
        bool HistoryEnabled,
        int HistoryYears);

    private readonly record struct LocalCacheKey(
        int Seed,
        int WorldWidth,
        int WorldHeight,
        int RegionWidth,
        int RegionHeight,
        bool HistoryEnabled,
        int HistoryYears,
        int WorldX,
        int WorldY,
        int RegionX,
        int RegionY,
        int Width,
        int Height,
        int Depth,
        string? BiomeOverrideId,
        int TreeBiasMilli,
        int OutcropBiasMilli,
        int StreamBandBias,
        int MarshPoolBias,
        int WetnessBiasMilli,
        int SoilDepthBiasMilli,
        int ForestPatchBiasMilli,
        int SettlementInfluenceMilli,
        int RoadInfluenceMilli,
        int StoneSurfaceMode)
    {
        public static LocalCacheKey From(
            int seed,
            MapGenerationSettings generationSettings,
            RegionCoord regionCoord,
            LocalGenerationSettings localSettings)
            => new(
                seed,
                generationSettings.WorldWidth,
                generationSettings.WorldHeight,
                generationSettings.RegionWidth,
                generationSettings.RegionHeight,
                generationSettings.EnableHistory,
                generationSettings.EnableHistory ? generationSettings.SimulatedHistoryYears : 0,
                regionCoord.WorldX,
                regionCoord.WorldY,
                regionCoord.RegionX,
                regionCoord.RegionY,
                localSettings.Width,
                localSettings.Height,
                localSettings.Depth,
                localSettings.BiomeOverrideId,
                Quantize(localSettings.TreeDensityBias),
                Quantize(localSettings.OutcropBias),
                localSettings.StreamBandBias,
                localSettings.MarshPoolBias,
                Quantize(localSettings.ParentWetnessBias),
                Quantize(localSettings.ParentSoilDepthBias),
                Quantize(localSettings.ForestPatchBias),
                Quantize(localSettings.SettlementInfluence),
                Quantize(localSettings.RoadInfluence),
                localSettings.StoneSurfaceOverride switch
                {
                    true => 1,
                    false => 0,
                    _ => -1,
                });

        private static int Quantize(float value)
            => (int)MathF.Round(Math.Clamp(value, -1f, 1f) * 1000f);
    }
}

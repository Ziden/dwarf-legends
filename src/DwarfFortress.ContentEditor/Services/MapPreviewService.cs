using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.WorldGen.Analysis;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Local;
using DwarfFortress.WorldGen.Maps;
using DwarfFortress.WorldGen.Regions;
using DwarfFortress.WorldGen.Story;
using DwarfFortress.WorldGen.World;

namespace DwarfFortress.ContentEditor.Services;

public sealed class MapPreviewService
{
    private static readonly IReadOnlyList<string> KnownBiomes = MacroBiomeIds.All;

    private readonly IWorldLayerGenerator _worldGenerator = new WorldLayerGenerator();
    private readonly IRegionLayerGenerator _regionGenerator = new RegionLayerGenerator();
    private readonly ILocalLayerGenerator _localGenerator = new LocalLayerGenerator();

    public IReadOnlyList<string> ListBiomes() => KnownBiomes;

    public MapPreviewRun Generate(MapPreviewRequest request)
    {
        var normalized = NormalizeRequest(request);
        var resolvedBiome = ResolveBiome(
            normalized.Seed,
            normalized.LocalWidth,
            normalized.LocalHeight,
            normalized.LocalDepth,
            normalized.BiomeId,
            normalized.UseLoreBiome);

        var world = _worldGenerator.Generate(normalized.Seed, normalized.WorldWidth, normalized.WorldHeight);
        var worldCoord = new WorldCoord(normalized.WorldX, normalized.WorldY);
        var worldTile = world.GetTile(worldCoord.X, worldCoord.Y);

        var region = _regionGenerator.Generate(world, worldCoord, normalized.RegionWidth, normalized.RegionHeight);
        var regionCoord = new RegionCoord(worldCoord.X, worldCoord.Y, normalized.RegionX, normalized.RegionY);
        var regionTile = region.GetTile(regionCoord.RegionX, regionCoord.RegionY);

        var localSettings = new LocalGenerationSettings(
            Width: normalized.LocalWidth,
            Height: normalized.LocalHeight,
            Depth: normalized.LocalDepth,
            BiomeOverrideId: resolvedBiome);
        var localMap = _localGenerator.Generate(region, regionCoord, localSettings);
        var mapMetrics = WorldGenAnalyzer.AnalyzeMap(localMap);

        return new MapPreviewRun(
            Request: normalized,
            ResolvedBiomeId: resolvedBiome,
            UsedLoreBiome: normalized.UseLoreBiome,
            WorldMap: world,
            SelectedWorldTile: worldTile,
            RegionMap: region,
            SelectedRegionTile: regionTile,
            LocalMap: localMap,
            LocalMapMetrics: mapMetrics,
            WorldStats: AnalyzeWorld(world),
            RegionStats: AnalyzeRegion(region),
            LocalStats: AnalyzeLocalSurface(localMap, regionTile));
    }

    public MapLayerStats AnalyzeLayer(GeneratedEmbarkMap map, int z)
    {
        if (z < 0 || z >= map.Depth)
            throw new ArgumentOutOfRangeException(nameof(z), $"Layer {z} is outside map depth 0..{map.Depth - 1}.");

        var passable = 0;
        var trees = 0;
        var water = 0;
        var walls = 0;
        var aquifer = 0;
        var ore = 0;
        var tileCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, z);
            if (tile.IsPassable) passable++;
            if (tile.TileDefId == GeneratedTileDefIds.Tree) trees++;
            if (tile.TileDefId == GeneratedTileDefIds.Water) water++;
            if (!tile.IsPassable) walls++;
            if (tile.IsAquifer) aquifer++;
            if (tile.OreId is not null) ore++;

            tileCounts.TryGetValue(tile.TileDefId, out var count);
            tileCounts[tile.TileDefId] = count + 1;
        }

        var summary = tileCounts
            .Select(kvp => new TileCountSummary(kvp.Key, kvp.Value))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.TileDefId, StringComparer.Ordinal)
            .ToList();

        return new MapLayerStats(
            Z: z,
            TotalTiles: map.Width * map.Height,
            PassableTiles: passable,
            TreeTiles: trees,
            WaterTiles: water,
            WallTiles: walls,
            AquiferTiles: aquifer,
            OreTiles: ore,
            TileBreakdown: summary);
    }

    private static WorldPreviewStats AnalyzeWorld(GeneratedWorldMap world)
    {
        var biomeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var elevation = 0f;
        var moisture = 0f;
        var drainage = 0f;
        var temperature = 0f;
        var factionPressure = 0f;

        for (var x = 0; x < world.Width; x++)
        for (var y = 0; y < world.Height; y++)
        {
            var tile = world.GetTile(x, y);
            biomeCounts.TryGetValue(tile.MacroBiomeId, out var count);
            biomeCounts[tile.MacroBiomeId] = count + 1;

            elevation += tile.ElevationBand;
            moisture += tile.MoistureBand;
            drainage += tile.DrainageBand;
            temperature += tile.TemperatureBand;
            factionPressure += tile.FactionPressure;
        }

        var total = world.Width * world.Height;
        if (total > 0)
        {
            elevation /= total;
            moisture /= total;
            drainage /= total;
            temperature /= total;
            factionPressure /= total;
        }

        var summary = biomeCounts
            .Select(x => new BiomeCountSummary(x.Key, x.Value))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.BiomeId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WorldPreviewStats(
            TotalTiles: total,
            AvgElevation: elevation,
            AvgMoisture: moisture,
            AvgDrainage: drainage,
            AvgTemperature: temperature,
            AvgFactionPressure: factionPressure,
            MacroBiomeBreakdown: summary);
    }

    private static RegionPreviewStats AnalyzeRegion(GeneratedRegionMap region)
    {
        var riverTiles = 0;
        var lakeTiles = 0;
        var roadTiles = 0;
        var settlementTiles = 0;
        var avgVegetation = 0f;
        var avgResources = 0f;
        var avgSlope = 0f;
        var variantCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
        {
            var tile = region.GetTile(x, y);
            if (tile.HasRiver) riverTiles++;
            if (tile.HasLake) lakeTiles++;
            if (tile.HasRoad) roadTiles++;
            if (tile.HasSettlement) settlementTiles++;

            avgVegetation += tile.VegetationDensity;
            avgResources += tile.ResourceRichness;
            avgSlope += tile.Slope / 255f;

            variantCounts.TryGetValue(tile.BiomeVariantId, out var count);
            variantCounts[tile.BiomeVariantId] = count + 1;
        }

        var total = region.Width * region.Height;
        if (total > 0)
        {
            avgVegetation /= total;
            avgResources /= total;
            avgSlope /= total;
        }

        var summary = variantCounts
            .Select(x => new BiomeCountSummary(x.Key, x.Value))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.BiomeId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RegionPreviewStats(
            TotalTiles: total,
            RiverTiles: riverTiles,
            LakeTiles: lakeTiles,
            RoadTiles: roadTiles,
            SettlementTiles: settlementTiles,
            AvgVegetationDensity: avgVegetation,
            AvgResourceRichness: avgResources,
            AvgSlope: avgSlope,
            BiomeVariantBreakdown: summary);
    }

    private static LocalPreviewStats AnalyzeLocalSurface(GeneratedEmbarkMap map, GeneratedRegionTile regionTile)
    {
        var trees = CountSurfaceTiles(map, GeneratedTileDefIds.Tree);
        var water = CountSurfaceTiles(map, GeneratedTileDefIds.Water);
        var walls = CountSurfaceWalls(map);

        var orePotential = Math.Clamp(regionTile.ResourceRichness, 0f, 1f);
        var oreLabel = orePotential switch
        {
            >= 0.80f => "very high",
            >= 0.60f => "high",
            >= 0.40f => "moderate",
            >= 0.20f => "low",
            _ => "very low",
        };

        return new LocalPreviewStats(
            TreeTiles: trees,
            WaterTiles: water,
            WallTiles: walls,
            OrePotential: orePotential,
            OrePotentialLabel: oreLabel);
    }

    private static int CountSurfaceTiles(GeneratedEmbarkMap map, string tileDefId)
    {
        var count = 0;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (map.GetTile(x, y, 0).TileDefId == tileDefId)
                count++;
        }

        return count;
    }

    private static int CountSurfaceWalls(GeneratedEmbarkMap map)
    {
        var count = 0;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (!tile.IsPassable && tile.TileDefId != GeneratedTileDefIds.Tree)
                count++;
        }

        return count;
    }

    private static string? ResolveBiome(
        int seed,
        int width,
        int height,
        int depth,
        string? biomeId,
        bool useLoreBiome)
    {
        if (useLoreBiome)
            return WorldLoreGenerator.Generate(seed, width, height, depth).BiomeId;

        return string.IsNullOrWhiteSpace(biomeId) ? null : biomeId.Trim();
    }

    private static MapPreviewRequest NormalizeRequest(MapPreviewRequest request)
    {
        ValidateDimensions(request);

        var worldX = Math.Clamp(request.WorldX, 0, request.WorldWidth - 1);
        var worldY = Math.Clamp(request.WorldY, 0, request.WorldHeight - 1);
        var regionX = Math.Clamp(request.RegionX, 0, request.RegionWidth - 1);
        var regionY = Math.Clamp(request.RegionY, 0, request.RegionHeight - 1);

        return request with
        {
            WorldX = worldX,
            WorldY = worldY,
            RegionX = regionX,
            RegionY = regionY,
        };
    }

    private static void ValidateDimensions(MapPreviewRequest request)
    {
        if (request.LocalWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.LocalWidth), "Local width must be greater than 0.");
        if (request.LocalHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.LocalHeight), "Local height must be greater than 0.");
        if (request.LocalDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.LocalDepth), "Local depth must be greater than 0.");
        if (request.WorldWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.WorldWidth), "World width must be greater than 0.");
        if (request.WorldHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.WorldHeight), "World height must be greater than 0.");
        if (request.RegionWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.RegionWidth), "Region width must be greater than 0.");
        if (request.RegionHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.RegionHeight), "Region height must be greater than 0.");

        if (request.LocalWidth > 256 || request.LocalHeight > 256 || request.LocalDepth > 64)
            throw new ArgumentOutOfRangeException(
                "Local map size is too large for preview. Maximum supported dimensions are 256 x 256 x 64.");
        if (request.WorldWidth > 128 || request.WorldHeight > 128)
            throw new ArgumentOutOfRangeException(
                "World map size is too large for preview. Maximum supported dimensions are 128 x 128.");
        if (request.RegionWidth > 64 || request.RegionHeight > 64)
            throw new ArgumentOutOfRangeException(
                "Region map size is too large for preview. Maximum supported dimensions are 64 x 64.");
    }
}

public sealed record MapPreviewRequest(
    int Seed = 42,
    int LocalWidth = 48,
    int LocalHeight = 48,
    int LocalDepth = 8,
    int WorldWidth = 24,
    int WorldHeight = 24,
    int RegionWidth = 16,
    int RegionHeight = 16,
    int WorldX = 12,
    int WorldY = 12,
    int RegionX = 8,
    int RegionY = 8,
    string? BiomeId = null,
    bool UseLoreBiome = false);

public sealed record MapPreviewRun(
    MapPreviewRequest Request,
    string? ResolvedBiomeId,
    bool UsedLoreBiome,
    GeneratedWorldMap WorldMap,
    GeneratedWorldTile SelectedWorldTile,
    GeneratedRegionMap RegionMap,
    GeneratedRegionTile SelectedRegionTile,
    GeneratedEmbarkMap LocalMap,
    MapMetrics LocalMapMetrics,
    WorldPreviewStats WorldStats,
    RegionPreviewStats RegionStats,
    LocalPreviewStats LocalStats);

public sealed record WorldPreviewStats(
    int TotalTiles,
    float AvgElevation,
    float AvgMoisture,
    float AvgDrainage,
    float AvgTemperature,
    float AvgFactionPressure,
    IReadOnlyList<BiomeCountSummary> MacroBiomeBreakdown);

public sealed record RegionPreviewStats(
    int TotalTiles,
    int RiverTiles,
    int LakeTiles,
    int RoadTiles,
    int SettlementTiles,
    float AvgVegetationDensity,
    float AvgResourceRichness,
    float AvgSlope,
    IReadOnlyList<BiomeCountSummary> BiomeVariantBreakdown);

public sealed record LocalPreviewStats(
    int TreeTiles,
    int WaterTiles,
    int WallTiles,
    float OrePotential,
    string OrePotentialLabel);

public sealed record BiomeCountSummary(
    string BiomeId,
    int Count);

public sealed record MapLayerStats(
    int Z,
    int TotalTiles,
    int PassableTiles,
    int TreeTiles,
    int WaterTiles,
    int WallTiles,
    int AquiferTiles,
    int OreTiles,
    IReadOnlyList<TileCountSummary> TileBreakdown);

public sealed record TileCountSummary(
    string TileDefId,
    int Count);

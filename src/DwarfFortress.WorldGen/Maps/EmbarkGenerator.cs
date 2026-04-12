using System;
using System.Collections.Generic;
using DwarfFortress.WorldGen.Config;
using DwarfFortress.WorldGen.Creatures;
using DwarfFortress.WorldGen.Geology;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Local;

namespace DwarfFortress.WorldGen.Maps;

public static class EmbarkGenerator
{
    private const int EmbarkProtectedHalfSpan = 5; // 10x10 spawn-safe core
    private const byte MinAquaticSpawnWaterLevel = 3;
    private static readonly float[] CaveDepthFractions = [0.46f, 0.64f, 0.80f];
    private static readonly (int X, int Y)[] CardinalDirections =
    [
        (0, -1),
        (1, 0),
        (0, 1),
        (-1, 0),
    ];
    private readonly record struct AnchoredPoint(int X, int Y, byte Strength);
    private readonly record struct RoadEndpoint(int X, int Y, byte Width);

    private sealed class LocalGenerationContext
    {
        public LocalGenerationContext(
            LocalGenerationSettings settings,
            int seed,
            GeneratedEmbarkMap map,
            Random rng,
            string biomeId,
            float wetnessBias,
            float soilDepthBias,
            float forestPatchBias,
            float treeCoverMin,
            float treeCoverMax,
            int outcropMin,
            int outcropMax,
            int streamBands,
            int marshPoolCount,
            StrataProfile strataProfile,
            bool useStoneSurface,
            float[,] terrain,
            float[,] moisture,
            float[,] canopyMask,
            float[,] forestPatchMask,
            float[,] forestOpeningMask,
            IReadOnlyList<int> caveLayers,
            GeneratedTile surface)
        {
            Settings = settings;
            Seed = seed;
            Map = map;
            Rng = rng;
            BiomeId = biomeId;
            WetnessBias = wetnessBias;
            SoilDepthBias = soilDepthBias;
            ForestPatchBias = forestPatchBias;
            TreeCoverMin = treeCoverMin;
            TreeCoverMax = treeCoverMax;
            OutcropMin = outcropMin;
            OutcropMax = outcropMax;
            StreamBands = streamBands;
            MarshPoolCount = marshPoolCount;
            StrataProfile = strataProfile;
            UseStoneSurface = useStoneSurface;
            Terrain = terrain;
            Moisture = moisture;
            CanopyMask = canopyMask;
            ForestPatchMask = forestPatchMask;
            ForestOpeningMask = forestOpeningMask;
            CaveLayers = caveLayers;
            Surface = surface;
        }

        public LocalGenerationSettings Settings { get; }
        public int Seed { get; }
        public GeneratedEmbarkMap Map { get; }
        public Random Rng { get; }
        public string BiomeId { get; }
        public float WetnessBias { get; }
        public float SoilDepthBias { get; }
        public float ForestPatchBias { get; }
        public float TreeCoverMin { get; }
        public float TreeCoverMax { get; }
        public int OutcropMin { get; }
        public int OutcropMax { get; }
        public int StreamBands { get; }
        public int MarshPoolCount { get; }
        public StrataProfile StrataProfile { get; }
        public bool UseStoneSurface { get; }
        public float[,] Terrain { get; }
        public float[,] Moisture { get; }
        public float[,] CanopyMask { get; }
        public float[,] ForestPatchMask { get; }
        public float[,] ForestOpeningMask { get; }
        public IReadOnlyList<int> CaveLayers { get; }
        public GeneratedTile Surface { get; }
        public List<EmbarkGenerationStageSnapshot> StageSnapshots { get; } = [];
    }

    public static GeneratedEmbarkMap Generate(
        int width = 48,
        int height = 48,
        int depth = 8,
        int seed = 0,
        string? biomeId = null)
    {
        var settings = new LocalGenerationSettings(width, height, depth, biomeId);
        return Generate(settings, seed);
    }

    public static GeneratedEmbarkMap Generate(LocalGenerationSettings settings, int seed = 0)
    {
        ValidateSettings(settings);

        var context = CreateGenerationContext(settings, seed);
        CaptureStageSnapshot(context, EmbarkGenerationStageId.Inputs);
        RunSurfaceShapeStage(context);
        CaptureStageSnapshot(context, EmbarkGenerationStageId.SurfaceShape);
        RunUndergroundStructureStage(context);
        CaptureStageSnapshot(context, EmbarkGenerationStageId.UndergroundStructure);
        RunHydrologyStage(context);
        CaptureStageSnapshot(context, EmbarkGenerationStageId.Hydrology);
        RunEcologyStage(context);
        CaptureStageSnapshot(context, EmbarkGenerationStageId.Ecology);
        RunHydrologyPolishStage(context);
        CaptureStageSnapshot(context, EmbarkGenerationStageId.HydrologyPolish);
        RunCivilizationOverlayStage(context);
        CaptureStageSnapshot(context, EmbarkGenerationStageId.CivilizationOverlay);
        RunPlayabilityStage(context);
        CaptureStageSnapshot(context, EmbarkGenerationStageId.Playability);
        RunPopulationStage(context);
        CaptureStageSnapshot(context, EmbarkGenerationStageId.Population);

        context.Map.Diagnostics = new EmbarkGenerationDiagnostics(context.Seed, context.StageSnapshots.ToArray());

        return context.Map;
    }

    private static void ValidateSettings(LocalGenerationSettings settings)
    {
        if (settings.Width <= 0) throw new ArgumentOutOfRangeException(nameof(settings.Width));
        if (settings.Height <= 0) throw new ArgumentOutOfRangeException(nameof(settings.Height));
        if (settings.Depth <= 0) throw new ArgumentOutOfRangeException(nameof(settings.Depth));
    }

    private static LocalGenerationContext CreateGenerationContext(LocalGenerationSettings settings, int seed)
    {
        var map = new GeneratedEmbarkMap(settings.Width, settings.Height, settings.Depth);
        var rng = new Random(seed);
        var biome = ResolveBiomePreset(settings.BiomeOverrideId, seed);
        var wetnessBias = Math.Clamp(settings.ParentWetnessBias, -1f, 1f);
        var soilDepthBias = Math.Clamp(settings.ParentSoilDepthBias, -1f, 1f);
        var forestPatchBias = Math.Clamp(settings.ForestPatchBias, -1f, 1f);
        var ecologyTreeBias = Math.Clamp(settings.TreeDensityBias + (wetnessBias * 0.45f) + (soilDepthBias * 0.30f), -1f, 1f);
        var ecologyOutcropBias = Math.Clamp(settings.OutcropBias - (wetnessBias * 0.20f) - (soilDepthBias * 0.40f), -1f, 1f);
        var streamBonus = wetnessBias >= 0.40f ? 1 : (wetnessBias <= -0.45f ? -1 : 0);
        var marshBonus = (int)MathF.Round(Math.Max(0f, wetnessBias) * 4f);

        var (treeCoverMin, treeCoverMax) = ApplyCoverageBias(biome.TreeCoverMin, biome.TreeCoverMax, ecologyTreeBias);
        (treeCoverMin, treeCoverMax) = ApplyForestCoverageTarget(treeCoverMin, treeCoverMax, settings.ForestCoverageTarget, biome);
        var (outcropMin, outcropMax) = ApplyRangeBias(biome.OutcropMin, biome.OutcropMax, ecologyOutcropBias, 0);
        var streamBands = Math.Max(0, biome.StreamBands + settings.StreamBandBias + streamBonus);
        var marshPoolCount = Math.Max(0, biome.MarshPoolCount + settings.MarshPoolBias + marshBonus);
        var strataProfile = StrataProfileRegistry.Resolve(settings.GeologyProfileId);
        var useStoneSurface = settings.StoneSurfaceOverride ?? biome.StoneSurface;
        var terrain = BuildSurfaceTerrainMap(settings.Width, settings.Height, seed, biome, settings.NoiseOriginX, settings.NoiseOriginY);
        var moisture = BuildSurfaceMoistureMap(
            settings.Width,
            settings.Height,
            seed,
            terrain,
            biome,
            wetnessBias,
            soilDepthBias,
            settings.NoiseOriginX,
            settings.NoiseOriginY);
        var canopyMask = BuildCanopyMaskMap(settings.Width, settings.Height, seed, settings.NoiseOriginX, settings.NoiseOriginY);
        var forestPatchMask = BuildForestPatchMaskMap(settings.Width, settings.Height, seed, forestPatchBias, settings.NoiseOriginX, settings.NoiseOriginY);
        var forestOpeningMask = BuildForestOpeningMaskMap(settings.Width, settings.Height, seed, settings.NoiseOriginX, settings.NoiseOriginY);
        var caveLayers = ResolveCaveLayerDepths(map.Depth);
        var surface = ResolveSurfaceTile(biome.Id, useStoneSurface, settings.SurfaceTileOverrideId);

        return new LocalGenerationContext(
            settings,
            seed,
            map,
            rng,
            biome.Id,
            wetnessBias,
            soilDepthBias,
            forestPatchBias,
            treeCoverMin,
            treeCoverMax,
            outcropMin,
            outcropMax,
            streamBands,
            marshPoolCount,
            strataProfile,
            useStoneSurface,
            terrain,
            moisture,
            canopyMask,
            forestPatchMask,
            forestOpeningMask,
            caveLayers,
            surface);
    }

    private static void RunSurfaceShapeStage(LocalGenerationContext context)
    {
        FillSurface(context.Map, context.Surface);
        ApplyBiomeSurfaceTransitions(
            context.Map,
            context.BiomeId,
            context.UseStoneSurface,
            context.Terrain,
            context.Moisture,
            context.Seed,
            context.Settings.SurfaceTileOverrideId);
    }

    private static void RunUndergroundStructureStage(LocalGenerationContext context)
    {
        var strataAssignments = FillUnderground(context.Map, context.StrataProfile, context.Seed);
        AddCaveSystems(context.Map, context.Seed, context.CaveLayers);
        AddMineralVeins(context.Map, context.StrataProfile, strataAssignments, context.Seed);
        ApplyAquiferBand(context.Map, context.StrataProfile);
        FillMagmaSea(context.Map);
    }

    private static void RunHydrologyStage(LocalGenerationContext context)
    {
        if (context.Settings.RiverPortals is { Length: > 0 })
        {
            AddAnchoredStreams(context.Map, context.Rng, context.Terrain, context.Settings.RiverPortals);
            if (context.StreamBands > 0)
                AddStreams(context.Map, context.Rng, Math.Max(0, context.StreamBands - 1), context.Terrain);
        }
        else
        {
            AddStreams(context.Map, context.Rng, context.StreamBands, context.Terrain);
        }

        AddSurfaceToCaveWaterConnections(context.Map, context.Rng, context.CaveLayers);
    }

    private static void RunEcologyStage(LocalGenerationContext context)
    {
        ApplySurfaceWaterMoistureFeedback(context.Map, context.Moisture);
        AddTrees(
            context.Map,
            context.Rng,
            context.TreeCoverMin,
            context.TreeCoverMax,
            context.Terrain,
            context.Moisture,
            context.CanopyMask,
            context.ForestPatchMask,
            context.ForestOpeningMask,
            context.BiomeId,
            context.ForestPatchBias);
        AddOutcrops(context.Map, context.Rng, context.OutcropMin, context.OutcropMax, context.Terrain);
        AddMarshPools(context.Map, context.Rng, context.MarshPoolCount, context.Terrain, context.Moisture);
    }

    private static void RunHydrologyPolishStage(LocalGenerationContext context)
    {
        FloodOceanSurface(context.Map, context.Rng, context.BiomeId, context.Terrain);
        HarmonizeSurfaceWaterDepths(context.Map, context.Terrain, context.BiomeId);
        ApplyRiparianSurfaceTransitions(context.Map, context.BiomeId, context.Seed);
    }

    private static void RunCivilizationOverlayStage(LocalGenerationContext context)
    {
        ApplySettlementAndRoadOverlay(
            context.Map,
            context.Seed,
            context.Surface,
            context.BiomeId,
            context.Settings.SettlementInfluence,
            context.Settings.RoadInfluence,
            context.Settings.SettlementAnchors,
            context.Settings.RoadPortals);
    }

    private static void RunPlayabilityStage(LocalGenerationContext context)
    {
        EnsureBorderPassable(context.Map, context.Surface);
        EnsureCentralEmbarkZone(context.Map, context.Surface);
        PlaceCentralStaircase(context.Map);
        AddPlants(
            context.Map,
            context.Seed,
            context.Rng,
            context.Terrain,
            context.Moisture,
            context.ForestPatchMask,
            context.ForestOpeningMask,
            context.BiomeId);
    }

    private static void RunPopulationStage(LocalGenerationContext context)
    {
        AddSurfaceCreatureSpawns(context.Map, context.Rng, context.BiomeId);
        AddCaveCreatureSpawns(context.Map, context.Rng, context.CaveLayers);
    }

    private static void CaptureStageSnapshot(LocalGenerationContext context, EmbarkGenerationStageId stageId)
    {
        context.StageSnapshots.Add(CreateStageSnapshot(context.Map, stageId));
    }

    private static EmbarkGenerationStageSnapshot CreateStageSnapshot(GeneratedEmbarkMap map, EmbarkGenerationStageId stageId)
    {
        var surfacePassableTiles = 0;
        var surfaceWaterTiles = 0;
        var surfaceTreeTiles = 0;
        var surfaceWallTiles = 0;
        var undergroundPassableTiles = 0;
        var aquiferTiles = 0;
        var oreTiles = 0;
        var magmaTiles = 0;

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var surfaceTile = map.GetTile(x, y, 0);
            if (surfaceTile.IsPassable)
                surfacePassableTiles++;
            else
                surfaceWallTiles++;

            if (surfaceTile.TileDefId == GeneratedTileDefIds.Tree)
                surfaceTreeTiles++;

            if (surfaceTile.TileDefId == GeneratedTileDefIds.Water || surfaceTile.FluidType == GeneratedFluidType.Water)
                surfaceWaterTiles++;

            for (var z = 1; z < map.Depth; z++)
            {
                var tile = map.GetTile(x, y, z);
                if (tile.IsPassable)
                    undergroundPassableTiles++;
                if (tile.IsAquifer)
                    aquiferTiles++;
                if (!string.IsNullOrWhiteSpace(tile.OreId))
                    oreTiles++;
                if (tile.TileDefId == GeneratedTileDefIds.Magma || tile.FluidType == GeneratedFluidType.Magma)
                    magmaTiles++;
            }
        }

        return new EmbarkGenerationStageSnapshot(
            StageId: stageId,
            SurfacePassableTiles: surfacePassableTiles,
            SurfaceWaterTiles: surfaceWaterTiles,
            SurfaceTreeTiles: surfaceTreeTiles,
            SurfaceWallTiles: surfaceWallTiles,
            UndergroundPassableTiles: undergroundPassableTiles,
            AquiferTiles: aquiferTiles,
            OreTiles: oreTiles,
            MagmaTiles: magmaTiles,
            CreatureSpawnCount: map.CreatureSpawns.Count);
    }

    private static (int Min, int Max) ApplyRangeBias(int min, int max, float bias, int floor)
    {
        var clamped = Math.Clamp(bias, -1f, 1f);
        if (Math.Abs(clamped) <= 0.0001f)
            return (min, max);

        var span = Math.Max(1, (max - min) + 1);
        var delta = (int)MathF.Round(span * clamped * 0.75f);
        var shiftedMin = Math.Max(floor, min + delta);
        var shiftedMax = Math.Max(shiftedMin, max + delta);
        return (shiftedMin, shiftedMax);
    }

    private static (float Min, float Max) ApplyCoverageBias(float min, float max, float bias)
    {
        var clamped = Math.Clamp(bias, -1f, 1f);
        var boundedMin = Math.Clamp(min, 0f, 0.95f);
        var boundedMax = Math.Clamp(max, boundedMin, 0.95f);
        if (Math.Abs(clamped) <= 0.0001f)
            return (boundedMin, boundedMax);

        var span = Math.Max(0.02f, boundedMax - boundedMin);
        var delta = span * clamped * 0.70f;
        var shiftedMin = Math.Clamp(boundedMin + delta, 0f, 0.95f);
        var shiftedMax = Math.Clamp(boundedMax + delta, shiftedMin, 0.95f);
        return (shiftedMin, shiftedMax);
    }

    private static (float Min, float Max) ApplyForestCoverageTarget(
        float min,
        float max,
        float? targetCoverage,
        BiomeGenerationProfile biome)
    {
        if (!targetCoverage.HasValue)
            return (min, max);

        var target = Math.Clamp(targetCoverage.Value, 0f, 0.95f);
        var center = Math.Clamp((min + max) * 0.5f, 0f, 0.95f);
        var span = Math.Clamp(max - min, 0.02f, 0.40f);
        var denseForestBiome = biome.DenseForest;
        var blend = denseForestBiome ? 0.72f : 0.60f;
        var adjustedCenter = Math.Clamp(center + ((target - center) * blend), 0f, 0.95f);
        var adjustedSpan = Math.Clamp(
            (span * 0.78f) + (MathF.Abs(target - center) * 0.20f),
            denseForestBiome ? 0.08f : 0.05f,
            0.24f);
        var adjustedMin = Math.Clamp(adjustedCenter - (adjustedSpan * 0.5f), 0f, 0.95f);
        var adjustedMax = Math.Clamp(adjustedCenter + (adjustedSpan * 0.5f), adjustedMin, 0.95f);
        return (adjustedMin, adjustedMax);
    }

    private static BiomeGenerationProfile ResolveBiomePreset(string? biomeId, int seed)
        => WorldGenContentRegistry.Current.ResolveBiomePreset(biomeId, seed);

    private static void FillSurface(GeneratedEmbarkMap map, GeneratedTile baseTile)
    {
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
            map.SetTile(x, y, 0, baseTile);
    }

    private static void ApplyBiomeSurfaceTransitions(
        GeneratedEmbarkMap map,
        string biomeId,
        bool useStoneSurface,
        float[,] terrain,
        float[,] moisture,
        int seed,
        string? surfaceTileOverrideId)
    {
        var preferredSurfaceTileDefId = ResolvePreferredSurfaceTileDefId(surfaceTileOverrideId);
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (!tile.IsPassable || tile.FluidType != GeneratedFluidType.None)
                continue;

            var fx = map.Width <= 1 ? 0f : x / (float)(map.Width - 1);
            var fy = map.Height <= 1 ? 0f : y / (float)(map.Height - 1);
            var macroNoise = CoherentNoise.DomainWarpedFractal2D(
                seed, fx * 1.45f, fy * 1.45f, octaves: 3, lacunarity: 2f, gain: 0.52f, warpStrength: 0.22f, salt: 463);
            var detailNoise = CoherentNoise.Fractal2D(
                seed, fx * 5.8f, fy * 5.8f, octaves: 2, lacunarity: 2f, gain: 0.5f, salt: 509);
            var blend = Math.Clamp((macroNoise * 0.62f) + (detailNoise * 0.18f) + (moisture[x, y] * 0.20f), 0f, 1f);

            var resolvedTileDefId = ResolveBiomeSurfaceTileDefId(
                biomeId,
                useStoneSurface,
                terrain[x, y],
                moisture[x, y],
                blend,
                preferredSurfaceTileDefId,
                tile.TileDefId);

            if (string.Equals(resolvedTileDefId, tile.TileDefId, StringComparison.OrdinalIgnoreCase))
                continue;

            map.SetTile(x, y, 0, new GeneratedTile(
                TileDefId: resolvedTileDefId,
                MaterialId: ResolveSurfaceMaterialId(resolvedTileDefId, tile.MaterialId),
                IsPassable: true));
        }
    }

    private static string ResolveBiomeSurfaceTileDefId(
        string biomeId,
        bool useStoneSurface,
        float terrain,
        float moisture,
        float blend,
        string? preferredSurfaceTileDefId,
        string currentTileDefId)
    {
        if (useStoneSurface && string.Equals(biomeId, MacroBiomeIds.Highland, StringComparison.OrdinalIgnoreCase))
            return GeneratedTileDefIds.StoneFloor;

        if (!string.IsNullOrWhiteSpace(preferredSurfaceTileDefId) &&
            !string.Equals(preferredSurfaceTileDefId, GeneratedTileDefIds.Grass, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveSurfaceTileDefIdFromPreferredIntent(
                preferredSurfaceTileDefId!,
                biomeId,
                useStoneSurface,
                terrain,
                moisture,
                blend,
                currentTileDefId);
        }
        

        if (useStoneSurface)
        {
            if (string.Equals(biomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase))
            {
                if (moisture > 0.52f && blend > 0.66f)
                    return GeneratedTileDefIds.Sand;
                return GeneratedTileDefIds.StoneFloor;
            }

            if (moisture > 0.66f && blend > 0.74f)
                return GeneratedTileDefIds.Mud;
            if (moisture > 0.48f && terrain < 0.46f && blend > 0.68f)
                return GeneratedTileDefIds.Soil;
            return GeneratedTileDefIds.StoneFloor;
        }

        if (string.Equals(biomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.OceanShallow, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.OceanDeep, StringComparison.OrdinalIgnoreCase))
        {
            if (moisture > 0.62f && blend > 0.70f)
                return GeneratedTileDefIds.Mud;
            if (moisture > 0.44f && blend > 0.54f)
                return GeneratedTileDefIds.Soil;
            return preferredSurfaceTileDefId ?? GeneratedTileDefIds.Sand;
        }

        if (string.Equals(biomeId, MacroBiomeIds.MistyMarsh, StringComparison.OrdinalIgnoreCase))
        {
            if (moisture >= 0.62f || blend >= 0.58f)
                return GeneratedTileDefIds.Mud;
            return preferredSurfaceTileDefId ?? GeneratedTileDefIds.Grass;
        }

        if (string.Equals(biomeId, MacroBiomeIds.Tundra, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase))
        {
            if (terrain > 0.72f && blend < 0.46f)
                return GeneratedTileDefIds.StoneFloor;
            if (moisture < 0.20f && blend < 0.35f)
                return GeneratedTileDefIds.Soil;
            return preferredSurfaceTileDefId ?? GeneratedTileDefIds.Snow;
        }

        if (string.Equals(biomeId, MacroBiomeIds.BorealForest, StringComparison.OrdinalIgnoreCase))
        {
            if (terrain > 0.66f && blend < 0.42f)
                return GeneratedTileDefIds.StoneFloor;
            if (moisture < 0.26f && blend < 0.34f)
                return GeneratedTileDefIds.Soil;
            if (moisture > 0.58f && blend > 0.62f)
                return GeneratedTileDefIds.Snow;
            return preferredSurfaceTileDefId ?? GeneratedTileDefIds.Grass;
        }

        if (string.Equals(biomeId, MacroBiomeIds.WindsweptSteppe, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.Savanna, StringComparison.OrdinalIgnoreCase))
        {
            if (moisture < 0.24f && blend < 0.40f)
                return GeneratedTileDefIds.Soil;
            if (terrain > 0.70f && blend < 0.36f)
                return GeneratedTileDefIds.StoneFloor;
            return preferredSurfaceTileDefId ?? GeneratedTileDefIds.Grass;
        }

        if (string.Equals(biomeId, MacroBiomeIds.TropicalRainforest, StringComparison.OrdinalIgnoreCase))
        {
            if (moisture > 0.66f && blend > 0.70f)
                return GeneratedTileDefIds.Mud;
            if (terrain > 0.72f && blend < 0.35f)
                return GeneratedTileDefIds.Soil;
            return preferredSurfaceTileDefId ?? GeneratedTileDefIds.Grass;
        }

        if (string.Equals(biomeId, MacroBiomeIds.ConiferForest, StringComparison.OrdinalIgnoreCase))
        {
            if (terrain > 0.68f && blend < 0.40f)
                return GeneratedTileDefIds.StoneFloor;
            if (moisture < 0.25f && blend < 0.35f)
                return GeneratedTileDefIds.Soil;
            return preferredSurfaceTileDefId ?? GeneratedTileDefIds.Grass;
        }

        if (string.Equals(biomeId, MacroBiomeIds.TemperatePlains, StringComparison.OrdinalIgnoreCase))
        {
            if (moisture < 0.24f && blend < 0.40f)
                return GeneratedTileDefIds.Soil;
            if (moisture > 0.64f && blend > 0.68f)
                return GeneratedTileDefIds.Mud;
            return preferredSurfaceTileDefId ?? GeneratedTileDefIds.Grass;
        }

        return preferredSurfaceTileDefId ?? currentTileDefId;
    }

    private static string ResolveSurfaceTileDefIdFromPreferredIntent(
        string preferredSurfaceTileDefId,
        string biomeId,
        bool useStoneSurface,
        float terrain,
        float moisture,
        float blend,
        string currentTileDefId)
    {
        var isAridBiome = string.Equals(biomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(biomeId, MacroBiomeIds.WindsweptSteppe, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(biomeId, MacroBiomeIds.Savanna, StringComparison.OrdinalIgnoreCase);
        var isColdBiome = string.Equals(biomeId, MacroBiomeIds.Tundra, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(biomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase);

        if (string.Equals(preferredSurfaceTileDefId, GeneratedTileDefIds.StoneFloor, StringComparison.OrdinalIgnoreCase))
        {
            if (!useStoneSurface && moisture > 0.76f && blend > 0.72f)
                return GeneratedTileDefIds.Mud;
            if (!useStoneSurface && moisture > 0.56f && blend > 0.60f)
                return GeneratedTileDefIds.Soil;
            return GeneratedTileDefIds.StoneFloor;
        }

        if (string.Equals(preferredSurfaceTileDefId, GeneratedTileDefIds.Sand, StringComparison.OrdinalIgnoreCase))
        {
            if (moisture > 0.80f && blend > 0.74f)
                return GeneratedTileDefIds.Mud;
            if (moisture > 0.58f && blend > 0.60f)
                return GeneratedTileDefIds.Soil;
            return GeneratedTileDefIds.Sand;
        }

        if (string.Equals(preferredSurfaceTileDefId, GeneratedTileDefIds.Mud, StringComparison.OrdinalIgnoreCase))
        {
            if (moisture < 0.24f && blend < 0.38f)
                return isAridBiome ? GeneratedTileDefIds.Sand : GeneratedTileDefIds.Soil;
            if (moisture < 0.32f && blend < 0.44f)
                return GeneratedTileDefIds.Soil;
            return GeneratedTileDefIds.Mud;
        }

        if (string.Equals(preferredSurfaceTileDefId, GeneratedTileDefIds.Snow, StringComparison.OrdinalIgnoreCase))
        {
            if (moisture > 0.78f && blend > 0.72f)
                return GeneratedTileDefIds.Mud;
            if (moisture > 0.58f && blend > 0.60f)
                return GeneratedTileDefIds.Soil;
            if (!isColdBiome && terrain > 0.72f && blend < 0.34f)
                return GeneratedTileDefIds.StoneFloor;
            return GeneratedTileDefIds.Snow;
        }

        if (string.Equals(preferredSurfaceTileDefId, GeneratedTileDefIds.Soil, StringComparison.OrdinalIgnoreCase))
        {
            if (moisture > 0.80f && blend > 0.72f)
                return GeneratedTileDefIds.Mud;
            if (terrain > 0.78f && blend < 0.34f)
                return GeneratedTileDefIds.StoneFloor;
            if (isAridBiome && moisture < 0.22f && blend < 0.46f)
                return GeneratedTileDefIds.Sand;
            return GeneratedTileDefIds.Soil;
        }

        return preferredSurfaceTileDefId ?? currentTileDefId;
    }

    private static string ResolveSurfaceMaterialId(string tileDefId, string? fallbackMaterialId)
    {
        return tileDefId switch
        {
            GeneratedTileDefIds.Sand => "sand",
            GeneratedTileDefIds.Mud => "mud",
            GeneratedTileDefIds.StoneFloor => string.IsNullOrWhiteSpace(fallbackMaterialId) ? "granite" : fallbackMaterialId!,
            _ => "soil",
        };
    }

    private static void ApplyRiparianSurfaceTransitions(GeneratedEmbarkMap map, string biomeId, int seed)
    {
        var isSnowBiome = string.Equals(biomeId, MacroBiomeIds.Tundra, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(biomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(biomeId, MacroBiomeIds.BorealForest, StringComparison.OrdinalIgnoreCase);
        var isAridBiome = string.Equals(biomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(biomeId, MacroBiomeIds.WindsweptSteppe, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(biomeId, MacroBiomeIds.Savanna, StringComparison.OrdinalIgnoreCase);
        var isMarshBiome = string.Equals(biomeId, MacroBiomeIds.MistyMarsh, StringComparison.OrdinalIgnoreCase);
        var isHighland = string.Equals(biomeId, MacroBiomeIds.Highland, StringComparison.OrdinalIgnoreCase);

        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (!tile.IsPassable || tile.FluidType != GeneratedFluidType.None)
                continue;
            if (tile.TileDefId is GeneratedTileDefIds.Water or GeneratedTileDefIds.Magma or GeneratedTileDefIds.Tree or GeneratedTileDefIds.Staircase)
                continue;

            if (!TryMeasureNearbyWater(map, x, y, maxRadius: 3, out var nearestDistance, out var nearbyWaterCount, out var maxNeighborWaterLevel))
                continue;
            if (nearestDistance > 2)
                continue;

            var fx = map.Width <= 1 ? 0f : x / (float)(map.Width - 1);
            var fy = map.Height <= 1 ? 0f : y / (float)(map.Height - 1);
            var rippleNoise = CoherentNoise.Fractal2D(
                seed, fx * 7.8f, fy * 7.8f, octaves: 2, lacunarity: 2f, gain: 0.5f, salt: 557);

            var shoreStrength = nearestDistance <= 1 ? 0.85f : 0.58f;
            var wetness = Math.Clamp(
                (shoreStrength * 0.52f) +
                ((nearbyWaterCount / 12f) * 0.28f) +
                ((maxNeighborWaterLevel / 7f) * 0.14f) +
                (rippleNoise * 0.06f), 0f, 1f);

            var targetTileDefId = ResolveRiparianTileDefId(
                tile.TileDefId,
                wetness,
                isSnowBiome,
                isAridBiome,
                isMarshBiome,
                isHighland);

            if (string.Equals(targetTileDefId, tile.TileDefId, StringComparison.OrdinalIgnoreCase))
                continue;

            map.SetTile(x, y, 0, new GeneratedTile(
                TileDefId: targetTileDefId,
                MaterialId: ResolveSurfaceMaterialId(targetTileDefId, tile.MaterialId),
                IsPassable: true));
        }
    }

    private static string ResolveRiparianTileDefId(
        string currentTileDefId,
        float wetness,
        bool isSnowBiome,
        bool isAridBiome,
        bool isMarshBiome,
        bool isHighland)
    {
        if (isMarshBiome)
            return wetness >= 0.38f ? GeneratedTileDefIds.Mud : GeneratedTileDefIds.Grass;

        if (isSnowBiome)
        {
            if (wetness >= 0.78f)
                return GeneratedTileDefIds.Mud;
            if (wetness >= 0.52f)
                return GeneratedTileDefIds.Soil;
            return GeneratedTileDefIds.Snow;
        }

        if (isAridBiome)
        {
            if (wetness >= 0.78f)
                return GeneratedTileDefIds.Mud;
            if (wetness >= 0.50f)
                return GeneratedTileDefIds.Soil;
            return GeneratedTileDefIds.Sand;
        }

        if (isHighland)
        {
            if (wetness >= 0.76f)
                return GeneratedTileDefIds.Soil;
            return currentTileDefId == GeneratedTileDefIds.StoneFloor
                ? GeneratedTileDefIds.StoneFloor
                : GeneratedTileDefIds.Grass;
        }

        if (wetness >= 0.76f)
            return GeneratedTileDefIds.Mud;
        if (wetness >= 0.52f)
            return GeneratedTileDefIds.Soil;
        return currentTileDefId;
    }

    private static bool TryMeasureNearbyWater(
        GeneratedEmbarkMap map,
        int x,
        int y,
        int maxRadius,
        out int nearestDistance,
        out int nearbyWaterCount,
        out byte maxNeighborWaterLevel)
    {
        nearestDistance = int.MaxValue;
        nearbyWaterCount = 0;
        maxNeighborWaterLevel = 0;

        for (var radius = 1; radius <= maxRadius; radius++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            for (var dy = -radius; dy <= radius; dy++)
            {
                var dist = Math.Abs(dx) + Math.Abs(dy);
                if (dist == 0 || dist > radius)
                    continue;

                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
                    continue;

                var neighbor = map.GetTile(nx, ny, 0);
                var isWater = neighbor.TileDefId == GeneratedTileDefIds.Water || neighbor.FluidType == GeneratedFluidType.Water;
                if (!isWater)
                    continue;

                nearbyWaterCount++;
                if (neighbor.FluidLevel > maxNeighborWaterLevel)
                    maxNeighborWaterLevel = neighbor.FluidLevel;
                if (dist < nearestDistance)
                    nearestDistance = dist;
            }
        }

        return nearbyWaterCount > 0;
    }

    private static string[] FillUnderground(GeneratedEmbarkMap map, StrataProfile profile, int seed)
    {
        var assignments = BuildStrataAssignments(map.Depth, profile, seed);
        for (var z = 1; z < map.Depth; z++)
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
            map.SetTile(x, y, z, WallTile(assignments[z - 1]));

        return assignments;
    }

    private static string[] BuildStrataAssignments(int depth, StrataProfile profile, int seed)
    {
        var strataDepth = Math.Max(0, depth - 1);
        var assignments = new string[strataDepth];
        if (strataDepth == 0 || profile.Layers.Count == 0)
            return assignments;

        var rng = new Random(SeedHash.Hash(seed, strataDepth, profile.SeedSalt, 6503));
        var write = 0;
        var layerIndex = 0;

        while (write < strataDepth)
        {
            var layer = profile.Layers[Math.Min(layerIndex, profile.Layers.Count - 1)];
            var min = Math.Max(1, layer.ThicknessMin);
            var max = Math.Max(min, layer.ThicknessMax);
            var thickness = Math.Min(strataDepth - write, rng.Next(min, max + 1));

            for (var i = 0; i < thickness; i++)
                assignments[write + i] = layer.RockTypeId;

            write += thickness;
            if (layerIndex < profile.Layers.Count - 1)
                layerIndex++;
        }

        return assignments;
    }

    private static void ApplyAquiferBand(GeneratedEmbarkMap map, StrataProfile profile)
    {
        if (map.Depth <= 1)
            return;

        var fraction = Math.Clamp(profile.AquiferDepthFraction, 0f, 1f);
        if (fraction <= 0f)
            return;

        var subterraneanDepth = map.Depth - 1;
        var layerIndex = (int)MathF.Round((subterraneanDepth - 1) * fraction);
        layerIndex = Math.Clamp(layerIndex, 0, subterraneanDepth - 1);
        var aquiferZ = layerIndex + 1;

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, aquiferZ);
            if (tile.IsPassable || tile.FluidType != GeneratedFluidType.None)
                continue;

            map.SetTile(x, y, aquiferZ, tile with { IsAquifer = true });
        }
    }

    private static void FillMagmaSea(GeneratedEmbarkMap map)
    {
        if (map.Depth < 12)
            return;

        var magmaZ = map.Depth - 1;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
            map.SetTile(x, y, magmaZ, MagmaSeaTile());
    }

    private static void AddCaveSystems(GeneratedEmbarkMap map, int seed, IReadOnlyList<int> caveLayers)
    {
        for (var i = 0; i < caveLayers.Count; i++)
        {
            var z = caveLayers[i];
            var layerSeed = SeedHash.Hash(seed, map.Width, map.Height, 17011 + (i * 97) + (z * 13));
            CarveCaveLayer(map, z, layerSeed);
        }
    }

    private static List<int> ResolveCaveLayerDepths(int depth)
    {
        var layers = new List<int>(3);
        if (depth < 12)
            return layers;

        var targetCount = depth >= 16 ? 3 : 2;
        var subterraneanDepth = depth - 1;
        var dedupe = new HashSet<int>();

        for (var i = 0; i < targetCount; i++)
        {
            var fraction = CaveDepthFractions[Math.Min(i, CaveDepthFractions.Length - 1)];
            var layerIndex = (int)MathF.Round((subterraneanDepth - 1) * fraction);
            layerIndex = Math.Clamp(layerIndex, 0, subterraneanDepth - 1);
            var z = layerIndex + 1;

            if (z <= 1 || z >= depth)
                continue;
            if (dedupe.Add(z))
                layers.Add(z);
        }

        layers.Sort();
        return layers;
    }

    private static void CarveCaveLayer(GeneratedEmbarkMap map, int z, int seed)
    {
        if (z <= 1 || z >= map.Depth)
            return;

        var mask = new bool[map.Width, map.Height];
        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            var fx = map.Width <= 1 ? 0f : x / (float)(map.Width - 1);
            var fy = map.Height <= 1 ? 0f : y / (float)(map.Height - 1);

            var baseNoise = CoherentNoise.DomainWarpedFractal2D(
                seed, fx * 3.1f, fy * 3.1f, octaves: 4, lacunarity: 2f, gain: 0.52f, warpStrength: 0.27f, salt: 401);
            var ridged = CoherentNoise.Ridged2D(
                seed, fx * 5.8f, fy * 5.8f, octaves: 3, lacunarity: 2f, gain: 0.58f, salt: 463);
            var value = (baseNoise * 0.74f) + (ridged * 0.26f);
            mask[x, y] = value >= 0.58f;
        }

        SmoothCaveMask(mask, map.Width, map.Height, passes: 2);
        var connectedMask = KeepLargestConnectedRegion(mask, map.Width, map.Height);
        CarveCaveCells(map, z, connectedMask);
    }

    private static void SmoothCaveMask(bool[,] mask, int width, int height, int passes)
    {
        for (var pass = 0; pass < passes; pass++)
        {
            var next = new bool[width, height];
            for (var x = 1; x < width - 1; x++)
            for (var y = 1; y < height - 1; y++)
            {
                var neighbours = CountOpenNeighbours(mask, x, y);
                next[x, y] = mask[x, y]
                    ? neighbours >= 4
                    : neighbours >= 5;
            }

            for (var x = 1; x < width - 1; x++)
            for (var y = 1; y < height - 1; y++)
                mask[x, y] = next[x, y];
        }
    }

    private static int CountOpenNeighbours(bool[,] mask, int x, int y)
    {
        var count = 0;
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0)
                continue;
            if (mask[x + dx, y + dy])
                count++;
        }

        return count;
    }

    private static bool[,] KeepLargestConnectedRegion(bool[,] mask, int width, int height)
    {
        var visited = new bool[width, height];
        var best = new List<int>();
        var queue = new Queue<(int X, int Y)>();
        var directions = new (int X, int Y)[]
        {
            (1, 0),
            (-1, 0),
            (0, 1),
            (0, -1),
        };

        for (var x = 1; x < width - 1; x++)
        for (var y = 1; y < height - 1; y++)
        {
            if (visited[x, y] || !mask[x, y])
                continue;

            var current = new List<int>();
            visited[x, y] = true;
            queue.Enqueue((x, y));

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                current.Add((cy * width) + cx);

                foreach (var dir in directions)
                {
                    var nx = cx + dir.X;
                    var ny = cy + dir.Y;
                    if (nx <= 0 || ny <= 0 || nx >= width - 1 || ny >= height - 1)
                        continue;
                    if (visited[nx, ny] || !mask[nx, ny])
                        continue;

                    visited[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }

            if (current.Count > best.Count)
                best = current;
        }

        var keep = new bool[width, height];
        if (best.Count == 0)
        {
            CarveFallbackCaveChamber(keep, width, height);
            return keep;
        }

        foreach (var index in best)
        {
            var x = index % width;
            var y = index / width;
            keep[x, y] = true;
        }

        return keep;
    }

    private static void CarveFallbackCaveChamber(bool[,] mask, int width, int height)
    {
        var cx = width / 2;
        var cy = height / 2;
        var radius = Math.Max(2, Math.Min(width, height) / 10);

        for (var x = cx - radius; x <= cx + radius; x++)
        for (var y = cy - radius; y <= cy + radius; y++)
        {
            if (x <= 0 || y <= 0 || x >= width - 1 || y >= height - 1)
                continue;
            if (Math.Abs(x - cx) + Math.Abs(y - cy) > radius + 1)
                continue;

            mask[x, y] = true;
        }
    }

    private static void CarveCaveCells(GeneratedEmbarkMap map, int z, bool[,] mask)
    {
        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            if (!mask[x, y])
                continue;

            var tile = map.GetTile(x, y, z);
            var floorMaterial = string.IsNullOrWhiteSpace(tile.MaterialId)
                ? RockTypeIds.Granite
                : tile.MaterialId;

            map.SetTile(
                x,
                y,
                z,
                new GeneratedTile(
                    TileDefId: GeneratedTileDefIds.StoneFloor,
                    MaterialId: floorMaterial,
                    IsPassable: true,
                    FluidType: GeneratedFluidType.None,
                    FluidLevel: 0,
                    OreId: null,
                    IsAquifer: false));
        }
    }

    private static void AddMineralVeins(
        GeneratedEmbarkMap map,
        StrataProfile profile,
        string[] strataAssignments,
        int seed)
    {
        if (map.Depth <= 1 || strataAssignments.Length == 0)
            return;

        var veinDefs = MineralVeinRegistry.Resolve(profile.GeologyProfileId);
        if (veinDefs.Count == 0)
            return;

        var areaScale = (map.Width * map.Height) / 1024f;
        var rng = new Random(SeedHash.Hash(seed, profile.SeedSalt, map.Width * map.Height, 9331));

        foreach (var def in veinDefs)
        {
            var candidateDepths = CollectCandidateDepths(strataAssignments, def.RequiredRockType);
            if (candidateDepths.Count == 0)
                continue;

            var expected = Math.Max(0f, def.Frequency * candidateDepths.Count * Math.Max(0.5f, areaScale));
            var veinCount = (int)MathF.Floor(expected);
            if (rng.NextDouble() < expected - veinCount)
                veinCount++;

            for (var i = 0; i < veinCount; i++)
            {
                var z = candidateDepths[rng.Next(candidateDepths.Count)];
                var x = rng.Next(1, map.Width - 1);
                var y = rng.Next(1, map.Height - 1);
                var size = rng.Next(Math.Max(1, def.SizeMin), Math.Max(def.SizeMin, def.SizeMax) + 1);
                PlaceVein(map, rng, def, x, y, z, size);
            }
        }
    }

    private static List<int> CollectCandidateDepths(string[] strataAssignments, string rockTypeId)
    {
        var depths = new List<int>(strataAssignments.Length);
        for (var i = 0; i < strataAssignments.Length; i++)
        {
            if (string.Equals(strataAssignments[i], rockTypeId, StringComparison.OrdinalIgnoreCase))
                depths.Add(i + 1);
        }

        return depths;
    }

    private static void PlaceVein(
        GeneratedEmbarkMap map,
        Random rng,
        MineralVeinDef def,
        int startX,
        int startY,
        int z,
        int size)
    {
        switch (def.Shape)
        {
            case VeinShape.Layer:
                PlaceLayerVein(map, rng, def, startX, startY, z, size);
                break;
            case VeinShape.Vein:
                PlaceLineVein(map, rng, def, startX, startY, z, size);
                break;
            case VeinShape.Scattered:
                PlaceScatteredVein(map, rng, def, startX, startY, z, size);
                break;
            default:
                PlaceClusterVein(map, rng, def, startX, startY, z, size);
                break;
        }
    }

    private static void PlaceClusterVein(
        GeneratedEmbarkMap map,
        Random rng,
        MineralVeinDef def,
        int x,
        int y,
        int z,
        int size)
    {
        var cx = x;
        var cy = y;
        for (var i = 0; i < size; i++)
        {
            SetOreTile(map, def, cx, cy, z);
            if (rng.NextDouble() < 0.35)
                SetOreTile(map, def, cx + rng.Next(-1, 2), cy + rng.Next(-1, 2), z);

            cx = Math.Clamp(cx + rng.Next(-1, 2), 1, map.Width - 2);
            cy = Math.Clamp(cy + rng.Next(-1, 2), 1, map.Height - 2);
        }
    }

    private static void PlaceLayerVein(
        GeneratedEmbarkMap map,
        Random rng,
        MineralVeinDef def,
        int x,
        int y,
        int z,
        int size)
    {
        var horizontal = rng.NextDouble() < 0.5;
        var thickness = 1 + rng.Next(0, 2);
        if (horizontal)
        {
            for (var dx = -size / 2; dx <= size / 2; dx++)
            for (var dy = 0; dy < thickness; dy++)
                SetOreTile(map, def, x + dx, y + dy, z);
            return;
        }

        for (var dy = -size / 2; dy <= size / 2; dy++)
        for (var dx = 0; dx < thickness; dx++)
            SetOreTile(map, def, x + dx, y + dy, z);
    }

    private static void PlaceLineVein(
        GeneratedEmbarkMap map,
        Random rng,
        MineralVeinDef def,
        int x,
        int y,
        int z,
        int size)
    {
        var dx = rng.Next(-1, 2);
        var dy = rng.Next(-1, 2);
        if (dx == 0 && dy == 0)
            dx = 1;

        var cx = x;
        var cy = y;
        for (var i = 0; i < size; i++)
        {
            SetOreTile(map, def, cx, cy, z);
            if (rng.NextDouble() < 0.22)
                SetOreTile(map, def, cx + rng.Next(-1, 2), cy + rng.Next(-1, 2), z);

            cx = Math.Clamp(cx + dx, 1, map.Width - 2);
            cy = Math.Clamp(cy + dy, 1, map.Height - 2);

            if (rng.NextDouble() < 0.18)
            {
                dx = Math.Clamp(dx + rng.Next(-1, 2), -1, 1);
                dy = Math.Clamp(dy + rng.Next(-1, 2), -1, 1);
                if (dx == 0 && dy == 0)
                    dx = 1;
            }
        }
    }

    private static void PlaceScatteredVein(
        GeneratedEmbarkMap map,
        Random rng,
        MineralVeinDef def,
        int x,
        int y,
        int z,
        int size)
    {
        var radius = Math.Max(2, (int)MathF.Round(MathF.Sqrt(size)));
        var attempts = size * 3;
        for (var i = 0; i < attempts; i++)
        {
            var sx = Math.Clamp(x + rng.Next(-radius, radius + 1), 1, map.Width - 2);
            var sy = Math.Clamp(y + rng.Next(-radius, radius + 1), 1, map.Height - 2);
            SetOreTile(map, def, sx, sy, z);
        }
    }

    private static void SetOreTile(GeneratedEmbarkMap map, MineralVeinDef def, int x, int y, int z)
    {
        if (x < 0 || y < 0 || z <= 0 || x >= map.Width || y >= map.Height || z >= map.Depth)
            return;

        var tile = map.GetTile(x, y, z);
        if (tile.IsPassable || tile.FluidType != GeneratedFluidType.None)
            return;
        if (!string.Equals(tile.MaterialId, def.RequiredRockType, StringComparison.OrdinalIgnoreCase))
            return;
        if (tile.OreId is not null)
            return;
        if (!MineralVeinRegistry.IsOreCompatible(def.OreId, tile.MaterialId))
            return;

        map.SetTile(x, y, z, tile with { OreId = def.OreId });
    }

    private static float[,] BuildSurfaceTerrainMap(
        int width,
        int height,
        int seed,
        BiomeGenerationProfile biome,
        int noiseOriginX,
        int noiseOriginY)
    {
        var map = new float[width, height];
        var biomeRuggedness = Math.Clamp(biome.TerrainRuggedness, 0f, 1f);

        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            var fx = ResolveNoiseSampleCoord(noiseOriginX, x, width);
            var fy = ResolveNoiseSampleCoord(noiseOriginY, y, height);
            var baseTerrain = CoherentNoise.DomainWarpedFractal2D(
                seed, fx * 2.2f, fy * 2.2f, octaves: 4, lacunarity: 2f, gain: 0.52f, warpStrength: 0.25f, salt: 91);
            var detail = CoherentNoise.Fractal2D(
                seed, fx * 7.6f, fy * 7.6f, octaves: 3, lacunarity: 2f, gain: 0.5f, salt: 131);
            var ridges = CoherentNoise.Ridged2D(
                seed, fx * 4.8f, fy * 4.8f, octaves: 3, lacunarity: 2f, gain: 0.55f, salt: 173);

            map[x, y] = Math.Clamp(
                (baseTerrain * 0.60f) +
                (detail * 0.20f) +
                (ridges * 0.20f * biomeRuggedness), 0f, 1f);
        }

        return map;
    }

    private static float ResolveNoiseSampleCoord(int noiseOrigin, int localCoord, int extent)
    {
        var safeExtent = Math.Max(1, extent - 1);
        return (noiseOrigin + localCoord) / (float)safeExtent;
    }

    private static float[,] BuildSurfaceMoistureMap(
        int width,
        int height,
        int seed,
        float[,] terrain,
        BiomeGenerationProfile biome,
        float wetnessBias,
        float soilDepthBias,
        int noiseOriginX,
        int noiseOriginY)
    {
        var map = new float[width, height];
        var biomeWetness = Math.Clamp(biome.BaseMoisture, 0f, 1f);

        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            var fx = ResolveNoiseSampleCoord(noiseOriginX, x, width);
            var fy = ResolveNoiseSampleCoord(noiseOriginY, y, height);
            var wetNoise = CoherentNoise.DomainWarpedFractal2D(
                seed, fx * 2.8f, fy * 2.8f, octaves: 3, lacunarity: 2f, gain: 0.5f, warpStrength: 0.2f, salt: 251);

            var runoffPenalty = Math.Max(0f, terrain[x, y] - 0.62f) * Math.Max(0f, -soilDepthBias) * 0.18f;
            map[x, y] = Math.Clamp(
                (wetNoise * 0.65f) +
                ((1f - terrain[x, y]) * 0.20f) +
                (biomeWetness * 0.15f) +
                (wetnessBias * 0.22f) +
                (soilDepthBias * 0.10f) -
                runoffPenalty, 0f, 1f);
        }

        return map;
    }

    private static float[,] BuildCanopyMaskMap(int width, int height, int seed, int noiseOriginX, int noiseOriginY)
    {
        var map = new float[width, height];
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            var fx = ResolveNoiseSampleCoord(noiseOriginX, x, width);
            var fy = ResolveNoiseSampleCoord(noiseOriginY, y, height);
            map[x, y] = CoherentNoise.DomainWarpedFractal2D(
                seed, fx * 1.9f, fy * 1.9f, octaves: 3, lacunarity: 2f, gain: 0.55f, warpStrength: 0.35f, salt: 331);
        }

        return map;
    }

    private static float[,] BuildForestPatchMaskMap(
        int width,
        int height,
        int seed,
        float continuityBias,
        int noiseOriginX,
        int noiseOriginY)
    {
        var map = new float[width, height];
        var clampedBias = Math.Clamp(continuityBias, -1f, 1f);
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            var fx = ResolveNoiseSampleCoord(noiseOriginX, x, width);
            var fy = ResolveNoiseSampleCoord(noiseOriginY, y, height);

            var patchNoise = CoherentNoise.DomainWarpedFractal2D(
                seed, fx * 1.15f, fy * 1.15f, octaves: 4, lacunarity: 2f, gain: 0.56f, warpStrength: 0.40f, salt: 353);
            var clearingNoise = CoherentNoise.Ridged2D(
                seed, fx * 2.70f, fy * 2.70f, octaves: 3, lacunarity: 2f, gain: 0.58f, salt: 397);
            var macroNoise = CoherentNoise.DomainWarpedFractal2D(
                seed, fx * 0.52f, fy * 0.52f, octaves: 3, lacunarity: 2f, gain: 0.5f, warpStrength: 0.30f, salt: 433);

            var patch = Math.Clamp(
                (patchNoise * 0.82f) +
                ((1f - clearingNoise) * 0.18f), 0f, 1f);
            var coherentPatch = Math.Clamp(
                (patch * 0.60f) +
                (macroNoise * 0.40f), 0f, 1f);
            var fragmentationBias = Math.Max(0f, -clampedBias) * 0.10f;

            map[x, y] = Math.Clamp(
                coherentPatch +
                (clampedBias * 0.18f) -
                fragmentationBias, 0f, 1f);
        }

        var smoothBlend = 0.26f + (Math.Max(0f, clampedBias) * 0.26f);
        var smoothPasses = clampedBias >= 0.30f ? 2 : 1;
        SmoothScalarMask(map, width, height, smoothPasses, smoothBlend);
        return map;
    }

    private static float[,] BuildForestOpeningMaskMap(int width, int height, int seed, int noiseOriginX, int noiseOriginY)
    {
        var map = new float[width, height];
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            var fx = ResolveNoiseSampleCoord(noiseOriginX, x, width);
            var fy = ResolveNoiseSampleCoord(noiseOriginY, y, height);

            var coarse = CoherentNoise.DomainWarpedFractal2D(
                seed, fx * 1.85f, fy * 1.85f, octaves: 3, lacunarity: 2f, gain: 0.54f, warpStrength: 0.28f, salt: 991);
            var laneSource = CoherentNoise.DomainWarpedFractal2D(
                seed, fx * 1.10f, fy * 1.10f, octaves: 2, lacunarity: 2f, gain: 0.50f, warpStrength: 0.18f, salt: 1021);
            var lanes = 1f - MathF.Abs((laneSource * 2f) - 1f);
            var detail = CoherentNoise.Fractal2D(
                seed, fx * 4.6f, fy * 4.6f, octaves: 2, lacunarity: 2f, gain: 0.50f, salt: 1039);

            map[x, y] = Math.Clamp((coarse * 0.54f) + (lanes * 0.34f) + (detail * 0.12f), 0f, 1f);
        }

        SmoothPlantNoise(map, width, height, passes: 2);
        return map;
    }

    private static void SmoothScalarMask(float[,] map, int width, int height, int passes, float blend)
    {
        if (passes <= 0 || blend <= 0f)
            return;

        var clampedBlend = Math.Clamp(blend, 0f, 1f);
        var scratch = new float[width, height];

        for (var pass = 0; pass < passes; pass++)
        {
            for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
            {
                var weighted = map[x, y] * 1.50f;
                var weight = 1.50f;

                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;

                    var isCardinal = dx == 0 || dy == 0;
                    var sampleWeight = isCardinal ? 0.85f : 0.58f;
                    weighted += map[nx, ny] * sampleWeight;
                    weight += sampleWeight;
                }

                var neighborhood = weight <= 0f ? map[x, y] : (weighted / weight);
                scratch[x, y] = Math.Clamp(
                    (map[x, y] * (1f - clampedBlend)) +
                    (neighborhood * clampedBlend), 0f, 1f);
            }

            for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
                map[x, y] = scratch[x, y];
        }
    }

    private static void AddStreams(GeneratedEmbarkMap map, Random rng, int bands, float[,] terrain)
    {
        if (bands <= 0)
            return;

        var maxSteps = Math.Max(8, map.Width + map.Height);
        for (var i = 0; i < bands; i++)
        {
            if (!TryPickStreamSource(map, terrain, rng, out var sourceX, out var sourceY))
                continue;

            var path = TraceDownhillPath(map, terrain, rng, sourceX, sourceY, maxSteps);
            if (path.Count == 0)
                continue;

            foreach (var (x, y) in path)
            {
                CarveWater(map, x, y, (byte)(2 + (i % 2)));
                if (i > 0)
                {
                    // Secondary channels are shallower and narrower.
                    if (rng.NextDouble() < 0.35)
                    {
                        var (dx, dy) = CardinalDirections[rng.Next(CardinalDirections.Length)];
                        CarveWater(map, x + dx, y + dy, 1);
                    }
                    if (rng.NextDouble() < 0.35)
                    {
                        var (dx, dy) = CardinalDirections[rng.Next(CardinalDirections.Length)];
                        CarveWater(map, x + dx, y + dy, 1);
                    }
                }
            }
        }
    }

    private static void AddAnchoredStreams(
        GeneratedEmbarkMap map,
        Random rng,
        float[,] terrain,
        LocalRiverPortal[] portals)
    {
        if (portals.Length == 0)
            return;

        var points = new List<AnchoredPoint>(portals.Length);
        var dedupe = new Dictionary<int, int>(portals.Length);

        foreach (var portal in portals)
        {
            var point = ResolvePortalCell(map, portal);
            var key = (point.Y * map.Width) + point.X;
            var strength = (byte)Math.Clamp((int)portal.Strength, 1, 8);

            if (dedupe.TryGetValue(key, out var existing))
            {
                if (strength > points[existing].Strength)
                    points[existing] = new AnchoredPoint(point.X, point.Y, strength);
                continue;
            }

            dedupe[key] = points.Count;
            points.Add(new AnchoredPoint(point.X, point.Y, strength));
        }

        if (points.Count == 0)
            return;

        if (points.Count == 1)
        {
            var lowland = FindLowlandTarget(map, terrain);
            CarveAnchoredPath(map, terrain, points[0], lowland, points[0].Strength);
            return;
        }

        var paired = new bool[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            if (paired[i])
                continue;

            var partner = FindNearestUnpaired(points, paired, i);
            if (partner < 0)
            {
                var lowland = FindLowlandTarget(map, terrain);
                CarveAnchoredPath(map, terrain, points[i], lowland, points[i].Strength);
                paired[i] = true;
                continue;
            }

            var strength = (byte)Math.Max(points[i].Strength, points[partner].Strength);
            CarveAnchoredPath(map, terrain, points[i], points[partner], strength);
            paired[i] = true;
            paired[partner] = true;
        }
    }

    private static (int X, int Y) ResolvePortalCell(GeneratedEmbarkMap map, LocalRiverPortal portal)
    {
        var offset = Math.Clamp(portal.NormalizedOffset, 0f, 1f);
        var interiorWidth = Math.Max(1, map.Width - 3);
        var interiorHeight = Math.Max(1, map.Height - 3);
        var x = 1 + (int)MathF.Round(offset * interiorWidth);
        var y = 1 + (int)MathF.Round(offset * interiorHeight);

        x = Math.Clamp(x, 1, map.Width - 2);
        y = Math.Clamp(y, 1, map.Height - 2);

        return portal.Edge switch
        {
            LocalMapEdge.North => (x, 1),
            LocalMapEdge.East => (map.Width - 2, y),
            LocalMapEdge.South => (x, map.Height - 2),
            LocalMapEdge.West => (1, y),
            _ => (x, y),
        };
    }

    private static int FindNearestUnpaired(
        List<AnchoredPoint> points,
        bool[] paired,
        int index)
    {
        var source = points[index];
        var best = -1;
        var bestDist = int.MaxValue;

        for (var i = 0; i < points.Count; i++)
        {
            if (i == index || paired[i])
                continue;

            var target = points[i];
            var dist = Math.Abs(target.X - source.X) + Math.Abs(target.Y - source.Y);
            if (dist >= bestDist)
                continue;

            bestDist = dist;
            best = i;
        }

        return best;
    }

    private static (int X, int Y) FindLowlandTarget(GeneratedEmbarkMap map, float[,] terrain)
    {
        var bestX = map.Width / 2;
        var bestY = map.Height / 2;
        var best = float.MaxValue;

        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                continue;

            var value = terrain[x, y];
            if (value >= best)
                continue;

            best = value;
            bestX = x;
            bestY = y;
        }

        return (bestX, bestY);
    }

    private static void CarveAnchoredPath(
        GeneratedEmbarkMap map,
        float[,] terrain,
        AnchoredPoint source,
        (int X, int Y) target,
        byte strength)
    {
        var sourceCoord = (source.X, source.Y);
        var path = FindAnchoredPath(map, terrain, sourceCoord, target);
        if (path.Count == 0)
            path = BuildFallbackAnchoredCorridor(map, sourceCoord, target);

        foreach (var (x, y) in path)
            CarveAnchoredWater(map, x, y, strength);
    }

    private static void CarveAnchoredPath(
        GeneratedEmbarkMap map,
        float[,] terrain,
        AnchoredPoint source,
        AnchoredPoint target,
        byte strength)
    {
        CarveAnchoredPath(map, terrain, source, (target.X, target.Y), strength);
    }

    private static void CarveAnchoredWater(GeneratedEmbarkMap map, int x, int y, byte strength)
    {
        var clampedStrength = Math.Clamp(strength, (byte)1, (byte)8);
        var radius = clampedStrength switch
        {
            >= 6 => 2,
            >= 3 => 1,
            _ => 0,
        };
        var centerLevel = (byte)Math.Clamp(1 + clampedStrength, 2, 7);

        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            var distance = Math.Abs(dx) + Math.Abs(dy);
            if (distance > radius)
                continue;

            var level = (byte)Math.Clamp(centerLevel - distance, 1, 7);
            CarveWater(map, x + dx, y + dy, level);
        }
    }

    private static void ApplySurfaceWaterMoistureFeedback(GeneratedEmbarkMap map, float[,] moisture)
    {
        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId != GeneratedTileDefIds.Water)
                continue;

            var levelNorm = tile.FluidLevel / 7f;
            var radius = tile.FluidLevel switch
            {
                >= 5 => 3,
                >= 3 => 2,
                _ => 1,
            };

            for (var dx = -radius; dx <= radius; dx++)
            for (var dy = -radius; dy <= radius; dy++)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx <= 0 || ny <= 0 || nx >= map.Width - 1 || ny >= map.Height - 1)
                    continue;

                var distance = Math.Abs(dx) + Math.Abs(dy);
                if (distance > radius)
                    continue;

                var falloff = 1f - (distance / (float)(radius + 1));
                var boost = (0.16f + (levelNorm * 0.24f)) * falloff;
                moisture[nx, ny] = Math.Clamp(moisture[nx, ny] + boost, 0f, 1f);
            }
        }
    }

    private static List<(int X, int Y)> FindAnchoredPath(
        GeneratedEmbarkMap map,
        float[,] terrain,
        (int X, int Y) source,
        (int X, int Y) target)
    {
        if (source.X <= 0 || source.Y <= 0 || source.X >= map.Width - 1 || source.Y >= map.Height - 1)
            return [];
        if (target.X <= 0 || target.Y <= 0 || target.X >= map.Width - 1 || target.Y >= map.Height - 1)
            return [];

        var width = map.Width;
        var height = map.Height;
        var gScore = new float[width, height];
        var fScore = new float[width, height];
        var open = new bool[width, height];
        var closed = new bool[width, height];
        var cameFromX = new int[width, height];
        var cameFromY = new int[width, height];
        var openList = new List<(int X, int Y)>(width + height);

        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            gScore[x, y] = float.MaxValue;
            fScore[x, y] = float.MaxValue;
            cameFromX[x, y] = -1;
            cameFromY[x, y] = -1;
        }

        gScore[source.X, source.Y] = 0f;
        fScore[source.X, source.Y] = Heuristic(source.X, source.Y, target.X, target.Y);
        open[source.X, source.Y] = true;
        openList.Add(source);

        var neighbors = new (int X, int Y)[]
        {
            (1, 0),
            (-1, 0),
            (0, 1),
            (0, -1),
        };

        while (openList.Count > 0)
        {
            var bestIndex = 0;
            var current = openList[0];
            var bestScore = fScore[current.X, current.Y];
            for (var i = 1; i < openList.Count; i++)
            {
                var candidate = openList[i];
                var candidateScore = fScore[candidate.X, candidate.Y];
                if (candidateScore >= bestScore)
                    continue;

                bestScore = candidateScore;
                bestIndex = i;
                current = candidate;
            }

            openList.RemoveAt(bestIndex);
            open[current.X, current.Y] = false;
            if (current.X == target.X && current.Y == target.Y)
                return ReconstructPath(source, target, cameFromX, cameFromY);

            closed[current.X, current.Y] = true;

            foreach (var (dx, dy) in neighbors)
            {
                var nx = current.X + dx;
                var ny = current.Y + dy;
                if (nx <= 0 || ny <= 0 || nx >= width - 1 || ny >= height - 1)
                    continue;
                if (closed[nx, ny])
                    continue;
                if (IsInCentralEmbarkZone(width, height, nx, ny) && (nx != target.X || ny != target.Y))
                    continue;

                var borderPenalty = (nx == 1 || ny == 1 || nx == width - 2 || ny == height - 2) ? 0.12f : 0f;
                var tentativeG = gScore[current.X, current.Y] + 1f + (terrain[nx, ny] * 0.80f) + borderPenalty;
                if (tentativeG >= gScore[nx, ny])
                    continue;

                cameFromX[nx, ny] = current.X;
                cameFromY[nx, ny] = current.Y;
                gScore[nx, ny] = tentativeG;
                fScore[nx, ny] = tentativeG + Heuristic(nx, ny, target.X, target.Y);

                if (open[nx, ny])
                    continue;

                open[nx, ny] = true;
                openList.Add((nx, ny));
            }
        }

        return [];
    }

    private static List<(int X, int Y)> BuildFallbackAnchoredCorridor(
        GeneratedEmbarkMap map,
        (int X, int Y) source,
        (int X, int Y) target)
    {
        var path = new List<(int X, int Y)>(Math.Max(map.Width, map.Height));
        var (centerMinX, centerMaxX, centerMinY, centerMaxY) = ResolveCentralEmbarkBounds(map.Width, map.Height);

        var intersectsCenter =
            source.X >= centerMinX && source.X <= centerMaxX &&
            target.X >= centerMinX && target.X <= centerMaxX &&
            Math.Min(source.Y, target.Y) <= centerMaxY &&
            Math.Max(source.Y, target.Y) >= centerMinY;

        if (!intersectsCenter)
        {
            AppendManhattanPath(path, source, target);
            return path;
        }

        var leftBypassX = Math.Max(1, centerMinX - 1);
        var rightBypassX = Math.Min(map.Width - 2, centerMaxX + 1);
        var leftCost = Math.Abs(source.X - leftBypassX) + Math.Abs(target.X - leftBypassX);
        var rightCost = Math.Abs(source.X - rightBypassX) + Math.Abs(target.X - rightBypassX);
        var bypassX = leftCost <= rightCost ? leftBypassX : rightBypassX;

        var legA = (X: bypassX, Y: source.Y);
        var legB = (X: bypassX, Y: target.Y);
        AppendManhattanPath(path, source, legA);
        AppendManhattanPath(path, legA, legB);
        AppendManhattanPath(path, legB, target);
        return path;
    }

    private static void AppendManhattanPath(
        List<(int X, int Y)> path,
        (int X, int Y) from,
        (int X, int Y) to)
    {
        var x = from.X;
        var y = from.Y;
        AddPathPoint(path, x, y);

        while (x != to.X)
        {
            x += x < to.X ? 1 : -1;
            AddPathPoint(path, x, y);
        }

        while (y != to.Y)
        {
            y += y < to.Y ? 1 : -1;
            AddPathPoint(path, x, y);
        }
    }

    private static void AddPathPoint(List<(int X, int Y)> path, int x, int y)
    {
        if (path.Count == 0 || path[^1] != (x, y))
            path.Add((x, y));
    }

    private static float Heuristic(int x, int y, int targetX, int targetY)
        => Math.Abs(x - targetX) + Math.Abs(y - targetY);

    private static List<(int X, int Y)> ReconstructPath(
        (int X, int Y) source,
        (int X, int Y) target,
        int[,] cameFromX,
        int[,] cameFromY)
    {
        var path = new List<(int X, int Y)>(64);
        var currentX = target.X;
        var currentY = target.Y;
        path.Add((currentX, currentY));

        while (currentX != source.X || currentY != source.Y)
        {
            var parentX = cameFromX[currentX, currentY];
            var parentY = cameFromY[currentX, currentY];
            if (parentX < 0 || parentY < 0)
                return [];

            currentX = parentX;
            currentY = parentY;
            path.Add((currentX, currentY));
        }

        path.Reverse();
        return path;
    }

    private static bool TryPickStreamSource(GeneratedEmbarkMap map, float[,] terrain, Random rng, out int sourceX, out int sourceY)
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var x = rng.Next(1, map.Width - 1);
            var y = rng.Next(1, map.Height - 1);
            if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                continue;
            if (terrain[x, y] < 0.60f)
                continue;

            sourceX = x;
            sourceY = y;
            return true;
        }

        // Fallback to highest terrain cell outside embark center.
        var bestX = -1;
        var bestY = -1;
        var bestTerrain = float.MinValue;
        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                continue;
            if (terrain[x, y] <= bestTerrain)
                continue;

            bestTerrain = terrain[x, y];
            bestX = x;
            bestY = y;
        }

        sourceX = bestX;
        sourceY = bestY;
        return bestX >= 0 && bestY >= 0;
    }

    private static List<(int X, int Y)> TraceDownhillPath(
        GeneratedEmbarkMap map,
        float[,] terrain,
        Random rng,
        int sourceX,
        int sourceY,
        int maxSteps)
    {
        var path = new List<(int X, int Y)>(maxSteps);
        var visited = new bool[map.Width, map.Height];

        var x = sourceX;
        var y = sourceY;
        var prevX = sourceX;
        var prevY = sourceY;

        for (var step = 0; step < maxSteps; step++)
        {
            if (x <= 0 || y <= 0 || x >= map.Width - 1 || y >= map.Height - 1)
                break;
            if (visited[x, y])
                break;

            visited[x, y] = true;
            path.Add((x, y));

            var bestX = x;
            var bestY = y;
            var bestScore = float.MaxValue;

            foreach (var (dx, dy) in CardinalDirections)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx <= 0 || ny <= 0 || nx >= map.Width - 1 || ny >= map.Height - 1)
                    continue;
                if (IsInCentralEmbarkZone(map.Width, map.Height, nx, ny))
                    continue;
                if (visited[nx, ny])
                    continue;

                var turnPenalty = (nx == prevX && ny == prevY) ? 0.08f : 0f;
                var jitter = (float)rng.NextDouble() * 0.015f;
                var score = terrain[nx, ny] + turnPenalty + jitter;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestX = nx;
                    bestY = ny;
                }
            }

            if (bestX == x && bestY == y)
                break;

            prevX = x;
            prevY = y;
            x = bestX;
            y = bestY;
        }

        return path;
    }

    private static void CarveWater(GeneratedEmbarkMap map, int x, int y, byte level, int z = 0)
    {
        if (x <= 0 || y <= 0 || x >= map.Width - 1 || y >= map.Height - 1)
            return;
        if (z < 0 || z >= map.Depth)
            return;
        if (z == 0 && IsInCentralEmbarkZone(map.Width, map.Height, x, y))
            return;
        var tile = map.GetTile(x, y, z);
        if (tile.TileDefId == GeneratedTileDefIds.Magma || tile.FluidType == GeneratedFluidType.Magma)
            return;

        map.SetTile(x, y, z, ShallowWaterTile(level));
    }

    private static void FloodOceanSurface(GeneratedEmbarkMap map, Random rng, string biomeId, float[,] terrain)
    {
        if (!IsOceanBiome(biomeId))
            return;

        var deep = string.Equals(biomeId, MacroBiomeIds.OceanDeep, StringComparison.OrdinalIgnoreCase);
        var seaLevel = deep ? 0.78f : 0.68f;

        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                continue;

            var threshold = seaLevel + ((float)rng.NextDouble() * 0.08f) - 0.04f;
            if (terrain[x, y] > threshold)
                continue;

            var level = deep
                ? (byte)(4 + rng.Next(0, 3))
                : (byte)(2 + rng.Next(0, 3));
            map.SetTile(x, y, 0, ShallowWaterTile(level));
        }
    }

    private static void HarmonizeSurfaceWaterDepths(GeneratedEmbarkMap map, float[,] terrain, string biomeId)
    {
        var visited = new bool[map.Width, map.Height];
        var queue = new Queue<(int X, int Y)>();
        var component = new List<(int X, int Y)>(128);
        var isOceanBiome = IsOceanBiome(biomeId);

        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            if (visited[x, y] || !IsSurfaceWater(map, x, y))
                continue;

            component.Clear();
            visited[x, y] = true;
            queue.Enqueue((x, y));

            var touchesNorth = false;
            var touchesSouth = false;
            var touchesWest = false;
            var touchesEast = false;
            byte maxLevel = 0;

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                component.Add((cx, cy));

                if (cx <= 1) touchesWest = true;
                if (cx >= map.Width - 2) touchesEast = true;
                if (cy <= 1) touchesNorth = true;
                if (cy >= map.Height - 2) touchesSouth = true;

                var level = map.GetTile(cx, cy, 0).FluidLevel;
                if (level > maxLevel)
                    maxLevel = level;

                foreach (var (dx, dy) in CardinalDirections)
                {
                    var nx = cx + dx;
                    var ny = cy + dy;
                    if (nx <= 0 || ny <= 0 || nx >= map.Width - 1 || ny >= map.Height - 1)
                        continue;
                    if (visited[nx, ny] || !IsSurfaceWater(map, nx, ny))
                        continue;

                    visited[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }

            if (component.Count <= 1)
                continue;

            var edgeTouchCount =
                (touchesNorth ? 1 : 0) +
                (touchesEast ? 1 : 0) +
                (touchesSouth ? 1 : 0) +
                (touchesWest ? 1 : 0);
            var traversesMap =
                (touchesNorth && touchesSouth) ||
                (touchesEast && touchesWest);
            var likelyRiver =
                !isOceanBiome &&
                (traversesMap ||
                 (edgeTouchCount >= 1 && maxLevel >= 5 && component.Count >= 12));

            SmoothSurfaceWaterComponent(map, terrain, component, likelyRiver, isOceanBiome);
        }
    }

    private static void SmoothSurfaceWaterComponent(
        GeneratedEmbarkMap map,
        float[,] terrain,
        List<(int X, int Y)> cells,
        bool isLikelyRiver,
        bool isOceanBiome)
    {
        if (cells.Count == 0)
            return;

        var keyToIndex = new Dictionary<int, int>(cells.Count);
        for (var i = 0; i < cells.Count; i++)
        {
            var (x, y) = cells[i];
            keyToIndex[(y * map.Width) + x] = i;
        }

        var shorelineDistance = new int[cells.Count];
        Array.Fill(shorelineDistance, -1);
        var queue = new Queue<int>(cells.Count);

        for (var i = 0; i < cells.Count; i++)
        {
            var (x, y) = cells[i];
            if (!IsSurfaceShorelineCell(map, x, y))
                continue;

            shorelineDistance[i] = 0;
            queue.Enqueue(i);
        }

        if (queue.Count == 0)
        {
            for (var i = 0; i < shorelineDistance.Length; i++)
                shorelineDistance[i] = 0;
        }
        else
        {
            while (queue.Count > 0)
            {
                var idx = queue.Dequeue();
                var (x, y) = cells[idx];
                var nextDistance = shorelineDistance[idx] + 1;

                foreach (var (dx, dy) in CardinalDirections)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx <= 0 || ny <= 0 || nx >= map.Width - 1 || ny >= map.Height - 1)
                        continue;
                    if (!keyToIndex.TryGetValue((ny * map.Width) + nx, out var neighborIdx))
                        continue;
                    if (shorelineDistance[neighborIdx] >= 0 && shorelineDistance[neighborIdx] <= nextDistance)
                        continue;

                    shorelineDistance[neighborIdx] = nextDistance;
                    queue.Enqueue(neighborIdx);
                }
            }
        }

        var smoothed = new byte[cells.Count];
        for (var i = 0; i < cells.Count; i++)
        {
            var (x, y) = cells[i];
            var tile = map.GetTile(x, y, 0);
            var currentLevel = tile.FluidLevel == 0 ? (byte)1 : tile.FluidLevel;
            var depthToShore = Math.Clamp(shorelineDistance[i], 0, 4);
            var lowland = 1f - terrain[x, y];

            var baseLevel = isOceanBiome
                ? 3.1f
                : (isLikelyRiver ? 2.2f : 1.5f);
            var shorelineWeight = isOceanBiome
                ? 0.80f
                : (isLikelyRiver ? 0.78f : 0.92f);
            var terrainWeight = isOceanBiome
                ? 1.65f
                : (isLikelyRiver ? 1.30f : 1.12f);
            var target = baseLevel + (depthToShore * shorelineWeight) + (lowland * terrainWeight);
            var blend = isLikelyRiver ? 0.56f : 0.70f;
            var blended = (target * blend) + (currentLevel * (1f - blend));

            var level = (byte)Math.Clamp((int)MathF.Round(blended), 1, 7);
            if (!isLikelyRiver && !isOceanBiome)
                level = (byte)Math.Min((int)level, 5);

            smoothed[i] = level;
        }

        var scratch = new byte[cells.Count];
        var allowedGradient = isLikelyRiver || isOceanBiome ? 2 : 1;
        for (var pass = 0; pass < 3; pass++)
        {
            var changed = false;
            for (var i = 0; i < cells.Count; i++)
            {
                var adjusted = (int)smoothed[i];
                var (x, y) = cells[i];

                foreach (var (dx, dy) in CardinalDirections)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (!keyToIndex.TryGetValue((ny * map.Width) + nx, out var neighborIdx))
                        continue;

                    var neighbor = (int)smoothed[neighborIdx];
                    if (adjusted > neighbor + allowedGradient)
                        adjusted = neighbor + allowedGradient;
                    else if (adjusted < neighbor - allowedGradient)
                        adjusted = neighbor - allowedGradient;
                }

                var clamped = (byte)Math.Clamp(adjusted, 1, 7);
                scratch[i] = clamped;
                if (clamped != smoothed[i])
                    changed = true;
            }

            Array.Copy(scratch, smoothed, smoothed.Length);
            if (!changed)
                break;
        }

        for (var i = 0; i < cells.Count; i++)
        {
            var (x, y) = cells[i];
            var level = smoothed[i];
            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId == GeneratedTileDefIds.Water &&
                tile.FluidType == GeneratedFluidType.Water &&
                tile.FluidLevel == level)
            {
                continue;
            }

            map.SetTile(x, y, 0, ShallowWaterTile(level));
        }
    }

    private static bool IsSurfaceWater(GeneratedEmbarkMap map, int x, int y)
    {
        var tile = map.GetTile(x, y, 0);
        return tile.TileDefId == GeneratedTileDefIds.Water || tile.FluidType == GeneratedFluidType.Water;
    }

    private static bool IsSurfaceShorelineCell(GeneratedEmbarkMap map, int x, int y)
    {
        foreach (var (dx, dy) in CardinalDirections)
        {
            var nx = x + dx;
            var ny = y + dy;
            if (nx <= 0 || ny <= 0 || nx >= map.Width - 1 || ny >= map.Height - 1)
                return true;
            if (!IsSurfaceWater(map, nx, ny))
                return true;
        }

        return false;
    }

    private static void AddTrees(
        GeneratedEmbarkMap map,
        Random rng,
        float minCoverage,
        float maxCoverage,
        float[,] terrain,
        float[,] moisture,
        float[,] canopyMask,
        float[,] forestPatchMask,
        float[,] forestOpeningMask,
        string biomeId,
        float forestPatchBias)
    {
        if (map.Width < 8 || map.Height < 8)
            return;
        var clampedMin = Math.Clamp(minCoverage, 0f, 0.95f);
        var clampedMax = Math.Clamp(maxCoverage, clampedMin, 0.95f);
        if (clampedMax <= 0f)
            return;

        var biomeProfile = WorldGenContentRegistry.Current.ResolveBiomePreset(biomeId, seed: 0);
        var clampedPatchBias = Math.Clamp(forestPatchBias, -1f, 1f);
        var biomeCoverageBoost = biomeProfile.TreeCoverageBoost;
        var suitabilityFloor = biomeProfile.TreeSuitabilityFloor;
        var forestTreeFillRatio = Math.Clamp(biomeProfile.ForestTreeFillRatio, 0.50f, 0.98f);
        var denseForestBiome = biomeProfile.DenseForest;
        var canopyWeight = 0.14f - (Math.Max(0f, clampedPatchBias) * 0.03f);
        var patchWeight = 0.28f + (Math.Max(0f, clampedPatchBias) * 0.14f);
        var moistureWeight = 0.40f + (Math.Max(0f, clampedPatchBias) * 0.04f);
        var riparianWeight = 0.24f + (Math.Max(0f, clampedPatchBias) * 0.04f);
        var ruggedPenaltyScale = 0.12f + (Math.Max(0f, -clampedPatchBias) * 0.06f);
        var candidates = new List<(int X, int Y, float Score)>(map.Width * map.Height);
        var eligibleLandTiles = 0;
        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                continue;
            var tileDefId = map.GetTile(x, y, 0).TileDefId;
            if (tileDefId == GeneratedTileDefIds.Water || IsSurfaceRockWallTile(tileDefId))
                continue;

            eligibleLandTiles++;
            var riparianBoost = EstimateRiparianBoost(map, x, y);
            var ruggedness = EstimateRuggedness(terrain, x, y, map.Width, map.Height);
            var forestCore = Math.Clamp((forestPatchMask[x, y] * 0.72f) + (canopyMask[x, y] * 0.28f), 0f, 1f);
            if (ShouldReserveForestOpening(forestCore, forestOpeningMask[x, y], forestTreeFillRatio))
                continue;

            var suitability = Math.Clamp(
                (moisture[x, y] * moistureWeight) +
                ((1f - terrain[x, y]) * 0.14f) +
                (canopyMask[x, y] * canopyWeight) +
                (forestPatchMask[x, y] * patchWeight) +
                (riparianBoost * riparianWeight) -
                (ruggedness * ruggedPenaltyScale), 0f, 1f);
            suitability = Math.Clamp(suitability + biomeCoverageBoost, 0f, 1f);
            if (suitability < suitabilityFloor)
                continue;

            var jitter = ((float)rng.NextDouble() - 0.5f) * 0.06f;
            candidates.Add((x, y, Math.Clamp(suitability + jitter, 0f, 1f)));
        }

        if (eligibleLandTiles == 0 || candidates.Count == 0)
            return;

        var baseTargetCoverage = clampedMin + ((float)rng.NextDouble() * (clampedMax - clampedMin));
        var boostedCoverage = Math.Clamp(
            baseTargetCoverage + (Math.Max(0f, biomeCoverageBoost) * 0.32f),
            clampedMin,
            0.95f);
        var targetCount = Math.Clamp((int)MathF.Round(boostedCoverage * eligibleLandTiles), 0, candidates.Count);
        if (targetCount <= 0)
            return;

        candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
        var treePlaced = new bool[map.Width, map.Height];
        var placed = 0;

        var seedFraction = denseForestBiome ? 0.026f : 0.017f;
        var seedCount = Math.Clamp((int)MathF.Round(targetCount * seedFraction), 1, 16);
        var seedBand = denseForestBiome ? 0.45f : 0.30f;
        var seedBandCount = Math.Clamp((int)MathF.Round(candidates.Count * seedBand), seedCount, candidates.Count);
        for (var i = 0; i < seedCount && placed < targetCount; i++)
        {
            var idx = Math.Min(candidates.Count - 1, (i * seedBandCount) / seedCount);
            var candidate = candidates[idx];
            if (TryPlaceTree(map, treePlaced, candidate.X, candidate.Y, biomeId, moisture, terrain, rng))
                placed++;
        }

        // Force a small riverbank-focused seed pass so waterways grow visible riparian bands.
        if (placed < targetCount)
            placed += PlaceRiparianTreeSeeds(map, treePlaced, candidates, biomeId, moisture, terrain, rng, targetCount - placed);

        var maxGrowthPasses = denseForestBiome ? 14 : 11;
        for (var pass = 0; pass < maxGrowthPasses && placed < targetCount; pass++)
        {
            var passPlaced = 0;
            var threshold = denseForestBiome
                ? 0.86f - (pass * 0.045f)
                : 0.84f - (pass * 0.055f);
            var minNeighborCount = pass < (denseForestBiome ? 5 : 3) ? 1 : 0;
            var neighborBoostScale = denseForestBiome ? 0.09f : 0.07f;
            for (var i = 0; i < candidates.Count && placed < targetCount; i++)
            {
                var candidate = candidates[i];
                if (treePlaced[candidate.X, candidate.Y])
                    continue;

                var neighbors = CountAdjacentPlacedTrees(treePlaced, candidate.X, candidate.Y, map.Width, map.Height);
                if (neighbors < minNeighborCount)
                    continue;

                var neighborBoost = neighbors * neighborBoostScale;
                var growth = candidate.Score + neighborBoost + (((float)rng.NextDouble() - 0.5f) * 0.05f);
                if (growth < threshold)
                    continue;

                if (TryPlaceTree(map, treePlaced, candidate.X, candidate.Y, biomeId, moisture, terrain, rng))
                {
                    placed++;
                    passPlaced++;
                }
            }

            if (passPlaced == 0 && pass >= 4)
                break;
        }

        // Final deterministic fill if growth passes undershot target.
        var fallbackMinScore = denseForestBiome ? 0.44f : 0.60f;
        for (var i = 0; i < candidates.Count && placed < targetCount; i++)
        {
            var candidate = candidates[i];
            if (treePlaced[candidate.X, candidate.Y])
                continue;

            var neighbors = CountAdjacentPlacedTrees(treePlaced, candidate.X, candidate.Y, map.Width, map.Height);
            if (neighbors == 0 && candidate.Score < fallbackMinScore)
                continue;

            if (TryPlaceTree(map, treePlaced, candidate.X, candidate.Y, biomeId, moisture, terrain, rng))
                placed++;
        }
    }

    private static int PlaceRiparianTreeSeeds(
        GeneratedEmbarkMap map,
        bool[,] treePlaced,
        List<(int X, int Y, float Score)> candidates,
        string biomeId,
        float[,] moisture,
        float[,] terrain,
        Random rng,
        int remaining)
    {
        if (remaining <= 0)
            return 0;
        if (string.Equals(biomeId, MacroBiomeIds.Highland, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var riparianCandidates = new List<(int X, int Y, float Score)>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (treePlaced[candidate.X, candidate.Y])
                continue;

            var riparianBoost = EstimateRiparianBoost(map, candidate.X, candidate.Y);
            if (riparianBoost < 0.70f)
                continue;

            riparianCandidates.Add((candidate.X, candidate.Y, candidate.Score + (riparianBoost * 0.28f)));
        }

        if (riparianCandidates.Count == 0)
            return 0;

        riparianCandidates.Sort((left, right) => right.Score.CompareTo(left.Score));
        var target = Math.Min(remaining, Math.Max(2, riparianCandidates.Count / 5));
        var placed = 0;
        for (var i = 0; i < riparianCandidates.Count && placed < target; i++)
        {
            var candidate = riparianCandidates[i];
            if (TryPlaceTree(map, treePlaced, candidate.X, candidate.Y, biomeId, moisture, terrain, rng))
                placed++;
        }

        return placed;
    }

    private static bool TryPlaceTree(
        GeneratedEmbarkMap map,
        bool[,] treePlaced,
        int x,
        int y,
        string biomeId,
        float[,] moisture,
        float[,] terrain,
        Random rng)
    {
        if (treePlaced[x, y])
            return false;
        if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
            return false;
        var surfaceTile = map.GetTile(x, y, 0);
        if (!IsSurfaceSuitableForTree(surfaceTile))
            return false;
        if (!EnsureTreeSubsurface(map, x, y, biomeId, moisture[x, y], terrain[x, y]))
            return false;

        var riparianBoost = EstimateRiparianBoost(map, x, y);
        var speciesId = ResolveTreeSpeciesId(biomeId, moisture[x, y], terrain[x, y], riparianBoost, rng);
        map.SetTile(x, y, 0, TreeTile(speciesId));
        treePlaced[x, y] = true;
        return true;
    }

    private static bool ShouldReserveForestOpening(float forestCore, float openingMask, float forestTreeFillRatio)
    {
        if (forestCore < 0.58f)
            return false;

        return openingMask >= forestTreeFillRatio;
    }

    private static bool IsSurfaceSuitableForTree(GeneratedTile tile)
        => tile.TileDefId is GeneratedTileDefIds.Grass or GeneratedTileDefIds.Soil or GeneratedTileDefIds.Mud &&
           tile.IsPassable &&
           tile.FluidType == GeneratedFluidType.None;

    private static bool EnsureTreeSubsurface(GeneratedEmbarkMap map, int x, int y, string biomeId, float moisture, float terrain)
    {
        if (map.Depth <= 1)
            return true;

        var below = map.GetTile(x, y, 1);
        if (below.TileDefId == GeneratedTileDefIds.SoilWall)
            return true;

        if (below.FluidType != GeneratedFluidType.None ||
            below.TileDefId is GeneratedTileDefIds.Water or GeneratedTileDefIds.Magma or GeneratedTileDefIds.Staircase)
        {
            return false;
        }

        var materialId = ResolveTreeSubsurfaceMaterialId(biomeId, moisture, terrain);
        map.SetTile(x, y, 1, SoilWallTile(materialId) with
        {
            IsAquifer = below.IsAquifer,
        });

        return true;
    }

    private static string ResolveTreeSubsurfaceMaterialId(string biomeId, float moisture, float terrain)
        => WorldGenContentRegistry.Current.ResolveTreeSubsurfaceMaterialId(biomeId);

    private static int CountAdjacentPlacedTrees(bool[,] treePlaced, int x, int y, int width, int height)
    {
        var count = 0;
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0)
                continue;

            var nx = x + dx;
            var ny = y + dy;
            if (nx <= 0 || ny <= 0 || nx >= width - 1 || ny >= height - 1)
                continue;
            if (treePlaced[nx, ny])
                count++;
        }

        return count;
    }

    private static float EstimateRiparianBoost(GeneratedEmbarkMap map, int x, int y)
    {
        var bestDistance = int.MaxValue;
        for (var dx = -2; dx <= 2; dx++)
        for (var dy = -2; dy <= 2; dy++)
        {
            if (dx == 0 && dy == 0)
                continue;

            var nx = x + dx;
            var ny = y + dy;
            if (nx <= 0 || ny <= 0 || nx >= map.Width - 1 || ny >= map.Height - 1)
                continue;

            if (map.GetTile(nx, ny, 0).TileDefId != GeneratedTileDefIds.Water)
                continue;

            var dist = Math.Abs(dx) + Math.Abs(dy);
            if (dist < bestDistance)
                bestDistance = dist;
        }

        if (bestDistance == int.MaxValue)
            return 0f;

        return bestDistance switch
        {
            <= 1 => 1f,
            2 => 0.7f,
            3 => 0.4f,
            _ => 0.2f,
        };
    }

    private static string ResolveTreeSpeciesId(string biomeId, float moisture, float terrain, float riparianBoost, Random rng)
        => WorldGenContentRegistry.Current.ResolveTreeSpeciesId(biomeId, moisture, terrain, riparianBoost, rng);

    private static void AddPlants(
        GeneratedEmbarkMap map,
        int seed,
        Random rng,
        float[,] terrain,
        float[,] moisture,
        float[,] forestPatchMask,
        float[,] forestOpeningMask,
        string biomeId)
    {
        var plantCatalog = WorldGenPlantRegistry.Current;
        var biomeProfile = WorldGenContentRegistry.Current.ResolveBiomePreset(biomeId, seed: 0);
        SeedFruitCanopies(map, rng, terrain, moisture, biomeId, plantCatalog);

        var density = WorldGenContentRegistry.Current.ResolveGroundPlantDensity(biomeId);
        if (density <= 0f)
            return;

        // Build a clustering noise layer so plants form natural patches rather than uniform grids.
        // Low-frequency noise creates large-scale "fertile" and "barren" zones.
        var plantClusterNoise = BuildPlantClusterNoise(map.Width, map.Height, seed + 7919);

        // Track placed plants for soft crowding penalty.
        var plantPlaced = new bool[map.Width, map.Height];
        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (!string.IsNullOrWhiteSpace(tile.PlantDefId))
                plantPlaced[x, y] = true;
        }

        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                continue;

            var tile = map.GetTile(x, y, 0);
            if (!CanHostGroundPlant(tile))
                continue;

            var riparianBoost = EstimateRiparianBoost(map, x, y);
            if (!plantCatalog.TryResolveBestGroundPlant(
                    biomeId,
                    tile.TileDefId,
                    moisture[x, y],
                    terrain[x, y],
                    riparianBoost,
                    out var plantDefinition,
                    out var score) || plantDefinition is null)
            {
                continue;
            }

            // Apply cluster noise: high-noise areas are more favorable for plant clusters.
            // This creates natural patches where plants group together.
            var clusterValue = plantClusterNoise[x, y];
            var clusterBoost = (clusterValue - 0.5f) * 0.30f; // +/- 15% boost/penalty
            var adjustedScore = Math.Clamp(score + clusterBoost, 0f, 1f);

            // Soft crowding penalty: each nearby plant slightly reduces placement chance.
            // Unlike hard exclusion, this allows some plants to be close together naturally.
            var nearbyCount = CountNearbyPlacedPlants(plantPlaced, x, y, radius: 3, map.Width, map.Height);
            var crowdingPenalty = nearbyCount * 0.08f; // Each nearby plant reduces score by 8%
            var nearbyTrees = CountNearbySurfaceTrees(map, x, y, radius: 1);
            var understoryBoost = ResolveForestUnderstoryBoost(
                forestPatchMask[x, y],
                forestOpeningMask[x, y],
                nearbyTrees,
                biomeProfile.ForestTreeFillRatio);
            var effectiveDensity = Math.Clamp(density + understoryBoost, 0f, 1f);

            // Threshold scales with biome density: dense biomes accept lower scores.
            var threshold = 0.72f - (effectiveDensity * 0.20f);
            if (tile.TileDefId == GeneratedTileDefIds.Mud)
                threshold -= 0.04f;

            var jitter = ((float)rng.NextDouble() - 0.5f) * 0.14f;
            var finalScore = adjustedScore + understoryBoost - crowdingPenalty + jitter;
            if (finalScore < threshold)
                continue;

            var stage = ResolvePlantGrowthStage(rng, plantDefinition.MaxGrowthStage);

            var yield = stage >= GeneratedPlantGrowthStages.Mature && rng.NextDouble() < 0.55d ? (byte)1 : (byte)0;
            var seedLevel = stage == GeneratedPlantGrowthStages.Seed ? (byte)1 : (byte)0;
            map.SetTile(x, y, 0, tile with
            {
                PlantDefId = plantDefinition.Id,
                PlantGrowthStage = stage,
                PlantGrowthProgressSeconds = 0f,
                PlantYieldLevel = yield,
                PlantSeedLevel = seedLevel,
            });
            plantPlaced[x, y] = true;
        }
    }

    private static float[,] BuildPlantClusterNoise(int width, int height, int seed)
    {
        var noise = new float[width, height];
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            var fx = width <= 1 ? 0f : x / (float)(width - 1);
            var fy = height <= 1 ? 0f : y / (float)(height - 1);

            // Low-frequency noise for large-scale clustering (patches of 5-10 tiles).
            var coarse = CoherentNoise.DomainWarpedFractal2D(
                seed, fx * 2.8f, fy * 2.8f, octaves: 3, lacunarity: 2f, gain: 0.55f, warpStrength: 0.30f, salt: 701);
            // Medium-frequency noise for sub-patch variation.
            var medium = CoherentNoise.Fractal2D(
                seed, fx * 6.5f, fy * 6.5f, octaves: 2, lacunarity: 2f, gain: 0.5f, salt: 709);

            noise[x, y] = Math.Clamp((coarse * 0.70f) + (medium * 0.30f), 0f, 1f);
        }

        // Smooth to soften harsh transitions between cluster zones.
        SmoothPlantNoise(noise, width, height, passes: 2);
        return noise;
    }

    private static void SmoothPlantNoise(float[,] noise, int width, int height, int passes)
    {
        var scratch = new float[width, height];
        for (var pass = 0; pass < passes; pass++)
        {
            for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
            {
                var sum = noise[x, y] * 2f;
                var weight = 2f;
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    var w = (dx == 0 || dy == 0) ? 1f : 0.7f;
                    sum += noise[nx, ny] * w;
                    weight += w;
                }
                scratch[x, y] = Math.Clamp(sum / weight, 0f, 1f);
            }
            for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
                noise[x, y] = scratch[x, y];
        }
    }

    private static int CountNearbyPlacedPlants(bool[,] plantPlaced, int x, int y, int radius, int width, int height)
    {
        var count = 0;
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
            if (plantPlaced[nx, ny]) count++;
        }
        return count;
    }

    private static int CountNearbySurfaceTrees(GeneratedEmbarkMap map, int x, int y, int radius)
    {
        var count = 0;
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            if (dx == 0 && dy == 0)
                continue;

            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
                continue;
            if (map.GetTile(nx, ny, 0).TileDefId == GeneratedTileDefIds.Tree)
                count++;
        }

        return count;
    }

    private static float ResolveForestUnderstoryBoost(
        float forestPatchValue,
        float forestOpeningValue,
        int nearbyTrees,
        float forestTreeFillRatio)
    {
        if (nearbyTrees <= 0)
            return 0f;

        var openingSignal = Math.Clamp((forestOpeningValue - forestTreeFillRatio) / Math.Max(0.02f, 1f - forestTreeFillRatio), 0f, 1f);
        if (openingSignal <= 0f)
            return 0f;

        var forestSignal = Math.Clamp((forestPatchValue - 0.42f) / 0.58f, 0f, 1f);
        if (forestSignal <= 0f)
            return 0f;

        var treeSignal = Math.Clamp(nearbyTrees / 6f, 0f, 1f);
        return openingSignal * forestSignal * treeSignal * 0.40f;
    }

    private static void SeedFruitCanopies(
        GeneratedEmbarkMap map,
        Random rng,
        float[,] terrain,
        float[,] moisture,
        string biomeId,
        WorldGenPlantCatalog plantCatalog)
    {
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId != GeneratedTileDefIds.Tree || string.IsNullOrWhiteSpace(tile.TreeSpeciesId))
                continue;

            var riparianBoost = EstimateRiparianBoost(map, x, y);
            if (!plantCatalog.TryResolveBestTreeCanopyPlant(
                    biomeId,
                    tile.TreeSpeciesId,
                    moisture[x, y],
                    terrain[x, y],
                    riparianBoost,
                    out var canopyDefinition,
                    out var score) || canopyDefinition is null)
            {
                continue;
            }

            if (score < 0.36f)
                continue;

            var stage = Math.Min(GeneratedPlantGrowthStages.Mature, canopyDefinition.MaxGrowthStage);

            map.SetTile(x, y, 0, tile with
            {
                PlantDefId = canopyDefinition.Id,
                PlantGrowthStage = (byte)stage,
                PlantGrowthProgressSeconds = 0f,
                PlantYieldLevel = stage >= GeneratedPlantGrowthStages.Mature && rng.NextDouble() < 0.60d ? (byte)1 : (byte)0,
                PlantSeedLevel = stage == GeneratedPlantGrowthStages.Seed ? (byte)1 : (byte)0,
            });
        }
    }

    private static bool CanHostGroundPlant(GeneratedTile tile)
        => tile.TileDefId is GeneratedTileDefIds.Grass or GeneratedTileDefIds.Soil or GeneratedTileDefIds.Mud or GeneratedTileDefIds.Sand or GeneratedTileDefIds.StoneFloor
           && tile.IsPassable
           && tile.FluidType == GeneratedFluidType.None
           && string.IsNullOrWhiteSpace(tile.PlantDefId);

    private static byte ResolvePlantGrowthStage(Random rng, byte maxGrowthStage)
    {
        var rolledStage = (float)rng.NextDouble() switch
        {
            < 0.08f => GeneratedPlantGrowthStages.Seed,
            < 0.34f => GeneratedPlantGrowthStages.Sprout,
            < 0.70f => GeneratedPlantGrowthStages.Young,
            _ => GeneratedPlantGrowthStages.Mature,
        };

        return (byte)Math.Min(Math.Clamp((int)maxGrowthStage, GeneratedPlantGrowthStages.Seed, GeneratedPlantGrowthStages.Mature), rolledStage);
    }

    private static void AddOutcrops(GeneratedEmbarkMap map, Random rng, int min, int max, float[,] terrain)
    {
        if (max <= 0 || map.Width < 10 || map.Height < 10)
            return;

        var count = rng.Next(min, max + 1);
        var placed = 0;
        var attempts = Math.Max(count * 35, 160);
        for (var attempt = 0; attempt < attempts && placed < count; attempt++)
        {
            var x = rng.Next(2, map.Width - 2);
            var y = rng.Next(2, map.Height - 2);

            if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                continue;
            if (map.GetTile(x, y, 0).TileDefId is GeneratedTileDefIds.Water or GeneratedTileDefIds.Tree)
                continue;

            var ruggedness = EstimateRuggedness(terrain, x, y, map.Width, map.Height);
            if (ruggedness + ((float)rng.NextDouble() * 0.25f) < 0.58f)
                continue;

            map.SetTile(x, y, 0, ResolveOutcropWallTile(rng));
            placed++;
        }
    }

    private static float EstimateRuggedness(float[,] terrain, int x, int y, int width, int height)
    {
        var center = terrain[x, y];
        var min = center;
        var max = center;

        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                continue;

            var value = terrain[nx, ny];
            if (value < min) min = value;
            if (value > max) max = value;
        }

        return Math.Clamp((max - min) * 3f, 0f, 1f);
    }

    private static void AddMarshPools(GeneratedEmbarkMap map, Random rng, int poolCount, float[,] terrain, float[,] moisture)
    {
        if (poolCount <= 0)
            return;

        for (var i = 0; i < poolCount; i++)
        {
            var centerX = rng.Next(2, map.Width - 2);
            var centerY = rng.Next(2, map.Height - 2);
            if (IsInCentralEmbarkZone(map.Width, map.Height, centerX, centerY))
                continue;
            if (terrain[centerX, centerY] > 0.56f || moisture[centerX, centerY] < 0.55f)
                continue;

            var radius = 1 + rng.Next(0, 3);
            for (var dx = -radius; dx <= radius; dx++)
            for (var dy = -radius; dy <= radius; dy++)
            {
                var x = centerX + dx;
                var y = centerY + dy;
                if (x <= 1 || y <= 1 || x >= map.Width - 1 || y >= map.Height - 1)
                    continue;
                if (Math.Abs(dx) + Math.Abs(dy) > radius + 1)
                    continue;
                if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                    continue;

                map.SetTile(x, y, 0, ShallowWaterTile((byte)(1 + rng.Next(0, 2))));
            }
        }
    }

    private static void ApplySettlementAndRoadOverlay(
        GeneratedEmbarkMap map,
        int seed,
        GeneratedTile baseTile,
        string biomeId,
        float settlementInfluence,
        float roadInfluence,
        LocalSettlementAnchor[]? settlementAnchors,
        LocalRoadPortal[]? roadPortals)
    {
        var settlement = Math.Clamp(settlementInfluence, 0f, 1f);
        var road = WorldGenFeatureFlags.EnableRoadGeneration
            ? Math.Clamp(roadInfluence, 0f, 1f)
            : 0f;
        if ((settlement <= 0f && road <= 0f) || IsOceanBiome(biomeId))
            return;

        var hash = SeedHash.Hash(seed, map.Width, map.Height, 38191);
        var anchoredSettlements = ResolveSettlementAnchors(map, hash, settlement, settlementAnchors);

        if (settlement > 0f && anchoredSettlements.Count > 0)
        {
            foreach (var anchor in anchoredSettlements)
            {
                var radius = ResolveSettlementRadius(map, settlement, anchor.Strength);
                CarveGroundCircle(map, anchor.X, anchor.Y, radius, baseTile, skipWater: true);
            }
        }

        if (road <= 0f)
            return;

        var hubs = anchoredSettlements.Count > 0
            ? anchoredSettlements
            : new List<AnchoredPoint>(1) { new(map.Width / 2, map.Height / 2, 3) };
        var roadEndpoints = ResolveRoadEndpoints(map, roadPortals);
        if (roadEndpoints.Count == 0)
            roadEndpoints = BuildFallbackRoadEndpoints(map, hash, hubs[0], road);

        var roadTile = RoadSurfaceTile(baseTile.MaterialId);
        ConnectRoadNetwork(map, roadEndpoints, hubs, road, roadTile);
    }

    private static List<AnchoredPoint> ResolveSettlementAnchors(
        GeneratedEmbarkMap map,
        int hash,
        float settlementInfluence,
        LocalSettlementAnchor[]? settlementAnchors)
    {
        var points = new List<AnchoredPoint>(4);
        var seen = new HashSet<int>();

        if (settlementAnchors is { Length: > 0 })
        {
            foreach (var anchor in settlementAnchors)
            {
                var x = ResolveSettlementAxisOffset(anchor.NormalizedX, map.Width);
                var y = ResolveSettlementAxisOffset(anchor.NormalizedY, map.Height);
                var strength = (byte)Math.Clamp((int)anchor.Strength, 1, 8);
                AddAnchoredPointUnique(points, seen, x, y, strength);
            }
        }

        if (points.Count > 0 || settlementInfluence <= 0f)
            return points;

        var centerX = map.Width / 2;
        var centerY = map.Height / 2;
        var spreadX = Math.Max(2, map.Width / 7);
        var spreadY = Math.Max(2, map.Height / 7);
        var ox = (((hash & 0xFF) / 255f) * 2f) - 1f;
        var oy = ((((hash >> 8) & 0xFF) / 255f) * 2f) - 1f;
        var primaryX = Math.Clamp(centerX + (int)MathF.Round(ox * spreadX), 2, map.Width - 3);
        var primaryY = Math.Clamp(centerY + (int)MathF.Round(oy * spreadY), 2, map.Height - 3);
        AddAnchoredPointUnique(points, seen, primaryX, primaryY, 3);

        if (settlementInfluence >= 0.72f)
        {
            var secondaryDx = ((hash >> 16) & 1) == 0 ? -Math.Max(2, map.Width / 8) : Math.Max(2, map.Width / 8);
            var secondaryDy = ((hash >> 17) & 1) == 0 ? -Math.Max(2, map.Height / 8) : Math.Max(2, map.Height / 8);
            var secondaryX = Math.Clamp(primaryX + secondaryDx, 2, map.Width - 3);
            var secondaryY = Math.Clamp(primaryY + secondaryDy, 2, map.Height - 3);
            AddAnchoredPointUnique(points, seen, secondaryX, secondaryY, 2);
        }

        return points;
    }

    private static int ResolveSettlementRadius(GeneratedEmbarkMap map, float settlementInfluence, byte strength)
    {
        var baseRadius = 2 + (int)MathF.Round(settlementInfluence * Math.Min(map.Width, map.Height) * 0.08f);
        var strengthBonus = Math.Clamp((int)strength - 2, 0, 4);
        return Math.Clamp(baseRadius + strengthBonus, 2, 10);
    }

    private static List<RoadEndpoint> ResolveRoadEndpoints(GeneratedEmbarkMap map, LocalRoadPortal[]? roadPortals)
    {
        var endpoints = new List<RoadEndpoint>(4);
        if (roadPortals is not { Length: > 0 })
            return endpoints;

        var seen = new HashSet<int>();
        foreach (var portal in roadPortals)
        {
            var (x, y) = ResolveRoadPortalPoint(map, portal.Edge, portal.NormalizedOffset);
            var width = (byte)Math.Clamp((int)portal.Width, 1, 3);
            AddRoadEndpointUnique(endpoints, seen, x, y, width);
        }

        return endpoints;
    }

    private static List<RoadEndpoint> BuildFallbackRoadEndpoints(
        GeneratedEmbarkMap map,
        int hash,
        AnchoredPoint hub,
        float road)
    {
        var endpoints = new List<RoadEndpoint>(4);
        var seen = new HashSet<int>();
        var defaultWidth = (byte)(road >= 0.78f ? 2 : 1);
        var primaryHorizontal = ((hash >> 19) & 1) == 0;
        var offsetA = ResolveRoadOffset(hash, map.Width, map.Height, positive: true);
        var offsetB = ResolveRoadOffset(hash, map.Width, map.Height, positive: false);

        if (primaryHorizontal)
        {
            var yA = Math.Clamp(hub.Y + offsetA, 1, map.Height - 2);
            AddRoadEndpointUnique(endpoints, seen, 1, yA, defaultWidth);
            AddRoadEndpointUnique(endpoints, seen, map.Width - 2, yA, defaultWidth);

            if (road >= 0.64f)
            {
                var xB = Math.Clamp(hub.X + offsetB, 1, map.Width - 2);
                AddRoadEndpointUnique(endpoints, seen, xB, 1, defaultWidth);
                AddRoadEndpointUnique(endpoints, seen, xB, map.Height - 2, defaultWidth);
            }
        }
        else
        {
            var xA = Math.Clamp(hub.X + offsetA, 1, map.Width - 2);
            AddRoadEndpointUnique(endpoints, seen, xA, 1, defaultWidth);
            AddRoadEndpointUnique(endpoints, seen, xA, map.Height - 2, defaultWidth);

            if (road >= 0.64f)
            {
                var yB = Math.Clamp(hub.Y + offsetB, 1, map.Height - 2);
                AddRoadEndpointUnique(endpoints, seen, 1, yB, defaultWidth);
                AddRoadEndpointUnique(endpoints, seen, map.Width - 2, yB, defaultWidth);
            }
        }

        return endpoints;
    }

    private static void ConnectRoadNetwork(
        GeneratedEmbarkMap map,
        List<RoadEndpoint> roadEndpoints,
        List<AnchoredPoint> hubs,
        float road,
        GeneratedTile roadTile)
    {
        if (roadEndpoints.Count == 0 || hubs.Count == 0)
            return;

        foreach (var endpoint in roadEndpoints)
        {
            var hub = ResolveNearestHub(endpoint, hubs);
            var width = ResolveRoadWidth(road, endpoint.Width);
            TraceRoadLine(map, (endpoint.X, endpoint.Y), (hub.X, hub.Y), width, roadTile, skipWater: true);
        }

        if (hubs.Count < 2 || road < 0.72f)
            return;

        for (var i = 1; i < hubs.Count; i++)
        {
            var width = ResolveRoadWidth(road, portalWidth: 1);
            TraceRoadLine(map, (hubs[i - 1].X, hubs[i - 1].Y), (hubs[i].X, hubs[i].Y), width, roadTile, skipWater: true);
        }
    }

    private static AnchoredPoint ResolveNearestHub(RoadEndpoint endpoint, List<AnchoredPoint> hubs)
    {
        var best = hubs[0];
        var bestDistance = Math.Abs(endpoint.X - best.X) + Math.Abs(endpoint.Y - best.Y);
        for (var i = 1; i < hubs.Count; i++)
        {
            var hub = hubs[i];
            var distance = Math.Abs(endpoint.X - hub.X) + Math.Abs(endpoint.Y - hub.Y);
            if (distance >= bestDistance)
                continue;

            best = hub;
            bestDistance = distance;
        }

        return best;
    }

    private static int ResolveRoadWidth(float road, byte portalWidth)
    {
        var roadWidth = road >= 0.78f ? 2 : 1;
        return Math.Clamp(Math.Max(roadWidth, portalWidth), 1, 3);
    }

    private static (int X, int Y) ResolveRoadPortalPoint(GeneratedEmbarkMap map, LocalMapEdge edge, float normalizedOffset)
    {
        var offsetX = ResolveInteriorAxisOffset(normalizedOffset, map.Width);
        var offsetY = ResolveInteriorAxisOffset(normalizedOffset, map.Height);
        return edge switch
        {
            LocalMapEdge.North => (offsetX, 1),
            LocalMapEdge.East => (map.Width - 2, offsetY),
            LocalMapEdge.South => (offsetX, map.Height - 2),
            LocalMapEdge.West => (1, offsetY),
            _ => (map.Width / 2, map.Height / 2),
        };
    }

    private static int ResolveSettlementAxisOffset(float normalizedOffset, int axisSize)
    {
        var min = 2;
        var max = axisSize - 3;
        if (max < min)
            return Math.Clamp(axisSize / 2, 1, Math.Max(1, axisSize - 2));

        var clamped = Math.Clamp(normalizedOffset, 0f, 1f);
        return min + (int)MathF.Round(clamped * (max - min));
    }

    private static int ResolveInteriorAxisOffset(float normalizedOffset, int axisSize)
    {
        var min = 1;
        var max = axisSize - 2;
        if (max < min)
            return Math.Clamp(axisSize / 2, 0, Math.Max(0, axisSize - 1));

        var clamped = Math.Clamp(normalizedOffset, 0f, 1f);
        return min + (int)MathF.Round(clamped * (max - min));
    }

    private static void AddAnchoredPointUnique(List<AnchoredPoint> points, HashSet<int> seen, int x, int y, byte strength)
    {
        var key = (y * 4096) + x;
        if (!seen.Add(key))
            return;

        points.Add(new AnchoredPoint(x, y, strength));
    }

    private static void AddRoadEndpointUnique(List<RoadEndpoint> endpoints, HashSet<int> seen, int x, int y, byte width)
    {
        var key = (y * 4096) + x;
        if (!seen.Add(key))
            return;

        endpoints.Add(new RoadEndpoint(x, y, width));
    }

    private static int ResolveRoadOffset(int hash, int width, int height, bool positive)
    {
        var bits = positive ? ((hash >> 22) & 0x0F) : ((hash >> 26) & 0x0F);
        var magnitude = 1 + (bits % Math.Max(2, Math.Min(width, height) / 8));
        return positive ? magnitude : -magnitude;
    }

    private static void CarveGroundCircle(
        GeneratedEmbarkMap map,
        int centerX,
        int centerY,
        int radius,
        GeneratedTile baseTile,
        bool skipWater)
    {
        var radiusSq = radius * radius;
        for (var y = centerY - radius; y <= centerY + radius; y++)
        for (var x = centerX - radius; x <= centerX + radius; x++)
        {
            if (x <= 0 || y <= 0 || x >= map.Width - 1 || y >= map.Height - 1)
                continue;

            var dx = x - centerX;
            var dy = y - centerY;
            if ((dx * dx) + (dy * dy) > radiusSq)
                continue;

            TryCarveGroundTile(map, x, y, baseTile, skipWater);
        }
    }

    private static void TraceRoadLine(
        GeneratedEmbarkMap map,
        (int X, int Y) from,
        (int X, int Y) to,
        int width,
        GeneratedTile roadTile,
        bool skipWater)
    {
        var horizontalFirst = Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y);
        if (horizontalFirst)
        {
            TraceAxisAlignedSegment(map, from, (to.X, from.Y), width, roadTile, skipWater);
            TraceAxisAlignedSegment(map, (to.X, from.Y), to, width, roadTile, skipWater);
        }
        else
        {
            TraceAxisAlignedSegment(map, from, (from.X, to.Y), width, roadTile, skipWater);
            TraceAxisAlignedSegment(map, (from.X, to.Y), to, width, roadTile, skipWater);
        }
    }

    private static void TraceAxisAlignedSegment(
        GeneratedEmbarkMap map,
        (int X, int Y) from,
        (int X, int Y) to,
        int width,
        GeneratedTile roadTile,
        bool skipWater)
    {
        var x = from.X;
        var y = from.Y;
        var dx = Math.Sign(to.X - from.X);
        var dy = Math.Sign(to.Y - from.Y);
        var steps = Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y);
        for (var i = 0; i <= steps; i++)
        {
            CarveRoadWidth(map, x, y, width, roadTile, skipWater);

            if (x != to.X)
                x += dx;
            else if (y != to.Y)
                y += dy;
        }
    }

    private static void CarveRoadWidth(
        GeneratedEmbarkMap map,
        int x,
        int y,
        int width,
        GeneratedTile roadTile,
        bool skipWater)
    {
        for (var oy = -width + 1; oy <= width - 1; oy++)
        for (var ox = -width + 1; ox <= width - 1; ox++)
        {
            var nx = x + ox;
            var ny = y + oy;
            if (nx <= 0 || ny <= 0 || nx >= map.Width - 1 || ny >= map.Height - 1)
                continue;
            if (Math.Abs(ox) + Math.Abs(oy) > width)
                continue;

            TryCarveGroundTile(map, nx, ny, roadTile, skipWater);
            var shoulderRadius = width >= 2 ? 2 : 1;
            ClearRoadsideTrees(map, nx, ny, shoulderRadius, roadTile, skipWater);
        }
    }

    private static void ClearRoadsideTrees(
        GeneratedEmbarkMap map,
        int centerX,
        int centerY,
        int radius,
        GeneratedTile roadTile,
        bool skipWater)
    {
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            if (dx == 0 && dy == 0)
                continue;
            if (Math.Abs(dx) + Math.Abs(dy) > radius + 1)
                continue;

            var x = centerX + dx;
            var y = centerY + dy;
            if (x <= 0 || y <= 0 || x >= map.Width - 1 || y >= map.Height - 1)
                continue;

            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId != GeneratedTileDefIds.Tree)
                continue;

            TryCarveGroundTile(map, x, y, roadTile, skipWater);
        }
    }

    private static void TryCarveGroundTile(
        GeneratedEmbarkMap map,
        int x,
        int y,
        GeneratedTile baseTile,
        bool skipWater)
    {
        var tile = map.GetTile(x, y, 0);
        if (tile.TileDefId == GeneratedTileDefIds.Magma)
            return;
        if (skipWater && (tile.TileDefId == GeneratedTileDefIds.Water || tile.FluidType == GeneratedFluidType.Water))
            return;

        if (tile.TileDefId == baseTile.TileDefId &&
            tile.MaterialId == baseTile.MaterialId &&
            tile.IsPassable == baseTile.IsPassable &&
            tile.FluidType == baseTile.FluidType &&
            tile.FluidLevel == baseTile.FluidLevel)
        {
            return;
        }

        map.SetTile(x, y, 0, baseTile);
    }

    private static void EnsureBorderPassable(GeneratedEmbarkMap map, GeneratedTile fallbackTile)
    {
        // Keep edge continuity by preserving already valid border terrain and only fixing unsafe blockers.
        for (var x = 0; x < map.Width; x++)
        {
            EnsureBorderCellSafe(map, x, 0, x, 1, fallbackTile);
            EnsureBorderCellSafe(map, x, map.Height - 1, x, map.Height - 2, fallbackTile);
        }

        for (var y = 0; y < map.Height; y++)
        {
            EnsureBorderCellSafe(map, 0, y, 1, y, fallbackTile);
            EnsureBorderCellSafe(map, map.Width - 1, y, map.Width - 2, y, fallbackTile);
        }
    }

    private static void EnsureBorderCellSafe(
        GeneratedEmbarkMap map,
        int edgeX,
        int edgeY,
        int inwardX,
        int inwardY,
        GeneratedTile fallbackTile)
    {
        var edge = map.GetTile(edgeX, edgeY, 0);
        if (IsSafePassableSurface(edge))
            return;

        if (inwardX >= 0 && inwardY >= 0 && inwardX < map.Width && inwardY < map.Height)
        {
            var inward = map.GetTile(inwardX, inwardY, 0);
            if (IsSafePassableSurface(inward))
            {
                map.SetTile(edgeX, edgeY, 0, inward);
                return;
            }
        }

        map.SetTile(edgeX, edgeY, 0, fallbackTile);
    }

    private static bool IsSafePassableSurface(GeneratedTile tile)
        => tile.IsPassable &&
           tile.FluidType != GeneratedFluidType.Magma &&
           tile.TileDefId != GeneratedTileDefIds.Magma;

    private static void EnsureCentralEmbarkZone(GeneratedEmbarkMap map, GeneratedTile baseTile)
    {
        var (minX, maxX, minY, maxY) = ResolveCentralEmbarkBounds(map.Width, map.Height);

        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
            EnsureSpawnSafeSurfaceTile(map, x, y, baseTile);
    }

    private static bool IsInCentralEmbarkZone(int width, int height, int x, int y)
    {
        var (minX, maxX, minY, maxY) = ResolveCentralEmbarkBounds(width, height);
        return x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    private static (int MinX, int MaxX, int MinY, int MaxY) ResolveCentralEmbarkBounds(int width, int height)
    {
        var cx = width / 2;
        var cy = height / 2;
        var minX = Math.Max(1, cx - EmbarkProtectedHalfSpan);
        var maxX = Math.Min(width - 2, cx + EmbarkProtectedHalfSpan - 1);
        var minY = Math.Max(1, cy - EmbarkProtectedHalfSpan);
        var maxY = Math.Min(height - 2, cy + EmbarkProtectedHalfSpan - 1);
        return (minX, maxX, minY, maxY);
    }

    private static void EnsureSpawnSafeSurfaceTile(GeneratedEmbarkMap map, int x, int y, GeneratedTile baseTile)
    {
        var tile = map.GetTile(x, y, 0);
        if (tile.IsPassable &&
            tile.FluidType == GeneratedFluidType.None &&
            tile.TileDefId != GeneratedTileDefIds.Water &&
            tile.TileDefId != GeneratedTileDefIds.Magma)
        {
            return;
        }

        map.SetTile(x, y, 0, baseTile);
    }

    private static void AddSurfaceCreatureSpawns(GeneratedEmbarkMap map, Random rng, string biomeId)
    {
        var surfaceTable = BiomeCreatureTable.GetSurface(biomeId);
        if (surfaceTable.Count == 0)
            return;

        var maxSpawns = Math.Clamp((map.Width * map.Height) / 120, 8, 40);
        var targetGroups = ResolveSurfaceCreatureGroups(map, biomeId, rng);
        var occupied = BuildCreatureSpawnOccupancy(map);

        for (var group = 0; group < targetGroups; group++)
        {
            if (map.CreatureSpawns.Count >= maxSpawns)
                break;

            var entry = PickWeightedSpawnEntry(surfaceTable, rng);
            var groupSize = rng.Next(Math.Max(1, entry.MinGroup), Math.Max(entry.MinGroup, entry.MaxGroup) + 1);
            if (!TryFindCreatureGroupOrigin(map, rng, entry, z: 0, out var originX, out var originY))
                continue;

            var placedThisGroup = 0;
            for (var i = 0; i < groupSize; i++)
            {
                if (map.CreatureSpawns.Count >= maxSpawns)
                    break;

                if (TryPlaceCreatureNearOrigin(map, rng, entry, originX, originY, z: 0, occupied))
                    placedThisGroup++;
            }

            // Retry with one guaranteed individual if the chosen origin did not fit the group.
            if (placedThisGroup == 0)
                TryPlaceCreatureNearOrigin(map, rng, entry, originX, originY, z: 0, occupied);
        }
    }

    private static void AddSurfaceToCaveWaterConnections(
        GeneratedEmbarkMap map,
        Random rng,
        IReadOnlyList<int> caveLayers)
    {
        if (caveLayers.Count == 0)
            return;

        var firstCaveZ = caveLayers[0];
        if (firstCaveZ <= 1 || firstCaveZ >= map.Depth)
            return;

        var surfaceWater = new List<(int X, int Y)>();
        for (var y = 1; y < map.Height - 1; y++)
        for (var x = 1; x < map.Width - 1; x++)
        {
            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId != GeneratedTileDefIds.Water && tile.FluidType != GeneratedFluidType.Water)
                continue;
            if (tile.FluidLevel < 2)
                continue;

            surfaceWater.Add((x, y));
        }

        if (surfaceWater.Count == 0)
            return;

        var targetCount = Math.Clamp((surfaceWater.Count / 140) + 1, 1, 8);
        for (var i = 0; i < targetCount && surfaceWater.Count > 0; i++)
        {
            var idx = rng.Next(surfaceWater.Count);
            var candidate = surfaceWater[idx];
            surfaceWater.RemoveAt(idx);

            if (!TryResolveCaveSeepTarget(map, candidate.X, candidate.Y, firstCaveZ, out var tx, out var ty))
                continue;

            CarveSeepColumn(map, candidate.X, candidate.Y, firstCaveZ, rng);
            StampCaveSeepPool(map, tx, ty, firstCaveZ, rng);
        }
    }

    private static bool TryResolveCaveSeepTarget(
        GeneratedEmbarkMap map,
        int x,
        int y,
        int caveZ,
        out int targetX,
        out int targetY)
    {
        if (IsValidCaveWaterTarget(map, x, y, caveZ))
        {
            targetX = x;
            targetY = y;
            return true;
        }

        for (var radius = 1; radius <= 8; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > radius)
                    continue;

                var nx = x + dx;
                var ny = y + dy;
                if (nx <= 0 || ny <= 0 || nx >= map.Width - 1 || ny >= map.Height - 1)
                    continue;
                if (!IsValidCaveWaterTarget(map, nx, ny, caveZ))
                    continue;

                targetX = nx;
                targetY = ny;
                return true;
            }
        }

        targetX = x;
        targetY = y;
        return false;
    }

    private static void CarveSeepColumn(GeneratedEmbarkMap map, int x, int y, int caveZ, Random rng)
    {
        var maxDepth = Math.Clamp(caveZ, 1, map.Depth - 1);
        for (var z = 1; z <= maxDepth; z++)
        {
            var tile = map.GetTile(x, y, z);
            if (tile.TileDefId == GeneratedTileDefIds.Magma || tile.FluidType == GeneratedFluidType.Magma)
                break;

            var level = z == maxDepth
                ? (byte)rng.Next(4, 6)
                : (byte)rng.Next(2, 4);
            CarveWater(map, x, y, level, z);
        }
    }

    private static bool IsValidCaveWaterTarget(GeneratedEmbarkMap map, int x, int y, int z)
    {
        if (z <= 0 || z >= map.Depth)
            return false;

        var tile = map.GetTile(x, y, z);
        if (!tile.IsPassable)
            return false;
        if (tile.TileDefId == GeneratedTileDefIds.Magma || tile.FluidType == GeneratedFluidType.Magma)
            return false;
        return true;
    }

    private static void StampCaveSeepPool(GeneratedEmbarkMap map, int x, int y, int z, Random rng)
    {
        if (!IsValidCaveWaterTarget(map, x, y, z))
            return;

        CarveWater(map, x, y, (byte)rng.Next(4, 6), z);
        foreach (var (dx, dy) in CardinalDirections)
        {
            var nx = x + dx;
            var ny = y + dy;
            if (nx <= 0 || ny <= 0 || nx >= map.Width - 1 || ny >= map.Height - 1)
                continue;
            if (!IsValidCaveWaterTarget(map, nx, ny, z))
                continue;
            if (rng.NextDouble() < 0.35)
                continue;

            CarveWater(map, nx, ny, (byte)rng.Next(2, 4), z);
        }
    }

    private static void AddCaveCreatureSpawns(GeneratedEmbarkMap map, Random rng, IReadOnlyList<int> caveLayers)
    {
        if (caveLayers.Count == 0)
            return;

        var occupied = BuildCreatureSpawnOccupancy(map);
        for (var i = 0; i < caveLayers.Count; i++)
        {
            var z = caveLayers[i];
            if (z <= 0 || z >= map.Depth)
                continue;

            var passableCells = CountPassableNonMagmaCellsAtZ(map, z);
            if (passableCells < 24)
                continue;

            var spawnTable = BiomeCreatureTable.GetCave(i + 1);
            if (spawnTable.Count == 0)
                continue;

            var groups = Math.Clamp((passableCells / 190) + rng.Next(1, 3), 1, 8);
            var maxLayerSpawns = Math.Clamp((passableCells / 55) + 2, 3, 22);
            var layerPlaced = 0;

            for (var group = 0; group < groups; group++)
            {
                if (layerPlaced >= maxLayerSpawns)
                    break;

                var entry = PickWeightedSpawnEntry(spawnTable, rng);
                var groupSize = rng.Next(Math.Max(1, entry.MinGroup), Math.Max(entry.MinGroup, entry.MaxGroup) + 1);
                if (!TryFindCreatureGroupOrigin(map, rng, entry, z, out var originX, out var originY))
                    continue;

                var placedThisGroup = 0;
                for (var spawn = 0; spawn < groupSize; spawn++)
                {
                    if (layerPlaced >= maxLayerSpawns)
                        break;

                    if (!TryPlaceCreatureNearOrigin(map, rng, entry, originX, originY, z, occupied))
                        continue;

                    layerPlaced++;
                    placedThisGroup++;
                }

                if (placedThisGroup == 0 && layerPlaced < maxLayerSpawns)
                {
                    if (TryPlaceCreatureNearOrigin(map, rng, entry, originX, originY, z, occupied))
                        layerPlaced++;
                }
            }
        }
    }

    private static int ResolveSurfaceCreatureGroups(GeneratedEmbarkMap map, string biomeId, Random rng)
    {
        var area = map.Width * map.Height;
        var areaBaseline = Math.Clamp((int)MathF.Round(area / 400f), 2, 14);
        var biomeBias = WorldGenContentRegistry.Current.ResolveSurfaceCreatureGroupBias(biomeId);

        var jitter = rng.Next(-1, 2);
        return Math.Clamp(areaBaseline + biomeBias + jitter, 1, 16);
    }

    private static SpawnEntry PickWeightedSpawnEntry(IReadOnlyList<SpawnEntry> entries, Random rng)
    {
        if (entries.Count == 1)
            return entries[0];

        var totalWeight = 0f;
        for (var i = 0; i < entries.Count; i++)
            totalWeight += Math.Max(0.01f, entries[i].Weight);

        var roll = (float)rng.NextDouble() * totalWeight;
        for (var i = 0; i < entries.Count; i++)
        {
            roll -= Math.Max(0.01f, entries[i].Weight);
            if (roll <= 0f)
                return entries[i];
        }

        return entries[^1];
    }

    private static bool TryFindCreatureGroupOrigin(
        GeneratedEmbarkMap map,
        Random rng,
        SpawnEntry entry,
        int z,
        out int originX,
        out int originY)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var x = rng.Next(1, Math.Max(2, map.Width - 1));
            var y = rng.Next(1, Math.Max(2, map.Height - 1));

            if (!IsCreatureSpawnCellValid(map, entry, x, y, z))
                continue;

            originX = x;
            originY = y;
            return true;
        }

        originX = map.Width / 2;
        originY = map.Height / 2;
        return false;
    }

    private static bool TryPlaceCreatureNearOrigin(
        GeneratedEmbarkMap map,
        Random rng,
        SpawnEntry entry,
        int originX,
        int originY,
        int z,
        HashSet<int> occupied)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var radius = attempt switch
            {
                < 4 => 0,
                < 10 => 1,
                < 18 => 2,
                _ => 3,
            };
            var x = originX + rng.Next(-radius, radius + 1);
            var y = originY + rng.Next(-radius, radius + 1);

            if (x <= 0 || y <= 0 || x >= map.Width - 1 || y >= map.Height - 1)
                continue;

            var key = CreatureSpawnOccupancyKey(x, y, z);
            if (!occupied.Add(key))
                continue;
            if (!IsCreatureSpawnCellValid(map, entry, x, y, z))
            {
                occupied.Remove(key);
                continue;
            }

            map.AddCreatureSpawn(new CreatureSpawn(entry.CreatureDefId, x, y, z));
            return true;
        }

        return false;
    }

    private static bool IsCreatureSpawnCellValid(GeneratedEmbarkMap map, SpawnEntry entry, int x, int y, int z)
    {
        if (z < 0 || z >= map.Depth)
            return false;

        if (entry.AvoidEmbarkCenter && z == 0 && IsInCentralEmbarkZone(map.Width, map.Height, x, y))
            return false;

        var tile = map.GetTile(x, y, z);
        if (entry.RequiresWater)
        {
            var hasWater = tile.TileDefId == GeneratedTileDefIds.Water || tile.FluidType == GeneratedFluidType.Water;
            return hasWater && tile.FluidLevel >= MinAquaticSpawnWaterLevel;
        }

        if (!tile.IsPassable)
            return false;
        if (tile.FluidType != GeneratedFluidType.None)
            return false;
        if (tile.TileDefId == GeneratedTileDefIds.Water || tile.TileDefId == GeneratedTileDefIds.Magma)
            return false;
        if (tile.TileDefId == GeneratedTileDefIds.Tree || tile.TileDefId == GeneratedTileDefIds.Staircase)
            return false;

        return true;
    }

    private static HashSet<int> BuildCreatureSpawnOccupancy(GeneratedEmbarkMap map)
    {
        var occupied = new HashSet<int>();
        for (var i = 0; i < map.CreatureSpawns.Count; i++)
        {
            var spawn = map.CreatureSpawns[i];
            occupied.Add(CreatureSpawnOccupancyKey(spawn.X, spawn.Y, spawn.Z));
        }

        return occupied;
    }

    private static int CreatureSpawnOccupancyKey(int x, int y, int z)
        => ((z & 0x03FF) << 22) ^ ((y & 0x07FF) << 11) ^ (x & 0x07FF);

    private static int CountPassableNonMagmaCellsAtZ(GeneratedEmbarkMap map, int z)
    {
        var count = 0;
        for (var y = 1; y < map.Height - 1; y++)
        for (var x = 1; x < map.Width - 1; x++)
        {
            var tile = map.GetTile(x, y, z);
            if (!tile.IsPassable)
                continue;
            if (tile.TileDefId == GeneratedTileDefIds.Magma || tile.FluidType == GeneratedFluidType.Magma)
                continue;
            count++;
        }

        return count;
    }

    private static bool IsOceanBiome(string biomeId)
        => string.Equals(biomeId, MacroBiomeIds.OceanShallow, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(biomeId, MacroBiomeIds.OceanDeep, StringComparison.OrdinalIgnoreCase);

    private static void PlaceCentralStaircase(GeneratedEmbarkMap map)
    {
        var stairX = map.Width / 2;
        var stairY = map.Height / 2;

        map.SetTile(stairX, stairY, 0, StaircaseTile());
        if (map.Depth > 1)
            map.SetTile(stairX, stairY, 1, StaircaseTile());
    }

    private static GeneratedTile ResolveSurfaceTile(string biomeId, bool forceStoneSurface, string? surfaceTileOverrideId)
    {
        var preferredSurfaceTileDefId = ResolvePreferredSurfaceTileDefId(surfaceTileOverrideId);
        if (preferredSurfaceTileDefId is not null)
            return CreateSurfaceTileFromTileDefId(preferredSurfaceTileDefId);

        if (forceStoneSurface)
            return StoneSurfaceTile();

        if (string.Equals(biomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.OceanShallow, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.OceanDeep, StringComparison.OrdinalIgnoreCase))
        {
            return SandSurfaceTile();
        }

        if (string.Equals(biomeId, MacroBiomeIds.MistyMarsh, StringComparison.OrdinalIgnoreCase))
            return MudSurfaceTile();

        if (string.Equals(biomeId, MacroBiomeIds.Tundra, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase))
        {
            return SnowSurfaceTile();
        }

        return GrassSurfaceTile();
    }

    private static string? ResolvePreferredSurfaceTileDefId(string? surfaceTileOverrideId)
    {
        if (string.IsNullOrWhiteSpace(surfaceTileOverrideId))
            return null;

        return surfaceTileOverrideId.Trim().ToLowerInvariant() switch
        {
            GeneratedTileDefIds.Grass => GeneratedTileDefIds.Grass,
            GeneratedTileDefIds.Soil => GeneratedTileDefIds.Soil,
            GeneratedTileDefIds.Sand => GeneratedTileDefIds.Sand,
            GeneratedTileDefIds.Mud => GeneratedTileDefIds.Mud,
            GeneratedTileDefIds.Snow => GeneratedTileDefIds.Snow,
            GeneratedTileDefIds.StoneFloor => GeneratedTileDefIds.StoneFloor,
            _ => null,
        };
    }

    private static GeneratedTile CreateSurfaceTileFromTileDefId(string tileDefId)
    {
        return tileDefId switch
        {
            GeneratedTileDefIds.Sand => SandSurfaceTile(),
            GeneratedTileDefIds.Mud => MudSurfaceTile(),
            GeneratedTileDefIds.Snow => SnowSurfaceTile(),
            GeneratedTileDefIds.Soil => SoilSurfaceTile(),
            GeneratedTileDefIds.StoneFloor => StoneSurfaceTile(),
            _ => GrassSurfaceTile(),
        };
    }

    private static GeneratedTile GrassSurfaceTile()
        => new(GeneratedTileDefIds.Grass, "soil", true);

    private static GeneratedTile SoilSurfaceTile()
        => new(GeneratedTileDefIds.Soil, "soil", true);

    private static GeneratedTile SandSurfaceTile()
        => new(GeneratedTileDefIds.Sand, "sand", true);

    private static GeneratedTile MudSurfaceTile()
        => new(GeneratedTileDefIds.Mud, "mud", true);

    private static GeneratedTile SnowSurfaceTile()
        => new(GeneratedTileDefIds.Snow, "soil", true);

    private static GeneratedTile StoneSurfaceTile()
        => new(GeneratedTileDefIds.StoneFloor, "granite", true);

    private static GeneratedTile RoadSurfaceTile(string? fallbackMaterialId)
        => new(
            GeneratedTileDefIds.StoneBrick,
            string.IsNullOrWhiteSpace(fallbackMaterialId) ? "granite" : fallbackMaterialId,
            true);

    private static GeneratedTile StoneWallTile(string materialId)
        => new(GeneratedTileDefIds.StoneWall, materialId, false);

    private static GeneratedTile SoilWallTile(string materialId)
        => new(GeneratedTileDefIds.SoilWall, materialId, false);

    private static GeneratedTile WallTile(string rockTypeId)
    {
        return rockTypeId switch
        {
            RockTypeIds.Limestone => StoneWallTile("limestone"),
            RockTypeIds.Sandstone => StoneWallTile("sandstone"),
            RockTypeIds.Basalt => StoneWallTile("basalt"),
            RockTypeIds.Shale => StoneWallTile("shale"),
            RockTypeIds.Slate => StoneWallTile("slate"),
            RockTypeIds.Marble => StoneWallTile("marble"),
            _ => StoneWallTile("granite"),
        };
    }

    private static GeneratedTile ResolveOutcropWallTile(Random rng)
    {
        return rng.Next(6) switch
        {
            1 => StoneWallTile("limestone"),
            2 => StoneWallTile("sandstone"),
            3 => StoneWallTile("basalt"),
            4 => StoneWallTile("shale"),
            5 => StoneWallTile("slate"),
            _ => StoneWallTile("granite"),
        };
    }

    private static bool IsSurfaceRockWallTile(string tileDefId)
    {
        return tileDefId == GeneratedTileDefIds.StoneWall;
    }

    private static GeneratedTile TreeTile(string speciesId)
        => new(GeneratedTileDefIds.Tree, "wood", false, TreeSpeciesId: speciesId);

    private static GeneratedTile ShallowWaterTile(byte level)
        => new(GeneratedTileDefIds.Water, null, true, GeneratedFluidType.Water, level);

    private static GeneratedTile MagmaSeaTile()
        => new(GeneratedTileDefIds.Magma, null, true, GeneratedFluidType.Magma, 7);

    private static GeneratedTile StaircaseTile()
        => new(GeneratedTileDefIds.Staircase, "granite", true);
}

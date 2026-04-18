using System;
using System.Collections.Generic;
using DwarfFortress.WorldGen.Config;
using DwarfFortress.WorldGen.Creatures;
using DwarfFortress.WorldGen.Geology;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Local;

namespace DwarfFortress.WorldGen.Maps;

public static partial class EmbarkGenerator
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
    private readonly record struct HydrologyCandidate(int X, int Y, float Score);

    private sealed class LocalGenerationContext
    {
        public LocalGenerationContext(
            LocalGenerationSettings settings,
            int seed,
            int continuitySeed,
            GeneratedEmbarkMap map,
            LocalRegionFieldMaps? regionFieldMaps,
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
            ContinuitySeed = continuitySeed;
            Map = map;
            RegionFieldMaps = regionFieldMaps;
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
        public int ContinuitySeed { get; }
        public GeneratedEmbarkMap Map { get; }
        public LocalRegionFieldMaps? RegionFieldMaps { get; }
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
        => Generate(settings, seed, regionFieldMaps: null);

    public static GeneratedEmbarkMap Generate(
        LocalGenerationSettings settings,
        int seed,
        LocalRegionFieldMaps? regionFieldMaps)
    {
        ValidateSettings(settings);

        var context = CreateGenerationContext(settings, seed, regionFieldMaps);
        CaptureStageSnapshot(context, EmbarkGenerationStageId.Inputs);
        foreach (var pass in GenerationPasses)
        {
            pass.Execute(context);
            CaptureStageSnapshot(context, pass.StageId);
        }

        context.Map.Diagnostics = new EmbarkGenerationDiagnostics(context.Seed, context.StageSnapshots.ToArray());

        return context.Map;
    }

    private static void ValidateSettings(LocalGenerationSettings settings)
    {
        if (settings.Width <= 0) throw new ArgumentOutOfRangeException(nameof(settings.Width));
        if (settings.Height <= 0) throw new ArgumentOutOfRangeException(nameof(settings.Height));
        if (settings.Depth <= 0) throw new ArgumentOutOfRangeException(nameof(settings.Depth));
    }

    private static LocalGenerationContext CreateGenerationContext(
        LocalGenerationSettings settings,
        int seed,
        LocalRegionFieldMaps? regionFieldMaps)
    {
        var resolvedSettings = ResolveContinuitySettings(settings, seed);
        var map = new GeneratedEmbarkMap(resolvedSettings.Width, resolvedSettings.Height, resolvedSettings.Depth);
        var rng = new Random(seed);
        var continuitySeed = resolvedSettings.ContinuitySeed ?? seed;
        var biome = ResolveBiomePreset(resolvedSettings.BiomeOverrideId, seed);
        var wetnessBias = Math.Clamp(resolvedSettings.ParentWetnessBias, -1f, 1f);
        var soilDepthBias = Math.Clamp(resolvedSettings.ParentSoilDepthBias, -1f, 1f);
        var forestPatchBias = Math.Clamp(resolvedSettings.ForestPatchBias, -1f, 1f);
        var ecologyTreeBias = Math.Clamp(resolvedSettings.TreeDensityBias + (wetnessBias * 0.45f) + (soilDepthBias * 0.30f), -1f, 1f);
        var ecologyOutcropBias = Math.Clamp(resolvedSettings.OutcropBias - (wetnessBias * 0.20f) - (soilDepthBias * 0.40f), -1f, 1f);
        var streamBonus = wetnessBias >= 0.40f ? 1 : (wetnessBias <= -0.45f ? -1 : 0);
        var marshBonus = (int)MathF.Round(Math.Max(0f, wetnessBias) * 4f);

        var (treeCoverMin, treeCoverMax) = ApplyCoverageBias(biome.TreeCoverMin, biome.TreeCoverMax, ecologyTreeBias);
        (treeCoverMin, treeCoverMax) = ApplyForestCoverageTarget(treeCoverMin, treeCoverMax, resolvedSettings.ForestCoverageTarget, biome);
        var (outcropMin, outcropMax) = ApplyRangeBias(biome.OutcropMin, biome.OutcropMax, ecologyOutcropBias, 0);
        var streamBands = Math.Max(0, biome.StreamBands + resolvedSettings.StreamBandBias + streamBonus);
        var marshPoolCount = Math.Max(0, biome.MarshPoolCount + resolvedSettings.MarshPoolBias + marshBonus);
        var strataProfile = StrataProfileRegistry.Resolve(resolvedSettings.GeologyProfileId);
        var useStoneSurface = resolvedSettings.StoneSurfaceOverride ?? biome.StoneSurface;
        var terrain = BuildSurfaceTerrainMap(resolvedSettings.Width, resolvedSettings.Height, continuitySeed, biome, resolvedSettings.NoiseOriginX, resolvedSettings.NoiseOriginY);
        var moisture = BuildSurfaceMoistureMap(
            resolvedSettings.Width,
            resolvedSettings.Height,
            continuitySeed,
            terrain,
            biome,
            wetnessBias,
            soilDepthBias,
            resolvedSettings.NoiseOriginX,
            resolvedSettings.NoiseOriginY);
        var canopyMask = BuildCanopyMaskMap(resolvedSettings.Width, resolvedSettings.Height, continuitySeed, resolvedSettings.NoiseOriginX, resolvedSettings.NoiseOriginY);
        var forestPatchMask = BuildForestPatchMaskMap(resolvedSettings.Width, resolvedSettings.Height, continuitySeed, forestPatchBias, resolvedSettings.NoiseOriginX, resolvedSettings.NoiseOriginY);
        var forestOpeningMask = BuildForestOpeningMaskMap(resolvedSettings.Width, resolvedSettings.Height, continuitySeed, resolvedSettings.NoiseOriginX, resolvedSettings.NoiseOriginY);
        if (regionFieldMaps is not null)
            ApplyRegionFieldMaps(moisture, canopyMask, forestPatchMask, forestOpeningMask, regionFieldMaps);
        else
            ApplyEcologyEdgeDescriptors(moisture, canopyMask, forestPatchMask, forestOpeningMask, resolvedSettings.EcologyEdges);
        var caveLayers = ResolveCaveLayerDepths(map.Depth);
        var surface = ResolveSurfaceTile(biome.Id, useStoneSurface, resolvedSettings.SurfaceTileOverrideId);

        return new LocalGenerationContext(
            resolvedSettings,
            seed,
            continuitySeed,
            map,
            regionFieldMaps,
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

    private static LocalGenerationSettings ResolveContinuitySettings(LocalGenerationSettings settings, int seed)
    {
        if (settings.ContinuityContract is not LocalContinuityContract continuityContract)
        {
            return settings with
            {
                ContinuitySeed = settings.ContinuitySeed ?? seed,
            };
        }

        return settings with
        {
            BiomeOverrideId = continuityContract.BiomeOverrideId ?? settings.BiomeOverrideId,
            TreeDensityBias = continuityContract.TreeDensityBias ?? settings.TreeDensityBias,
            OutcropBias = continuityContract.OutcropBias ?? settings.OutcropBias,
            StreamBandBias = continuityContract.StreamBandBias ?? settings.StreamBandBias,
            MarshPoolBias = continuityContract.MarshPoolBias ?? settings.MarshPoolBias,
            ParentWetnessBias = continuityContract.ParentWetnessBias ?? settings.ParentWetnessBias,
            ParentSoilDepthBias = continuityContract.ParentSoilDepthBias ?? settings.ParentSoilDepthBias,
            GeologyProfileId = continuityContract.GeologyProfileId ?? settings.GeologyProfileId,
            StoneSurfaceOverride = continuityContract.StoneSurfaceOverride ?? settings.StoneSurfaceOverride,
            RiverPortals = continuityContract.RiverPortals ?? settings.RiverPortals,
            ForestPatchBias = continuityContract.ForestPatchBias ?? settings.ForestPatchBias,
            SettlementInfluence = continuityContract.SettlementInfluence ?? settings.SettlementInfluence,
            RoadInfluence = continuityContract.RoadInfluence ?? settings.RoadInfluence,
            SettlementAnchors = continuityContract.SettlementAnchors ?? settings.SettlementAnchors,
            RoadPortals = continuityContract.RoadPortals ?? settings.RoadPortals,
            SurfaceTileOverrideId = continuityContract.SurfaceTileOverrideId ?? settings.SurfaceTileOverrideId,
            ForestCoverageTarget = continuityContract.ForestCoverageTarget ?? settings.ForestCoverageTarget,
            EcologyEdges = continuityContract.EcologyEdges ?? settings.EcologyEdges,
            NoiseOriginX = continuityContract.NoiseOriginX ?? settings.NoiseOriginX,
            NoiseOriginY = continuityContract.NoiseOriginY ?? settings.NoiseOriginY,
            ContinuitySeed = continuityContract.ContinuitySeed ?? settings.ContinuitySeed ?? seed,
            SurfaceIntentGrid = continuityContract.SurfaceIntentGrid ?? settings.SurfaceIntentGrid,
        };
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
            context.ContinuitySeed,
            context.Settings.SurfaceTileOverrideId,
            context.Settings.SurfaceIntentGrid,
            context.RegionFieldMaps,
            context.Settings.NoiseOriginX,
            context.Settings.NoiseOriginY);
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
            AddAnchoredStreams(context.Map, context.Terrain, context.Settings.RiverPortals);
        }
        else if (context.RegionFieldMaps is not null)
        {
            AddFieldGuidedStreams(
                context.Map,
                context.ContinuitySeed,
                context.StreamBands,
                context.Terrain,
                context.Moisture,
                context.RegionFieldMaps,
                context.Settings.NoiseOriginX,
                context.Settings.NoiseOriginY);
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
            context.ContinuitySeed,
            context.TreeCoverMin,
            context.TreeCoverMax,
            context.Terrain,
            context.Moisture,
            context.CanopyMask,
            context.ForestPatchMask,
            context.ForestOpeningMask,
            context.RegionFieldMaps,
            context.BiomeId,
            context.ForestPatchBias,
            context.Settings.NoiseOriginX,
            context.Settings.NoiseOriginY);
        if (context.RegionFieldMaps is not null)
        {
            AddFieldGuidedOutcrops(
                context.Map,
                context.ContinuitySeed,
                context.OutcropMin,
                context.OutcropMax,
                context.Terrain,
                context.RegionFieldMaps,
                context.Settings.NoiseOriginX,
                context.Settings.NoiseOriginY);
        }
        else
        {
            AddOutcrops(context.Map, context.Rng, context.OutcropMin, context.OutcropMax, context.Terrain);
        }
        if (context.RegionFieldMaps is not null)
        {
            AddFieldGuidedMarshPools(
                context.Map,
                context.ContinuitySeed,
                context.MarshPoolCount,
                context.Terrain,
                context.Moisture,
                context.RegionFieldMaps,
                context.Settings.NoiseOriginX,
                context.Settings.NoiseOriginY);
        }
        else
        {
            AddMarshPools(context.Map, context.Rng, context.MarshPoolCount, context.Terrain, context.Moisture);
        }
    }

    private static void RunHydrologyPolishStage(LocalGenerationContext context)
    {
        FloodOceanSurface(context.Map, context.Rng, context.BiomeId, context.Terrain);
        HarmonizeSurfaceWaterDepths(context.Map, context.Terrain, context.BiomeId);
        ApplyRiparianSurfaceTransitions(
            context.Map,
            context.BiomeId,
            context.Terrain,
            context.Moisture,
            context.RegionFieldMaps,
            context.ContinuitySeed,
            context.Settings.NoiseOriginX,
            context.Settings.NoiseOriginY);
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
        EnsureSurfaceConnectivity(context.Map, context.Surface);
        PlaceCentralStaircase(context.Map);
        AddPlants(
            context.Map,
            context.ContinuitySeed,
            context.Terrain,
            context.Moisture,
            context.ForestPatchMask,
            context.ForestOpeningMask,
            context.BiomeId,
            context.RegionFieldMaps,
            context.Settings.NoiseOriginX,
            context.Settings.NoiseOriginY);
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
        string? surfaceTileOverrideId,
        LocalSurfaceIntentGrid? surfaceIntentGrid,
        LocalRegionFieldMaps? regionFieldMaps,
        int noiseOriginX,
        int noiseOriginY)
    {
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (!tile.IsPassable || tile.FluidType != GeneratedFluidType.None)
                continue;

            var fx = ResolveNoiseSampleCoord(noiseOriginX, x, map.Width);
            var fy = ResolveNoiseSampleCoord(noiseOriginY, y, map.Height);
            var surfaceSelectorNoise = CoherentNoise.DomainWarpedFractal2D(
                seed, fx * 1.18f, fy * 1.18f, octaves: 2, lacunarity: 2f, gain: 0.54f, warpStrength: 0.16f, salt: 277);
            var surfaceSelector = Math.Clamp((surfaceSelectorNoise * 0.5f) + 0.5f, 0f, 0.9999f);
            var preferredSurfaceTileDefId = ResolvePreferredSurfaceTileDefId(
                surfaceTileOverrideId,
                surfaceIntentGrid,
                regionFieldMaps,
                x,
                y,
                map.Width,
                map.Height,
                surfaceSelector);
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

    private static void ApplyRiparianSurfaceTransitions(
        GeneratedEmbarkMap map,
        string biomeId,
        float[,] terrain,
        float[,] moisture,
        LocalRegionFieldMaps? regionFieldMaps,
        int seed,
        int noiseOriginX,
        int noiseOriginY)
    {
        var isSnowBiome = string.Equals(biomeId, MacroBiomeIds.Tundra, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(biomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(biomeId, MacroBiomeIds.BorealForest, StringComparison.OrdinalIgnoreCase);
        var isAridBiome = string.Equals(biomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(biomeId, MacroBiomeIds.WindsweptSteppe, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(biomeId, MacroBiomeIds.Savanna, StringComparison.OrdinalIgnoreCase);
        var isMarshBiome = string.Equals(biomeId, MacroBiomeIds.MistyMarsh, StringComparison.OrdinalIgnoreCase);
        var isHighland = string.Equals(biomeId, MacroBiomeIds.Highland, StringComparison.OrdinalIgnoreCase);

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (!tile.IsPassable || tile.FluidType != GeneratedFluidType.None)
                continue;
            if (tile.TileDefId is GeneratedTileDefIds.Water or GeneratedTileDefIds.Magma or GeneratedTileDefIds.Tree or GeneratedTileDefIds.Staircase)
                continue;
            if (x == 0 || y == 0 || x == map.Width - 1 || y == map.Height - 1)
                continue;

            var fx = ResolveNoiseSampleCoord(noiseOriginX, x, map.Width);
            var fy = ResolveNoiseSampleCoord(noiseOriginY, y, map.Height);
            var rippleNoise = CoherentNoise.Fractal2D(
                seed, fx * 7.8f, fy * 7.8f, octaves: 2, lacunarity: 2f, gain: 0.5f, salt: 557);

            var localWetness = 0f;
            if (TryMeasureNearbyWater(map, x, y, maxRadius: 3, out var nearestDistance, out var nearbyWaterCount, out var maxNeighborWaterLevel) && nearestDistance <= 2)
            {
                var shoreStrength = nearestDistance <= 1 ? 0.85f : 0.58f;
                localWetness = Math.Clamp(
                    (shoreStrength * 0.52f) +
                    ((nearbyWaterCount / 12f) * 0.28f) +
                    ((maxNeighborWaterLevel / 7f) * 0.14f) +
                    (rippleNoise * 0.06f),
                    0f,
                    1f);
            }

            var fieldWetness = regionFieldMaps is null
                ? 0f
                : Math.Clamp(
                    (ResolveFieldRiparianBoost(regionFieldMaps, moisture, terrain, x, y) * 0.88f) +
                    (rippleNoise * 0.12f),
                    0f,
                    1f);
            var wetness = Math.Max(localWetness, fieldWetness);
            if (wetness <= 0f)
                continue;

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

    private static void ApplyEcologyEdgeDescriptors(
        float[,] moisture,
        float[,] canopyMask,
        float[,] forestPatchMask,
        float[,] forestOpeningMask,
        EcologyEdgeDescriptors? ecologyEdges)
    {
        if (!ecologyEdges.HasValue)
            return;

        var width = moisture.GetLength(0);
        var height = moisture.GetLength(1);
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            if (!TryResolveEcologyEdgeInfluence(ecologyEdges.Value, width, height, x, y, out var profile, out var edgeWeight))
                continue;

            var ecologySignal = Math.Clamp(
                (MathF.Abs(profile.VegetationDensity - 0.5f) * 0.45f) +
                (MathF.Abs(profile.VegetationSuitability - 0.5f) * 0.35f) +
                (MathF.Abs(profile.SoilDepth - 0.5f) * 0.10f) +
                (MathF.Abs(profile.Groundwater - 0.5f) * 0.10f),
                0f,
                1f);
            var influence = Math.Clamp(edgeWeight * (0.10f + (ecologySignal * 0.65f)), 0f, 0.78f);
            if (influence <= 0f)
                continue;

            var moistureTarget = Math.Clamp(
                (profile.Groundwater * 0.48f) +
                (profile.SoilDepth * 0.22f) +
                (profile.VegetationSuitability * 0.20f) +
                (profile.VegetationDensity * 0.10f),
                0f,
                1f);
            var canopyTarget = Math.Clamp(
                (profile.VegetationDensity * 0.64f) +
                (profile.VegetationSuitability * 0.20f) +
                (profile.Groundwater * 0.16f),
                0f,
                1f);
            var patchTarget = Math.Clamp(
                (profile.VegetationDensity * 0.72f) +
                (profile.VegetationSuitability * 0.28f),
                0f,
                1f);
            var openingTarget = Math.Clamp(
                1f - ((profile.VegetationDensity * 0.72f) +
                      (profile.VegetationSuitability * 0.18f) +
                      (profile.Groundwater * 0.10f)),
                0f,
                1f);

            moisture[x, y] = Math.Clamp(
                (moisture[x, y] * (1f - (influence * 0.55f))) + (moistureTarget * (influence * 0.55f)),
                0f,
                1f);
            canopyMask[x, y] = Math.Clamp(
                (canopyMask[x, y] * (1f - (influence * 0.40f))) + (canopyTarget * (influence * 0.40f)),
                0f,
                1f);
            forestPatchMask[x, y] = Math.Clamp(
                (forestPatchMask[x, y] * (1f - (influence * 0.62f))) + (patchTarget * (influence * 0.62f)),
                0f,
                1f);
            forestOpeningMask[x, y] = Math.Clamp(
                (forestOpeningMask[x, y] * (1f - (influence * 0.50f))) + (openingTarget * (influence * 0.50f)),
                0f,
                1f);
        }
    }

    private static void ApplyRegionFieldMaps(
        float[,] moisture,
        float[,] canopyMask,
        float[,] forestPatchMask,
        float[,] forestOpeningMask,
        LocalRegionFieldMaps regionFieldMaps)
    {
        var width = moisture.GetLength(0);
        var height = moisture.GetLength(1);
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            var hydrologyInfluence = Math.Clamp(
                (regionFieldMaps.RiverInfluence[x, y] * 0.34f) +
                (regionFieldMaps.LakeInfluence[x, y] * 0.18f) +
                (regionFieldMaps.FlowAccumulationBand[x, y] * 0.20f) +
                (regionFieldMaps.RiverDischargeBand[x, y] * 0.16f) +
                (regionFieldMaps.RiverOrderBand[x, y] * 0.12f),
                0f,
                1f);
            var vegetationDensity = regionFieldMaps.VegetationDensity[x, y];
            var vegetationSuitability = regionFieldMaps.VegetationSuitability[x, y];
            var soilDepth = regionFieldMaps.SoilDepth[x, y];
            var groundwater = regionFieldMaps.Groundwater[x, y];
            var moistureBand = regionFieldMaps.MoistureBand[x, y];
            var slope = regionFieldMaps.Slope[x, y];

            var moistureTarget = Math.Clamp(
                (moistureBand * 0.32f) +
                (groundwater * 0.32f) +
                (soilDepth * 0.12f) +
                (vegetationSuitability * 0.12f) +
                (hydrologyInfluence * 0.12f),
                0f,
                1f);
            var canopyTarget = Math.Clamp(
                (vegetationDensity * 0.58f) +
                (vegetationSuitability * 0.20f) +
                (groundwater * 0.12f) +
                (hydrologyInfluence * 0.10f) -
                (slope * 0.08f),
                0f,
                1f);
            var patchTarget = Math.Clamp(
                (vegetationDensity * 0.52f) +
                (vegetationSuitability * 0.24f) +
                (moistureBand * 0.10f) +
                (hydrologyInfluence * 0.14f) -
                (slope * 0.06f),
                0f,
                1f);
            var openingTarget = Math.Clamp(
                1f - ((vegetationDensity * 0.62f) +
                      (vegetationSuitability * 0.18f) +
                      (hydrologyInfluence * 0.12f) +
                      (groundwater * 0.08f)),
                0f,
                1f);

            moisture[x, y] = Math.Clamp((moisture[x, y] * 0.52f) + (moistureTarget * 0.48f), 0f, 1f);
            canopyMask[x, y] = Math.Clamp((canopyMask[x, y] * 0.42f) + (canopyTarget * 0.58f), 0f, 1f);
            forestPatchMask[x, y] = Math.Clamp((forestPatchMask[x, y] * 0.46f) + (patchTarget * 0.54f), 0f, 1f);
            forestOpeningMask[x, y] = Math.Clamp((forestOpeningMask[x, y] * 0.48f) + (openingTarget * 0.52f), 0f, 1f);
        }
    }

    private static bool TryResolveEcologyEdgeInfluence(
        EcologyEdgeDescriptors ecologyEdges,
        int width,
        int height,
        int x,
        int y,
        out EcologyEdgeProfile profile,
        out float edgeWeight)
    {
        var northWeight = ResolveEcologyEdgeWeight(y, height);
        var eastWeight = ResolveEcologyEdgeWeight((width - 1) - x, width);
        var southWeight = ResolveEcologyEdgeWeight((height - 1) - y, height);
        var westWeight = ResolveEcologyEdgeWeight(x, width);
        var totalWeight = northWeight + eastWeight + southWeight + westWeight;
        if (totalWeight <= 0f)
        {
            profile = EcologyEdgeProfile.Neutral;
            edgeWeight = 0f;
            return false;
        }

        profile = new EcologyEdgeProfile(
            VegetationDensity: ((ecologyEdges.North.VegetationDensity * northWeight) + (ecologyEdges.East.VegetationDensity * eastWeight) + (ecologyEdges.South.VegetationDensity * southWeight) + (ecologyEdges.West.VegetationDensity * westWeight)) / totalWeight,
            VegetationSuitability: ((ecologyEdges.North.VegetationSuitability * northWeight) + (ecologyEdges.East.VegetationSuitability * eastWeight) + (ecologyEdges.South.VegetationSuitability * southWeight) + (ecologyEdges.West.VegetationSuitability * westWeight)) / totalWeight,
            SoilDepth: ((ecologyEdges.North.SoilDepth * northWeight) + (ecologyEdges.East.SoilDepth * eastWeight) + (ecologyEdges.South.SoilDepth * southWeight) + (ecologyEdges.West.SoilDepth * westWeight)) / totalWeight,
            Groundwater: ((ecologyEdges.North.Groundwater * northWeight) + (ecologyEdges.East.Groundwater * eastWeight) + (ecologyEdges.South.Groundwater * southWeight) + (ecologyEdges.West.Groundwater * westWeight)) / totalWeight);
        edgeWeight = Math.Clamp(totalWeight, 0f, 1f);
        return true;
    }

    private static float ResolveEcologyEdgeWeight(int distanceFromEdge, int extent)
    {
        var band = Math.Clamp(extent / 6f, 5f, 10f);
        var normalized = 1f - Math.Clamp(distanceFromEdge / band, 0f, 1f);
        return normalized * normalized * (3f - (2f * normalized));
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

    private static void AddFieldGuidedStreams(
        GeneratedEmbarkMap map,
        int continuitySeed,
        int bands,
        float[,] terrain,
        float[,] moisture,
        LocalRegionFieldMaps regionFieldMaps,
        int noiseOriginX,
        int noiseOriginY)
    {
        if (bands <= 0)
            return;

        var candidates = BuildFieldGuidedStreamCandidates(
            map,
            terrain,
            moisture,
            regionFieldMaps,
            continuitySeed,
            noiseOriginX,
            noiseOriginY);
        if (candidates.Count == 0)
        {
            var fallbackRng = new Random(SeedHash.Hash(continuitySeed, noiseOriginX, noiseOriginY, 17611));
            AddStreams(map, fallbackRng, bands, terrain);
            return;
        }

        var maxSteps = Math.Max(10, map.Width + map.Height);
        var minSpacing = Math.Clamp(Math.Min(map.Width, map.Height) / 8, 4, 8);
        var placedSources = new List<HydrologyCandidate>(bands);

        for (var i = 0; i < candidates.Count && placedSources.Count < bands; i++)
        {
            var candidate = candidates[i];
            if (!IsFarEnoughFromHydrologyCandidates(placedSources, candidate.X, candidate.Y, minSpacing))
                continue;

            var path = TraceFieldGuidedPath(
                map,
                terrain,
                moisture,
                regionFieldMaps,
                continuitySeed,
                noiseOriginX,
                noiseOriginY,
                candidate.X,
                candidate.Y,
                maxSteps);
            if (path.Count < 4)
                continue;

            for (var pathIndex = 0; pathIndex < path.Count; pathIndex++)
            {
                var (x, y) = path[pathIndex];
                var support = ResolveHydrologySupport(regionFieldMaps, moisture, terrain, x, y);
                var baseLevel = support >= 0.82f ? 4 : (support >= 0.64f ? 3 : 2);
                CarveWater(map, x, y, (byte)baseLevel, allowBoundary: true);

                if (support >= 0.78f && pathIndex > 0 && pathIndex < path.Count - 1)
                {
                    foreach (var (dx, dy) in CardinalDirections)
                    {
                        if (ResolveTileJitter(continuitySeed, noiseOriginX, noiseOriginY, x + dx, y + dy, 17653, 0.5f) <= 0f)
                            continue;

                        CarveWater(map, x + dx, y + dy, 1, allowBoundary: true);
                    }
                }
            }

            placedSources.Add(candidate);
        }

        if (placedSources.Count == 0)
        {
            var fallbackRng = new Random(SeedHash.Hash(continuitySeed, noiseOriginX, noiseOriginY, 17641));
            AddStreams(map, fallbackRng, bands, terrain);
        }
    }

    private static void AddAnchoredStreams(
        GeneratedEmbarkMap map,
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
            LocalMapEdge.North => (x, 0),
            LocalMapEdge.East => (map.Width - 1, y),
            LocalMapEdge.South => (x, map.Height - 1),
            LocalMapEdge.West => (0, y),
            _ => (x, y),
        };
    }

    private static (int X, int Y) ResolveBoundaryInteriorPoint(GeneratedEmbarkMap map, (int X, int Y) point)
    {
        var x = point.X;
        var y = point.Y;

        if (x == 0)
            x = Math.Min(1, map.Width - 1);
        else if (x == map.Width - 1)
            x = Math.Max(0, map.Width - 2);

        if (y == 0)
            y = Math.Min(1, map.Height - 1);
        else if (y == map.Height - 1)
            y = Math.Max(0, map.Height - 2);

        return (x, y);
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
        var entry = ResolveBoundaryInteriorPoint(map, sourceCoord);
        var exit = ResolveBoundaryInteriorPoint(map, target);
        var path = FindAnchoredPath(map, terrain, entry, exit);
        if (path.Count == 0)
            path = BuildFallbackAnchoredCorridor(map, entry, exit);

        var stitchedPath = new List<(int X, int Y)>(path.Count + 2);
        AddPathPoint(stitchedPath, sourceCoord.X, sourceCoord.Y);
        foreach (var (x, y) in path)
            AddPathPoint(stitchedPath, x, y);
        AddPathPoint(stitchedPath, target.X, target.Y);

        foreach (var (x, y) in stitchedPath)
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
            CarveWater(map, x + dx, y + dy, level, allowBoundary: true);
        }
    }

    private static void ApplySurfaceWaterMoistureFeedback(GeneratedEmbarkMap map, float[,] moisture)
    {
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
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
                if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
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

    private static List<HydrologyCandidate> BuildFieldGuidedStreamCandidates(
        GeneratedEmbarkMap map,
        float[,] terrain,
        float[,] moisture,
        LocalRegionFieldMaps regionFieldMaps,
        int continuitySeed,
        int noiseOriginX,
        int noiseOriginY)
    {
        var candidates = new List<HydrologyCandidate>(map.Width * map.Height / 6);
        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                continue;

            var support = ResolveHydrologySupport(regionFieldMaps, moisture, terrain, x, y);
            var sourceScore = Math.Clamp(
                (support * 0.62f) +
                (terrain[x, y] * 0.20f) +
                (regionFieldMaps.Slope[x, y] * 0.10f) +
                ResolveTileJitter(continuitySeed, noiseOriginX, noiseOriginY, x, y, 17671, 0.08f),
                0f,
                1f);

            if (terrain[x, y] < 0.42f || support < 0.34f || sourceScore < 0.44f)
                continue;

            candidates.Add(new HydrologyCandidate(x, y, sourceScore));
        }

        candidates.Sort(static (left, right) => CompareHydrologyCandidates(left, right));
        return candidates;
    }

    private static int CompareHydrologyCandidates(HydrologyCandidate left, HydrologyCandidate right)
    {
        var scoreCompare = right.Score.CompareTo(left.Score);
        if (scoreCompare != 0)
            return scoreCompare;

        var yCompare = left.Y.CompareTo(right.Y);
        if (yCompare != 0)
            return yCompare;

        return left.X.CompareTo(right.X);
    }

    private static bool IsFarEnoughFromHydrologyCandidates(
        List<HydrologyCandidate> candidates,
        int x,
        int y,
        int minDistance)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            var distance = Math.Abs(candidates[i].X - x) + Math.Abs(candidates[i].Y - y);
            if (distance < minDistance)
                return false;
        }

        return true;
    }

    private static List<(int X, int Y)> TraceFieldGuidedPath(
        GeneratedEmbarkMap map,
        float[,] terrain,
        float[,] moisture,
        LocalRegionFieldMaps regionFieldMaps,
        int continuitySeed,
        int noiseOriginX,
        int noiseOriginY,
        int sourceX,
        int sourceY,
        int maxSteps)
    {
        var path = new List<(int X, int Y)>(maxSteps);
        var visited = new bool[map.Width, map.Height];
        var x = sourceX;
        var y = sourceY;
        var previousDx = 0;
        var previousDy = 0;

        for (var step = 0; step < maxSteps; step++)
        {
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
                break;
            if (visited[x, y])
                break;

            visited[x, y] = true;
            path.Add((x, y));
            if (x == 0 || y == 0 || x == map.Width - 1 || y == map.Height - 1)
                break;

            var bestX = x;
            var bestY = y;
            var bestDx = 0;
            var bestDy = 0;
            var bestScore = float.MaxValue;

            foreach (var (dx, dy) in CardinalDirections)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
                    continue;
                if (visited[nx, ny])
                    continue;
                if (IsInCentralEmbarkZone(map.Width, map.Height, nx, ny) &&
                    nx > 0 && ny > 0 && nx < map.Width - 1 && ny < map.Height - 1)
                {
                    continue;
                }

                var support = ResolveHydrologySupport(regionFieldMaps, moisture, terrain, nx, ny);
                var descent = terrain[x, y] - terrain[nx, ny];
                var descentBonus = descent > 0f ? descent * 0.44f : 0f;
                var supportBonus = support * 0.26f;
                var boundaryBonus = nx == 0 || ny == 0 || nx == map.Width - 1 || ny == map.Height - 1 ? 0.14f : 0f;
                var turnPenalty = previousDx == dx && previousDy == dy ? 0f : 0.04f;
                var jitter = ResolveTileJitter(continuitySeed, noiseOriginX, noiseOriginY, nx, ny, 17713, 0.015f);
                var score = terrain[nx, ny] - descentBonus - supportBonus - boundaryBonus + turnPenalty + jitter;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                bestX = nx;
                bestY = ny;
                bestDx = dx;
                bestDy = dy;
            }

            if (bestX == x && bestY == y)
                break;

            previousDx = bestDx;
            previousDy = bestDy;
            x = bestX;
            y = bestY;
        }

        return path;
    }

    private static float ResolveHydrologySupport(
        LocalRegionFieldMaps regionFieldMaps,
        float[,] moisture,
        float[,] terrain,
        int x,
        int y)
    {
        return Math.Clamp(
            (regionFieldMaps.MoistureBand[x, y] * 0.18f) +
            (regionFieldMaps.Groundwater[x, y] * 0.18f) +
            (moisture[x, y] * 0.12f) +
            (regionFieldMaps.RiverInfluence[x, y] * 0.12f) +
            (regionFieldMaps.LakeInfluence[x, y] * 0.08f) +
            (regionFieldMaps.FlowAccumulationBand[x, y] * 0.14f) +
            (regionFieldMaps.RiverDischargeBand[x, y] * 0.10f) +
            (regionFieldMaps.RiverOrderBand[x, y] * 0.06f) +
            ((1f - regionFieldMaps.Slope[x, y]) * 0.02f),
            0f,
            1f);
    }

    private static float ResolveFieldRiparianBoost(
        LocalRegionFieldMaps regionFieldMaps,
        float[,] moisture,
        float[,] terrain,
        int x,
        int y)
    {
        return Math.Clamp(
            (regionFieldMaps.RiverInfluence[x, y] * 0.24f) +
            (regionFieldMaps.LakeInfluence[x, y] * 0.14f) +
            (regionFieldMaps.FlowAccumulationBand[x, y] * 0.22f) +
            (regionFieldMaps.RiverDischargeBand[x, y] * 0.18f) +
            (regionFieldMaps.RiverOrderBand[x, y] * 0.08f) +
            (regionFieldMaps.Groundwater[x, y] * 0.06f) +
            (moisture[x, y] * 0.05f) +
            ((1f - terrain[x, y]) * 0.03f),
            0f,
            1f);
    }

    private static float ResolveRiparianBoost(
        GeneratedEmbarkMap map,
        LocalRegionFieldMaps? regionFieldMaps,
        float[,] moisture,
        float[,] terrain,
        int x,
        int y)
    {
        if (regionFieldMaps is not null)
            return ResolveFieldRiparianBoost(regionFieldMaps, moisture, terrain, x, y);

        return EstimateRiparianBoost(map, x, y);
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

    private static void CarveWater(GeneratedEmbarkMap map, int x, int y, byte level, int z = 0, bool allowBoundary = false)
    {
        if (IsOutsideSurfaceBounds(map, x, y, allowBoundary))
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

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
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

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
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

                if (cx == 0) touchesWest = true;
                if (cx == map.Width - 1) touchesEast = true;
                if (cy == 0) touchesNorth = true;
                if (cy == map.Height - 1) touchesSouth = true;

                var level = map.GetTile(cx, cy, 0).FluidLevel;
                if (level > maxLevel)
                    maxLevel = level;

                foreach (var (dx, dy) in CardinalDirections)
                {
                    var nx = cx + dx;
                    var ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
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
                    if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
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
            if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
                return true;
            if (!IsSurfaceWater(map, nx, ny))
                return true;
        }

        return false;
    }

    private static void AddTrees(
        GeneratedEmbarkMap map,
        int continuitySeed,
        float minCoverage,
        float maxCoverage,
        float[,] terrain,
        float[,] moisture,
        float[,] canopyMask,
        float[,] forestPatchMask,
        float[,] forestOpeningMask,
        LocalRegionFieldMaps? regionFieldMaps,
        string biomeId,
        float forestPatchBias,
        int noiseOriginX,
        int noiseOriginY)
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
        var suitabilitySum = 0f;
        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                continue;
            if (regionFieldMaps is not null)
            {
                var distanceFromEdge = Math.Min(Math.Min(x, map.Width - 1 - x), Math.Min(y, map.Height - 1 - y));
                if (distanceFromEdge > 0 && distanceFromEdge < 6)
                    continue;
            }
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

            suitabilitySum += suitability;
            var jitter = ResolveTileJitter(continuitySeed, noiseOriginX, noiseOriginY, x, y, 23111, 0.06f);
            candidates.Add((x, y, Math.Clamp(suitability + jitter, 0f, 1f)));
        }

        if (eligibleLandTiles == 0 || candidates.Count == 0)
            return;

        var averageSuitability = suitabilitySum / candidates.Count;
        var coverageSignal = Math.Clamp(
            (averageSuitability - suitabilityFloor) / Math.Max(0.05f, 1f - suitabilityFloor),
            0f,
            1f);
        var coverageNoise = SeedHash.Unit(
            continuitySeed,
            noiseOriginX + (map.Width / 2),
            noiseOriginY + (map.Height / 2),
            23129);
        var baseTargetCoverage = Math.Clamp(
            clampedMin + (coverageSignal * (clampedMax - clampedMin)) + ((coverageNoise - 0.5f) * 0.08f),
            clampedMin,
            clampedMax);
        var boostedCoverage = Math.Clamp(
            baseTargetCoverage + (Math.Max(0f, biomeCoverageBoost) * 0.32f),
            clampedMin,
            0.95f);
        var targetCount = Math.Clamp((int)MathF.Round(boostedCoverage * eligibleLandTiles), 0, candidates.Count);
        if (targetCount <= 0)
            return;

        candidates.Sort(CompareTreeCandidates);
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
            if (TryPlaceTree(map, treePlaced, candidate.X, candidate.Y, biomeId, moisture, terrain, continuitySeed, noiseOriginX, noiseOriginY))
                placed++;
        }

        // Force a small riverbank-focused seed pass so waterways grow visible riparian bands.
        if (placed < targetCount)
            placed += PlaceRiparianTreeSeeds(
                map,
                treePlaced,
                candidates,
                biomeId,
                moisture,
                terrain,
                continuitySeed,
                noiseOriginX,
                noiseOriginY,
                targetCount - placed);

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
                var growth = candidate.Score +
                             neighborBoost +
                             ResolveTileJitter(continuitySeed, noiseOriginX, noiseOriginY, candidate.X, candidate.Y, 23167 + pass, 0.05f);
                if (growth < threshold)
                    continue;

                if (TryPlaceTree(map, treePlaced, candidate.X, candidate.Y, biomeId, moisture, terrain, continuitySeed, noiseOriginX, noiseOriginY))
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

            if (TryPlaceTree(map, treePlaced, candidate.X, candidate.Y, biomeId, moisture, terrain, continuitySeed, noiseOriginX, noiseOriginY))
                placed++;
        }

        if (regionFieldMaps is not null)
        {
            AddFieldGuidedTrees(
                map,
                continuitySeed,
                clampedMin,
                clampedMax,
                terrain,
                moisture,
                canopyMask,
                forestPatchMask,
                forestOpeningMask,
                regionFieldMaps,
                biomeId,
                forestPatchBias,
                noiseOriginX,
                noiseOriginY);
        }
        else
        {
            ApplyBoundaryTreeContinuity(
                map,
                treePlaced,
                biomeId,
                moisture,
                terrain,
                canopyMask,
                forestPatchMask,
                forestOpeningMask,
                continuitySeed,
                noiseOriginX,
                noiseOriginY);
        }
    }

    private static void AddFieldGuidedTrees(
        GeneratedEmbarkMap map,
        int continuitySeed,
        float minCoverage,
        float maxCoverage,
        float[,] terrain,
        float[,] moisture,
        float[,] canopyMask,
        float[,] forestPatchMask,
        float[,] forestOpeningMask,
        LocalRegionFieldMaps regionFieldMaps,
        string biomeId,
        float forestPatchBias,
        int noiseOriginX,
        int noiseOriginY)
    {
        var biomeProfile = WorldGenContentRegistry.Current.ResolveBiomePreset(biomeId, seed: 0);
        var clampedPatchBias = Math.Clamp(forestPatchBias, -1f, 1f);
        var forestTreeFillRatio = Math.Clamp(biomeProfile.ForestTreeFillRatio, 0.50f, 0.98f);
        var suitabilityFloor = biomeProfile.TreeSuitabilityFloor;
        var treePlaced = new bool[map.Width, map.Height];

        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            var distanceFromEdge = Math.Min(Math.Min(x, map.Width - 1 - x), Math.Min(y, map.Height - 1 - y));
            if (distanceFromEdge <= 0 || distanceFromEdge >= 6)
                continue;
            if (IsSurfaceCornerCell(map.Width, map.Height, x, y) || IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                continue;

            var tile = map.GetTile(x, y, 0);
            if (!IsSurfaceSuitableForTree(tile))
                continue;

            var ruggedness = EstimateRuggedness(terrain, x, y, map.Width, map.Height);
            var riparianBoost = ResolveRiparianBoost(map, regionFieldMaps, moisture, terrain, x, y);
            var fieldDensity = Math.Clamp(
                (regionFieldMaps.VegetationDensity[x, y] * 0.24f) +
                (regionFieldMaps.VegetationSuitability[x, y] * 0.18f) +
                (regionFieldMaps.Groundwater[x, y] * 0.12f) +
                (regionFieldMaps.MoistureBand[x, y] * 0.10f) +
                (regionFieldMaps.FlowAccumulationBand[x, y] * 0.10f) +
                (regionFieldMaps.RiverDischargeBand[x, y] * 0.08f) +
                (regionFieldMaps.RiverOrderBand[x, y] * 0.04f) -
                (regionFieldMaps.Slope[x, y] * 0.06f),
                0f,
                1f);
            var forestCore = Math.Clamp(
                (forestPatchMask[x, y] * (0.54f + (Math.Max(0f, clampedPatchBias) * 0.10f))) +
                (canopyMask[x, y] * 0.18f) +
                (fieldDensity * 0.28f),
                0f,
                1f);
            if (ShouldReserveForestOpening(forestCore, forestOpeningMask[x, y], forestTreeFillRatio))
                continue;

            var densitySignal = Math.Clamp(
                (fieldDensity * 0.42f) +
                (forestCore * 0.20f) +
                (moisture[x, y] * 0.12f) +
                ((1f - terrain[x, y]) * 0.06f) +
                (riparianBoost * 0.10f) -
                (ruggedness * (0.08f + (Math.Max(0f, -clampedPatchBias) * 0.04f))),
                0f,
                1f);
            densitySignal = Math.Clamp(densitySignal + (biomeProfile.TreeCoverageBoost * 0.60f), 0f, 1f);
            if (densitySignal < (suitabilityFloor * 0.72f))
                continue;

            var fx = ResolveNoiseSampleCoord(noiseOriginX, x, map.Width);
            var fy = ResolveNoiseSampleCoord(noiseOriginY, y, map.Height);
            var groveNoise = CoherentNoise.DomainWarpedFractal2D(
                continuitySeed,
                fx * 0.46f,
                fy * 0.46f,
                octaves: 2,
                lacunarity: 2f,
                gain: 0.54f,
                warpStrength: 0.22f,
                salt: 23137);
            var groveSupport = Math.Clamp((groveNoise - 0.30f) / 0.54f, 0f, 1f);
            var openingReserve = Math.Clamp(
                (forestOpeningMask[x, y] - Math.Max(0.30f, 1f - forestTreeFillRatio)) /
                Math.Max(0.08f, forestTreeFillRatio * 0.72f),
                0f,
                1f);
            if (groveSupport < 0.04f && fieldDensity < 0.52f && riparianBoost < 0.64f)
                continue;

            var coverageBias = Math.Clamp((((minCoverage + maxCoverage) * 0.5f) - 0.42f) * 0.38f, -0.12f, 0.12f);
            var chance = Math.Clamp(
                0.05f +
                (densitySignal * 0.50f) +
                (forestCore * 0.18f) +
                (groveSupport * 0.20f) +
                (riparianBoost * 0.08f) +
                coverageBias +
                ((groveNoise - 0.5f) * 0.06f) -
                (openingReserve * 0.18f),
                0f,
                0.95f);
            var selector = CoherentNoise.DomainWarpedFractal2D(
                continuitySeed,
                fx * 3.8f,
                fy * 3.8f,
                octaves: 2,
                lacunarity: 2f,
                gain: 0.52f,
                warpStrength: 0.16f,
                salt: 23111);
            if (selector > chance)
                continue;

            TryPlaceTree(
                map,
                treePlaced,
                x,
                y,
                biomeId,
                moisture,
                terrain,
                continuitySeed,
                noiseOriginX,
                noiseOriginY,
                regionFieldMaps);
        }
    }

    private static void ApplyBoundaryTreeContinuity(
        GeneratedEmbarkMap map,
        bool[,] treePlaced,
        string biomeId,
        float[,] moisture,
        float[,] terrain,
        float[,] canopyMask,
        float[,] forestPatchMask,
        float[,] forestOpeningMask,
        int continuitySeed,
        int noiseOriginX,
        int noiseOriginY)
    {
        const int bandWidth = 4;
        var denseForestBiome = WorldGenContentRegistry.Current.ResolveBiomePreset(biomeId, seed: 0).DenseForest;

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (IsSurfaceCornerCell(map.Width, map.Height, x, y) || treePlaced[x, y])
                continue;
            if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                continue;

            var distanceFromEdge = Math.Min(Math.Min(x, map.Width - 1 - x), Math.Min(y, map.Height - 1 - y));
            if (distanceFromEdge <= 0 || distanceFromEdge >= bandWidth)
                continue;

            var boundarySignal = Math.Clamp(
                (forestPatchMask[x, y] * 0.42f) +
                (canopyMask[x, y] * 0.30f) +
                (moisture[x, y] * 0.18f) +
                ((1f - forestOpeningMask[x, y]) * 0.10f),
                0f,
                1f);
            var edgeWeight = 1f - ((distanceFromEdge - 1) / (float)Math.Max(1, bandWidth - 1));
            var threshold = denseForestBiome
                ? 0.72f - (edgeWeight * 0.14f)
                : 0.78f - (edgeWeight * 0.18f);
            var score = boundarySignal + ResolveTileJitter(continuitySeed, noiseOriginX, noiseOriginY, x, y, 23287, 0.04f);
            var neighbors = CountAdjacentPlacedTrees(treePlaced, x, y, map.Width, map.Height);

            if (score < threshold)
                continue;
            if (neighbors == 0 && score < threshold + 0.06f)
                continue;

            TryPlaceTree(map, treePlaced, x, y, biomeId, moisture, terrain, continuitySeed, noiseOriginX, noiseOriginY);
        }
    }

    private static int PlaceRiparianTreeSeeds(
        GeneratedEmbarkMap map,
        bool[,] treePlaced,
        List<(int X, int Y, float Score)> candidates,
        string biomeId,
        float[,] moisture,
        float[,] terrain,
        int continuitySeed,
        int noiseOriginX,
        int noiseOriginY,
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

        riparianCandidates.Sort(CompareTreeCandidates);
        var target = Math.Min(remaining, Math.Max(2, riparianCandidates.Count / 5));
        var placed = 0;
        for (var i = 0; i < riparianCandidates.Count && placed < target; i++)
        {
            var candidate = riparianCandidates[i];
            if (TryPlaceTree(map, treePlaced, candidate.X, candidate.Y, biomeId, moisture, terrain, continuitySeed, noiseOriginX, noiseOriginY))
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
        int continuitySeed,
        int noiseOriginX,
        int noiseOriginY,
        LocalRegionFieldMaps? regionFieldMaps = null)
    {
        if (treePlaced[x, y])
            return false;
        if (IsSurfaceCornerCell(map.Width, map.Height, x, y))
            return false;
        if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
            return false;
        var surfaceTile = map.GetTile(x, y, 0);
        if (!IsSurfaceSuitableForTree(surfaceTile))
            return false;
        if (!EnsureTreeSubsurface(map, x, y, biomeId, moisture[x, y], terrain[x, y]))
            return false;

        var riparianBoost = ResolveRiparianBoost(map, regionFieldMaps, moisture, terrain, x, y);
        var speciesId = ResolveTreeSpeciesId(
            biomeId,
            moisture[x, y],
            terrain[x, y],
            riparianBoost,
            CreateTileRandom(continuitySeed, noiseOriginX, noiseOriginY, x, y, 23221));
        map.SetTile(x, y, 0, TreeTile(speciesId));
        treePlaced[x, y] = true;
        return true;
    }

    private static int CompareTreeCandidates((int X, int Y, float Score) left, (int X, int Y, float Score) right)
    {
        var scoreCompare = right.Score.CompareTo(left.Score);
        if (scoreCompare != 0)
            return scoreCompare;

        var yCompare = left.Y.CompareTo(right.Y);
        if (yCompare != 0)
            return yCompare;

        return left.X.CompareTo(right.X);
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
            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
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
        float[,] terrain,
        float[,] moisture,
        float[,] forestPatchMask,
        float[,] forestOpeningMask,
        string biomeId,
        LocalRegionFieldMaps? regionFieldMaps,
        int noiseOriginX,
        int noiseOriginY)
    {
        var plantCatalog = WorldGenPlantRegistry.Current;
        var biomeProfile = WorldGenContentRegistry.Current.ResolveBiomePreset(biomeId, seed: 0);
        SeedFruitCanopies(map, seed, terrain, moisture, biomeId, plantCatalog, regionFieldMaps, noiseOriginX, noiseOriginY);

        var density = WorldGenContentRegistry.Current.ResolveGroundPlantDensity(biomeId);
        if (density <= 0f)
            return;

        // Build a clustering noise layer so plants form natural patches rather than uniform grids.
        // Low-frequency noise creates large-scale "fertile" and "barren" zones.
        var plantClusterNoise = BuildPlantClusterNoise(map.Width, map.Height, seed + 7919, noiseOriginX, noiseOriginY);

        // Track placed plants for soft crowding penalty.
        var plantPlaced = new bool[map.Width, map.Height];
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (!string.IsNullOrWhiteSpace(tile.PlantDefId))
                plantPlaced[x, y] = true;
        }

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                continue;

            var tile = map.GetTile(x, y, 0);
            if (!CanHostGroundPlant(tile))
                continue;

                var riparianBoost = ResolveRiparianBoost(map, regionFieldMaps, moisture, terrain, x, y);
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
            var clusterValue = ResolvePlantClusterValue(
                plantClusterNoise[x, y],
                regionFieldMaps,
                forestPatchMask,
                forestOpeningMask,
                moisture,
                terrain,
                x,
                y);
            var clusterBoost = (clusterValue - 0.5f) * 0.30f; // +/- 15% boost/penalty
            var fieldSupport = ResolvePlantFieldSupport(regionFieldMaps, moisture, terrain, x, y);
            var adjustedScore = Math.Clamp(score + clusterBoost + (fieldSupport * 0.12f), 0f, 1f);

            // Soft crowding penalty: each nearby plant slightly reduces placement chance.
            // Unlike hard exclusion, this allows some plants to be close together naturally.
            var nearbyTrees = CountNearbySurfaceTrees(map, x, y, radius: 1);
            var understoryBoost = ResolveForestUnderstoryBoost(
                forestPatchMask[x, y],
                forestOpeningMask[x, y],
                nearbyTrees,
                biomeProfile.ForestTreeFillRatio);
            var effectiveDensity = Math.Clamp(density + understoryBoost + (fieldSupport * 0.10f), 0f, 1f);

            // Threshold scales with biome density: dense biomes accept lower scores.
            var threshold = 0.72f - (effectiveDensity * 0.20f);
            if (tile.TileDefId == GeneratedTileDefIds.Mud)
                threshold -= 0.04f;

            var jitter = ResolveTileJitter(seed, noiseOriginX, noiseOriginY, x, y, 71341, 0.14f);
            float finalScore;
            if (regionFieldMaps is null)
            {
                var nearbyCount = CountNearbyPlacedPlants(plantPlaced, x, y, radius: 3, map.Width, map.Height);
                var crowdingPenalty = nearbyCount * 0.08f; // Each nearby plant reduces score by 8%
                finalScore = adjustedScore + understoryBoost - crowdingPenalty + jitter;
            }
            else
            {
                finalScore = adjustedScore + understoryBoost + jitter;
                var placementNoise = SeedHash.Unit(seed, noiseOriginX + x, noiseOriginY + y, 71323);
                var placementCutoff = Math.Clamp(
                    (effectiveDensity * 0.50f) +
                    (Math.Max(0f, finalScore - threshold) * 0.92f) +
                    (clusterValue * 0.14f),
                    0f,
                    1f);
                if (placementNoise > placementCutoff)
                    continue;
            }

            if (finalScore < threshold)
                continue;

            var plantRandom = CreateTileRandom(seed, noiseOriginX, noiseOriginY, x, y, 71379);
            var stage = ResolvePlantGrowthStage(plantRandom, plantDefinition.MaxGrowthStage);
            var yield = stage >= GeneratedPlantGrowthStages.Mature && plantRandom.NextDouble() < 0.55d ? (byte)1 : (byte)0;
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

    private static float[,] BuildPlantClusterNoise(int width, int height, int seed, int noiseOriginX, int noiseOriginY)
    {
        var noise = new float[width, height];
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            var fx = ResolveNoiseSampleCoord(noiseOriginX, x, width);
            var fy = ResolveNoiseSampleCoord(noiseOriginY, y, height);

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

    private static float ResolvePlantClusterValue(
        float baseClusterValue,
        LocalRegionFieldMaps? regionFieldMaps,
        float[,] forestPatchMask,
        float[,] forestOpeningMask,
        float[,] moisture,
        float[,] terrain,
        int x,
        int y)
    {
        if (regionFieldMaps is null)
            return baseClusterValue;

        var fieldCluster = Math.Clamp(
            (regionFieldMaps.VegetationDensity[x, y] * 0.28f) +
            (regionFieldMaps.VegetationSuitability[x, y] * 0.20f) +
            (regionFieldMaps.MoistureBand[x, y] * 0.18f) +
            (regionFieldMaps.Groundwater[x, y] * 0.16f) +
            (forestPatchMask[x, y] * 0.10f) +
            (Math.Max(0f, 1f - forestOpeningMask[x, y]) * 0.05f) +
            (moisture[x, y] * 0.03f),
            0f,
            1f);

        return Math.Clamp((baseClusterValue * 0.52f) + (fieldCluster * 0.48f), 0f, 1f);
    }

    private static float ResolvePlantFieldSupport(
        LocalRegionFieldMaps? regionFieldMaps,
        float[,] moisture,
        float[,] terrain,
        int x,
        int y)
    {
        if (regionFieldMaps is null)
            return 0f;

        return Math.Clamp(
            (regionFieldMaps.VegetationSuitability[x, y] * 0.24f) +
            (regionFieldMaps.VegetationDensity[x, y] * 0.20f) +
            (regionFieldMaps.MoistureBand[x, y] * 0.18f) +
            (regionFieldMaps.Groundwater[x, y] * 0.16f) +
            (regionFieldMaps.RiverInfluence[x, y] * 0.08f) +
            (regionFieldMaps.LakeInfluence[x, y] * 0.06f) +
            (moisture[x, y] * 0.05f) +
            ((1f - terrain[x, y]) * 0.03f),
            0f,
            1f);
    }

    private static void SeedFruitCanopies(
        GeneratedEmbarkMap map,
        int seed,
        float[,] terrain,
        float[,] moisture,
        string biomeId,
        WorldGenPlantCatalog plantCatalog,
        LocalRegionFieldMaps? regionFieldMaps,
        int noiseOriginX,
        int noiseOriginY)
    {
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId != GeneratedTileDefIds.Tree || string.IsNullOrWhiteSpace(tile.TreeSpeciesId))
                continue;

                var riparianBoost = ResolveRiparianBoost(map, regionFieldMaps, moisture, terrain, x, y);
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

            var fieldSupport = ResolvePlantFieldSupport(regionFieldMaps, moisture, terrain, x, y);
            if ((score + (fieldSupport * 0.08f)) < 0.36f)
                continue;

            var stage = Math.Min(GeneratedPlantGrowthStages.Mature, canopyDefinition.MaxGrowthStage);
            var canopyRandom = CreateTileRandom(seed, noiseOriginX, noiseOriginY, x, y, 71417);

            map.SetTile(x, y, 0, tile with
            {
                PlantDefId = canopyDefinition.Id,
                PlantGrowthStage = (byte)stage,
                PlantGrowthProgressSeconds = 0f,
                PlantYieldLevel = stage >= GeneratedPlantGrowthStages.Mature && canopyRandom.NextDouble() < 0.60d ? (byte)1 : (byte)0,
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

    private static float ResolveTileJitter(int seed, int noiseOriginX, int noiseOriginY, int x, int y, int salt, float amplitude)
    {
        if (amplitude <= 0f)
            return 0f;

        var sample = SeedHash.Unit(seed, noiseOriginX + x, noiseOriginY + y, salt);
        return ((sample * 2f) - 1f) * amplitude;
    }

    private static Random CreateTileRandom(int seed, int noiseOriginX, int noiseOriginY, int x, int y, int salt)
        => new(SeedHash.Hash(seed, noiseOriginX + x, noiseOriginY + y, salt));

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

    private static void AddFieldGuidedOutcrops(
        GeneratedEmbarkMap map,
        int continuitySeed,
        int min,
        int max,
        float[,] terrain,
        LocalRegionFieldMaps regionFieldMaps,
        int noiseOriginX,
        int noiseOriginY)
    {
        if (max <= 0 || map.Width < 10 || map.Height < 10)
            return;

        var clampedMin = Math.Max(0, min);
        var clampedMax = Math.Max(clampedMin, max);
        var count = clampedMin;
        if (clampedMax > clampedMin)
        {
            var countUnit = SeedHash.Unit(continuitySeed, noiseOriginX + (map.Width / 2), noiseOriginY + (map.Height / 2), 17861);
            count += (int)MathF.Round((clampedMax - clampedMin) * countUnit);
        }

        if (count <= 0)
            return;

        var candidates = new List<HydrologyCandidate>(map.Width * map.Height / 8);
        for (var x = 2; x < map.Width - 2; x++)
        for (var y = 2; y < map.Height - 2; y++)
        {
            if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                continue;
            if (map.GetTile(x, y, 0).TileDefId is GeneratedTileDefIds.Water or GeneratedTileDefIds.Tree)
                continue;

            var ruggedness = EstimateRuggedness(terrain, x, y, map.Width, map.Height);
            var score = Math.Clamp(
                (ruggedness * 0.46f) +
                (regionFieldMaps.SurfaceStoneWeight[x, y] * 0.24f) +
                (regionFieldMaps.Slope[x, y] * 0.18f) +
                (terrain[x, y] * 0.08f) +
                ResolveTileJitter(continuitySeed, noiseOriginX, noiseOriginY, x, y, 17879, 0.10f),
                0f,
                1f);
            if (score < 0.58f)
                continue;

            candidates.Add(new HydrologyCandidate(x, y, score));
        }

        if (candidates.Count == 0)
        {
            var fallbackRng = new Random(SeedHash.Hash(continuitySeed, noiseOriginX, noiseOriginY, 17909));
            AddOutcrops(map, fallbackRng, clampedMin, clampedMax, terrain);
            return;
        }

        candidates.Sort(static (left, right) => CompareHydrologyCandidates(left, right));
        var placed = new List<HydrologyCandidate>(count);
        const int minSpacing = 3;
        for (var i = 0; i < candidates.Count && placed.Count < count; i++)
        {
            var candidate = candidates[i];
            if (!IsFarEnoughFromHydrologyCandidates(placed, candidate.X, candidate.Y, minSpacing))
                continue;

            var outcropRandom = CreateTileRandom(continuitySeed, noiseOriginX, noiseOriginY, candidate.X, candidate.Y, 17941);
            map.SetTile(candidate.X, candidate.Y, 0, ResolveOutcropWallTile(outcropRandom));
            placed.Add(candidate);
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

    private static void AddFieldGuidedMarshPools(
        GeneratedEmbarkMap map,
        int continuitySeed,
        int poolCount,
        float[,] terrain,
        float[,] moisture,
        LocalRegionFieldMaps regionFieldMaps,
        int noiseOriginX,
        int noiseOriginY)
    {
        if (poolCount <= 0)
            return;

        var candidates = new List<HydrologyCandidate>(map.Width * map.Height / 5);
        for (var x = 2; x < map.Width - 2; x++)
        for (var y = 2; y < map.Height - 2; y++)
        {
            if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                continue;

            var wetlandSignal = Math.Clamp(
                (moisture[x, y] * 0.28f) +
                (regionFieldMaps.Groundwater[x, y] * 0.26f) +
                (regionFieldMaps.MoistureBand[x, y] * 0.18f) +
                (regionFieldMaps.LakeInfluence[x, y] * 0.14f) +
                (regionFieldMaps.RiverInfluence[x, y] * 0.08f) +
                ((1f - terrain[x, y]) * 0.08f) -
                (regionFieldMaps.Slope[x, y] * 0.06f) +
                ResolveTileJitter(continuitySeed, noiseOriginX, noiseOriginY, x, y, 17749, 0.08f),
                0f,
                1f);

            if (terrain[x, y] > 0.68f || wetlandSignal < 0.56f)
                continue;

            candidates.Add(new HydrologyCandidate(x, y, wetlandSignal));
        }

        candidates.Sort(static (left, right) => CompareHydrologyCandidates(left, right));
        var placed = new List<HydrologyCandidate>(poolCount);
        var minSpacing = Math.Clamp(Math.Min(map.Width, map.Height) / 10, 3, 6);

        for (var i = 0; i < candidates.Count && placed.Count < poolCount; i++)
        {
            var candidate = candidates[i];
            if (!IsFarEnoughFromHydrologyCandidates(placed, candidate.X, candidate.Y, minSpacing))
                continue;

            var radiusSignal = candidate.Score + ResolveTileJitter(continuitySeed, noiseOriginX, noiseOriginY, candidate.X, candidate.Y, 17783, 0.08f);
            var radius = radiusSignal >= 0.82f ? 3 : (radiusSignal >= 0.66f ? 2 : 1);
            for (var dx = -radius; dx <= radius; dx++)
            for (var dy = -radius; dy <= radius; dy++)
            {
                var x = candidate.X + dx;
                var y = candidate.Y + dy;
                if (x <= 1 || y <= 1 || x >= map.Width - 1 || y >= map.Height - 1)
                    continue;
                if (Math.Abs(dx) + Math.Abs(dy) > radius + 1)
                    continue;
                if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                    continue;

                var distance = Math.Abs(dx) + Math.Abs(dy);
                var localSignal = candidate.Score - (distance * 0.16f) + ResolveTileJitter(continuitySeed, noiseOriginX, noiseOriginY, x, y, 17821, 0.06f);
                if (localSignal < 0.42f)
                    continue;

                var level = localSignal >= 0.76f ? (byte)2 : (byte)1;
                CarveWater(map, x, y, level, allowBoundary: true);
            }

            placed.Add(candidate);
        }

        if (placed.Count == 0)
        {
            var fallbackRng = new Random(SeedHash.Hash(continuitySeed, noiseOriginX, noiseOriginY, 17843));
            AddMarshPools(map, fallbackRng, poolCount, terrain, moisture);
        }

        ApplyFieldGuidedBoundaryWetlands(
            map,
            continuitySeed,
            terrain,
            moisture,
            regionFieldMaps,
            noiseOriginX,
            noiseOriginY);
    }

    private static void ApplyFieldGuidedBoundaryWetlands(
        GeneratedEmbarkMap map,
        int continuitySeed,
        float[,] terrain,
        float[,] moisture,
        LocalRegionFieldMaps regionFieldMaps,
        int noiseOriginX,
        int noiseOriginY)
    {
        const int bandWidth = 6;

        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            var distanceFromEdge = Math.Min(Math.Min(x, map.Width - 1 - x), Math.Min(y, map.Height - 1 - y));
            if (distanceFromEdge <= 0 || distanceFromEdge >= bandWidth)
                continue;
            if (IsInCentralEmbarkZone(map.Width, map.Height, x, y))
                continue;

            var wetlandSupport = Math.Clamp(
                (moisture[x, y] * 0.24f) +
                (regionFieldMaps.Groundwater[x, y] * 0.22f) +
                (regionFieldMaps.MoistureBand[x, y] * 0.18f) +
                (regionFieldMaps.LakeInfluence[x, y] * 0.14f) +
                (regionFieldMaps.RiverInfluence[x, y] * 0.08f) +
                (regionFieldMaps.FlowAccumulationBand[x, y] * 0.08f) +
                ((1f - terrain[x, y]) * 0.06f) -
                (regionFieldMaps.Slope[x, y] * 0.04f),
                0f,
                1f);
            if (terrain[x, y] > 0.72f || wetlandSupport < 0.48f)
                continue;

            var fx = ResolveNoiseSampleCoord(noiseOriginX, x, map.Width);
            var fy = ResolveNoiseSampleCoord(noiseOriginY, y, map.Height);
            var poolNoiseSource = CoherentNoise.DomainWarpedFractal2D(
                continuitySeed,
                fx * 0.88f,
                fy * 0.88f,
                octaves: 3,
                lacunarity: 2f,
                gain: 0.54f,
                warpStrength: 0.22f,
                salt: 17749);
            var basinNoise = CoherentNoise.DomainWarpedFractal2D(
                continuitySeed,
                fx * 0.44f,
                fy * 0.44f,
                octaves: 2,
                lacunarity: 2f,
                gain: 0.50f,
                warpStrength: 0.18f,
                salt: 17783);
            var poolRibbon = 1f - MathF.Abs((poolNoiseSource * 2f) - 1f);
            var chance = Math.Clamp(
                (wetlandSupport * 0.46f) +
                (poolRibbon * 0.18f) +
                (basinNoise * 0.16f) -
                (((distanceFromEdge - 1) / (float)Math.Max(1, bandWidth - 1)) * 0.04f),
                0f,
                1f);
            var selector = CoherentNoise.DomainWarpedFractal2D(
                continuitySeed,
                fx * 3.2f,
                fy * 3.2f,
                octaves: 2,
                lacunarity: 2f,
                gain: 0.52f,
                warpStrength: 0.16f,
                salt: 17821);
            if (selector > chance)
                continue;

            var levelSignal = chance + (wetlandSupport * 0.20f);
            var level = levelSignal >= 0.82f ? (byte)2 : (byte)1;
            CarveWater(map, x, y, level, allowBoundary: true);
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
            AddRoadEndpointUnique(endpoints, seen, 0, yA, defaultWidth);
            AddRoadEndpointUnique(endpoints, seen, map.Width - 1, yA, defaultWidth);

            if (road >= 0.64f)
            {
                var xB = Math.Clamp(hub.X + offsetB, 1, map.Width - 2);
                AddRoadEndpointUnique(endpoints, seen, xB, 0, defaultWidth);
                AddRoadEndpointUnique(endpoints, seen, xB, map.Height - 1, defaultWidth);
            }
        }
        else
        {
            var xA = Math.Clamp(hub.X + offsetA, 1, map.Width - 2);
            AddRoadEndpointUnique(endpoints, seen, xA, 0, defaultWidth);
            AddRoadEndpointUnique(endpoints, seen, xA, map.Height - 1, defaultWidth);

            if (road >= 0.64f)
            {
                var yB = Math.Clamp(hub.Y + offsetB, 1, map.Height - 2);
                AddRoadEndpointUnique(endpoints, seen, 0, yB, defaultWidth);
                AddRoadEndpointUnique(endpoints, seen, map.Width - 1, yB, defaultWidth);
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
            TraceRoadLine(map, (endpoint.X, endpoint.Y), (hub.X, hub.Y), width, roadTile, skipWater: true, allowBoundary: true);
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
            LocalMapEdge.North => (offsetX, 0),
            LocalMapEdge.East => (map.Width - 1, offsetY),
            LocalMapEdge.South => (offsetX, map.Height - 1),
            LocalMapEdge.West => (0, offsetY),
            _ => (map.Width / 2, map.Height / 2),
        };
    }

    private static int ResolveSettlementAxisOffset(float normalizedOffset, int axisSize)
        => ResolveNormalizedAxisOffset(normalizedOffset, axisSize, min: 2, max: axisSize - 3, fallbackMin: 1, fallbackMax: axisSize - 2);

    private static int ResolveInteriorAxisOffset(float normalizedOffset, int axisSize)
        => ResolveNormalizedAxisOffset(normalizedOffset, axisSize, min: 1, max: axisSize - 2, fallbackMin: 0, fallbackMax: axisSize - 1);

    private static int ResolveNormalizedAxisOffset(float normalizedOffset, int axisSize, int min, int max, int fallbackMin, int fallbackMax)
    {
        if (max < min)
            return Math.Clamp(axisSize / 2, fallbackMin, Math.Max(fallbackMin, fallbackMax));

        var clamped = Math.Clamp(normalizedOffset, 0f, 1f);
        return min + (int)MathF.Round(clamped * (max - min));
    }

    private static bool IsSurfaceCornerCell(int width, int height, int x, int y)
        => (x == 0 || x == width - 1) &&
           (y == 0 || y == height - 1);

    private static bool IsOutsideSurfaceBounds(GeneratedEmbarkMap map, int x, int y, bool allowBoundary)
    {
        return allowBoundary
            ? x < 0 || y < 0 || x >= map.Width || y >= map.Height
            : x <= 0 || y <= 0 || x >= map.Width - 1 || y >= map.Height - 1;
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
        bool skipWater,
        bool allowBoundary = false)
    {
        var horizontalFirst = Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y);
        if (horizontalFirst)
        {
            TraceAxisAlignedSegment(map, from, (to.X, from.Y), width, roadTile, skipWater, allowBoundary);
            TraceAxisAlignedSegment(map, (to.X, from.Y), to, width, roadTile, skipWater, allowBoundary);
        }
        else
        {
            TraceAxisAlignedSegment(map, from, (from.X, to.Y), width, roadTile, skipWater, allowBoundary);
            TraceAxisAlignedSegment(map, (from.X, to.Y), to, width, roadTile, skipWater, allowBoundary);
        }
    }

    private static void TraceAxisAlignedSegment(
        GeneratedEmbarkMap map,
        (int X, int Y) from,
        (int X, int Y) to,
        int width,
        GeneratedTile roadTile,
        bool skipWater,
        bool allowBoundary)
    {
        var x = from.X;
        var y = from.Y;
        var dx = Math.Sign(to.X - from.X);
        var dy = Math.Sign(to.Y - from.Y);
        var steps = Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y);
        for (var i = 0; i <= steps; i++)
        {
            CarveRoadWidth(map, x, y, width, roadTile, skipWater, allowBoundary);

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
        bool skipWater,
        bool allowBoundary)
    {
        for (var oy = -width + 1; oy <= width - 1; oy++)
        for (var ox = -width + 1; ox <= width - 1; ox++)
        {
            var nx = x + ox;
            var ny = y + oy;
            if (IsOutsideSurfaceBounds(map, nx, ny, allowBoundary))
                continue;
            if (Math.Abs(ox) + Math.Abs(oy) > width)
                continue;

            TryCarveGroundTile(map, nx, ny, roadTile, skipWater, allowBoundary);
            var shoulderRadius = width >= 2 ? 2 : 1;
            ClearRoadsideTrees(map, nx, ny, shoulderRadius, roadTile, skipWater, allowBoundary);
        }
    }

    private static void ClearRoadsideTrees(
        GeneratedEmbarkMap map,
        int centerX,
        int centerY,
        int radius,
        GeneratedTile roadTile,
        bool skipWater,
        bool allowBoundary)
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
            if (IsOutsideSurfaceBounds(map, x, y, allowBoundary))
                continue;

            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId != GeneratedTileDefIds.Tree)
                continue;

            TryCarveGroundTile(map, x, y, roadTile, skipWater, allowBoundary);
        }
    }

    private static void TryCarveGroundTile(
        GeneratedEmbarkMap map,
        int x,
        int y,
        GeneratedTile baseTile,
        bool skipWater,
        bool allowBoundary = false)
    {
        if (IsOutsideSurfaceBounds(map, x, y, allowBoundary))
            return;

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
        // Keep corners and at least one access point per edge safe without flattening the whole border.
        EnsureSurfaceCellSafe(map, 0, 0, fallbackTile);
        EnsureSurfaceCellSafe(map, map.Width - 1, 0, fallbackTile);
        EnsureSurfaceCellSafe(map, 0, map.Height - 1, fallbackTile);
        EnsureSurfaceCellSafe(map, map.Width - 1, map.Height - 1, fallbackTile);

        EnsureEdgeHasPassableAccess(map, LocalMapEdge.North, fallbackTile);
        EnsureEdgeHasPassableAccess(map, LocalMapEdge.East, fallbackTile);
        EnsureEdgeHasPassableAccess(map, LocalMapEdge.South, fallbackTile);
        EnsureEdgeHasPassableAccess(map, LocalMapEdge.West, fallbackTile);
    }

    private static void EnsureEdgeHasPassableAccess(GeneratedEmbarkMap map, LocalMapEdge edge, GeneratedTile fallbackTile)
    {
        if (HasSafeBorderAccess(map, edge))
            return;

        var (edgeX, edgeY, inwardX, inwardY) = ResolveBorderAccessPoint(map, edge);
        EnsureSurfaceCellSafe(map, edgeX, edgeY, fallbackTile);
        EnsureSurfaceCellSafe(map, inwardX, inwardY, fallbackTile);
    }

    private static bool HasSafeBorderAccess(GeneratedEmbarkMap map, LocalMapEdge edge)
    {
        if (edge is LocalMapEdge.North or LocalMapEdge.South)
        {
            var y = edge == LocalMapEdge.North ? 0 : map.Height - 1;
            for (var x = 1; x < map.Width - 1; x++)
            {
                if (IsSafePassableSurface(map.GetTile(x, y, 0)))
                    return true;
            }

            return false;
        }

        var borderX = edge == LocalMapEdge.West ? 0 : map.Width - 1;
        for (var y = 1; y < map.Height - 1; y++)
        {
            if (IsSafePassableSurface(map.GetTile(borderX, y, 0)))
                return true;
        }

        return false;
    }

    private static (int EdgeX, int EdgeY, int InwardX, int InwardY) ResolveBorderAccessPoint(GeneratedEmbarkMap map, LocalMapEdge edge)
    {
        var midX = map.Width / 2;
        var midY = map.Height / 2;
        return edge switch
        {
            LocalMapEdge.North => (midX, 0, midX, Math.Min(1, map.Height - 1)),
            LocalMapEdge.East => (map.Width - 1, midY, Math.Max(0, map.Width - 2), midY),
            LocalMapEdge.South => (midX, map.Height - 1, midX, Math.Max(0, map.Height - 2)),
            LocalMapEdge.West => (0, midY, Math.Min(1, map.Width - 1), midY),
            _ => (midX, 0, midX, Math.Min(1, map.Height - 1)),
        };
    }

    private static void EnsureSurfaceCellSafe(GeneratedEmbarkMap map, int x, int y, GeneratedTile fallbackTile)
    {
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            return;

        var tile = map.GetTile(x, y, 0);
        if (IsSafePassableSurface(tile))
            return;

        map.SetTile(x, y, 0, ResolveSafeBorderSurface(tile, fallbackTile));
    }

    private static bool IsSafePassableSurface(GeneratedTile tile)
        => tile.IsPassable &&
           tile.FluidType != GeneratedFluidType.Magma &&
           tile.TileDefId != GeneratedTileDefIds.Magma;

    private static GeneratedTile ResolveSafeBorderSurface(GeneratedTile edge, GeneratedTile fallbackTile)
    {
        if (edge.TileDefId == GeneratedTileDefIds.StoneWall)
        {
            return StoneSurfaceTile() with
            {
                MaterialId = ResolveBorderMaterialId(edge.MaterialId, fallbackTile.MaterialId, "granite"),
            };
        }

        if (edge.TileDefId == GeneratedTileDefIds.SoilWall)
        {
            return ResolvePassableFallbackSurface(fallbackTile, edge.MaterialId is { Length: > 0 } ? edge.MaterialId : "soil");
        }

        return ResolvePassableFallbackSurface(fallbackTile, edge.MaterialId);
    }

    private static GeneratedTile ResolvePassableFallbackSurface(GeneratedTile fallbackTile, string? preferredMaterialId)
    {
        return fallbackTile.TileDefId switch
        {
            GeneratedTileDefIds.Grass => GrassSurfaceTile(),
            GeneratedTileDefIds.Soil => SoilSurfaceTile() with
            {
                MaterialId = ResolveBorderMaterialId(preferredMaterialId, fallbackTile.MaterialId, "soil"),
            },
            GeneratedTileDefIds.Sand => SandSurfaceTile(),
            GeneratedTileDefIds.Mud => MudSurfaceTile(),
            GeneratedTileDefIds.Snow => SnowSurfaceTile(),
            GeneratedTileDefIds.StoneBrick => RoadSurfaceTile(ResolveBorderMaterialId(preferredMaterialId, fallbackTile.MaterialId, "granite")),
            GeneratedTileDefIds.StoneFloor or GeneratedTileDefIds.StoneWall => StoneSurfaceTile() with
            {
                MaterialId = ResolveBorderMaterialId(preferredMaterialId, fallbackTile.MaterialId, "granite"),
            },
            GeneratedTileDefIds.SoilWall => SoilSurfaceTile() with
            {
                MaterialId = ResolveBorderMaterialId(preferredMaterialId, fallbackTile.MaterialId, "soil"),
            },
            _ => SoilSurfaceTile() with
            {
                MaterialId = ResolveBorderMaterialId(preferredMaterialId, fallbackTile.MaterialId, "soil"),
            },
        };
    }

    private static string ResolveBorderMaterialId(string? preferredMaterialId, string? fallbackMaterialId, string defaultMaterialId)
        => !string.IsNullOrWhiteSpace(preferredMaterialId)
            ? preferredMaterialId
            : !string.IsNullOrWhiteSpace(fallbackMaterialId)
                ? fallbackMaterialId
                : defaultMaterialId;

    private static void EnsureCentralEmbarkZone(GeneratedEmbarkMap map, GeneratedTile baseTile)
    {
        var (minX, maxX, minY, maxY) = ResolveCentralEmbarkBounds(map.Width, map.Height);

        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
            EnsureSpawnSafeSurfaceTile(map, x, y, baseTile);
    }

    private static void EnsureSurfaceConnectivity(GeneratedEmbarkMap map, GeneratedTile baseTile)
    {
        while (TryFindLargestTraversableSurfaceComponent(map, out var mainComponent, out var traversableCount, out var largestComponentCount) &&
               largestComponentCount < traversableCount)
        {
            if (!TryFindDisconnectedTraversableSurfaceTile(map, mainComponent, out var startX, out var startY))
                return;

            if (!TryCarveSurfaceConnectionToComponent(map, mainComponent, startX, startY, baseTile))
                return;
        }
    }

    private static bool TryFindLargestTraversableSurfaceComponent(
        GeneratedEmbarkMap map,
        out bool[,] componentMask,
        out int traversableCount,
        out int largestComponentCount)
    {
        componentMask = new bool[map.Width, map.Height];
        traversableCount = 0;
        largestComponentCount = 0;

        if (map.Width <= 0 || map.Height <= 0)
            return false;

        var visited = new bool[map.Width, map.Height];
        var queue = new Queue<(int X, int Y)>();
        var cells = new List<(int X, int Y)>(Math.Max(16, map.Width + map.Height));

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (visited[x, y])
                continue;

            var tile = map.GetTile(x, y, 0);
            if (!IsTraversableSurfaceTile(tile))
                continue;

            visited[x, y] = true;
            queue.Enqueue((x, y));
            cells.Clear();

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                cells.Add((cx, cy));

                EnqueueTraversableSurfaceNeighbor(map, visited, queue, cx + 1, cy);
                EnqueueTraversableSurfaceNeighbor(map, visited, queue, cx - 1, cy);
                EnqueueTraversableSurfaceNeighbor(map, visited, queue, cx, cy + 1);
                EnqueueTraversableSurfaceNeighbor(map, visited, queue, cx, cy - 1);
            }

            traversableCount += cells.Count;
            if (cells.Count <= largestComponentCount)
                continue;

            Array.Clear(componentMask, 0, componentMask.Length);
            foreach (var (cellX, cellY) in cells)
                componentMask[cellX, cellY] = true;

            largestComponentCount = cells.Count;
        }

        return traversableCount > 0;
    }

    private static void EnqueueTraversableSurfaceNeighbor(
        GeneratedEmbarkMap map,
        bool[,] visited,
        Queue<(int X, int Y)> queue,
        int x,
        int y)
    {
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            return;
        if (visited[x, y])
            return;
        if (!IsTraversableSurfaceTile(map.GetTile(x, y, 0)))
            return;

        visited[x, y] = true;
        queue.Enqueue((x, y));
    }

    private static bool TryFindDisconnectedTraversableSurfaceTile(GeneratedEmbarkMap map, bool[,] mainComponent, out int x, out int y)
    {
        for (var sx = 0; sx < map.Width; sx++)
        for (var sy = 0; sy < map.Height; sy++)
        {
            if (mainComponent[sx, sy])
                continue;
            if (!IsTraversableSurfaceTile(map.GetTile(sx, sy, 0)))
                continue;

            x = sx;
            y = sy;
            return true;
        }

        x = -1;
        y = -1;
        return false;
    }

    private static bool TryCarveSurfaceConnectionToComponent(
        GeneratedEmbarkMap map,
        bool[,] mainComponent,
        int startX,
        int startY,
        GeneratedTile baseTile)
    {
        var visited = new bool[map.Width, map.Height];
        var parentX = new int[map.Width, map.Height];
        var parentY = new int[map.Width, map.Height];
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            parentX[x, y] = -1;
            parentY[x, y] = -1;
        }

        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((startX, startY));
        visited[startX, startY] = true;

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            if (mainComponent[x, y])
            {
                CarveSurfaceConnectionPath(map, parentX, parentY, x, y, baseTile);
                return true;
            }

            TryEnqueueSurfaceConnectionNode(map, visited, parentX, parentY, queue, x, y, x + 1, y);
            TryEnqueueSurfaceConnectionNode(map, visited, parentX, parentY, queue, x, y, x - 1, y);
            TryEnqueueSurfaceConnectionNode(map, visited, parentX, parentY, queue, x, y, x, y + 1);
            TryEnqueueSurfaceConnectionNode(map, visited, parentX, parentY, queue, x, y, x, y - 1);
        }

        return false;
    }

    private static void TryEnqueueSurfaceConnectionNode(
        GeneratedEmbarkMap map,
        bool[,] visited,
        int[,] parentX,
        int[,] parentY,
        Queue<(int X, int Y)> queue,
        int fromX,
        int fromY,
        int x,
        int y)
    {
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            return;
        if (visited[x, y])
            return;

        var tile = map.GetTile(x, y, 0);
        if (!CanTraverseOrCarveSurfaceTile(tile))
            return;

        visited[x, y] = true;
        parentX[x, y] = fromX;
        parentY[x, y] = fromY;
        queue.Enqueue((x, y));
    }

    private static void CarveSurfaceConnectionPath(
        GeneratedEmbarkMap map,
        int[,] parentX,
        int[,] parentY,
        int endX,
        int endY,
        GeneratedTile baseTile)
    {
        var x = endX;
        var y = endY;

        while (x >= 0 && y >= 0)
        {
            var tile = map.GetTile(x, y, 0);
            if (!IsTraversableSurfaceTile(tile))
                map.SetTile(x, y, 0, ResolveSafeBorderSurface(tile, baseTile));

            var nextX = parentX[x, y];
            var nextY = parentY[x, y];
            if (nextX < 0 || nextY < 0)
                break;

            x = nextX;
            y = nextY;
        }
    }

    private static bool IsTraversableSurfaceTile(GeneratedTile tile)
        => tile.IsPassable &&
           tile.FluidType != GeneratedFluidType.Magma &&
           tile.TileDefId != GeneratedTileDefIds.Magma;

    private static bool CanTraverseOrCarveSurfaceTile(GeneratedTile tile)
        => tile.FluidType != GeneratedFluidType.Magma &&
           tile.TileDefId != GeneratedTileDefIds.Magma;

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

    private static string? ResolvePreferredSurfaceTileDefId(
        string? surfaceTileOverrideId,
        LocalSurfaceIntentGrid? surfaceIntentGrid,
        LocalRegionFieldMaps? regionFieldMaps,
        int x,
        int y,
        int width,
        int height,
        float selector)
    {
        var fallbackSurfaceTileDefId = ResolvePreferredSurfaceTileDefId(surfaceTileOverrideId);
        if (regionFieldMaps is not null)
        {
            return ResolveWeightedSurfaceTileDefId(
                regionFieldMaps.SurfaceGrassWeight[x, y],
                regionFieldMaps.SurfaceSoilWeight[x, y],
                regionFieldMaps.SurfaceSandWeight[x, y],
                regionFieldMaps.SurfaceMudWeight[x, y],
                regionFieldMaps.SurfaceSnowWeight[x, y],
                regionFieldMaps.SurfaceStoneWeight[x, y],
                selector,
                fallbackSurfaceTileDefId);
        }

        if (surfaceIntentGrid is not LocalSurfaceIntentGrid resolvedSurfaceIntentGrid)
            return fallbackSurfaceTileDefId;

        return ResolveBlendedSurfaceTileDefId(resolvedSurfaceIntentGrid, fallbackSurfaceTileDefId, x, y, width, height, selector);
    }

    private static string? ResolveBlendedSurfaceTileDefId(
        LocalSurfaceIntentGrid surfaceIntentGrid,
        string? fallbackSurfaceTileDefId,
        int x,
        int y,
        int width,
        int height,
        float selector)
    {
        var sampleX = width <= 1 ? 0.5f : x / (float)(width - 1);
        var sampleY = height <= 1 ? 0.5f : y / (float)(height - 1);
        var (leftOffsetX, rightOffsetX, tx) = ResolveSurfaceIntentAxis(sampleX);
        var (topOffsetY, bottomOffsetY, ty) = ResolveSurfaceIntentAxis(sampleY);

        var topLeft = surfaceIntentGrid.GetTileDefId(leftOffsetX, topOffsetY);
        var topRight = surfaceIntentGrid.GetTileDefId(rightOffsetX, topOffsetY);
        var bottomLeft = surfaceIntentGrid.GetTileDefId(leftOffsetX, bottomOffsetY);
        var bottomRight = surfaceIntentGrid.GetTileDefId(rightOffsetX, bottomOffsetY);

        var grassScore = ResolveSurfaceIntentScore(GeneratedTileDefIds.Grass, topLeft, topRight, bottomLeft, bottomRight, tx, ty);
        var soilScore = ResolveSurfaceIntentScore(GeneratedTileDefIds.Soil, topLeft, topRight, bottomLeft, bottomRight, tx, ty);
        var sandScore = ResolveSurfaceIntentScore(GeneratedTileDefIds.Sand, topLeft, topRight, bottomLeft, bottomRight, tx, ty);
        var mudScore = ResolveSurfaceIntentScore(GeneratedTileDefIds.Mud, topLeft, topRight, bottomLeft, bottomRight, tx, ty);
        var snowScore = ResolveSurfaceIntentScore(GeneratedTileDefIds.Snow, topLeft, topRight, bottomLeft, bottomRight, tx, ty);
        var stoneScore = ResolveSurfaceIntentScore(GeneratedTileDefIds.StoneFloor, topLeft, topRight, bottomLeft, bottomRight, tx, ty);

        return ResolveWeightedSurfaceTileDefId(
            grassScore,
            soilScore,
            sandScore,
            mudScore,
            snowScore,
            stoneScore,
            selector,
            fallbackSurfaceTileDefId);
    }

    private static (int LeftOffset, int RightOffset, float T) ResolveSurfaceIntentAxis(float sample)
    {
        if (sample <= 0.5f)
            return (-1, 0, Math.Clamp(sample + 0.5f, 0f, 1f));

        return (0, 1, Math.Clamp(sample - 0.5f, 0f, 1f));
    }

    private static float ResolveSurfaceIntentScore(
        string targetTileDefId,
        string topLeft,
        string topRight,
        string bottomLeft,
        string bottomRight,
        float tx,
        float ty)
    {
        var top = LerpSurfacePresence(targetTileDefId, topLeft, topRight, tx);
        var bottom = LerpSurfacePresence(targetTileDefId, bottomLeft, bottomRight, tx);
        return top + ((bottom - top) * ty);
    }

    private static float LerpSurfacePresence(string targetTileDefId, string leftTileDefId, string rightTileDefId, float t)
    {
        var left = string.Equals(leftTileDefId, targetTileDefId, StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
        var right = string.Equals(rightTileDefId, targetTileDefId, StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
        return left + ((right - left) * t);
    }

    private static string? ResolveWeightedSurfaceTileDefId(
        float grassScore,
        float soilScore,
        float sandScore,
        float mudScore,
        float snowScore,
        float stoneScore,
        float selector,
        string? fallbackSurfaceTileDefId)
    {
        var totalScore = grassScore + soilScore + sandScore + mudScore + snowScore + stoneScore;
        if (totalScore <= 0f)
            return fallbackSurfaceTileDefId;

        var threshold = selector * totalScore;
        var cumulative = grassScore;
        if (threshold <= cumulative)
            return GeneratedTileDefIds.Grass;

        cumulative += soilScore;
        if (threshold <= cumulative)
            return GeneratedTileDefIds.Soil;

        cumulative += sandScore;
        if (threshold <= cumulative)
            return GeneratedTileDefIds.Sand;

        cumulative += mudScore;
        if (threshold <= cumulative)
            return GeneratedTileDefIds.Mud;

        cumulative += snowScore;
        if (threshold <= cumulative)
            return GeneratedTileDefIds.Snow;

        return GeneratedTileDefIds.StoneFloor;
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


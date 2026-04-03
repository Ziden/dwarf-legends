using DwarfFortress.WorldGen.Geology;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Local;
using DwarfFortress.WorldGen.Maps;
using System.Linq;

namespace DwarfFortress.WorldGen.Tests;

public sealed class EmbarkGeneratorTests
{
    [Fact]
    public void Generate_SameSeedAndBiome_ProducesSameTiles()
    {
        var mapA = EmbarkGenerator.Generate(width: 32, height: 32, depth: 6, seed: 123, biomeId: MacroBiomeIds.MistyMarsh);
        var mapB = EmbarkGenerator.Generate(width: 32, height: 32, depth: 6, seed: 123, biomeId: MacroBiomeIds.MistyMarsh);

        for (var x = 0; x < mapA.Width; x++)
        for (var y = 0; y < mapA.Height; y++)
        for (var z = 0; z < mapA.Depth; z++)
        {
            var a = mapA.GetTile(x, y, z);
            var b = mapB.GetTile(x, y, z);
            Assert.Equal(a.TileDefId, b.TileDefId);
            Assert.Equal(a.MaterialId, b.MaterialId);
            Assert.Equal(a.IsPassable, b.IsPassable);
            Assert.Equal(a.FluidType, b.FluidType);
            Assert.Equal(a.FluidLevel, b.FluidLevel);
            Assert.Equal(a.OreId, b.OreId);
            Assert.Equal(a.IsAquifer, b.IsAquifer);
            Assert.Equal(a.TreeSpeciesId, b.TreeSpeciesId);
        }
    }

    [Fact]
    public void Generate_SameSeedAndBiome_ProducesSameCreatureSpawns()
    {
        var mapA = EmbarkGenerator.Generate(width: 48, height: 48, depth: 8, seed: 917, biomeId: MacroBiomeIds.ConiferForest);
        var mapB = EmbarkGenerator.Generate(width: 48, height: 48, depth: 8, seed: 917, biomeId: MacroBiomeIds.ConiferForest);

        Assert.Equal(mapA.CreatureSpawns.Count, mapB.CreatureSpawns.Count);

        for (var i = 0; i < mapA.CreatureSpawns.Count; i++)
        {
            Assert.Equal(mapA.CreatureSpawns[i], mapB.CreatureSpawns[i]);
        }
    }

    [Fact]
    public void Generate_CustomSettings_SameSeed_ProducesSameEmbark()
    {
        var settings = new LocalGenerationSettings(
            Width: 48,
            Height: 48,
            Depth: 12,
            BiomeOverrideId: MacroBiomeIds.Highland,
            TreeDensityBias: -0.18f,
            OutcropBias: 0.34f,
            StreamBandBias: 1,
            MarshPoolBias: 2,
            ParentWetnessBias: 0.42f,
            ParentSoilDepthBias: -0.15f,
            GeologyProfileId: GeologyProfileIds.IgneousUplift,
            StoneSurfaceOverride: true,
            RiverPortals:
            [
                new LocalRiverPortal(LocalMapEdge.North, 0.25f, Strength: 3),
                new LocalRiverPortal(LocalMapEdge.South, 0.72f, Strength: 2),
            ],
            ForestPatchBias: -0.20f,
            SettlementInfluence: 0.55f,
            RoadInfluence: 0.70f,
            SettlementAnchors:
            [
                new LocalSettlementAnchor(0.35f, 0.40f, Strength: 4),
                new LocalSettlementAnchor(0.68f, 0.58f, Strength: 2),
            ],
            RoadPortals:
            [
                new LocalRoadPortal(LocalMapEdge.West, 0.48f, Width: 2),
                new LocalRoadPortal(LocalMapEdge.East, 0.52f, Width: 2),
            ],
            SurfaceTileOverrideId: GeneratedTileDefIds.Grass,
            ForestCoverageTarget: 0.12f,
            NoiseOriginX: 320,
            NoiseOriginY: 640);

        var mapA = EmbarkGenerator.Generate(settings, seed: 7331);
        var mapB = EmbarkGenerator.Generate(settings, seed: 7331);

        for (var x = 0; x < mapA.Width; x++)
        for (var y = 0; y < mapA.Height; y++)
        for (var z = 0; z < mapA.Depth; z++)
        {
            var a = mapA.GetTile(x, y, z);
            var b = mapB.GetTile(x, y, z);
            Assert.Equal(a.TileDefId, b.TileDefId);
            Assert.Equal(a.MaterialId, b.MaterialId);
            Assert.Equal(a.IsPassable, b.IsPassable);
            Assert.Equal(a.FluidType, b.FluidType);
            Assert.Equal(a.FluidLevel, b.FluidLevel);
            Assert.Equal(a.OreId, b.OreId);
            Assert.Equal(a.IsAquifer, b.IsAquifer);
            Assert.Equal(a.TreeSpeciesId, b.TreeSpeciesId);
        }

        Assert.Equal(mapA.CreatureSpawns.Count, mapB.CreatureSpawns.Count);
        for (var i = 0; i < mapA.CreatureSpawns.Count; i++)
            Assert.Equal(mapA.CreatureSpawns[i], mapB.CreatureSpawns[i]);
    }

    [Fact]
    public void Generate_PopulatesStageDiagnostics_InExecutionOrder()
    {
        var map = EmbarkGenerator.Generate(width: 32, height: 32, depth: 8, seed: 341, biomeId: MacroBiomeIds.ConiferForest);

        var diagnostics = Assert.IsType<EmbarkGenerationDiagnostics>(map.Diagnostics);
        Assert.Equal(341, diagnostics.Seed);

        var expectedStages = new[]
        {
            EmbarkGenerationStageId.Inputs,
            EmbarkGenerationStageId.SurfaceShape,
            EmbarkGenerationStageId.UndergroundStructure,
            EmbarkGenerationStageId.Hydrology,
            EmbarkGenerationStageId.Ecology,
            EmbarkGenerationStageId.HydrologyPolish,
            EmbarkGenerationStageId.CivilizationOverlay,
            EmbarkGenerationStageId.Playability,
            EmbarkGenerationStageId.Population,
        };

        Assert.Equal(expectedStages, diagnostics.StageSnapshots.Select(s => s.StageId).ToArray());

        var inputsSnapshot = diagnostics.StageSnapshots[0];
        Assert.Equal(map.Width * map.Height, inputsSnapshot.SurfacePassableTiles);
        Assert.Equal(map.Width * map.Height * (map.Depth - 1), inputsSnapshot.UndergroundPassableTiles);
        Assert.Equal(0, inputsSnapshot.CreatureSpawnCount);

        var finalSnapshot = diagnostics.StageSnapshots[^1];
        Assert.Equal(map.CreatureSpawns.Count, finalSnapshot.CreatureSpawnCount);
        Assert.True(finalSnapshot.SurfacePassableTiles > 0);
        Assert.True(finalSnapshot.SurfaceTreeTiles >= 0);
    }

    [Fact]
    public void Generate_TropicalRainforest_HasMoreWildlifeThanDesert()
    {
        var tropicalCounts = new List<int>(10);
        var desertCounts = new List<int>(10);

        for (var seed = 1200; seed < 1210; seed++)
        {
            var tropical = EmbarkGenerator.Generate(width: 48, height: 48, depth: 8, seed: seed, biomeId: MacroBiomeIds.TropicalRainforest);
            var desert = EmbarkGenerator.Generate(width: 48, height: 48, depth: 8, seed: seed, biomeId: MacroBiomeIds.Desert);
            tropicalCounts.Add(tropical.CreatureSpawns.Count);
            desertCounts.Add(desert.CreatureSpawns.Count);
        }

        tropicalCounts.Sort();
        desertCounts.Sort();
        var tropicalMedian = tropicalCounts[tropicalCounts.Count / 2];
        var desertMedian = desertCounts[desertCounts.Count / 2];

        Assert.True(tropicalMedian > desertMedian,
            $"Expected tropical rainforest to have denser wildlife than desert ({tropicalMedian} vs {desertMedian}).");
    }

    [Fact]
    public void Generate_OceanBiome_SpawnsAquaticCreaturesInWater()
    {
        var map = EmbarkGenerator.Generate(width: 48, height: 48, depth: 8, seed: 1608, biomeId: MacroBiomeIds.OceanDeep);

        Assert.NotEmpty(map.CreatureSpawns);
        Assert.All(map.CreatureSpawns, spawn =>
        {
            Assert.Equal(CreatureDefIds.GiantCarp, spawn.CreatureDefId);
            var tile = map.GetTile(spawn.X, spawn.Y, spawn.Z);
            Assert.True(tile.TileDefId == GeneratedTileDefIds.Water || tile.FluidType == GeneratedFluidType.Water);
            Assert.True(tile.FluidLevel >= 3, $"Expected aquatic spawn tile to be swimmable depth, got {tile.FluidLevel}/7.");
        });
    }

    [Fact]
    public void Generate_ShallowEmbark_DoesNotSpawnCaveCreatures()
    {
        var map = EmbarkGenerator.Generate(width: 48, height: 48, depth: 8, seed: 2209, biomeId: MacroBiomeIds.ConiferForest);
        Assert.DoesNotContain(map.CreatureSpawns, spawn => spawn.Z > 0);
    }

    [Fact]
    public void Generate_DeepEmbark_SpawnsCaveCreaturesBelowSurface()
    {
        var map = EmbarkGenerator.Generate(width: 48, height: 48, depth: 16, seed: 2209, biomeId: MacroBiomeIds.ConiferForest);
        Assert.Contains(map.CreatureSpawns, spawn => spawn.Z > 0);
    }

    [Fact]
    public void Generate_CaveCreatureSpawns_LandOnValidCaveTiles()
    {
        var map = EmbarkGenerator.Generate(width: 48, height: 48, depth: 16, seed: 2301, biomeId: MacroBiomeIds.MistyMarsh);

        Assert.All(map.CreatureSpawns.Where(spawn => spawn.Z > 0), spawn =>
        {
            var tile = map.GetTile(spawn.X, spawn.Y, spawn.Z);
            Assert.True(tile.IsPassable || tile.FluidType == GeneratedFluidType.Water);
            Assert.NotEqual(GeneratedTileDefIds.Magma, tile.TileDefId);
            Assert.NotEqual(GeneratedFluidType.Magma, tile.FluidType);
        });
    }

    [Fact]
    public void Generate_ForestHasMoreTrees_ThanSteppe()
    {
        var forest = EmbarkGenerator.Generate(width: 48, height: 48, depth: 8, seed: 55, biomeId: MacroBiomeIds.ConiferForest);
        var steppe = EmbarkGenerator.Generate(width: 48, height: 48, depth: 8, seed: 55, biomeId: MacroBiomeIds.WindsweptSteppe);

        var forestTrees = CountSurfaceTiles(forest, GeneratedTileDefIds.Tree);
        var steppeTrees = CountSurfaceTiles(steppe, GeneratedTileDefIds.Tree);

        Assert.True(forestTrees > steppeTrees,
            $"Expected forest to have more trees than steppe ({forestTrees} vs {steppeTrees}).");
    }

    [Fact]
    public void Generate_ConiferForest_HasMeaningfulCanopyCoverage()
    {
        const int width = 48;
        const int height = 48;
        var samples = new List<float>(10);

        for (var seed = 100; seed < 110; seed++)
        {
            var map = EmbarkGenerator.Generate(width: width, height: height, depth: 8, seed: seed, biomeId: MacroBiomeIds.ConiferForest);
            var ratio = CountSurfaceTiles(map, GeneratedTileDefIds.Tree) / (float)(width * height);
            samples.Add(ratio);
        }

        samples.Sort();
        var median = samples[samples.Count / 2];
        Assert.True(median >= 0.18f, $"Expected conifer median canopy >= 0.18, got {median:F3}.");
    }

    [Fact]
    public void Generate_TropicalRainforest_HasDenseCanopyCoverage()
    {
        const int width = 48;
        const int height = 48;
        var samples = new List<float>(8);

        for (var seed = 200; seed < 208; seed++)
        {
            var map = EmbarkGenerator.Generate(width: width, height: height, depth: 8, seed: seed, biomeId: MacroBiomeIds.TropicalRainforest);
            var ratio = CountSurfaceTiles(map, GeneratedTileDefIds.Tree) / (float)(width * height);
            samples.Add(ratio);
        }

        samples.Sort();
        var median = samples[samples.Count / 2];
        Assert.True(median >= 0.26f, $"Expected tropical median canopy >= 0.26, got {median:F3}.");
    }

    [Fact]
    public void Generate_BorealForest_HasDenseCanopyCoverage()
    {
        const int width = 48;
        const int height = 48;
        var samples = new List<float>(8);

        for (var seed = 840; seed < 848; seed++)
        {
            var map = EmbarkGenerator.Generate(width: width, height: height, depth: 8, seed: seed, biomeId: MacroBiomeIds.BorealForest);
            var ratio = CountSurfaceTiles(map, GeneratedTileDefIds.Tree) / (float)(width * height);
            samples.Add(ratio);
        }

        samples.Sort();
        var median = samples[samples.Count / 2];
        Assert.True(median >= 0.22f, $"Expected boreal median canopy >= 0.22, got {median:F3}.");
    }

    [Fact]
    public void Generate_ConiferForest_FormsLargeConnectedForestStand()
    {
        const int width = 48;
        const int height = 48;
        var largestPatchSamples = new List<int>(8);

        for (var seed = 520; seed < 528; seed++)
        {
            var map = EmbarkGenerator.Generate(width: width, height: height, depth: 8, seed: seed, biomeId: MacroBiomeIds.ConiferForest);
            largestPatchSamples.Add(CountLargestConnectedSurfacePatch(map, GeneratedTileDefIds.Tree));
        }

        largestPatchSamples.Sort();
        var medianPatch = largestPatchSamples[largestPatchSamples.Count / 2];
        Assert.True(
            medianPatch >= 70,
            $"Expected conifer maps to form sizeable connected forest stands, median largest patch={medianPatch}.");
    }

    [Theory]
    [InlineData(MacroBiomeIds.TemperatePlains)]
    [InlineData(MacroBiomeIds.ConiferForest)]
    [InlineData(MacroBiomeIds.Highland)]
    [InlineData(MacroBiomeIds.MistyMarsh)]
    [InlineData(MacroBiomeIds.WindsweptSteppe)]
    public void Generate_AllBiomes_KeepPassableSurfaceSafety(string biomeId)
    {
        const int size = 32;
        var map = EmbarkGenerator.Generate(width: size, height: size, depth: 6, seed: 7, biomeId: biomeId);

        var passable = 0;
        for (var x = 0; x < size; x++)
        for (var y = 0; y < size; y++)
        {
            if (map.GetTile(x, y, 0).IsPassable)
                passable++;
        }

        Assert.True(passable >= (int)(size * size * 0.55f),
            $"Expected at least 55% passable surface in '{biomeId}', got {passable}/{size * size}.");
        Assert.True(map.GetTile(0, 0, 0).IsPassable);
        Assert.True(map.GetTile(size - 1, 0, 0).IsPassable);
        Assert.True(map.GetTile(0, size - 1, 0).IsPassable);
        Assert.True(map.GetTile(size - 1, size - 1, 0).IsPassable);
    }

    [Fact]
    public void Generate_ConiferForestSpawnsTreesAwayFromMapEdges()
    {
        const int size = 48;
        const int edgeMargin = 6;

        var seedsWithTrees = 0;
        var seedsWithInteriorTrees = 0;

        for (var seed = 10; seed < 24; seed++)
        {
            var map = EmbarkGenerator.Generate(width: size, height: size, depth: 8, seed: seed, biomeId: MacroBiomeIds.ConiferForest);
            var totalTrees = 0;
            var interiorTrees = 0;

            for (var x = 0; x < map.Width; x++)
            for (var y = 0; y < map.Height; y++)
            {
                if (map.GetTile(x, y, 0).TileDefId != GeneratedTileDefIds.Tree)
                    continue;

                totalTrees++;
                if (x >= edgeMargin && x < map.Width - edgeMargin &&
                    y >= edgeMargin && y < map.Height - edgeMargin)
                {
                    interiorTrees++;
                }
            }

            if (totalTrees == 0)
                continue;

            seedsWithTrees++;
            if (interiorTrees > 0)
                seedsWithInteriorTrees++;
        }

        Assert.True(seedsWithTrees >= 10, $"Expected forest seeds to produce trees frequently, got {seedsWithTrees}.");
        Assert.True(
            seedsWithInteriorTrees >= (int)Math.Ceiling(seedsWithTrees * 0.70f),
            $"Expected interior tree placement in most seeds, got {seedsWithInteriorTrees}/{seedsWithTrees}.");
    }

    [Fact]
    public void Generate_ConiferForest_PreservesNaturalFeaturesNearEmbarkCore()
    {
        const int size = 48;
        const int legacyHalfSpan = 8; // historical 16x16 clear
        const int spawnCoreHalfSpan = 5; // current 10x10 protected core
        var seedsWithCenterRingFeatures = 0;

        for (var seed = 300; seed < 320; seed++)
        {
            var map = EmbarkGenerator.Generate(width: size, height: size, depth: 8, seed: seed, biomeId: MacroBiomeIds.ConiferForest);
            if (HasNaturalFeatureInCenterRing(map, legacyHalfSpan, spawnCoreHalfSpan))
                seedsWithCenterRingFeatures++;
        }

        Assert.True(
            seedsWithCenterRingFeatures >= 8,
            $"Expected natural center-ring features in multiple seeds, got {seedsWithCenterRingFeatures}/20.");
    }

    [Fact]
    public void Generate_TemperatePlains_UsesGrassAsPrimarySurface()
    {
        const int width = 48;
        const int height = 48;
        var grassRatios = new List<float>(8);

        for (var seed = 410; seed < 418; seed++)
        {
            var map = EmbarkGenerator.Generate(width: width, height: height, depth: 8, seed: seed, biomeId: MacroBiomeIds.TemperatePlains);
            var grass = CountSurfaceTiles(map, GeneratedTileDefIds.Grass);
            grassRatios.Add(grass / (float)(width * height));
        }

        grassRatios.Sort();
        var median = grassRatios[grassRatios.Count / 2];
        Assert.True(median >= 0.45f, $"Expected temperate plains median grass coverage >= 0.45, got {median:F3}.");
    }

    [Fact]
    public void Generate_Desert_UsesSandAsPrimarySurface()
    {
        const int width = 48;
        const int height = 48;
        var sandRatios = new List<float>(8);

        for (var seed = 510; seed < 518; seed++)
        {
            var map = EmbarkGenerator.Generate(width: width, height: height, depth: 8, seed: seed, biomeId: MacroBiomeIds.Desert);
            var sand = CountSurfaceTiles(map, GeneratedTileDefIds.Sand);
            sandRatios.Add(sand / (float)(width * height));
        }

        sandRatios.Sort();
        var median = sandRatios[sandRatios.Count / 2];
        Assert.True(median >= 0.60f, $"Expected desert median sand coverage >= 0.60, got {median:F3}.");
    }

    [Fact]
    public void Generate_MistyMarsh_UsesMudAsPrimarySurface()
    {
        const int width = 48;
        const int height = 48;
        var mudRatios = new List<float>(8);

        for (var seed = 620; seed < 628; seed++)
        {
            var map = EmbarkGenerator.Generate(width: width, height: height, depth: 8, seed: seed, biomeId: MacroBiomeIds.MistyMarsh);
            var mud = CountSurfaceTiles(map, GeneratedTileDefIds.Mud);
            mudRatios.Add(mud / (float)(width * height));
        }

        mudRatios.Sort();
        var median = mudRatios[mudRatios.Count / 2];
        Assert.True(median >= 0.30f, $"Expected marsh median mud coverage >= 0.30, got {median:F3}.");
    }

    [Fact]
    public void Generate_Tundra_UsesSnowAsPrimarySurface()
    {
        const int width = 48;
        const int height = 48;
        var snowRatios = new List<float>(8);

        for (var seed = 730; seed < 738; seed++)
        {
            var map = EmbarkGenerator.Generate(width: width, height: height, depth: 8, seed: seed, biomeId: MacroBiomeIds.Tundra);
            var snow = CountSurfaceTiles(map, GeneratedTileDefIds.Snow);
            snowRatios.Add(snow / (float)(width * height));
        }

        snowRatios.Sort();
        var median = snowRatios[snowRatios.Count / 2];
        Assert.True(median >= 0.55f, $"Expected tundra median snow coverage >= 0.55, got {median:F3}.");
    }

    [Fact]
    public void Generate_SpawnCore_RemainsDryAndPassable()
    {
        const int size = 48;
        const int spawnCoreHalfSpan = 5;
        var map = EmbarkGenerator.Generate(width: size, height: size, depth: 8, seed: 4021, biomeId: MacroBiomeIds.TropicalRainforest);

        var cx = size / 2;
        var cy = size / 2;
        var minX = Math.Max(1, cx - spawnCoreHalfSpan);
        var maxX = Math.Min(size - 2, cx + spawnCoreHalfSpan - 1);
        var minY = Math.Max(1, cy - spawnCoreHalfSpan);
        var maxY = Math.Min(size - 2, cy + spawnCoreHalfSpan - 1);

        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var tile = map.GetTile(x, y, 0);
            Assert.True(tile.IsPassable, $"Spawn core tile should be passable at ({x},{y}).");
            Assert.NotEqual(GeneratedTileDefIds.Water, tile.TileDefId);
            Assert.Equal(GeneratedFluidType.None, tile.FluidType);
        }
    }

    [Fact]
    public void Generate_AnchoredNorthSouthRiver_RemainsConnectedAcrossSurface()
    {
        var settings = new LocalGenerationSettings(
            Width: 48,
            Height: 48,
            Depth: 8,
            BiomeOverrideId: MacroBiomeIds.TemperatePlains,
            RiverPortals:
            [
                new LocalRiverPortal(LocalMapEdge.North, 0.5f),
                new LocalRiverPortal(LocalMapEdge.South, 0.5f),
            ]);

        var map = EmbarkGenerator.Generate(settings, seed: 7331);
        var northEdgeWater = CountSurfaceWaterAtY(map, 1);
        var southEdgeWater = CountSurfaceWaterAtY(map, map.Height - 2);

        Assert.True(northEdgeWater > 0, $"Expected anchored river water near north edge, got {northEdgeWater}.");
        Assert.True(southEdgeWater > 0, $"Expected anchored river water near south edge, got {southEdgeWater}.");
        Assert.True(
            HasConnectedSurfaceWaterFromNorthToSouth(map),
            "Expected a continuous anchored channel from north to south without isolated center gaps.");
    }

    [Fact]
    public void Generate_AnchoredRiverStrength_IncreasesWaterFootprintAndDepth()
    {
        var weakSettings = new LocalGenerationSettings(
            Width: 48,
            Height: 48,
            Depth: 8,
            BiomeOverrideId: MacroBiomeIds.TemperatePlains,
            RiverPortals:
            [
                new LocalRiverPortal(LocalMapEdge.North, 0.5f, Strength: 1),
                new LocalRiverPortal(LocalMapEdge.South, 0.5f, Strength: 1),
            ]);

        var strongSettings = weakSettings with
        {
            RiverPortals =
            [
                new LocalRiverPortal(LocalMapEdge.North, 0.5f, Strength: 6),
                new LocalRiverPortal(LocalMapEdge.South, 0.5f, Strength: 6),
            ],
        };

        var weak = EmbarkGenerator.Generate(weakSettings, seed: 7331);
        var strong = EmbarkGenerator.Generate(strongSettings, seed: 7331);

        var weakWater = CountSurfaceTiles(weak, GeneratedTileDefIds.Water);
        var strongWater = CountSurfaceTiles(strong, GeneratedTileDefIds.Water);
        var weakDeepWater = CountSurfaceWaterAtOrAboveLevel(weak, minLevel: 5);
        var strongDeepWater = CountSurfaceWaterAtOrAboveLevel(strong, minLevel: 5);

        Assert.True(
            strongWater > weakWater,
            $"Expected stronger anchored river to increase water footprint ({strongWater} vs {weakWater}).");
        Assert.True(
            strongDeepWater > weakDeepWater,
            $"Expected stronger anchored river to increase deep-water cells ({strongDeepWater} vs {weakDeepWater}).");
    }

    [Fact]
    public void Generate_MarshPools_AvoidExtremeAdjacentSurfaceWaterDepthCliffs()
    {
        var settings = new LocalGenerationSettings(
            Width: 48,
            Height: 48,
            Depth: 8,
            BiomeOverrideId: MacroBiomeIds.MistyMarsh,
            StreamBandBias: -3,
            MarshPoolBias: 12);

        var map = EmbarkGenerator.Generate(settings, seed: 8461);
        var waterCells = CountSurfaceTiles(map, GeneratedTileDefIds.Water);
        var maxAdjacentDelta = MaxAdjacentSurfaceWaterLevelDelta(map);

        Assert.True(waterCells > 0, "Expected marsh settings to generate surface water.");
        Assert.True(
            maxAdjacentDelta <= 2,
            $"Expected neighboring water tiles to avoid extreme depth cliffs, but observed delta {maxAdjacentDelta}.");
    }

    [Fact]
    public void Generate_TemperateAnchoredRiver_ProducesRiparianWillowTrees()
    {
        var settings = new LocalGenerationSettings(
            Width: 48,
            Height: 48,
            Depth: 8,
            BiomeOverrideId: MacroBiomeIds.TemperatePlains,
            TreeDensityBias: 0.65f,
            RiverPortals:
            [
                new LocalRiverPortal(LocalMapEdge.North, 0.42f, Strength: 4),
                new LocalRiverPortal(LocalMapEdge.South, 0.42f, Strength: 4),
            ]);

        var map = EmbarkGenerator.Generate(settings, seed: 8123);
        var riparianWillows = 0;

        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId != GeneratedTileDefIds.Tree)
                continue;
            if (!string.Equals(tile.TreeSpeciesId, TreeSpeciesIds.Willow, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!HasSurfaceWaterWithinDistance(map, x, y, maxDistance: 2))
                continue;

            riparianWillows++;
        }

        Assert.True(riparianWillows > 0, "Expected at least one willow tree to appear along anchored riverbanks.");
    }

    [Fact]
    public void Generate_ExplicitSettlementAnchor_CreatesLocalForestClearingNearAnchor()
    {
        var baseline = new LocalGenerationSettings(
            Width: 48,
            Height: 48,
            Depth: 8,
            BiomeOverrideId: MacroBiomeIds.ConiferForest,
            TreeDensityBias: 0.55f,
            ForestPatchBias: 0.45f);
        var settled = baseline with
        {
            SettlementInfluence = 1f,
            SettlementAnchors = [new LocalSettlementAnchor(0.18f, 0.82f, Strength: 5)],
        };

        var baselineMap = EmbarkGenerator.Generate(baseline, seed: 8442);
        var settledMap = EmbarkGenerator.Generate(settled, seed: 8442);
        var anchorX = ResolveSettlementAxisCoordinate(0.18f, settledMap.Width);
        var anchorY = ResolveSettlementAxisCoordinate(0.82f, settledMap.Height);
        var baselineTrees = CountSurfaceTreesInRadius(baselineMap, anchorX, anchorY, radius: 6);
        var settledTrees = CountSurfaceTreesInRadius(settledMap, anchorX, anchorY, radius: 6);

        Assert.True(
            settledTrees + 6 < baselineTrees,
            $"Expected explicit settlement anchor to carve a visible local clearing ({settledTrees} vs {baselineTrees}).");
    }

    [Fact]
    public void Generate_ExplicitRoadPortals_CarveDeterministicForestCorridors()
    {
        var baseSettings = new LocalGenerationSettings(
            Width: 48,
            Height: 48,
            Depth: 8,
            BiomeOverrideId: MacroBiomeIds.ConiferForest,
            TreeDensityBias: 0.58f,
            ForestPatchBias: 0.48f,
            SettlementInfluence: 0.72f,
            SettlementAnchors: [new LocalSettlementAnchor(0.5f, 0.5f, Strength: 4)]);
        var withRoads = baseSettings with
        {
            RoadInfluence = 1f,
            RoadPortals =
            [
                new LocalRoadPortal(LocalMapEdge.North, 0.36f, Width: 2),
                new LocalRoadPortal(LocalMapEdge.South, 0.36f, Width: 2),
            ],
        };

        var noRoadMap = EmbarkGenerator.Generate(baseSettings, seed: 9113);
        var roadMap = EmbarkGenerator.Generate(withRoads, seed: 9113);
        var noRoadTrees = CountSurfaceTiles(noRoadMap, GeneratedTileDefIds.Tree);
        var roadTrees = CountSurfaceTiles(roadMap, GeneratedTileDefIds.Tree);
        var northNoRoadTrees = CountSurfaceTreesAtY(noRoadMap, y: 1);
        var northRoadTrees = CountSurfaceTreesAtY(roadMap, y: 1);
        var noRoadSurfaceRoadTiles = CountSurfaceTiles(noRoadMap, GeneratedTileDefIds.StoneBrick);
        var roadSurfaceRoadTiles = CountSurfaceTiles(roadMap, GeneratedTileDefIds.StoneBrick);

        Assert.True(
            roadTrees + 8 < noRoadTrees,
            $"Expected explicit road portals to reduce forest cover along carved corridors ({roadTrees} vs {noRoadTrees}).");
        Assert.True(
            northRoadTrees < northNoRoadTrees,
            $"Expected explicit road portal to clear trees near north edge ({northRoadTrees} vs {northNoRoadTrees}).");
        Assert.True(
            roadSurfaceRoadTiles > noRoadSurfaceRoadTiles,
            $"Expected explicit road portals to stamp visible road surface tiles ({roadSurfaceRoadTiles} vs {noRoadSurfaceRoadTiles}).");
    }

    [Fact]
    public void Generate_ForestCoverageTarget_BiasesTreeDensity()
    {
        var sparseSettings = new LocalGenerationSettings(
            Width: 48,
            Height: 48,
            Depth: 8,
            BiomeOverrideId: MacroBiomeIds.ConiferForest,
            ForestCoverageTarget: 0.12f);
        var denseSettings = sparseSettings with { ForestCoverageTarget = 0.62f };

        var sparseMap = EmbarkGenerator.Generate(sparseSettings, seed: 9119);
        var denseMap = EmbarkGenerator.Generate(denseSettings, seed: 9119);
        var sparseTrees = CountSurfaceTiles(sparseMap, GeneratedTileDefIds.Tree);
        var denseTrees = CountSurfaceTiles(denseMap, GeneratedTileDefIds.Tree);

        Assert.True(
            denseTrees > sparseTrees + 24,
            $"Expected higher forest target to increase trees ({denseTrees} vs {sparseTrees}).");
    }

    [Fact]
    public void Generate_AlluvialGeology_ProducesLayeredUndergroundRockTypes()
    {
        var settings = new LocalGenerationSettings(
            Width: 32,
            Height: 32,
            Depth: 10,
            BiomeOverrideId: MacroBiomeIds.TemperatePlains,
            GeologyProfileId: GeologyProfileIds.AlluvialBasin);
        var map = EmbarkGenerator.Generate(settings, seed: 321);

        var undergroundWallMaterials = new HashSet<string>(StringComparer.Ordinal);
        for (var z = 1; z < map.Depth; z++)
        {
            var tile = map.GetTile(0, 0, z);
            Assert.Equal(GeneratedTileDefIds.StoneWall, tile.TileDefId);
            if (!string.IsNullOrWhiteSpace(tile.MaterialId))
                undergroundWallMaterials.Add(tile.MaterialId!);
        }

        Assert.Contains("sandstone", undergroundWallMaterials);
        Assert.Contains("limestone", undergroundWallMaterials);
        Assert.True(
            undergroundWallMaterials.Count >= 2,
            $"Expected layered underground materials, got: {string.Join(", ", undergroundWallMaterials)}");
    }

    [Fact]
    public void Generate_GeologyProfileOverride_ChangesUndergroundFingerprint()
    {
        var baseSettings = new LocalGenerationSettings(
            Width: 32,
            Height: 32,
            Depth: 10,
            BiomeOverrideId: MacroBiomeIds.TemperatePlains,
            GeologyProfileId: GeologyProfileIds.MixedBedrock);
        var altSettings = baseSettings with { GeologyProfileId = GeologyProfileIds.MetamorphicSpine };

        var mixed = EmbarkGenerator.Generate(baseSettings, seed: 77);
        var metamorphic = EmbarkGenerator.Generate(altSettings, seed: 77);

        var mixedFingerprint = UndergroundFingerprint(mixed);
        var metamorphicFingerprint = UndergroundFingerprint(metamorphic);
        Assert.NotEqual(mixedFingerprint, metamorphicFingerprint);
    }

    [Fact]
    public void Generate_AlluvialGeology_PlacesDeterministicCompatibleUndergroundOre()
    {
        var settings = new LocalGenerationSettings(
            Width: 48,
            Height: 48,
            Depth: 12,
            BiomeOverrideId: MacroBiomeIds.TemperatePlains,
            GeologyProfileId: GeologyProfileIds.AlluvialBasin);
        var map = EmbarkGenerator.Generate(settings, seed: 919);

        var oreTiles = 0;
        for (var z = 1; z < map.Depth; z++)
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, z);
            if (tile.OreId is null)
                continue;

            oreTiles++;
            Assert.False(tile.IsPassable);
            Assert.True(MineralVeinRegistry.IsOreCompatible(tile.OreId, tile.MaterialId),
                $"Ore {tile.OreId} incompatible with host rock {tile.MaterialId} at ({x},{y},{z}).");
        }

        Assert.True(oreTiles > 0, "Expected alluvial geology to generate at least one underground ore tile.");

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
            Assert.Null(map.GetTile(x, y, 0).OreId);
    }

    [Fact]
    public void Generate_AlluvialGeology_ProducesSingleUndergroundAquiferBand()
    {
        var settings = new LocalGenerationSettings(
            Width: 40,
            Height: 40,
            Depth: 12,
            BiomeOverrideId: MacroBiomeIds.TemperatePlains,
            GeologyProfileId: GeologyProfileIds.AlluvialBasin);
        var map = EmbarkGenerator.Generate(settings, seed: 2026);

        var aquiferLayers = new HashSet<int>();
        var aquiferTiles = 0;
        for (var z = 1; z < map.Depth; z++)
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, z);
            if (!tile.IsAquifer)
                continue;

            aquiferTiles++;
            aquiferLayers.Add(z);
            Assert.False(tile.IsPassable);
            Assert.Equal(GeneratedFluidType.None, tile.FluidType);
        }

        Assert.True(aquiferTiles > 0, "Expected at least one aquifer tile in alluvial geology.");
        Assert.Single(aquiferLayers);

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
            Assert.False(map.GetTile(x, y, 0).IsAquifer);
    }

    [Fact]
    public void Generate_DeepMap_CarvesThreeConnectedCaveLayers()
    {
        var settings = new LocalGenerationSettings(
            Width: 48,
            Height: 48,
            Depth: 20,
            BiomeOverrideId: MacroBiomeIds.Highland,
            GeologyProfileId: GeologyProfileIds.MixedBedrock);
        var map = EmbarkGenerator.Generate(settings, seed: 9876);

        var caveLayers = new List<int>();
        for (var z = 2; z < map.Depth; z++)
        {
            var floorTiles = CountUndergroundCaveFloorTiles(map, z);
            if (floorTiles == 0)
                continue;

            caveLayers.Add(z);
            var connected = CountConnectedUndergroundCaveTiles(map, z);
            Assert.Equal(floorTiles, connected);
        }

        Assert.Equal(3, caveLayers.Count);
    }

    [Fact]
    public void Generate_DeepMap_WithAnchoredRiver_FeedsFirstCaveLayerWithWater()
    {
        var settings = new LocalGenerationSettings(
            Width: 48,
            Height: 48,
            Depth: 20,
            BiomeOverrideId: MacroBiomeIds.TemperatePlains,
            RiverPortals:
            [
                new LocalRiverPortal(LocalMapEdge.North, 0.5f),
                new LocalRiverPortal(LocalMapEdge.South, 0.5f),
            ]);
        var map = EmbarkGenerator.Generate(settings, seed: 7314);

        var caveLayers = new List<int>();
        for (var z = 2; z < map.Depth; z++)
        {
            if (CountUndergroundCaveFloorTiles(map, z) <= 0)
                continue;
            caveLayers.Add(z);
        }

        Assert.NotEmpty(caveLayers);
        var firstCaveZ = caveLayers[0];
        var waterTiles = CountWaterTilesAtZ(map, firstCaveZ);
        Assert.True(waterTiles > 0, $"Expected cave layer z={firstCaveZ} to receive seep water from anchored river.");
    }

    [Fact]
    public void Generate_DeepMap_WithAnchoredRiver_CarriesWaterAcrossIntermediateDepths()
    {
        var settings = new LocalGenerationSettings(
            Width: 48,
            Height: 48,
            Depth: 20,
            BiomeOverrideId: MacroBiomeIds.TemperatePlains,
            RiverPortals:
            [
                new LocalRiverPortal(LocalMapEdge.North, 0.5f),
                new LocalRiverPortal(LocalMapEdge.South, 0.5f),
            ]);
        var map = EmbarkGenerator.Generate(settings, seed: 7314);

        var caveLayers = new List<int>();
        for (var z = 2; z < map.Depth; z++)
        {
            if (CountUndergroundCaveFloorTiles(map, z) <= 0)
                continue;
            caveLayers.Add(z);
        }

        Assert.NotEmpty(caveLayers);
        var firstCaveZ = caveLayers[0];
        var hasIntermediateWater = false;
        for (var z = 1; z < firstCaveZ; z++)
        {
            if (CountWaterTilesAtZ(map, z) <= 0)
                continue;

            hasIntermediateWater = true;
            break;
        }

        Assert.True(
            hasIntermediateWater,
            $"Expected at least one intermediate z-layer below surface and above first cave (z={firstCaveZ}) to contain seep water.");
    }

    [Theory]
    [InlineData(MacroBiomeIds.TropicalRainforest)]
    [InlineData(MacroBiomeIds.Savanna)]
    [InlineData(MacroBiomeIds.Desert)]
    [InlineData(MacroBiomeIds.Tundra)]
    [InlineData(MacroBiomeIds.BorealForest)]
    [InlineData(MacroBiomeIds.IcePlains)]
    [InlineData(MacroBiomeIds.OceanShallow)]
    [InlineData(MacroBiomeIds.OceanDeep)]
    public void Generate_ExpandedBiomes_AreDeterministic(string biomeId)
    {
        var a = EmbarkGenerator.Generate(width: 32, height: 32, depth: 8, seed: 341, biomeId: biomeId);
        var b = EmbarkGenerator.Generate(width: 32, height: 32, depth: 8, seed: 341, biomeId: biomeId);

        for (var x = 0; x < a.Width; x++)
        for (var y = 0; y < a.Height; y++)
        for (var z = 0; z < a.Depth; z++)
            Assert.Equal(a.GetTile(x, y, z), b.GetTile(x, y, z));
    }

    [Fact]
    public void Generate_OceanDeep_FloodsMostSurfaceOutsideCentralEmbarkZone()
    {
        const int size = 48;
        var map = EmbarkGenerator.Generate(width: size, height: size, depth: 8, seed: 442, biomeId: MacroBiomeIds.OceanDeep);

        var flooded = 0;
        var sampled = 0;
        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            if (IsInEmbarkCenter(size, size, x, y))
                continue;

            sampled++;
            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId == GeneratedTileDefIds.Water)
                flooded++;
        }

        Assert.True(sampled > 0);
        Assert.True(
            (flooded / (float)sampled) >= 0.55f,
            $"Expected ocean deep surface to be mostly flooded, got {flooded}/{sampled}.");

        var center = map.GetTile(size / 2, size / 2, 0);
        Assert.NotEqual(GeneratedTileDefIds.Water, center.TileDefId);
    }

    [Fact]
    public void Generate_ConiferForest_UsesConiferTreeSpeciesOnly()
    {
        var map = EmbarkGenerator.Generate(width: 48, height: 48, depth: 8, seed: 1502, biomeId: MacroBiomeIds.ConiferForest);
        var species = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId != GeneratedTileDefIds.Tree)
                continue;

            Assert.NotNull(tile.TreeSpeciesId);
            species.Add(tile.TreeSpeciesId!);
        }

        Assert.True(species.Count > 0, "Expected forest map to contain trees.");
        Assert.All(species, value =>
            Assert.True(
                string.Equals(value, TreeSpeciesIds.Pine, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, TreeSpeciesIds.Spruce, StringComparison.OrdinalIgnoreCase),
                $"Unexpected conifer species '{value}'."));
    }

    [Fact]
    public void Generate_ConiferForest_SurfaceTrees_CreateSoilSubsurfaceBelow()
    {
        var map = EmbarkGenerator.Generate(width: 48, height: 48, depth: 8, seed: 1502, biomeId: MacroBiomeIds.ConiferForest);
        var treeCount = 0;

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId != GeneratedTileDefIds.Tree)
                continue;

            treeCount++;
            var below = map.GetTile(x, y, 1);
            Assert.Equal(GeneratedTileDefIds.SoilWall, below.TileDefId);
            Assert.Equal("soil", below.MaterialId);
            Assert.False(below.IsPassable);
        }

        Assert.True(treeCount > 0, "Expected forest map to contain at least one tree.");
    }

    [Fact]
    public void Generate_MistyMarsh_SurfaceTrees_CreateMudSubsurfaceBelow()
    {
        var map = EmbarkGenerator.Generate(width: 48, height: 48, depth: 8, seed: 123, biomeId: MacroBiomeIds.MistyMarsh);
        var treeCount = 0;

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId != GeneratedTileDefIds.Tree)
                continue;

            treeCount++;
            var below = map.GetTile(x, y, 1);
            Assert.Equal(GeneratedTileDefIds.SoilWall, below.TileDefId);
            Assert.Equal("mud", below.MaterialId);
        }

        Assert.True(treeCount > 0, "Expected marsh map to contain at least one tree.");
    }

    [Fact]
    public void Generate_Highland_StoneSurface_DoesNotSpawnSurfaceTrees()
    {
        for (var seed = 40; seed < 46; seed++)
        {
            var map = EmbarkGenerator.Generate(width: 48, height: 48, depth: 8, seed: seed, biomeId: MacroBiomeIds.Highland);
            Assert.Equal(0, CountSurfaceTiles(map, GeneratedTileDefIds.Tree));
        }
    }

    [Fact]
    public void Generate_TropicalRainforest_UsesTropicalTreeSpeciesOnly()
    {
        var map = EmbarkGenerator.Generate(width: 48, height: 48, depth: 8, seed: 1503, biomeId: MacroBiomeIds.TropicalRainforest);
        var species = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId != GeneratedTileDefIds.Tree)
                continue;

            Assert.NotNull(tile.TreeSpeciesId);
            species.Add(tile.TreeSpeciesId!);
        }

        Assert.True(species.Count > 0, "Expected rainforest map to contain trees.");
        Assert.All(species, value =>
            Assert.True(
                string.Equals(value, TreeSpeciesIds.Palm, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, TreeSpeciesIds.Baobab, StringComparison.OrdinalIgnoreCase),
                $"Unexpected tropical species '{value}'."));
    }

    [Fact]
    public void Generate_DeepMap_FillsBottomLayerWithMagmaSea()
    {
        var settings = new LocalGenerationSettings(
            Width: 36,
            Height: 36,
            Depth: 20,
            BiomeOverrideId: MacroBiomeIds.Highland,
            GeologyProfileId: GeologyProfileIds.MixedBedrock);
        var map = EmbarkGenerator.Generate(settings, seed: 1618);

        var magmaZ = map.Depth - 1;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, magmaZ);
            Assert.Equal(GeneratedTileDefIds.Magma, tile.TileDefId);
            Assert.True(tile.IsPassable);
            Assert.Equal(GeneratedFluidType.Magma, tile.FluidType);
            Assert.Equal((byte)7, tile.FluidLevel);
            Assert.Null(tile.OreId);
            Assert.False(tile.IsAquifer);
        }
    }

    [Fact]
    public void Generate_ShallowMap_DoesNotCreateMagmaSea()
    {
        var settings = new LocalGenerationSettings(
            Width: 36,
            Height: 36,
            Depth: 11,
            BiomeOverrideId: MacroBiomeIds.Highland,
            GeologyProfileId: GeologyProfileIds.MixedBedrock);
        var map = EmbarkGenerator.Generate(settings, seed: 1618);

        for (var z = 1; z < map.Depth; z++)
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, z);
            Assert.NotEqual(GeneratedTileDefIds.Magma, tile.TileDefId);
            Assert.NotEqual(GeneratedFluidType.Magma, tile.FluidType);
        }
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

    private static bool HasNaturalFeatureInCenterRing(
        GeneratedEmbarkMap map,
        int outerHalfSpan,
        int innerHalfSpan)
    {
        var cx = map.Width / 2;
        var cy = map.Height / 2;
        var outerMinX = Math.Max(1, cx - outerHalfSpan);
        var outerMaxX = Math.Min(map.Width - 2, cx + outerHalfSpan - 1);
        var outerMinY = Math.Max(1, cy - outerHalfSpan);
        var outerMaxY = Math.Min(map.Height - 2, cy + outerHalfSpan - 1);
        var innerMinX = Math.Max(1, cx - innerHalfSpan);
        var innerMaxX = Math.Min(map.Width - 2, cx + innerHalfSpan - 1);
        var innerMinY = Math.Max(1, cy - innerHalfSpan);
        var innerMaxY = Math.Min(map.Height - 2, cy + innerHalfSpan - 1);

        for (var x = outerMinX; x <= outerMaxX; x++)
        for (var y = outerMinY; y <= outerMaxY; y++)
        {
            var inInner =
                x >= innerMinX && x <= innerMaxX &&
                y >= innerMinY && y <= innerMaxY;
            if (inInner)
                continue;

            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId != GeneratedTileDefIds.Soil &&
                tile.TileDefId != GeneratedTileDefIds.Grass &&
                tile.TileDefId != GeneratedTileDefIds.Sand &&
                tile.TileDefId != GeneratedTileDefIds.Mud &&
                tile.TileDefId != GeneratedTileDefIds.Snow &&
                tile.TileDefId != GeneratedTileDefIds.StoneFloor)
            {
                return true;
            }
        }

        return false;
    }

    private static int CountUndergroundCaveFloorTiles(GeneratedEmbarkMap map, int z)
    {
        var count = 0;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, z);
            if (tile.IsPassable && tile.TileDefId == GeneratedTileDefIds.StoneFloor)
                count++;
        }

        return count;
    }

    private static int CountWaterTilesAtZ(GeneratedEmbarkMap map, int z)
    {
        var count = 0;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, z);
            if (tile.TileDefId == GeneratedTileDefIds.Water || tile.FluidType == GeneratedFluidType.Water)
                count++;
        }

        return count;
    }

    private static int CountConnectedUndergroundCaveTiles(GeneratedEmbarkMap map, int z)
    {
        var visited = new bool[map.Width, map.Height];
        var queue = new Queue<(int X, int Y)>();
        (int X, int Y) start = (-1, -1);
        for (var x = 0; x < map.Width && start.X < 0; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, z);
            if (tile.IsPassable && tile.TileDefId == GeneratedTileDefIds.StoneFloor)
            {
                start = (x, y);
                break;
            }
        }

        if (start.X < 0)
            return 0;

        visited[start.X, start.Y] = true;
        queue.Enqueue(start);
        var connected = 0;
        var offsets = new (int X, int Y)[]
        {
            (1, 0),
            (-1, 0),
            (0, 1),
            (0, -1),
        };

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            connected++;

            foreach (var (dx, dy) in offsets)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
                    continue;
                if (visited[nx, ny])
                    continue;

                var tile = map.GetTile(nx, ny, z);
                if (!tile.IsPassable || tile.TileDefId != GeneratedTileDefIds.StoneFloor)
                    continue;

                visited[nx, ny] = true;
                queue.Enqueue((nx, ny));
            }
        }

        return connected;
    }

    private static string UndergroundFingerprint(GeneratedEmbarkMap map)
    {
        var parts = new List<string>(map.Depth);
        for (var z = 1; z < map.Depth; z++)
        {
            var tile = map.GetTile(0, 0, z);
            parts.Add($"{z}:{tile.TileDefId}:{tile.MaterialId}:{tile.OreId}:{tile.IsAquifer}");
        }

        return string.Join("|", parts);
    }

    private static bool IsInEmbarkCenter(int width, int height, int x, int y)
    {
        const int halfSpan = 8;
        var cx = width / 2;
        var cy = height / 2;
        var minX = Math.Max(1, cx - halfSpan);
        var maxX = Math.Min(width - 2, cx + halfSpan - 1);
        var minY = Math.Max(1, cy - halfSpan);
        var maxY = Math.Min(height - 2, cy + halfSpan - 1);
        return x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    private static int CountSurfaceWaterAtY(GeneratedEmbarkMap map, int y)
    {
        var count = 0;
        for (var x = 0; x < map.Width; x++)
        {
            if (map.GetTile(x, y, 0).TileDefId == GeneratedTileDefIds.Water)
                count++;
        }

        return count;
    }

    private static int CountSurfaceWaterAtOrAboveLevel(GeneratedEmbarkMap map, byte minLevel)
    {
        var count = 0;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId == GeneratedTileDefIds.Water && tile.FluidLevel >= minLevel)
                count++;
        }

        return count;
    }

    private static int MaxAdjacentSurfaceWaterLevelDelta(GeneratedEmbarkMap map)
    {
        var maxDelta = 0;
        var offsets = new (int X, int Y)[]
        {
            (1, 0),
            (0, 1),
        };

        for (var x = 1; x < map.Width - 1; x++)
        for (var y = 1; y < map.Height - 1; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId != GeneratedTileDefIds.Water)
                continue;

            foreach (var (dx, dy) in offsets)
            {
                var neighbor = map.GetTile(x + dx, y + dy, 0);
                if (neighbor.TileDefId != GeneratedTileDefIds.Water)
                    continue;

                var delta = Math.Abs(tile.FluidLevel - neighbor.FluidLevel);
                if (delta > maxDelta)
                    maxDelta = delta;
            }
        }

        return maxDelta;
    }

    private static bool HasSurfaceWaterWithinDistance(GeneratedEmbarkMap map, int x, int y, int maxDistance)
    {
        for (var dx = -maxDistance; dx <= maxDistance; dx++)
        for (var dy = -maxDistance; dy <= maxDistance; dy++)
        {
            var distance = Math.Abs(dx) + Math.Abs(dy);
            if (distance == 0 || distance > maxDistance)
                continue;

            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
                continue;

            var tile = map.GetTile(nx, ny, 0);
            if (tile.TileDefId == GeneratedTileDefIds.Water || tile.FluidType == GeneratedFluidType.Water)
                return true;
        }

        return false;
    }

    private static int CountSurfaceTreesInRadius(GeneratedEmbarkMap map, int centerX, int centerY, int radius)
    {
        var count = 0;
        var radiusSq = radius * radius;
        for (var y = centerY - radius; y <= centerY + radius; y++)
        for (var x = centerX - radius; x <= centerX + radius; x++)
        {
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
                continue;

            var dx = x - centerX;
            var dy = y - centerY;
            if ((dx * dx) + (dy * dy) > radiusSq)
                continue;

            if (map.GetTile(x, y, 0).TileDefId == GeneratedTileDefIds.Tree)
                count++;
        }

        return count;
    }

    private static int CountSurfaceTreesAtY(GeneratedEmbarkMap map, int y)
    {
        var count = 0;
        for (var x = 0; x < map.Width; x++)
        {
            if (map.GetTile(x, y, 0).TileDefId == GeneratedTileDefIds.Tree)
                count++;
        }

        return count;
    }

    private static int ResolveSettlementAxisCoordinate(float normalized, int axisSize)
    {
        var min = 2;
        var max = axisSize - 3;
        if (max < min)
            return Math.Clamp(axisSize / 2, 1, Math.Max(1, axisSize - 2));

        var clamped = Math.Clamp(normalized, 0f, 1f);
        return min + (int)MathF.Round(clamped * (max - min));
    }

    private static bool HasConnectedSurfaceWaterFromNorthToSouth(GeneratedEmbarkMap map)
    {
        var visited = new bool[map.Width, map.Height];
        var queue = new Queue<(int X, int Y)>();

        for (var x = 0; x < map.Width; x++)
        {
            if (map.GetTile(x, 1, 0).TileDefId != GeneratedTileDefIds.Water)
                continue;

            visited[x, 1] = true;
            queue.Enqueue((x, 1));
        }

        var offsets = new (int X, int Y)[]
        {
            (1, 0),
            (-1, 0),
            (0, 1),
            (0, -1),
        };

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            if (y == map.Height - 2)
                return true;

            foreach (var (dx, dy) in offsets)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
                    continue;
                if (visited[nx, ny])
                    continue;
                if (map.GetTile(nx, ny, 0).TileDefId != GeneratedTileDefIds.Water)
                    continue;

                visited[nx, ny] = true;
                queue.Enqueue((nx, ny));
            }
        }

        return false;
    }

    private static int CountLargestConnectedSurfacePatch(GeneratedEmbarkMap map, string tileDefId)
    {
        var visited = new bool[map.Width, map.Height];
        var queue = new Queue<(int X, int Y)>();
        var best = 0;
        var offsets = new (int X, int Y)[]
        {
            (1, 0),
            (-1, 0),
            (0, 1),
            (0, -1),
        };

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (visited[x, y])
                continue;
            if (map.GetTile(x, y, 0).TileDefId != tileDefId)
                continue;

            visited[x, y] = true;
            queue.Enqueue((x, y));
            var size = 0;

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                size++;

                foreach (var (dx, dy) in offsets)
                {
                    var nx = cx + dx;
                    var ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
                        continue;
                    if (visited[nx, ny])
                        continue;
                    if (map.GetTile(nx, ny, 0).TileDefId != tileDefId)
                        continue;

                    visited[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }

            if (size > best)
                best = size;
        }

        return best;
    }
}

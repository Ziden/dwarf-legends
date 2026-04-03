using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Local;
using DwarfFortress.WorldGen.Maps;
using DwarfFortress.WorldGen.Regions;

namespace DwarfFortress.WorldGen.Tests;

public sealed class LocalLayerGeneratorTests
{
    [Fact]
    public void Generate_SameRegionAndSettings_ProducesDeterministicLocalMap()
    {
        var region = new GeneratedRegionMap(seed: 901, width: 8, height: 8, worldCoord: new WorldCoord(3, 4));
        region.SetTile(2, 5, new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.DenseConifer,
            Slope: 24,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.82f,
            ResourceRichness: 0.41f,
            SoilDepth: 0.68f,
            Groundwater: 0.62f,
            HasRoad: false,
            HasSettlement: false));

        var coord = new RegionCoord(3, 4, 2, 5);
        var settings = new LocalGenerationSettings(48, 48, 8);
        var generator = new LocalLayerGenerator();

        var mapA = generator.Generate(region, coord, settings);
        var mapB = generator.Generate(region, coord, settings);

        Assert.Equal(Fingerprint(mapA), Fingerprint(mapB));
    }

    [Fact]
    public void Generate_RegionSurfaceClassIntent_InfluencesLocalGroundPalette()
    {
        var region = new GeneratedRegionMap(
            seed: 911,
            width: 1,
            height: 1,
            worldCoord: new WorldCoord(0, 0),
            parentMacroBiomeId: MacroBiomeIds.TemperatePlains);

        region.SetTile(0, 0, new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperatePlainsOpen,
            SurfaceClassId: RegionSurfaceClassIds.Sand,
            Slope: 30,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.42f,
            ResourceRichness: 0.38f,
            SoilDepth: 0.34f,
            Groundwater: 0.20f,
            HasRoad: false,
            HasSettlement: false,
            TemperatureBand: 0.56f,
            MoistureBand: 0.22f));

        var map = new LocalLayerGenerator().Generate(
            region,
            new RegionCoord(0, 0, 0, 0),
            new LocalGenerationSettings(48, 48, 8));

        var sand = CountSurfaceTiles(map, GeneratedTileDefIds.Sand);
        var grass = CountSurfaceTiles(map, GeneratedTileDefIds.Grass);
        Assert.True(sand > grass, $"Expected sand intent to bias local ground palette ({sand} sand vs {grass} grass).");
    }

    [Fact]
    public void Generate_RegionSoilSurfaceIntent_ProducesMostlySoilGround()
    {
        var region = new GeneratedRegionMap(
            seed: 915,
            width: 1,
            height: 1,
            worldCoord: new WorldCoord(0, 0),
            parentMacroBiomeId: MacroBiomeIds.TemperatePlains);

        region.SetTile(0, 0, new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperatePlainsOpen,
            SurfaceClassId: RegionSurfaceClassIds.Soil,
            Slope: 48,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.34f,
            ResourceRichness: 0.44f,
            SoilDepth: 0.26f,
            Groundwater: 0.22f,
            HasRoad: false,
            HasSettlement: false,
            TemperatureBand: 0.56f,
            MoistureBand: 0.20f));

        var map = new LocalLayerGenerator().Generate(
            region,
            new RegionCoord(0, 0, 0, 0),
            new LocalGenerationSettings(48, 48, 8));

        var soil = CountSurfaceTiles(map, GeneratedTileDefIds.Soil);
        var grass = CountSurfaceTiles(map, GeneratedTileDefIds.Grass);
        Assert.True(soil > grass, $"Expected soil intent to bias local ground palette ({soil} soil vs {grass} grass).");
    }

    [Fact]
    public void Generate_VegetatedRegionTile_GeneratesMoreTrees_ThanDrySteppeTile()
    {
        var lushRegion = new GeneratedRegionMap(seed: 1701, width: 4, height: 4, worldCoord: new WorldCoord(1, 1));
        var dryRegion = new GeneratedRegionMap(seed: 1701, width: 4, height: 4, worldCoord: new WorldCoord(1, 1));

        lushRegion.SetTile(1, 1, new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.DenseConifer,
            Slope: 35,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.88f,
            ResourceRichness: 0.46f,
            SoilDepth: 0.84f,
            Groundwater: 0.80f,
            HasRoad: false,
            HasSettlement: false));

        dryRegion.SetTile(1, 1, new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.DrySteppe,
            Slope: 120,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.14f,
            ResourceRichness: 0.64f,
            SoilDepth: 0.18f,
            Groundwater: 0.12f,
            HasRoad: false,
            HasSettlement: false));

        var coord = new RegionCoord(1, 1, 1, 1);
        var settings = new LocalGenerationSettings(48, 48, 8);
        var generator = new LocalLayerGenerator();

        var lushMap = generator.Generate(lushRegion, coord, settings);
        var dryMap = generator.Generate(dryRegion, coord, settings);

        var lushTrees = CountSurfaceTiles(lushMap, GeneratedTileDefIds.Tree);
        var dryTrees = CountSurfaceTiles(dryMap, GeneratedTileDefIds.Tree);

        Assert.True(lushTrees > dryTrees, $"Expected lush region to produce more trees ({lushTrees} vs {dryTrees}).");
    }

    [Fact]
    public void Generate_WetterAndDeeperSoilRegionTile_PromotesTreeGrowth()
    {
        var wetRegion = new GeneratedRegionMap(seed: 1777, width: 4, height: 4, worldCoord: new WorldCoord(1, 1));
        var dryRegion = new GeneratedRegionMap(seed: 1777, width: 4, height: 4, worldCoord: new WorldCoord(1, 1));

        var baseline = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperateWoodland,
            Slope: 45,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.58f,
            ResourceRichness: 0.44f,
            SoilDepth: 0.50f,
            Groundwater: 0.50f,
            HasRoad: false,
            HasSettlement: false);

        for (var x = 0; x < 4; x++)
        for (var y = 0; y < 4; y++)
        {
            wetRegion.SetTile(x, y, baseline);
            dryRegion.SetTile(x, y, baseline);
        }

        wetRegion.SetTile(1, 1, baseline with { SoilDepth = 0.86f, Groundwater = 0.84f });
        dryRegion.SetTile(1, 1, baseline with { SoilDepth = 0.16f, Groundwater = 0.12f });

        var generator = new LocalLayerGenerator();
        var settings = new LocalGenerationSettings(48, 48, 8);
        var coord = new RegionCoord(1, 1, 1, 1);

        var wetMap = generator.Generate(wetRegion, coord, settings);
        var dryMap = generator.Generate(dryRegion, coord, settings);

        var wetTrees = CountSurfaceTiles(wetMap, GeneratedTileDefIds.Tree);
        var dryTrees = CountSurfaceTiles(dryMap, GeneratedTileDefIds.Tree);

        Assert.True(wetTrees > dryTrees, $"Expected wetter/deeper soil region to produce more trees ({wetTrees} vs {dryTrees}).");
    }

    [Fact]
    public void Generate_ForestedNeighborhood_ProducesDenserLocalTrees_ThanSparseNeighborhood()
    {
        var lushRegion = new GeneratedRegionMap(seed: 1888, width: 5, height: 5, worldCoord: new WorldCoord(2, 2), parentMacroBiomeId: MacroBiomeIds.ConiferForest);
        var sparseRegion = new GeneratedRegionMap(seed: 1888, width: 5, height: 5, worldCoord: new WorldCoord(2, 2), parentMacroBiomeId: MacroBiomeIds.ConiferForest);

        var centerTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperateWoodland,
            Slope: 48,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.55f,
            ResourceRichness: 0.44f,
            SoilDepth: 0.52f,
            Groundwater: 0.50f,
            HasRoad: false,
            HasSettlement: false);
        var lushNeighbor = centerTile with
        {
            BiomeVariantId = RegionBiomeVariantIds.DenseConifer,
            VegetationDensity = 0.86f,
            SoilDepth = 0.72f,
            Groundwater = 0.68f,
        };
        var sparseNeighbor = centerTile with
        {
            BiomeVariantId = RegionBiomeVariantIds.DrySteppe,
            VegetationDensity = 0.18f,
            SoilDepth = 0.30f,
            Groundwater = 0.22f,
        };

        for (var x = 0; x < 5; x++)
        for (var y = 0; y < 5; y++)
        {
            lushRegion.SetTile(x, y, sparseNeighbor);
            sparseRegion.SetTile(x, y, sparseNeighbor);
        }

        lushRegion.SetTile(2, 2, centerTile);
        sparseRegion.SetTile(2, 2, centerTile);

        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0)
                continue;

            lushRegion.SetTile(2 + dx, 2 + dy, lushNeighbor);
        }

        var generator = new LocalLayerGenerator();
        var settings = new LocalGenerationSettings(48, 48, 8);
        var coord = new RegionCoord(2, 2, 2, 2);

        var lushMap = generator.Generate(lushRegion, coord, settings);
        var sparseMap = generator.Generate(sparseRegion, coord, settings);

        var lushTrees = CountSurfaceTiles(lushMap, GeneratedTileDefIds.Tree);
        var sparseTrees = CountSurfaceTiles(sparseMap, GeneratedTileDefIds.Tree);

        Assert.True(
            lushTrees > sparseTrees,
            $"Expected forested neighborhood to increase local tree count ({lushTrees} vs {sparseTrees}).");
    }

    [Fact]
    public void Generate_HighVegetationSuitability_ProducesDenserLocalTrees()
    {
        var lushRegion = new GeneratedRegionMap(seed: 2088, width: 3, height: 3, worldCoord: new WorldCoord(1, 1), parentMacroBiomeId: MacroBiomeIds.TemperatePlains);
        var sparseRegion = new GeneratedRegionMap(seed: 2088, width: 3, height: 3, worldCoord: new WorldCoord(1, 1), parentMacroBiomeId: MacroBiomeIds.TemperatePlains);

        var baseTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperateWoodland,
            Slope: 52,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.52f,
            ResourceRichness: 0.46f,
            SoilDepth: 0.54f,
            Groundwater: 0.50f,
            HasRoad: false,
            HasSettlement: false);

        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
        {
            lushRegion.SetTile(x, y, baseTile with { VegetationSuitability = 0.86f });
            sparseRegion.SetTile(x, y, baseTile with { VegetationSuitability = 0.18f });
        }

        var generator = new LocalLayerGenerator();
        var settings = new LocalGenerationSettings(48, 48, 8);
        var coord = new RegionCoord(1, 1, 1, 1);

        var lushMap = generator.Generate(lushRegion, coord, settings);
        var sparseMap = generator.Generate(sparseRegion, coord, settings);

        var lushTrees = CountSurfaceTiles(lushMap, GeneratedTileDefIds.Tree);
        var sparseTrees = CountSurfaceTiles(sparseMap, GeneratedTileDefIds.Tree);

        Assert.True(
            lushTrees > sparseTrees,
            $"Expected higher vegetation suitability to increase local tree count ({lushTrees} vs {sparseTrees}).");
    }

    [Fact]
    public void Generate_HigherParentForestCover_ProducesDenserLocalTrees()
    {
        var forestParentRegion = new GeneratedRegionMap(
            seed: 2144,
            width: 3,
            height: 3,
            worldCoord: new WorldCoord(1, 1),
            parentMacroBiomeId: MacroBiomeIds.TemperatePlains,
            parentForestCover: 0.88f,
            parentMountainCover: 0.20f,
            parentRelief: 0.40f,
            parentMoistureBand: 0.58f,
            parentTemperatureBand: 0.52f);
        var sparseParentRegion = new GeneratedRegionMap(
            seed: 2144,
            width: 3,
            height: 3,
            worldCoord: new WorldCoord(1, 1),
            parentMacroBiomeId: MacroBiomeIds.TemperatePlains,
            parentForestCover: 0.08f,
            parentMountainCover: 0.20f,
            parentRelief: 0.40f,
            parentMoistureBand: 0.58f,
            parentTemperatureBand: 0.52f);

        var baseTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperateWoodland,
            Slope: 48,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.50f,
            ResourceRichness: 0.44f,
            SoilDepth: 0.52f,
            Groundwater: 0.50f,
            HasRoad: false,
            HasSettlement: false,
            VegetationSuitability: 0.54f);

        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
        {
            forestParentRegion.SetTile(x, y, baseTile);
            sparseParentRegion.SetTile(x, y, baseTile);
        }

        var generator = new LocalLayerGenerator();
        var settings = new LocalGenerationSettings(48, 48, 8);
        var coord = new RegionCoord(1, 1, 1, 1);

        var forestParentMap = generator.Generate(forestParentRegion, coord, settings);
        var sparseParentMap = generator.Generate(sparseParentRegion, coord, settings);

        var forestTrees = CountSurfaceTiles(forestParentMap, GeneratedTileDefIds.Tree);
        var sparseTrees = CountSurfaceTiles(sparseParentMap, GeneratedTileDefIds.Tree);

        Assert.True(
            forestTrees > sparseTrees,
            $"Expected higher parent forest cover to increase local tree count ({forestTrees} vs {sparseTrees}).");
    }

    [Fact]
    public void Generate_RiverCellWithRiverNeighbors_AnchorsLocalWaterAtOppositeEdges()
    {
        var region = new GeneratedRegionMap(seed: 2201, width: 5, height: 5, worldCoord: new WorldCoord(2, 2));
        var baseTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperateWoodland,
            Slope: 42,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.55f,
            ResourceRichness: 0.50f,
            SoilDepth: 0.52f,
            Groundwater: 0.50f,
            HasRoad: false,
            HasSettlement: false);

        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
            region.SetTile(x, y, baseTile);

        region.SetTile(2, 2, baseTile with { HasRiver = true });
        region.SetTile(1, 2, baseTile with { HasRiver = true });
        region.SetTile(3, 2, baseTile with { HasRiver = true });

        var generator = new LocalLayerGenerator();
        var map = generator.Generate(region, new RegionCoord(2, 2, 2, 2), new LocalGenerationSettings(48, 48, 8));

        var westEdgeWater = CountSurfaceWaterAtX(map, 1);
        var eastEdgeWater = CountSurfaceWaterAtX(map, map.Width - 2);

        Assert.True(westEdgeWater > 0, $"Expected anchored river water near west edge, got {westEdgeWater}.");
        Assert.True(eastEdgeWater > 0, $"Expected anchored river water near east edge, got {eastEdgeWater}.");
    }

    [Fact]
    public void Generate_RiverCellWithExplicitRiverEdges_AnchorsLocalWaterAtContractEdges()
    {
        var region = new GeneratedRegionMap(seed: 3119, width: 3, height: 3, worldCoord: new WorldCoord(1, 1));
        var baseTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperateWoodland,
            Slope: 40,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.55f,
            ResourceRichness: 0.50f,
            SoilDepth: 0.52f,
            Groundwater: 0.50f,
            HasRoad: false,
            HasSettlement: false);

        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
            region.SetTile(x, y, baseTile);

        region.SetTile(1, 1, baseTile with
        {
            HasRiver = true,
            RiverEdges = RegionRiverEdges.North | RegionRiverEdges.South,
        });

        var map = new LocalLayerGenerator().Generate(
            region,
            new RegionCoord(1, 1, 1, 1),
            new LocalGenerationSettings(48, 48, 8));

        var northEdgeWater = CountSurfaceWaterAtY(map, 1);
        var southEdgeWater = CountSurfaceWaterAtY(map, map.Height - 2);

        Assert.True(northEdgeWater > 0, $"Expected anchored river water near north edge, got {northEdgeWater}.");
        Assert.True(southEdgeWater > 0, $"Expected anchored river water near south edge, got {southEdgeWater}.");
    }

    [Fact]
    public void Generate_StraightNorthSouthRiverContract_KeepsAlignedLocalChannel()
    {
        var region = new GeneratedRegionMap(seed: 9137, width: 1, height: 1, worldCoord: new WorldCoord(0, 0));
        region.SetTile(0, 0, new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperateWoodland,
            Slope: 40,
            HasRiver: true,
            HasLake: false,
            VegetationDensity: 0.55f,
            ResourceRichness: 0.50f,
            SoilDepth: 0.52f,
            Groundwater: 0.50f,
            HasRoad: false,
            HasSettlement: false,
            RiverEdges: RegionRiverEdges.North | RegionRiverEdges.South,
            RiverDischarge: 6f,
            RiverOrder: 4));

        var map = new LocalLayerGenerator().Generate(
            region,
            new RegionCoord(0, 0, 0, 0),
            new LocalGenerationSettings(48, 48, 8));

        var northCenter = ResolveSurfaceWaterCenterAtY(map, 1);
        var southCenter = ResolveSurfaceWaterCenterAtY(map, map.Height - 2);

        Assert.NotNull(northCenter);
        Assert.NotNull(southCenter);
        Assert.True(
            Math.Abs(northCenter.Value - southCenter.Value) <= 4f,
            $"Expected straight-through river contract to stay aligned across embark ({northCenter:0.0} vs {southCenter:0.0}).");
    }

    [Fact]
    public void Generate_RiverCellWithSingleNeighborContract_ExtendsAcrossEmbark()
    {
        var region = new GeneratedRegionMap(seed: 4127, width: 3, height: 3, worldCoord: new WorldCoord(1, 1));
        var baseTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperateWoodland,
            Slope: 40,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.55f,
            ResourceRichness: 0.50f,
            SoilDepth: 0.52f,
            Groundwater: 0.50f,
            HasRoad: false,
            HasSettlement: false);

        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
            region.SetTile(x, y, baseTile);

        region.SetTile(1, 0, baseTile with
        {
            HasRiver = true,
            RiverEdges = RegionRiverEdges.South,
        });
        region.SetTile(1, 1, baseTile with
        {
            HasRiver = true,
            RiverEdges = RegionRiverEdges.None,
        });

        var map = new LocalLayerGenerator().Generate(
            region,
            new RegionCoord(1, 1, 1, 1),
            new LocalGenerationSettings(48, 48, 8));

        var northEdgeWater = CountSurfaceWaterAtY(map, 1);
        var southEdgeWater = CountSurfaceWaterAtY(map, map.Height - 2);

        Assert.True(northEdgeWater > 0, $"Expected inherited river contract near north edge, got {northEdgeWater}.");
        Assert.True(southEdgeWater > 0, $"Expected single-edge fallback to continue river through south edge, got {southEdgeWater}.");
    }

    [Fact]
    public void Generate_AdjacentRegionCells_ProduceContinuousEmbarkBoundaryFamilies()
    {
        var region = new GeneratedRegionMap(
            seed: 7201,
            width: 2,
            height: 1,
            worldCoord: new WorldCoord(0, 0),
            parentMacroBiomeId: MacroBiomeIds.ConiferForest,
            parentForestCover: 0.76f,
            parentMountainCover: 0.22f,
            parentRelief: 0.48f,
            parentMoistureBand: 0.62f,
            parentTemperatureBand: 0.52f);

        var tile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.ConiferWoodland,
            SurfaceClassId: RegionSurfaceClassIds.Grass,
            Slope: 52,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.72f,
            ResourceRichness: 0.46f,
            SoilDepth: 0.62f,
            Groundwater: 0.58f,
            HasRoad: false,
            HasSettlement: false,
            VegetationSuitability: 0.74f,
            TemperatureBand: 0.52f,
            MoistureBand: 0.62f);
        region.SetTile(0, 0, tile);
        region.SetTile(1, 0, tile);

        var settings = new LocalGenerationSettings(48, 48, 8);
        var generator = new LocalLayerGenerator();
        var left = generator.Generate(region, new RegionCoord(0, 0, 0, 0), settings);
        var right = generator.Generate(region, new RegionCoord(0, 0, 1, 0), settings);

        var matchingFamilies = 0;
        for (var y = 0; y < left.Height; y++)
        {
            var leftFamily = ResolveSurfaceFamily(left.GetTile(left.Width - 1, y, 0).TileDefId);
            var rightFamily = ResolveSurfaceFamily(right.GetTile(0, y, 0).TileDefId);
            if (string.Equals(leftFamily, rightFamily, StringComparison.Ordinal))
                matchingFamilies++;
        }

        var matchRatio = matchingFamilies / (float)left.Height;
        Assert.True(
            matchRatio >= 0.45f,
            $"Expected neighboring embarks to share boundary surface families, got ratio {matchRatio:F2}.");
    }

    [Fact]
    public void Generate_ParentRiverSignal_AddsLocalStreams_WhenSelectedCellHasNoRiver()
    {
        var parentRiverRegion = new GeneratedRegionMap(
            seed: 3197,
            width: 3,
            height: 3,
            worldCoord: new WorldCoord(1, 1),
            parentMacroBiomeId: MacroBiomeIds.WindsweptSteppe,
            parentForestCover: 0.10f,
            parentMountainCover: 0.22f,
            parentRelief: 0.40f,
            parentMoistureBand: 0.46f,
            parentTemperatureBand: 0.55f,
            parentHasRiver: true,
            parentRiverOrder: 6,
            parentRiverDischarge: 0.90f);
        var dryParentRegion = new GeneratedRegionMap(
            seed: 3197,
            width: 3,
            height: 3,
            worldCoord: new WorldCoord(1, 1),
            parentMacroBiomeId: MacroBiomeIds.WindsweptSteppe,
            parentForestCover: 0.10f,
            parentMountainCover: 0.22f,
            parentRelief: 0.40f,
            parentMoistureBand: 0.46f,
            parentTemperatureBand: 0.55f,
            parentHasRiver: false,
            parentRiverOrder: 0,
            parentRiverDischarge: 0f);

        var baseTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.DrySteppe,
            Slope: 58,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.20f,
            ResourceRichness: 0.40f,
            SoilDepth: 0.28f,
            Groundwater: 0.20f,
            HasRoad: false,
            HasSettlement: false,
            MoistureBand: 0.24f,
            TemperatureBand: 0.58f);

        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
        {
            parentRiverRegion.SetTile(x, y, baseTile);
            dryParentRegion.SetTile(x, y, baseTile);
        }

        var generator = new LocalLayerGenerator();
        var settings = new LocalGenerationSettings(48, 48, 8);
        var coord = new RegionCoord(1, 1, 1, 1);

        var parentRiverMap = generator.Generate(parentRiverRegion, coord, settings);
        var dryParentMap = generator.Generate(dryParentRegion, coord, settings);

        var parentRiverWater = CountSurfaceTiles(parentRiverMap, GeneratedTileDefIds.Water);
        var dryParentWater = CountSurfaceTiles(dryParentMap, GeneratedTileDefIds.Water);

        Assert.True(
            parentRiverWater > dryParentWater,
            $"Expected parent river signal to increase local water footprint ({parentRiverWater} vs {dryParentWater}).");
    }

    [Fact]
    public void Generate_HigherRegionRiverDischarge_ProducesWiderLocalChannelFootprint()
    {
        var lowRegion = new GeneratedRegionMap(seed: 4219, width: 3, height: 3, worldCoord: new WorldCoord(1, 1));
        var highRegion = new GeneratedRegionMap(seed: 4219, width: 3, height: 3, worldCoord: new WorldCoord(1, 1));
        var baseTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperateWoodland,
            Slope: 40,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.55f,
            ResourceRichness: 0.50f,
            SoilDepth: 0.52f,
            Groundwater: 0.50f,
            HasRoad: false,
            HasSettlement: false);

        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
        {
            lowRegion.SetTile(x, y, baseTile);
            highRegion.SetTile(x, y, baseTile);
        }

        lowRegion.SetTile(1, 1, baseTile with
        {
            HasRiver = true,
            RiverEdges = RegionRiverEdges.North | RegionRiverEdges.South,
            RiverDischarge = 2f,
        });
        highRegion.SetTile(1, 1, baseTile with
        {
            HasRiver = true,
            RiverEdges = RegionRiverEdges.North | RegionRiverEdges.South,
            RiverDischarge = 9f,
        });

        var settings = new LocalGenerationSettings(48, 48, 8);
        var coord = new RegionCoord(1, 1, 1, 1);
        var generator = new LocalLayerGenerator();

        var lowMap = generator.Generate(lowRegion, coord, settings);
        var highMap = generator.Generate(highRegion, coord, settings);

        var lowWater = CountSurfaceTiles(lowMap, GeneratedTileDefIds.Water);
        var highWater = CountSurfaceTiles(highMap, GeneratedTileDefIds.Water);

        Assert.True(
            highWater > lowWater,
            $"Expected high discharge river to produce wider local water footprint ({highWater} vs {lowWater}).");
    }

    [Fact]
    public void Generate_HigherRiverOrder_ProducesWiderLocalChannelFootprint_AtSameDischarge()
    {
        var lowOrderRegion = new GeneratedRegionMap(seed: 5144, width: 3, height: 3, worldCoord: new WorldCoord(1, 1));
        var highOrderRegion = new GeneratedRegionMap(seed: 5144, width: 3, height: 3, worldCoord: new WorldCoord(1, 1));
        var baseTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperateWoodland,
            Slope: 40,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.55f,
            ResourceRichness: 0.50f,
            SoilDepth: 0.52f,
            Groundwater: 0.50f,
            HasRoad: false,
            HasSettlement: false);

        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
        {
            lowOrderRegion.SetTile(x, y, baseTile);
            highOrderRegion.SetTile(x, y, baseTile);
        }

        lowOrderRegion.SetTile(1, 1, baseTile with
        {
            HasRiver = true,
            RiverEdges = RegionRiverEdges.North | RegionRiverEdges.South,
            RiverDischarge = 5f,
            RiverOrder = 1,
        });
        highOrderRegion.SetTile(1, 1, baseTile with
        {
            HasRiver = true,
            RiverEdges = RegionRiverEdges.North | RegionRiverEdges.South,
            RiverDischarge = 5f,
            RiverOrder = 6,
        });

        var settings = new LocalGenerationSettings(48, 48, 8);
        var coord = new RegionCoord(1, 1, 1, 1);
        var generator = new LocalLayerGenerator();

        var lowOrderMap = generator.Generate(lowOrderRegion, coord, settings);
        var highOrderMap = generator.Generate(highOrderRegion, coord, settings);

        var lowWater = CountSurfaceTiles(lowOrderMap, GeneratedTileDefIds.Water);
        var highWater = CountSurfaceTiles(highOrderMap, GeneratedTileDefIds.Water);

        Assert.True(
            highWater > lowWater,
            $"Expected higher river order to produce wider local water footprint ({highWater} vs {lowWater}).");
    }

    [Fact]
    public void Generate_SettlementRegionCell_CreatesSurfaceClearings()
    {
        var settledRegion = new GeneratedRegionMap(seed: 6201, width: 3, height: 3, worldCoord: new WorldCoord(1, 1));
        var wildRegion = new GeneratedRegionMap(seed: 6201, width: 3, height: 3, worldCoord: new WorldCoord(1, 1));
        var baseTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.DenseConifer,
            Slope: 28,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.92f,
            ResourceRichness: 0.42f,
            SoilDepth: 0.74f,
            Groundwater: 0.80f,
            HasRoad: false,
            HasSettlement: false);

        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
        {
            settledRegion.SetTile(x, y, baseTile);
            wildRegion.SetTile(x, y, baseTile);
        }

        settledRegion.SetTile(1, 1, baseTile with { HasSettlement = true });

        var settings = new LocalGenerationSettings(48, 48, 8);
        var coord = new RegionCoord(1, 1, 1, 1);
        var generator = new LocalLayerGenerator();

        var settledMap = generator.Generate(settledRegion, coord, settings);
        var wildMap = generator.Generate(wildRegion, coord, settings);

        var settledTrees = CountSurfaceTiles(settledMap, GeneratedTileDefIds.Tree);
        var wildTrees = CountSurfaceTiles(wildMap, GeneratedTileDefIds.Tree);

        Assert.True(
            settledTrees + 5 < wildTrees,
            $"Expected settlement-influenced embark to have fewer trees due to clearings ({settledTrees} vs {wildTrees}).");
    }

    [Fact]
    public void Generate_RoadRegionCell_CreatesSurfaceCorridorClearings()
    {
        var roadRegion = new GeneratedRegionMap(seed: 6317, width: 3, height: 3, worldCoord: new WorldCoord(1, 1));
        var wildRegion = new GeneratedRegionMap(seed: 6317, width: 3, height: 3, worldCoord: new WorldCoord(1, 1));
        var baseTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.DenseConifer,
            Slope: 26,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.88f,
            ResourceRichness: 0.46f,
            SoilDepth: 0.70f,
            Groundwater: 0.74f,
            HasRoad: false,
            HasSettlement: false);

        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
        {
            roadRegion.SetTile(x, y, baseTile);
            wildRegion.SetTile(x, y, baseTile);
        }

        roadRegion.SetTile(1, 1, baseTile with { HasRoad = true });

        var settings = new LocalGenerationSettings(48, 48, 8);
        var coord = new RegionCoord(1, 1, 1, 1);
        var generator = new LocalLayerGenerator();

        var roadMap = generator.Generate(roadRegion, coord, settings);
        var wildMap = generator.Generate(wildRegion, coord, settings);

        var roadTrees = CountSurfaceTiles(roadMap, GeneratedTileDefIds.Tree);
        var wildTrees = CountSurfaceTiles(wildMap, GeneratedTileDefIds.Tree);

        Assert.True(
            roadTrees + 3 < wildTrees,
            $"Expected road-influenced embark to have fewer trees due to road corridors ({roadTrees} vs {wildTrees}).");
    }

    [Fact]
    public void Generate_RoadRegionCellWithExplicitRoadEdges_ChangesLocalRoadImprint()
    {
        var edgedRoadRegion = new GeneratedRegionMap(seed: 6449, width: 3, height: 3, worldCoord: new WorldCoord(1, 1));
        var fallbackRoadRegion = new GeneratedRegionMap(seed: 6449, width: 3, height: 3, worldCoord: new WorldCoord(1, 1));
        var baseTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.DenseConifer,
            Slope: 26,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.88f,
            ResourceRichness: 0.46f,
            SoilDepth: 0.70f,
            Groundwater: 0.74f,
            HasRoad: false,
            HasSettlement: false);

        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
        {
            edgedRoadRegion.SetTile(x, y, baseTile);
            fallbackRoadRegion.SetTile(x, y, baseTile);
        }

        edgedRoadRegion.SetTile(1, 1, baseTile with
        {
            HasRoad = true,
            RoadEdges = RegionRoadEdges.North | RegionRoadEdges.South,
        });
        fallbackRoadRegion.SetTile(1, 1, baseTile with
        {
            HasRoad = true,
            RoadEdges = RegionRoadEdges.None,
        });

        var settings = new LocalGenerationSettings(48, 48, 8);
        var coord = new RegionCoord(1, 1, 1, 1);
        var generator = new LocalLayerGenerator();

        var edgedRoadMap = generator.Generate(edgedRoadRegion, coord, settings);
        var fallbackRoadMap = generator.Generate(fallbackRoadRegion, coord, settings);

        Assert.NotEqual(
            Fingerprint(edgedRoadMap),
            Fingerprint(fallbackRoadMap));
    }

    [Fact]
    public void Generate_InheritsGeologyProfileFromRegionTile_ForUndergroundStrata()
    {
        var region = new GeneratedRegionMap(seed: 4121, width: 3, height: 3, worldCoord: new WorldCoord(0, 0));
        var baseTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperateWoodland,
            Slope: 35,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.50f,
            ResourceRichness: 0.48f,
            SoilDepth: 0.50f,
            Groundwater: 0.50f,
            HasRoad: false,
            HasSettlement: false,
            GeologyProfileId: GeologyProfileIds.MixedBedrock);

        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
            region.SetTile(x, y, baseTile);

        region.SetTile(1, 1, baseTile with { GeologyProfileId = GeologyProfileIds.AlluvialBasin });
        var map = new LocalLayerGenerator().Generate(
            region,
            new RegionCoord(0, 0, 1, 1),
            new LocalGenerationSettings(48, 48, 10));

        var firstUnderground = map.GetTile(0, 0, 1);
        Assert.Equal(GeneratedTileDefIds.StoneWall, firstUnderground.TileDefId);
        Assert.Equal(RockTypeIds.Sandstone, firstUnderground.MaterialId);
    }

    [Fact]
    public void Generate_LocalHistoryContext_CarvesSettlementAndRoadContinuity_WithoutRegionBooleans()
    {
        var region = new GeneratedRegionMap(
            seed: 5301,
            width: 3,
            height: 3,
            worldCoord: new WorldCoord(1, 1),
            parentMacroBiomeId: MacroBiomeIds.ConiferForest);
        var baseTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.DenseConifer,
            Slope: 36,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.84f,
            ResourceRichness: 0.44f,
            SoilDepth: 0.72f,
            Groundwater: 0.68f,
            HasRoad: false,
            HasSettlement: false);

        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
            region.SetTile(x, y, baseTile);

        var settings = new LocalGenerationSettings(
            Width: 48,
            Height: 48,
            Depth: 8,
            BiomeOverrideId: MacroBiomeIds.ConiferForest,
            TreeDensityBias: 0.58f,
            ForestPatchBias: 0.48f);
        var historyContext = new LocalHistoryContext(
            OwnerCivilizationId: "civ_alpha",
            TerritoryOwnerCivilizationId: "civ_alpha",
            PrimarySite: new LocalHistorySite("site_alpha", "Alpha Hold", "fortress", "civ_alpha", 1, 1, 0.84f, 0.76f),
            NearbySites:
            [
                new LocalHistorySite("site_beta", "Beta Hamlet", "hamlet", "civ_alpha", 2, 1, 0.48f, 0.52f),
            ],
            NearbyRoads:
            [
                new LocalHistoryRoad(
                    "road_alpha_beta",
                    "civ_alpha",
                    "site_alpha",
                    "site_beta",
                    DistanceFromEmbark: 0,
                    PortalEdges: [LocalMapEdge.West, LocalMapEdge.East]),
            ]);

        var generator = new LocalLayerGenerator();
        var coord = new RegionCoord(1, 1, 1, 1);
        var baselineMap = generator.Generate(region, coord, settings);
        var contextualMap = generator.Generate(region, coord, settings, historyContext);

        var baselineTrees = CountSurfaceTiles(baselineMap, GeneratedTileDefIds.Tree);
        var contextualTrees = CountSurfaceTiles(contextualMap, GeneratedTileDefIds.Tree);
        var baselineRoadTiles = CountSurfaceTiles(baselineMap, GeneratedTileDefIds.StoneBrick);
        var contextualRoadTiles = CountSurfaceTiles(contextualMap, GeneratedTileDefIds.StoneBrick);

        Assert.True(
            contextualTrees + 8 < baselineTrees,
            $"Expected named local history to carve visible civic clearings ({contextualTrees} vs {baselineTrees}).");
        Assert.True(
            contextualRoadTiles > baselineRoadTiles,
            $"Expected named local history roads to stamp visible road tiles ({contextualRoadTiles} vs {baselineRoadTiles}).");
    }

    private static string Fingerprint(GeneratedEmbarkMap map)
    {
        long hash = 1469598103934665603L;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        for (var z = 0; z < map.Depth; z++)
        {
            var tile = map.GetTile(x, y, z);
            hash ^= tile.TileDefId.GetHashCode(StringComparison.Ordinal);
            hash *= 1099511628211L;
            hash ^= tile.FluidLevel;
            hash *= 1099511628211L;
        }

        return hash.ToString();
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

    private static int CountSurfaceWaterAtX(GeneratedEmbarkMap map, int x)
    {
        var count = 0;
        for (var y = 0; y < map.Height; y++)
        {
            if (map.GetTile(x, y, 0).TileDefId == GeneratedTileDefIds.Water)
                count++;
        }

        return count;
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

    private static float? ResolveSurfaceWaterCenterAtY(GeneratedEmbarkMap map, int y)
    {
        var weightedX = 0f;
        var weight = 0f;
        for (var x = 0; x < map.Width; x++)
        {
            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId != GeneratedTileDefIds.Water)
                continue;

            var localWeight = Math.Max(1, (int)tile.FluidLevel);
            weightedX += x * localWeight;
            weight += localWeight;
        }

        if (weight <= 0f)
            return null;

        return weightedX / weight;
    }

    private static string ResolveSurfaceFamily(string tileDefId)
    {
        if (tileDefId == GeneratedTileDefIds.Water)
            return "water";
        if (tileDefId == GeneratedTileDefIds.Magma)
            return "magma";
        if (tileDefId == GeneratedTileDefIds.Tree)
            return "tree";
        if (tileDefId == GeneratedTileDefIds.Sand)
            return "sand";
        if (tileDefId == GeneratedTileDefIds.Mud)
            return "mud";
        if (tileDefId == GeneratedTileDefIds.Snow)
            return "snow";
        if (tileDefId == GeneratedTileDefIds.Soil || tileDefId == GeneratedTileDefIds.Grass)
            return "soil";
        if (tileDefId == GeneratedTileDefIds.StoneFloor ||
            tileDefId == GeneratedTileDefIds.GraniteWall ||
            tileDefId == GeneratedTileDefIds.LimestoneWall ||
            tileDefId == GeneratedTileDefIds.SandstoneWall ||
            tileDefId == GeneratedTileDefIds.BasaltWall ||
            tileDefId == GeneratedTileDefIds.ShaleWall ||
            tileDefId == GeneratedTileDefIds.SlateWall ||
            tileDefId == GeneratedTileDefIds.MarbleWall)
        {
            return "stone";
        }

        return tileDefId;
    }

}

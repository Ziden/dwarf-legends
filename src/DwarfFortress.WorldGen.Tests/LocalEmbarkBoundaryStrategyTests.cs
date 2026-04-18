using DwarfFortress.WorldGen.Analysis;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Local;
using DwarfFortress.WorldGen.Maps;
using DwarfFortress.WorldGen.Regions;

namespace DwarfFortress.WorldGen.Tests;

public sealed class LocalEmbarkBoundaryStrategyTests
{
    [Fact]
    public void Generate_IdentityScenario_DenseForestKeepsBoundaryBandsAligned()
    {
        var region = new GeneratedRegionMap(
            seed: 8301,
            width: 2,
            height: 1,
            worldCoord: new WorldCoord(0, 0),
            parentMacroBiomeId: MacroBiomeIds.ConiferForest,
            parentForestCover: 0.90f,
            parentMountainCover: 0.12f,
            parentRelief: 0.24f,
            parentMoistureBand: 0.72f,
            parentTemperatureBand: 0.46f);

        var tile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.DenseConifer,
            SurfaceClassId: RegionSurfaceClassIds.Grass,
            Slope: 24,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.88f,
            ResourceRichness: 0.44f,
            SoilDepth: 0.72f,
            Groundwater: 0.74f,
            HasRoad: false,
            HasSettlement: false,
            VegetationSuitability: 0.86f,
            TemperatureBand: 0.46f,
            MoistureBand: 0.72f);
        region.SetTile(0, 0, tile);
        region.SetTile(1, 0, tile);

        var (left, right) = GenerateAdjacentEmbarks(region);
        var comparison = EmbarkBoundaryContinuity.CompareBoundaryBand(left, right, isEastNeighbor: true, bandWidth: 4);

        Assert.True(
            comparison.SurfaceFamilyMismatchRatio <= 0.18f,
            $"Expected identical dense-forest parents to keep surface families aligned across the seam band, got {comparison.SurfaceFamilyMismatchRatio:F2}.");
        Assert.True(
            comparison.TreeMismatchRatio <= 0.45f,
            $"Expected identical dense-forest parents to keep tree occupancy aligned across the seam band, got {comparison.TreeMismatchRatio:F2}.");
        Assert.True(
            comparison.EcologyMismatchRatio <= 0.35f,
            $"Expected identical dense-forest parents to keep ecology aligned across the seam band, got {comparison.EcologyMismatchRatio:F2}.");
    }

    [Fact]
    public void Generate_IdentityScenario_DenseForestDoesNotCreateBoundaryTreeWall()
    {
        var region = new GeneratedRegionMap(
            seed: 8301,
            width: 2,
            height: 1,
            worldCoord: new WorldCoord(0, 0),
            parentMacroBiomeId: MacroBiomeIds.ConiferForest,
            parentForestCover: 0.90f,
            parentMountainCover: 0.12f,
            parentRelief: 0.24f,
            parentMoistureBand: 0.72f,
            parentTemperatureBand: 0.46f);

        var tile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.DenseConifer,
            SurfaceClassId: RegionSurfaceClassIds.Grass,
            Slope: 24,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.88f,
            ResourceRichness: 0.44f,
            SoilDepth: 0.72f,
            Groundwater: 0.74f,
            HasRoad: false,
            HasSettlement: false,
            VegetationSuitability: 0.86f,
            TemperatureBand: 0.46f,
            MoistureBand: 0.72f);
        region.SetTile(0, 0, tile);
        region.SetTile(1, 0, tile);

        var (left, right) = GenerateAdjacentEmbarks(region);
        var boundaryBandWidth = 2;
        var interiorBandWidth = 4;
        var leftBoundaryRatio = CountSurfaceTilesInXRange(left, GeneratedTileDefIds.Tree, left.Width - boundaryBandWidth, left.Width - 1)
            / (float)(boundaryBandWidth * left.Height);
        var leftInteriorRatio = CountSurfaceTilesInXRange(left, GeneratedTileDefIds.Tree, left.Width - (boundaryBandWidth + interiorBandWidth), left.Width - boundaryBandWidth - 1)
            / (float)(interiorBandWidth * left.Height);
        var rightBoundaryRatio = CountSurfaceTilesInXRange(right, GeneratedTileDefIds.Tree, 0, boundaryBandWidth - 1)
            / (float)(boundaryBandWidth * right.Height);
        var rightInteriorRatio = CountSurfaceTilesInXRange(right, GeneratedTileDefIds.Tree, boundaryBandWidth, boundaryBandWidth + interiorBandWidth - 1)
            / (float)(interiorBandWidth * right.Height);
        var combinedBoundaryRatio =
            (CountSurfaceTilesInXRange(left, GeneratedTileDefIds.Tree, left.Width - boundaryBandWidth, left.Width - 1) +
             CountSurfaceTilesInXRange(right, GeneratedTileDefIds.Tree, 0, boundaryBandWidth - 1)) /
            (float)((boundaryBandWidth * 2) * left.Height);
        var combinedInteriorRatio =
            (CountSurfaceTilesInXRange(left, GeneratedTileDefIds.Tree, left.Width - (boundaryBandWidth + interiorBandWidth), left.Width - boundaryBandWidth - 1) +
             CountSurfaceTilesInXRange(right, GeneratedTileDefIds.Tree, boundaryBandWidth, boundaryBandWidth + interiorBandWidth - 1)) /
            (float)((interiorBandWidth * 2) * left.Height);

        Assert.True(
            leftBoundaryRatio <= leftInteriorRatio + 0.10f,
            $"Expected the left embark boundary tree band to stay close to nearby interior density instead of forming a wall ({leftBoundaryRatio:F2} boundary vs {leftInteriorRatio:F2} interior)."
        );
        Assert.True(
            rightBoundaryRatio <= rightInteriorRatio + 0.10f,
            $"Expected the right embark boundary tree band to stay close to nearby interior density instead of forming a wall ({rightBoundaryRatio:F2} boundary vs {rightInteriorRatio:F2} interior)."
        );
        Assert.True(
            combinedBoundaryRatio <= combinedInteriorRatio + 0.08f,
            $"Expected the shared seam tree band to stay close to nearby interior density instead of forming a double-width wall ({combinedBoundaryRatio:F2} seam vs {combinedInteriorRatio:F2} interior)."
        );
    }

    [Fact]
    public void Generate_IdentityScenario_RiverCorridorKeepsBoundaryWaterBandsAligned()
    {
        var region = new GeneratedRegionMap(
            seed: 8307,
            width: 2,
            height: 1,
            worldCoord: new WorldCoord(0, 0),
            parentMacroBiomeId: MacroBiomeIds.TemperatePlains,
            parentForestCover: 0.20f,
            parentMountainCover: 0.10f,
            parentRelief: 0.18f,
            parentMoistureBand: 0.68f,
            parentTemperatureBand: 0.56f,
            parentHasRiver: true,
            parentRiverOrder: 5,
            parentRiverDischarge: 0.82f);

        var tile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperateWoodland,
            SurfaceClassId: RegionSurfaceClassIds.Grass,
            Slope: 18,
            HasRiver: true,
            RiverEdges: RegionRiverEdges.West | RegionRiverEdges.East,
            RiverOrder: 5,
            RiverDischarge: 7.5f,
            HasLake: false,
            VegetationDensity: 0.42f,
            ResourceRichness: 0.46f,
            SoilDepth: 0.58f,
            Groundwater: 0.72f,
            HasRoad: false,
            HasSettlement: false,
            VegetationSuitability: 0.58f,
            TemperatureBand: 0.56f,
            MoistureBand: 0.68f);
        region.SetTile(0, 0, tile);
        region.SetTile(1, 0, tile);

        var (left, right) = GenerateAdjacentEmbarks(region);
        var comparison = EmbarkBoundaryContinuity.CompareBoundaryBand(left, right, isEastNeighbor: true, bandWidth: 4);
        var leftWater = CountSurfaceWaterInXRange(left, left.Width - 6, left.Width - 1);
        var rightWater = CountSurfaceWaterInXRange(right, 0, 5);

        Assert.True(leftWater + rightWater > 0, "Expected the shared river corridor to produce seam-band water.");
        Assert.True(
            comparison.WaterMismatchRatio <= 0.22f,
            $"Expected identical river parents to keep seam-band water aligned, got {comparison.WaterMismatchRatio:F2}.");
        Assert.True(
            comparison.SurfaceFamilyMismatchRatio <= 0.55f,
            $"Expected identical river parents to keep seam-band surface families aligned, got {comparison.SurfaceFamilyMismatchRatio:F2}.");
    }

    [Fact]
    public void Generate_TransitionScenario_ForestToPlainsUsesBlendBandInsteadOfHardCut()
    {
        var region = new GeneratedRegionMap(
            seed: 8313,
            width: 2,
            height: 1,
            worldCoord: new WorldCoord(0, 0),
            parentMacroBiomeId: MacroBiomeIds.TemperatePlains,
            parentForestCover: 0.52f,
            parentMountainCover: 0.12f,
            parentRelief: 0.18f,
            parentMoistureBand: 0.46f,
            parentTemperatureBand: 0.56f);

        var lushTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperateWoodland,
            SurfaceClassId: RegionSurfaceClassIds.Grass,
            Slope: 20,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.82f,
            ResourceRichness: 0.44f,
            SoilDepth: 0.68f,
            Groundwater: 0.70f,
            HasRoad: false,
            HasSettlement: false,
            VegetationSuitability: 0.80f,
            TemperatureBand: 0.56f,
            MoistureBand: 0.64f);
        var sparseTile = lushTile with
        {
            BiomeVariantId = RegionBiomeVariantIds.TemperatePlainsOpen,
            VegetationDensity = 0.18f,
            VegetationSuitability = 0.22f,
            SoilDepth = 0.30f,
            Groundwater = 0.16f,
            MoistureBand = 0.18f,
        };

        region.SetTile(0, 0, lushTile);
        region.SetTile(1, 0, sparseTile);

        var (left, right) = GenerateAdjacentEmbarks(region);
        var comparison = EmbarkBoundaryContinuity.CompareBoundaryBand(left, right, isEastNeighbor: true, bandWidth: 6);
        var bandTileCount = 6 * left.Height;
        var leftFarRatio = CountSurfaceTilesInXRange(left, GeneratedTileDefIds.Tree, 0, 5) / (float)bandTileCount;
        var leftSeamRatio = CountSurfaceTilesInXRange(left, GeneratedTileDefIds.Tree, left.Width - 6, left.Width - 1) / (float)bandTileCount;
        var rightSeamRatio = CountSurfaceTilesInXRange(right, GeneratedTileDefIds.Tree, 0, 5) / (float)bandTileCount;

        Assert.True(
            leftSeamRatio <= leftFarRatio + 0.22f,
            $"Expected the forest side to taper into the seam band instead of spiking ({leftSeamRatio:F2} seam vs {leftFarRatio:F2} interior).");
        Assert.True(
            rightSeamRatio >= 0.08f,
            $"Expected the plains side to inherit some tree cover in the seam blend band, got {rightSeamRatio:F2}.");
        Assert.True(
            comparison.TreeMismatchRatio <= 0.70f,
            $"Expected the transition seam band to blend tree occupancy instead of hard-cutting, got mismatch ratio {comparison.TreeMismatchRatio:F2}.");
    }

    [Fact]
    public void Generate_TransitionScenario_GrassToSandUsesBlendBandInsteadOfExactMatch()
    {
        var region = new GeneratedRegionMap(
            seed: 8321,
            width: 2,
            height: 1,
            worldCoord: new WorldCoord(0, 0),
            parentMacroBiomeId: MacroBiomeIds.TemperatePlains,
            parentForestCover: 0.18f,
            parentMountainCover: 0.08f,
            parentRelief: 0.18f,
            parentMoistureBand: 0.26f,
            parentTemperatureBand: 0.58f);

        var grassTile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperatePlainsOpen,
            SurfaceClassId: RegionSurfaceClassIds.Grass,
            Slope: 24,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.42f,
            ResourceRichness: 0.38f,
            SoilDepth: 0.54f,
            Groundwater: 0.34f,
            HasRoad: false,
            HasSettlement: false,
            VegetationSuitability: 0.48f,
            TemperatureBand: 0.56f,
            MoistureBand: 0.34f);
        var sandTile = grassTile with
        {
            SurfaceClassId = RegionSurfaceClassIds.Sand,
            SoilDepth = 0.24f,
            Groundwater = 0.14f,
            MoistureBand = 0.14f,
            VegetationDensity = 0.16f,
            VegetationSuitability = 0.18f,
        };

        region.SetTile(0, 0, grassTile);
        region.SetTile(1, 0, sandTile);

        var (left, right) = GenerateAdjacentEmbarks(region);
        var comparison = EmbarkBoundaryContinuity.CompareBoundaryBand(left, right, isEastNeighbor: true, bandWidth: 4);
        var bandWidth = left.Width / 4;
        var leftWestSand = CountSurfaceTilesInXRange(left, GeneratedTileDefIds.Sand, 0, bandWidth - 1);
        var leftEastSand = CountSurfaceTilesInXRange(left, GeneratedTileDefIds.Sand, left.Width - bandWidth, left.Width - 1);
        var rightWestSand = CountSurfaceTilesInXRange(right, GeneratedTileDefIds.Sand, 0, bandWidth - 1);
        var rightEastSand = CountSurfaceTilesInXRange(right, GeneratedTileDefIds.Sand, right.Width - bandWidth, right.Width - 1);

        Assert.True(
            comparison.SurfaceFamilyMismatchRatio <= 0.45f,
            $"Expected the grass-to-sand seam band to blend surface families rather than hard-cut, got mismatch ratio {comparison.SurfaceFamilyMismatchRatio:F2}.");
        Assert.True(
            leftEastSand > leftWestSand,
            $"Expected sand influence to increase toward the seam inside the grass-side embark ({leftEastSand} east-band sand vs {leftWestSand} west-band sand).");
        Assert.True(
            rightWestSand < rightEastSand,
            $"Expected grass influence to reduce sand near the seam inside the sand-side embark ({rightWestSand} west-band sand vs {rightEastSand} east-band sand).");
    }

    private static (GeneratedEmbarkMap Left, GeneratedEmbarkMap Right) GenerateAdjacentEmbarks(GeneratedRegionMap region)
    {
        var settings = new LocalGenerationSettings(48, 48, 8);
        var generator = new LocalLayerGenerator();
        var left = generator.Generate(region, new RegionCoord(0, 0, 0, 0), settings);
        var right = generator.Generate(region, new RegionCoord(0, 0, 1, 0), settings);
        return (left, right);
    }

    private static int CountSurfaceTilesInXRange(GeneratedEmbarkMap map, string tileDefId, int minX, int maxX)
    {
        var clampedMinX = Math.Max(0, minX);
        var clampedMaxX = Math.Min(map.Width - 1, maxX);
        var count = 0;

        for (var x = clampedMinX; x <= clampedMaxX; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (map.GetTile(x, y, 0).TileDefId == tileDefId)
                count++;
        }

        return count;
    }

    private static int CountSurfaceWaterInXRange(GeneratedEmbarkMap map, int minX, int maxX)
    {
        var clampedMinX = Math.Max(0, minX);
        var clampedMaxX = Math.Min(map.Width - 1, maxX);
        var count = 0;

        for (var x = clampedMinX; x <= clampedMaxX; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (tile.TileDefId == GeneratedTileDefIds.Water || tile.FluidType == GeneratedFluidType.Water)
                count++;
        }

        return count;
    }
}

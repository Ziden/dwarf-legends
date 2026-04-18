using GameMapGenerationService = DwarfFortress.GameLogic.World.MapGenerationService;
using GameMapGenerationSettings = DwarfFortress.GameLogic.World.MapGenerationSettings;
using DwarfFortress.WorldGen.Analysis;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Local;
using DwarfFortress.WorldGen.Maps;

namespace DwarfFortress.WorldGen.Tests;

public sealed class StartupEmbarkSeedRegressionTests
{
    [Fact]
    public void Seed42_DefaultEmbarkWindow_KeepsWorstStartupSeamWithinCurrentBaseline()
    {
        const int seed = 42;
        var generationSettings = GameMapGenerationSettings.Default;
        var service = new GameMapGenerationService();
        var anchor = service.ResolveDefaultRegionCoord(seed, generationSettings);
        var localSettings = new LocalGenerationSettings(48, 48, 8);

        var anchorAbsoluteX = (anchor.WorldX * generationSettings.RegionWidth) + anchor.RegionX;
        var anchorAbsoluteY = (anchor.WorldY * generationSettings.RegionHeight) + anchor.RegionY;
        var totalRegionWidth = generationSettings.WorldWidth * generationSettings.RegionWidth;
        var startAbsoluteX = Math.Clamp(anchorAbsoluteX - 1, 0, Math.Max(0, totalRegionWidth - 3));

        var leftCoord = ResolveAbsoluteRegionCoord(startAbsoluteX, anchorAbsoluteY, generationSettings.RegionWidth, generationSettings.RegionHeight);
        var centerCoord = ResolveAbsoluteRegionCoord(startAbsoluteX + 1, anchorAbsoluteY, generationSettings.RegionWidth, generationSettings.RegionHeight);
        var rightCoord = ResolveAbsoluteRegionCoord(startAbsoluteX + 2, anchorAbsoluteY, generationSettings.RegionWidth, generationSettings.RegionHeight);

        var left = service.GetOrCreateLocal(seed, leftCoord, localSettings, generationSettings);
        var center = service.GetOrCreateLocal(seed, centerCoord, localSettings, generationSettings);
        var right = service.GetOrCreateLocal(seed, rightCoord, localSettings, generationSettings);

        var leftComparison = EmbarkBoundaryContinuity.CompareBoundary(left, center, isEastNeighbor: true);
        var rightComparison = EmbarkBoundaryContinuity.CompareBoundary(center, right, isEastNeighbor: true);

        Assert.True(
            leftComparison.SurfaceFamilyMismatchRatio <= 0.14f,
            $"Expected the seed-42 default startup seam to stay within the current surface-family baseline, got {leftComparison.SurfaceFamilyMismatchRatio:F2} for {FormatCoord(leftCoord)}->{FormatCoord(centerCoord)}.");
        Assert.True(
            leftComparison.TreeMismatchRatio <= 0.10f,
            $"Expected the seed-42 default startup seam to stay within the current tree baseline, got {leftComparison.TreeMismatchRatio:F2} for {FormatCoord(leftCoord)}->{FormatCoord(centerCoord)}.");
        Assert.True(
            rightComparison.SurfaceFamilyMismatchRatio <= 0.06f,
            $"Expected the adjacent startup seam on the far side of the default embark to remain nearly exact, got {rightComparison.SurfaceFamilyMismatchRatio:F2} for {FormatCoord(centerCoord)}->{FormatCoord(rightCoord)}.");
        Assert.True(
            rightComparison.TreeMismatchRatio <= 0.06f,
            $"Expected the adjacent startup seam on the far side of the default embark to remain nearly exact for trees, got {rightComparison.TreeMismatchRatio:F2} for {FormatCoord(centerCoord)}->{FormatCoord(rightCoord)}.");
    }

    [Fact]
    public void Seed42_DefaultEmbarkWindow_DoesNotSpikeTreeDensityAcrossWorstStartupSeam()
    {
        const int seed = 42;
        var generationSettings = GameMapGenerationSettings.Default;
        var service = new GameMapGenerationService();
        var anchor = service.ResolveDefaultRegionCoord(seed, generationSettings);
        var localSettings = new LocalGenerationSettings(48, 48, 8);

        var anchorAbsoluteX = (anchor.WorldX * generationSettings.RegionWidth) + anchor.RegionX;
        var anchorAbsoluteY = (anchor.WorldY * generationSettings.RegionHeight) + anchor.RegionY;
        var totalRegionWidth = generationSettings.WorldWidth * generationSettings.RegionWidth;
        var startAbsoluteX = Math.Clamp(anchorAbsoluteX - 1, 0, Math.Max(0, totalRegionWidth - 3));

        var leftCoord = ResolveAbsoluteRegionCoord(startAbsoluteX, anchorAbsoluteY, generationSettings.RegionWidth, generationSettings.RegionHeight);
        var centerCoord = ResolveAbsoluteRegionCoord(startAbsoluteX + 1, anchorAbsoluteY, generationSettings.RegionWidth, generationSettings.RegionHeight);
        var left = service.GetOrCreateLocal(seed, leftCoord, localSettings, generationSettings);
        var center = service.GetOrCreateLocal(seed, centerCoord, localSettings, generationSettings);

        const int boundaryBandWidth = 2;
        const int interiorBandWidth = 4;
        var leftBoundaryTrees = CountSurfaceTilesInXRange(left, GeneratedTileDefIds.Tree, left.Width - boundaryBandWidth, left.Width - 1);
        var leftInteriorTrees = CountSurfaceTilesInXRange(left, GeneratedTileDefIds.Tree, left.Width - (boundaryBandWidth + interiorBandWidth), left.Width - boundaryBandWidth - 1);
        var centerBoundaryTrees = CountSurfaceTilesInXRange(center, GeneratedTileDefIds.Tree, 0, boundaryBandWidth - 1);
        var centerInteriorTrees = CountSurfaceTilesInXRange(center, GeneratedTileDefIds.Tree, boundaryBandWidth, boundaryBandWidth + interiorBandWidth - 1);

        var boundaryRatio = (leftBoundaryTrees + centerBoundaryTrees) / (float)((boundaryBandWidth * 2) * left.Height);
        var interiorRatio = (leftInteriorTrees + centerInteriorTrees) / (float)((interiorBandWidth * 2) * left.Height);

        Assert.True(
            boundaryRatio <= interiorRatio + 0.14f,
            $"Expected the seed-42 startup seam to avoid a strong double-width tree wall, got boundary ratio {boundaryRatio:F2} vs nearby interior ratio {interiorRatio:F2} for {FormatCoord(leftCoord)}->{FormatCoord(centerCoord)}.");
    }

    [Fact]
    public void Seed42_DefaultEmbarkWindow_ReportsExactBoundaryContinuityMetrics()
    {
        const int seed = 42;
        var generationSettings = GameMapGenerationSettings.Default;
        var service = new GameMapGenerationService();
        var anchor = service.ResolveDefaultRegionCoord(seed, generationSettings);
        var localSettings = new LocalGenerationSettings(48, 48, 8);

        var center = service.GetOrCreateLocal(seed, anchor, localSettings, generationSettings);

        var diagnostics = Assert.IsType<EmbarkGenerationDiagnostics>(center.Diagnostics);
        var metrics = Assert.IsType<ExactBoundaryContinuityMetrics>(diagnostics.ExactBoundaryContinuity);

        Assert.Equal((center.Width * 2) + (center.Height * 2) - 8, metrics.BoundaryCellsProcessed);
        Assert.True(metrics.SurfaceCellsAdjusted <= 111, $"Expected seed-42 startup exact-boundary surface repair to stay within the current ceiling, got {metrics.SurfaceCellsAdjusted}.");
        Assert.True(metrics.TreesPlaced <= 72, $"Expected seed-42 startup exact-boundary tree placements to stay within the current ceiling, got {metrics.TreesPlaced}.");
        Assert.True(metrics.TreesRemoved <= 15, $"Expected seed-42 startup exact-boundary tree removals to stay within the current ceiling, got {metrics.TreesRemoved}.");
        Assert.True(metrics.TreeCanopyAdjustedCells <= 35, $"Expected seed-42 startup exact-boundary canopy adjustments to stay within the current ceiling, got {metrics.TreeCanopyAdjustedCells}.");
        Assert.Equal(0, metrics.GroundPlantAdjustedCells);
        Assert.True(metrics.VegetationAdjustedCells <= 122, $"Expected seed-42 startup exact-boundary vegetation work to stay within the current ceiling, got {metrics.VegetationAdjustedCells}.");
    }

    private static RegionCoord ResolveAbsoluteRegionCoord(int absoluteRegionX, int absoluteRegionY, int regionWidth, int regionHeight)
        => new(
            absoluteRegionX / regionWidth,
            absoluteRegionY / regionHeight,
            absoluteRegionX % regionWidth,
            absoluteRegionY % regionHeight);

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

    private static string FormatCoord(RegionCoord coord)
        => $"({coord.WorldX},{coord.WorldY})/({coord.RegionX},{coord.RegionY})";
}
using DwarfFortress.WorldGen.Analysis;
using DwarfFortress.WorldGen.Maps;

namespace DwarfFortress.WorldGen.Tests;

public sealed class EmbarkBoundaryContinuityTests
{
    [Fact]
    public void CompareBoundaryBand_UsesMirroredInteriorColumnsAcrossSeam()
    {
        var left = CreateMap(width: 3, height: 1);
        var right = CreateMap(width: 3, height: 1);
        left.SetTile(1, 0, 0, new GeneratedTile(GeneratedTileDefIds.Sand, "sand", true));
        right.SetTile(1, 0, 0, new GeneratedTile(GeneratedTileDefIds.Sand, "sand", true));

        var comparison = EmbarkBoundaryContinuity.CompareBoundaryBand(left, right, isEastNeighbor: true, bandWidth: 2);

        Assert.Equal(2, comparison.SampleCount);
        Assert.Equal(0, comparison.SurfaceFamilyMismatchCount);
    }

    [Fact]
    public void CompareBoundaryBand_CatchesNearBoundaryMismatchBeyondExactEdge()
    {
        var left = CreateMap(width: 3, height: 1);
        var right = CreateMap(width: 3, height: 1);
        left.SetTile(1, 0, 0, new GeneratedTile(GeneratedTileDefIds.Sand, "sand", true));
        right.SetTile(1, 0, 0, new GeneratedTile(GeneratedTileDefIds.Soil, "soil", true));

        var edgeComparison = EmbarkBoundaryContinuity.CompareBoundary(left, right, isEastNeighbor: true);
        var bandComparison = EmbarkBoundaryContinuity.CompareBoundaryBand(left, right, isEastNeighbor: true, bandWidth: 2);

        Assert.Equal(0, edgeComparison.SurfaceFamilyMismatchCount);
        Assert.Equal(1, bandComparison.SurfaceFamilyMismatchCount);
    }

    [Fact]
    public void CompareTiles_DifferentGroundPlants_FlagEcologyMismatch()
    {
        var left = new GeneratedTile(
            TileDefId: GeneratedTileDefIds.Grass,
            MaterialId: "soil",
            IsPassable: true,
            PlantDefId: "marsh_reed",
            PlantGrowthStage: 2);
        var right = left with { PlantDefId = "cattail" };

        var mismatch = EmbarkBoundaryContinuity.CompareTiles(left, right);

        Assert.True((mismatch & EmbarkBoundaryMismatchKind.Ecology) != 0);
        Assert.True((mismatch & EmbarkBoundaryMismatchKind.Tree) == 0);
    }

    [Fact]
    public void CompareTiles_MatchingGroundPlants_DoNotFlagEcologyMismatch()
    {
        var left = new GeneratedTile(
            TileDefId: GeneratedTileDefIds.Mud,
            MaterialId: "soil",
            IsPassable: true,
            PlantDefId: "marsh_reed",
            PlantGrowthStage: 2,
            PlantYieldLevel: 1);
        var right = left with { PlantYieldLevel = 0, PlantSeedLevel = 1 };

        var mismatch = EmbarkBoundaryContinuity.CompareTiles(left, right);

        Assert.True((mismatch & EmbarkBoundaryMismatchKind.Ecology) == 0);
        Assert.True((mismatch & EmbarkBoundaryMismatchKind.Tree) == 0);
    }

    [Fact]
    public void CompareTiles_DifferentTreeSpecies_FlagTreeMismatchWithoutEcologyMismatch()
    {
        var left = new GeneratedTile(
            TileDefId: GeneratedTileDefIds.Tree,
            MaterialId: "wood",
            IsPassable: false,
            TreeSpeciesId: "oak");
        var right = left with { TreeSpeciesId = "pine" };

        var mismatch = EmbarkBoundaryContinuity.CompareTiles(left, right);

        Assert.True((mismatch & EmbarkBoundaryMismatchKind.Tree) != 0);
        Assert.True((mismatch & EmbarkBoundaryMismatchKind.Ecology) == 0);
    }

    private static GeneratedEmbarkMap CreateMap(int width, int height)
    {
        var map = new GeneratedEmbarkMap(width, height, depth: 1);
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
            map.SetTile(x, y, 0, new GeneratedTile(GeneratedTileDefIds.Grass, "soil", true));

        return map;
    }
}
using DwarfFortress.WorldGen.Analysis;
using DwarfFortress.WorldGen.Maps;

namespace DwarfFortress.WorldGen.Tests;

public sealed class EmbarkBoundaryContinuityAcceptanceTests
{
    [Fact]
    public void CompareBoundary_DifferentBoundaryGroundPlants_FlagEcologyMismatchAcrossBoundary()
    {
        var left = CreateBoundaryPlantMap("marsh_reed");
        var right = CreateBoundaryPlantMap("cattail");

        var comparison = EmbarkBoundaryContinuity.CompareBoundary(left, right, isEastNeighbor: true);

        Assert.Equal(4, comparison.SampleCount);
        Assert.Equal(4, comparison.EcologyMismatchCount);
        Assert.Equal(1f, comparison.EcologyMismatchRatio);
        Assert.Equal(0, comparison.TreeMismatchCount);
    }

    private static GeneratedEmbarkMap CreateBoundaryPlantMap(string plantDefId)
    {
        var map = new GeneratedEmbarkMap(width: 3, height: 4, depth: 1);
        var empty = new GeneratedTile(TileDefId: GeneratedTileDefIds.Grass, MaterialId: "soil", IsPassable: true);
        var boundary = empty with { PlantDefId = plantDefId, PlantGrowthStage = 2 };

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
            map.SetTile(x, y, 0, empty);

        for (var y = 0; y < map.Height; y++)
        {
            map.SetTile(0, y, 0, boundary);
            map.SetTile(map.Width - 1, y, 0, boundary);
        }

        return map;
    }
}

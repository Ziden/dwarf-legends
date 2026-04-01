using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Tests.Fakes;
using DwarfFortress.GameLogic.World;
using DwarfFortress.WorldGen.Ids;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

/// <summary>
/// Tests for WorldGenerator.GenerateEmbark output guarantees.
/// </summary>
public sealed class WorldGeneratorTests
{
    [Fact]
    public void GenerateEmbark_Produces_Map_With_Requested_Dimensions()
    {
        var map = new WorldMap();

        WorldGenerator.GenerateEmbark(map, width: 16, height: 16, depth: 4, seed: 42);

        Assert.Equal(16, map.Width);
        Assert.Equal(16, map.Height);
        Assert.Equal(4,  map.Depth);
    }

    [Fact]
    public void GenerateEmbark_Surface_Layer_Is_Passable()
    {
        var map = new WorldMap();
        WorldGenerator.GenerateEmbark(map, 16, 16, 4, seed: 1);

        int passableCount = 0;
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        {
            var tile = map.GetTile(new Vec3i(x, y, 0));
            if (tile.IsPassable) passableCount++;
        }

        // At least 50% of the surface should be walkable floor tiles
        Assert.True(passableCount >= 16 * 16 / 2,
            $"Expected surface to be mostly passable, but only {passableCount}/256 tiles are passable.");
    }

    [Fact]
    public void GenerateEmbark_Underground_Layer_Is_Mostly_Impassable()
    {
        var map = new WorldMap();
        WorldGenerator.GenerateEmbark(map, 16, 16, 4, seed: 1);

        int impassableCount = 0;
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        {
            var tile = map.GetTile(new Vec3i(x, y, 3)); // deepest z-level
            if (!tile.IsPassable) impassableCount++;
        }

        // Deepest underground should be overwhelmingly wall tiles
        Assert.True(impassableCount >= 16 * 16 / 2,
            $"Expected underground to be mostly impassable, but only {impassableCount}/256 tiles are walls.");
    }

    [Fact]
    public void GenerateEmbark_Has_Staircase_Connecting_Surface_To_Depth1()
    {
        var map = new WorldMap();
        WorldGenerator.GenerateEmbark(map, 16, 16, 4, seed: 1);

        // WorldGenerator places a staircase at the map centre so dwarves can descend
        int cx = 8, cy = 8;
        var surface  = map.GetTile(new Vec3i(cx, cy, 0));
        var depth1   = map.GetTile(new Vec3i(cx, cy, 1));

        Assert.True(surface.IsPassable || depth1.IsPassable,
            "Expected a passable staircase corridor at the map centre connecting z=0 to z=1.");
    }

    [Fact]
    public void GenerateEmbark_Pathfinder_Can_Route_Across_Surface()
    {
        var map = new WorldMap();
        WorldGenerator.GenerateEmbark(map, 16, 16, 4, seed: 1);

        // Route from one corner to the other on the surface
        var path = Pathfinder.FindPath(map, new Vec3i(0, 0, 0), new Vec3i(15, 15, 0));

        Assert.NotNull(path);
        Assert.True(path!.Count > 0, "Pathfinder should find a route across the generated surface.");
    }

    [Fact]
    public void GenerateEmbark_Same_Seed_Produces_Same_Map()
    {
        var mapA = new WorldMap();
        var mapB = new WorldMap();

        WorldGenerator.GenerateEmbark(mapA, 16, 16, 4, seed: 100);
        WorldGenerator.GenerateEmbark(mapB, 16, 16, 4, seed: 100);

        int differences = 0;
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 4; z++)
        {
            if (mapA.GetTile(new Vec3i(x, y, z)).TileDefId !=
                mapB.GetTile(new Vec3i(x, y, z)).TileDefId)
                differences++;
        }

        Assert.Equal(0, differences);
    }

    [Fact]
    public void GenerateEmbark_BiomePresets_Create_Different_Surface_Mixes()
    {
        var forest = new WorldMap();
        var steppe = new WorldMap();
        var marsh = new WorldMap();
        var highland = new WorldMap();

        WorldGenerator.GenerateEmbark(forest, 48, 48, 8, seed: 77, biomeId: MacroBiomeIds.ConiferForest);
        WorldGenerator.GenerateEmbark(steppe, 48, 48, 8, seed: 77, biomeId: MacroBiomeIds.WindsweptSteppe);
        WorldGenerator.GenerateEmbark(marsh, 48, 48, 8, seed: 77, biomeId: MacroBiomeIds.MistyMarsh);
        WorldGenerator.GenerateEmbark(highland, 48, 48, 8, seed: 77, biomeId: MacroBiomeIds.Highland);

        int forestTrees = CountSurfaceTiles(forest, TileDefIds.Tree);
        int steppeTrees = CountSurfaceTiles(steppe, TileDefIds.Tree);
        int marshWater = CountSurfaceTiles(marsh, TileDefIds.Water);
        int highlandWater = CountSurfaceTiles(highland, TileDefIds.Water);

        Assert.True(forestTrees > steppeTrees,
            $"Expected forest to have more trees than steppe ({forestTrees} vs {steppeTrees}).");
        Assert.True(marshWater > highlandWater,
            $"Expected marsh to have more shallow water than highland ({marshWater} vs {highlandWater}).");
    }

    [Theory]
    [InlineData(MacroBiomeIds.TemperatePlains)]
    [InlineData(MacroBiomeIds.ConiferForest)]
    [InlineData(MacroBiomeIds.Highland)]
    [InlineData(MacroBiomeIds.MistyMarsh)]
    [InlineData(MacroBiomeIds.WindsweptSteppe)]
    public void GenerateEmbark_EachBiome_Keeps_Surface_Navigation_Safe(string biomeId)
    {
        var map = new WorldMap();
        WorldGenerator.GenerateEmbark(map, 32, 32, 6, seed: 19, biomeId: biomeId);

        var passable = 0;
        const int size = 32;
        for (var x = 0; x < size; x++)
        for (var y = 0; y < size; y++)
        {
            if (map.GetTile(new Vec3i(x, y, 0)).IsPassable)
                passable++;
        }

        Assert.True(passable >= (int)(size * size * 0.55f),
            $"Expected at least 55% passable surface for biome '{biomeId}', got {passable}/{size * size}.");

        Assert.True(map.GetTile(new Vec3i(0, 0, 0)).IsPassable);
        Assert.True(map.GetTile(new Vec3i(size - 1, 0, 0)).IsPassable);
        Assert.True(map.GetTile(new Vec3i(0, size - 1, 0)).IsPassable);
        Assert.True(map.GetTile(new Vec3i(size - 1, size - 1, 0)).IsPassable);

        var path = Pathfinder.FindPath(map, new Vec3i(0, 0, 0), new Vec3i(size - 1, size - 1, 0));
        Assert.NotNull(path);
    }

    [Fact]
    public void GenerateEmbark_TreeTiles_Expose_TreeSpecies()
    {
        var map = new WorldMap();
        WorldGenerator.GenerateEmbark(map, 48, 48, 8, seed: 77, biomeId: MacroBiomeIds.ConiferForest);

        var treeCount = 0;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(new Vec3i(x, y, 0));
            if (!string.Equals(tile.TileDefId, TileDefIds.Tree))
                continue;

            treeCount++;
            Assert.False(string.IsNullOrWhiteSpace(tile.TreeSpeciesId));
        }

        Assert.True(treeCount > 0, "Expected forest biome to include trees on the surface.");
    }

    private static int CountSurfaceTiles(WorldMap map, string tileDefId)
    {
        var count = 0;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (map.GetTile(new Vec3i(x, y, 0)).TileDefId == tileDefId)
                count++;
        }
        return count;
    }
}

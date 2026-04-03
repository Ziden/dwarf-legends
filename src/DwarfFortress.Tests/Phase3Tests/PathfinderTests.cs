using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.World;
using DwarfFortress.GameLogic.Tests;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase3Tests;

public sealed class PathfinderTests
{
    private static WorldMap CreatePassableMap(int size = 16)
    {
        var (ctx, _, _) = TestFixtures.CreateContext();
        var map = new WorldMap();
        map.Initialize(ctx);
        map.SetDimensions(size, size, 4);

        // Fill a single z-level with passable tiles
        for (int x = 0; x < size; x++)
        for (int y = 0; y < size; y++)
        {
            map.SetTile(new Vec3i(x, y, 0), new TileData { IsPassable = true });
            for (int z = 1; z < 4; z++)
            {
                map.SetTile(new Vec3i(x, y, z), new TileData
                {
                    TileDefId = TileDefIds.GraniteWall,
                    IsPassable = false,
                });
            }
        }

        return map;
    }

    [Fact]
    public void FindPath_Returns_Single_Element_For_Same_Start_And_Goal()
    {
        var map  = CreatePassableMap();
        var pos  = new Vec3i(2, 2, 0);

        var path = Pathfinder.FindPath(map, pos, pos);

        Assert.Single(path);
        Assert.Equal(pos, path[0]);
    }

    [Fact]
    public void FindPath_Finds_Path_On_Open_Floor()
    {
        var map   = CreatePassableMap();
        var start = new Vec3i(0, 0, 0);
        var goal  = new Vec3i(5, 5, 0);

        var path = Pathfinder.FindPath(map, start, goal);

        Assert.NotEmpty(path);
        Assert.Equal(start, path[0]);
        Assert.Equal(goal,  path[^1]);
    }

    [Fact]
    public void FindPath_Returns_Empty_When_Goal_Is_Impassable()
    {
        var map  = CreatePassableMap();
        var goal = new Vec3i(3, 3, 0);
        map.SetTile(goal, new TileData { IsPassable = false });

        var path = Pathfinder.FindPath(map, new Vec3i(0, 0, 0), goal);

        Assert.Empty(path);
    }

    [Fact]
    public void FindPath_Navigates_Around_Wall()
    {
        var size = 8;
        var (ctx, _, _) = TestFixtures.CreateContext();
        var map = new WorldMap();
        map.Initialize(ctx);
        map.SetDimensions(size, size, 4);

        // Open floor
        for (int x = 0; x < size; x++)
        for (int y = 0; y < size; y++)
            map.SetTile(new Vec3i(x, y, 0), new TileData { IsPassable = true });

        // Wall in the middle except for a single gap at y=0
        for (int y = 1; y < size; y++)
            map.SetTile(new Vec3i(3, y, 0), new TileData { IsPassable = false });

        var start = new Vec3i(2, 4, 0);
        var goal  = new Vec3i(4, 4, 0);

        var path = Pathfinder.FindPath(map, start, goal);

        Assert.NotEmpty(path);
        Assert.Equal(goal, path[^1]);
    }

    [Fact]
    public void FindPath_Path_Contains_Only_Adjacent_Steps()
    {
        var map   = CreatePassableMap();
        var start = new Vec3i(0, 0, 0);
        var goal  = new Vec3i(4, 0, 0);

        var path = Pathfinder.FindPath(map, start, goal);

        for (int i = 1; i < path.Count; i++)
        {
            int dx = Math.Abs(path[i].X - path[i - 1].X);
            int dy = Math.Abs(path[i].Y - path[i - 1].Y);
            int dz = Math.Abs(path[i].Z - path[i - 1].Z);
            // Each step should move exactly one unit in one axis (no diagonals)
            Assert.Equal(1, dx + dy + dz);
        }
    }

    [Fact]
    public void FindPath_Avoids_Water_Tiles()
    {
        var map = CreatePassableMap(size: 7);
        var start = new Vec3i(0, 3, 0);
        var goal = new Vec3i(6, 3, 0);

        for (int x = 1; x <= 5; x++)
        {
            map.SetTile(new Vec3i(x, 3, 0), new TileData
            {
                TileDefId = TileDefIds.Water,
                IsPassable = true,
                FluidType = FluidType.Water,
                FluidLevel = 7,
            });
        }

        var path = Pathfinder.FindPath(map, start, goal);

        Assert.NotEmpty(path);
        Assert.DoesNotContain(path, pos => pos.Y == 3 && pos.X is >= 1 and <= 5);
        Assert.Equal(goal, path[^1]);
    }

    [Fact]
    public void FindPath_RequiresSwimming_Allows_Aquatic_Path_Through_DeepWater()
    {
        var map = CreatePassableMap(size: 7);
        var start = new Vec3i(1, 3, 0);
        var goal = new Vec3i(5, 3, 0);

        for (var x = 1; x <= 5; x++)
        {
            map.SetTile(new Vec3i(x, 3, 0), new TileData
            {
                TileDefId = TileDefIds.Water,
                IsPassable = true,
                FluidType = FluidType.Water,
                FluidLevel = 7,
            });
        }

        var walkPath = Pathfinder.FindPath(map, start, goal);
        var swimPath = Pathfinder.FindPath(map, start, goal, canSwim: true, requiresSwimming: true);

        Assert.Empty(walkPath);
        Assert.NotEmpty(swimPath);
        Assert.Equal(start, swimPath[0]);
        Assert.Equal(goal, swimPath[^1]);
        Assert.All(swimPath, pos => Assert.True(map.IsSwimmable(pos), $"Expected swimmable tile in aquatic path at {pos}."));
    }

    [Fact]
    public void FindPath_CanSwim_NotRequired_Crosses_DeepWater_When_LandRoute_Is_Blocked()
    {
        var map = CreatePassableMap(size: 7);
        var start = new Vec3i(3, 1, 0);
        var goal = new Vec3i(3, 5, 0);

        for (var x = 0; x < 7; x++)
        {
            map.SetTile(new Vec3i(x, 3, 0), new TileData
            {
                TileDefId = TileDefIds.GraniteWall,
                IsPassable = false,
            });
        }

        map.SetTile(new Vec3i(3, 3, 0), new TileData
        {
            TileDefId = TileDefIds.Water,
            IsPassable = true,
            FluidType = FluidType.Water,
            FluidLevel = 7,
        });

        var walkPath = Pathfinder.FindPath(map, start, goal);
        var swimPath = Pathfinder.FindPath(map, start, goal, canSwim: true, requiresSwimming: false);

        Assert.Empty(walkPath);
        Assert.NotEmpty(swimPath);
        Assert.Contains(new Vec3i(3, 3, 0), swimPath);
        Assert.Equal(goal, swimPath[^1]);
    }

    [Fact]
    public void FindPath_DoesNotChangeZWithoutConnectedStairs()
    {
        var map = CreatePassableMap(size: 5);
        var start = new Vec3i(2, 2, 0);
        var goal = new Vec3i(2, 2, 1);

        map.SetTile(goal, new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            IsPassable = true,
        });

        var path = Pathfinder.FindPath(map, start, goal);

        Assert.Empty(path);
    }

    [Fact]
    public void FindPath_CanChangeZThroughConnectedStairs()
    {
        var map = CreatePassableMap(size: 5);
        var start = new Vec3i(0, 2, 0);
        var lowerStair = new Vec3i(1, 2, 0);
        var upperStair = new Vec3i(1, 2, 1);
        var goal = new Vec3i(2, 2, 1);

        map.SetTile(lowerStair, new TileData
        {
            TileDefId = TileDefIds.Staircase,
            MaterialId = MaterialIds.Granite,
            IsPassable = true,
        });
        map.SetTile(upperStair, new TileData
        {
            TileDefId = TileDefIds.Staircase,
            MaterialId = MaterialIds.Granite,
            IsPassable = true,
        });
        map.SetTile(goal, new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            MaterialId = MaterialIds.Granite,
            IsPassable = true,
        });

        var path = Pathfinder.FindPath(map, start, goal);

        Assert.NotEmpty(path);
        Assert.Equal(start, path[0]);
        Assert.Equal(goal, path[^1]);
        Assert.Contains(lowerStair, path);
        Assert.Contains(upperStair, path);
    }
}

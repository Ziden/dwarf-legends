using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;
using DwarfFortress.GameLogic.Tests;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase3Tests;

public sealed class WorldMapTests
{
    private static (WorldMap map, GameContext ctx) CreateMap()
    {
        var (ctx, _, _) = TestFixtures.CreateContext();
        var map = new WorldMap();
        map.Initialize(ctx);
        map.SetDimensions(64, 64, 8);
        return (map, ctx);
    }

    [Fact]
    public void SetTile_And_GetTile_Roundtrip()
    {
        var (map, _) = CreateMap();
        var pos = new Vec3i(5, 5, 0);
        map.SetTile(pos, new TileData { TileDefId = TileDefIds.StoneFloor, IsPassable = true });

        var tile = map.GetTile(pos);

        Assert.Equal(TileDefIds.StoneFloor, tile.TileDefId);
        Assert.True(tile.IsPassable);
    }

    [Fact]
    public void GetTile_Returns_Empty_For_Out_Of_Bounds_Position()
    {
        var (map, _) = CreateMap();
        var pos = new Vec3i(999, 999, 999);

        var tile = map.GetTile(pos);

        Assert.Equal(TileData.Empty, tile);
    }

    [Fact]
    public void SetTile_Emits_TileChangedEvent()
    {
        var (map, ctx) = CreateMap();
        var pos = new Vec3i(2, 3, 0);
        Vec3i? receivedPos = null;
        ctx.EventBus.On<TileChangedEvent>(e => receivedPos = e.Pos);

        map.SetTile(pos, new TileData { TileDefId = TileDefIds.StoneFloor, IsPassable = true });

        Assert.Equal(pos, receivedPos);
    }

    [Fact]
    public void IsPassable_Reflects_Tile_Data()
    {
        var (map, _) = CreateMap();
        var open = new Vec3i(1, 1, 0);
        var blocked = new Vec3i(2, 2, 0);
        map.SetTile(open, new TileData { IsPassable = true });
        map.SetTile(blocked, new TileData { IsPassable = false });

        Assert.True(map.IsPassable(open));
        Assert.False(map.IsPassable(blocked));
    }

    [Fact]
    public void IsWalkable_Allows_ShallowWater_Blocks_DeepWater_And_Magma()
    {
        var (map, _) = CreateMap();
        var floor = new Vec3i(1, 1, 0);
        var shallowWater = new Vec3i(2, 2, 0);
        var deepWater = new Vec3i(3, 3, 0);
        var magma = new Vec3i(4, 4, 0);

        map.SetTile(floor, new TileData { TileDefId = TileDefIds.StoneFloor, IsPassable = true });
        map.SetTile(shallowWater, new TileData { TileDefId = TileDefIds.Water, IsPassable = true, FluidType = FluidType.Water, FluidLevel = 2 });
        map.SetTile(deepWater, new TileData { TileDefId = TileDefIds.Water, IsPassable = true, FluidType = FluidType.Water, FluidLevel = 7 });
        map.SetTile(magma, new TileData { TileDefId = TileDefIds.Magma, IsPassable = true, FluidType = FluidType.Magma, FluidLevel = 7 });

        Assert.True(map.IsWalkable(floor));
        Assert.True(map.IsWalkable(shallowWater));
        Assert.False(map.IsWalkable(deepWater));
        Assert.False(map.IsWalkable(magma));
        Assert.True(map.IsSwimmable(deepWater));
        Assert.False(map.IsSwimmable(shallowWater));
    }

    [Fact]
    public void SetTile_Normalizes_Legacy_Water_Tile_Into_Fluid_State()
    {
        var (map, _) = CreateMap();
        var pos = new Vec3i(4, 4, 0);

        map.SetTile(pos, new TileData { TileDefId = TileDefIds.Water, IsPassable = true, FluidLevel = 5 });

        var tile = map.GetTile(pos);
        Assert.Equal(FluidType.Water, tile.FluidType);
        Assert.True(tile.HasFluid);
    }

    [Fact]
    public void GetDirtyChunks_Contains_Chunk_After_SetTile()
    {
        var (map, _) = CreateMap();
        // After SetDimensions all chunks start dirty; clear them first
        foreach (var chunk in map.AllChunks()) chunk.ClearDirty();

        map.SetTile(new Vec3i(0, 0, 0), new TileData { TileDefId = TileDefIds.StoneFloor, IsPassable = true });

        Assert.NotEmpty(map.GetDirtyChunks());
    }

    [Fact]
    public void ClearDirty_On_Chunk_Removes_It_From_Dirty_List()
    {
        var (map, _) = CreateMap();
        map.SetTile(new Vec3i(0, 0, 0), new TileData { IsPassable = true });

        foreach (var chunk in map.AllChunks()) chunk.ClearDirty();

        Assert.Empty(map.GetDirtyChunks());
    }
}

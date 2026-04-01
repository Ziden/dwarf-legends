using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class SpatialIndexSystemTests
{
    [Fact]
    public void SpatialIndexSystem_Tracks_Dwarf_Movement_Incrementally()
    {
        var (sim, map, er, _, _) = TestFixtures.BuildFullSim();
        var spatial = sim.Context.Get<SpatialIndexSystem>();

        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(5, 5, 0));
        er.Register(dwarf);

        var oldPos = dwarf.Position.Position;
        var newPos = new Vec3i(6, 5, 0);
        map.SetTile(newPos, new TileData { TileDefId = TileDefIds.StoneFloor, MaterialId = "granite", IsPassable = true });

        dwarf.Position.Position = newPos;
        sim.EventBus.Emit(new EntityMovedEvent(dwarf.Id, oldPos, newPos));

        Assert.DoesNotContain(dwarf.Id, spatial.GetDwarvesAt(oldPos));
        Assert.Contains(dwarf.Id, spatial.GetDwarvesAt(newPos));
    }

    [Fact]
    public void SpatialIndexSystem_Tracks_Item_Moves_Incrementally()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();
        var spatial = sim.Context.Get<SpatialIndexSystem>();

        var item = items.CreateItem("log", "wood", new Vec3i(8, 8, 0));
        var oldPos = item.Position.Position;
        var newPos = new Vec3i(9, 8, 0);

        items.MoveItem(item.Id, newPos);

        Assert.DoesNotContain(item.Id, spatial.GetItemsAt(oldPos));
        Assert.Contains(item.Id, spatial.GetItemsAt(newPos));
    }

    [Fact]
    public void SpatialIndexSystem_Indexes_Building_Footprints_By_Tile()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();
        var spatial = sim.Context.Get<SpatialIndexSystem>();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 1, Width: 48, Height: 48, Depth: 8));

        var building = sim.Context.Get<BuildingSystem>().GetAll()
            .First(b => b.BuildingDefId == BuildingDefIds.CarpenterWorkshop);

        Assert.Equal(building.Id, spatial.GetBuildingAt(building.Origin));
        Assert.Equal(building.Id, spatial.GetBuildingAt(building.Origin + new Vec3i(1, 0, 0)));
        Assert.Equal(building.Id, spatial.GetBuildingAt(building.Origin + new Vec3i(0, 1, 0)));
        Assert.Equal(building.Id, spatial.GetBuildingAt(building.Origin + new Vec3i(1, 1, 0)));
    }
}
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Items;
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
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();
        var spatial = sim.Context.Get<SpatialIndexSystem>();

        items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, new Vec3i(2, 2, 0));
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: BuildingDefIds.CarpenterWorkshop,
            Origin: new Vec3i(8, 5, 0)));

        var building = sim.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(8, 5, 0));
        Assert.NotNull(building);

        Assert.Equal(building!.Id, spatial.GetBuildingAt(building.Origin));
        Assert.Equal(building.Id, spatial.GetBuildingAt(building.Origin + new Vec3i(1, 0, 0)));
        Assert.Equal(building.Id, spatial.GetBuildingAt(building.Origin + new Vec3i(0, 1, 0)));
        Assert.Equal(building.Id, spatial.GetBuildingAt(building.Origin + new Vec3i(1, 1, 0)));
    }

    [Fact]
    public void SpatialIndexSystem_Collects_Only_Ids_Inside_Visible_Bounds()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var spatial = sim.Context.Get<SpatialIndexSystem>();

        var insideDwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(5, 5, 0));
        var outsideDwarf = new Dwarf(er.NextId(), "Domas", new Vec3i(14, 14, 0));
        var insideCreature = new Creature(er.NextId(), "rat", new Vec3i(6, 5, 0), 20f);
        var outsideCreature = new Creature(er.NextId(), "rat", new Vec3i(15, 15, 0), 20f);
        er.Register(insideDwarf);
        er.Register(outsideDwarf);
        er.Register(insideCreature);
        er.Register(outsideCreature);

        var insideItem = items.CreateItem("log", "wood", new Vec3i(5, 6, 0));
        var outsideItem = items.CreateItem("log", "wood", new Vec3i(16, 16, 0));

        var dwarfIds = new List<int>();
        var creatureIds = new List<int>();
        var itemIds = new List<int>();

        spatial.CollectDwarvesInBounds(0, 4, 4, 6, 6, dwarfIds);
        spatial.CollectCreaturesInBounds(0, 4, 4, 6, 6, creatureIds);
        spatial.CollectItemsInBounds(0, 4, 4, 6, 6, itemIds);

        Assert.Contains(insideDwarf.Id, dwarfIds);
        Assert.DoesNotContain(outsideDwarf.Id, dwarfIds);
        Assert.Contains(insideCreature.Id, creatureIds);
        Assert.DoesNotContain(outsideCreature.Id, creatureIds);
        Assert.Contains(insideItem.Id, itemIds);
        Assert.DoesNotContain(outsideItem.Id, itemIds);
    }

    [Fact]
    public void SpatialIndexSystem_Collects_Containers_Inside_Visible_Bounds()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();
        var spatial = sim.Context.Get<SpatialIndexSystem>();

        var insideBox = new Box(er.NextId(), new Vec3i(5, 5, 0));
        var outsideBox = new Box(er.NextId(), new Vec3i(15, 15, 0));
        er.Register(insideBox);
        er.Register(outsideBox);

        var containerIds = new List<int>();
        spatial.CollectContainersInBounds(0, 4, 4, 6, 6, containerIds);

        Assert.Contains(insideBox.Id, containerIds);
        Assert.DoesNotContain(outsideBox.Id, containerIds);
    }

    [Fact]
    public void ItemSystem_CollectLooseItemsInBounds_Skips_Boxed_And_Carried_Items()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var looseLog = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, new Vec3i(5, 5, 0));
        var boxedMeal = items.CreateItem(ItemDefIds.Meal, MaterialIds.Food, new Vec3i(5, 5, 0));
        var carriedOre = items.CreateItem(ItemDefIds.CoalOre, MaterialIds.Coal, new Vec3i(5, 5, 0));
        var box = new Box(er.NextId(), new Vec3i(5, 5, 0));
        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(5, 5, 0));
        er.Register(box);
        er.Register(dwarf);

        items.StoreItemInBox(boxedMeal.Id, box);
        Assert.True(items.PickUpItem(carriedOre.Id, dwarf.Id, dwarf.Position.Position));

        var visibleItemIds = new List<int>();
        items.CollectLooseItemsInBounds(0, 4, 4, 6, 6, visibleItemIds);

        Assert.Contains(looseLog.Id, visibleItemIds);
        Assert.DoesNotContain(boxedMeal.Id, visibleItemIds);
        Assert.DoesNotContain(carriedOre.Id, visibleItemIds);
    }
}

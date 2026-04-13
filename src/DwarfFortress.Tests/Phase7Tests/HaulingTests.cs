using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Jobs.Strategies;
using DwarfFortress.GameLogic.Systems;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class HaulingTests
{
    [Fact]
    public void ItemSystem_Hauling_Uses_Hands_Without_Consuming_Inventory_Slots()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var dwarf = new Dwarf(er.NextId(), "Hauler", new Vec3i(10, 10, 0));
        er.Register(dwarf);

        for (var i = 0; i < InventoryComponent.MaxCapacity; i++)
        {
            var meal = items.CreateItem(ItemDefIds.Meal, MaterialIds.Food, dwarf.Position.Position);
            Assert.True(items.PickUpItem(meal.Id, dwarf.Id, dwarf.Position.Position));
        }

        var log = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, dwarf.Position.Position);

        Assert.True(items.PickUpItem(log.Id, dwarf.Id, dwarf.Position.Position, ItemCarryMode.Hauling));
        Assert.Equal(InventoryComponent.MaxCapacity, dwarf.Inventory.Count);
        Assert.True(dwarf.Hauling.IsHauling);
        Assert.Equal(log.Id, dwarf.Hauling.HauledItemId);
        Assert.Equal(ItemCarryMode.Hauling, log.CarryMode);

        var carriedIds = items.GetItemsCarriedBy(dwarf.Id).Select(item => item.Id).OrderBy(id => id).ToArray();
        Assert.Equal(InventoryComponent.MaxCapacity + 1, carriedIds.Length);
        Assert.Contains(log.Id, carriedIds);
    }

    [Fact]
    public void HaulItemStrategy_Can_Execute_When_Inventory_Is_Full_And_Uses_Hauling_Mode()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var dwarf = new Dwarf(er.NextId(), "Stocker", new Vec3i(8, 8, 0));
        er.Register(dwarf);

        for (var i = 0; i < InventoryComponent.MaxCapacity; i++)
        {
            var meal = items.CreateItem(ItemDefIds.Meal, MaterialIds.Food, dwarf.Position.Position);
            Assert.True(items.PickUpItem(meal.Id, dwarf.Id, dwarf.Position.Position));
        }

        var boulder = items.CreateItem(ItemDefIds.GraniteBoulder, MaterialIds.Granite, new Vec3i(9, 8, 0));
        var job = new Job(1, JobDefIds.HaulItem, boulder.Position.Position, priority: 5, entityId: boulder.Id);
        var strategy = new HaulItemStrategy();

        Assert.True(strategy.CanExecute(job, dwarf.Id, sim.Context));

        var pickup = Assert.Single(strategy.GetSteps(job, dwarf.Id, sim.Context).OfType<PickUpItemStep>());
        Assert.Equal(ItemCarryMode.Hauling, pickup.CarryMode);
    }

    [Fact]
    public void HaulItem_Jobs_Interrupt_Idle_And_Prefer_Closest_Eligible_Dwarf()
    {
        var (sim, _, er, js, items) = TestFixtures.BuildFullSim();

        var nearDwarf = new Dwarf(er.NextId(), "Near Hauler", new Vec3i(10, 10, 0));
        nearDwarf.Labors.DisableAll();
        nearDwarf.Labors.Enable(LaborIds.Hauling);
        er.Register(nearDwarf);

        var farDwarf = new Dwarf(er.NextId(), "Far Hauler", new Vec3i(2, 2, 0));
        farDwarf.Labors.DisableAll();
        farDwarf.Labors.Enable(LaborIds.Hauling);
        er.Register(farDwarf);

        sim.Context.Commands.Dispatch(new CreateStockpileCommand(
            new Vec3i(14, 10, 0),
            new Vec3i(14, 10, 0),
            []));

        sim.Tick(0.1f);
        Assert.Contains(js.GetAllJobs(), job => job.JobDefId == JobDefIds.Idle && job.AssignedDwarfId == nearDwarf.Id);
        Assert.Contains(js.GetAllJobs(), job => job.JobDefId == JobDefIds.Idle && job.AssignedDwarfId == farDwarf.Id);

        var log = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, new Vec3i(11, 10, 0));
        js.CreateJob(JobDefIds.HaulItem, log.Position.Position, priority: 5, entityId: log.Id);

        sim.Tick(0.1f);

        var haulJob = Assert.Single(js.GetAllJobs().Where(job => job.JobDefId == JobDefIds.HaulItem));
        Assert.Equal(nearDwarf.Id, haulJob.AssignedDwarfId);
        Assert.DoesNotContain(js.GetAllJobs(), job => job.JobDefId == JobDefIds.Idle && job.AssignedDwarfId == nearDwarf.Id);
        Assert.Contains(js.GetAllJobs(), job => job.JobDefId == JobDefIds.Idle && job.AssignedDwarfId == farDwarf.Id);
    }

    [Fact]
    public void PlaceBoxStrategy_Uses_Hauling_Mode_For_Box_Items()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var dwarf = new Dwarf(er.NextId(), "Box Carrier", new Vec3i(8, 8, 0));
        er.Register(dwarf);

        var boxItem = items.CreateItem(ItemDefIds.Box, MaterialIds.Wood, new Vec3i(9, 8, 0));
        var job = new Job(2, JobDefIds.PlaceBox, new Vec3i(10, 8, 0), priority: 5, entityId: boxItem.Id);
        var strategy = new PlaceBoxStrategy();

        Assert.True(strategy.CanExecute(job, dwarf.Id, sim.Context));

        var pickup = Assert.Single(strategy.GetSteps(job, dwarf.Id, sim.Context).OfType<PickUpItemStep>());
        Assert.Equal(ItemCarryMode.Hauling, pickup.CarryMode);
    }

    [Fact]
    public void WorldQuerySystem_Separates_Inventory_Items_From_Hauled_Item()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(6, 6, 0));
        er.Register(dwarf);

        var meal = items.CreateItem(ItemDefIds.Meal, MaterialIds.Food, dwarf.Position.Position);
        var log = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, dwarf.Position.Position);

        Assert.True(items.PickUpItem(meal.Id, dwarf.Id, dwarf.Position.Position));
        Assert.True(items.PickUpItem(log.Id, dwarf.Id, dwarf.Position.Position, ItemCarryMode.Hauling));

        var view = queries.GetDwarfView(dwarf.Id);

        Assert.NotNull(view);
        var carried = Assert.Single(view!.CarriedItems);
        Assert.Equal(meal.Id, carried.Id);
        Assert.Equal(ItemCarryMode.Inventory, carried.CarryMode);
        Assert.NotNull(view.HauledItem);
        Assert.Equal(log.Id, view.HauledItem!.Id);
        Assert.Equal(ItemCarryMode.Hauling, view.HauledItem.CarryMode);
    }

    [Fact]
    public void DeathSystem_Stores_Hauled_Items_Inside_Corpse()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var pos = new Vec3i(12, 12, 0);
        var creature = new Creature(er.NextId(), DefIds.Elk, pos, maxHealth: 85f);
        er.Register(creature);

        var log = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, pos);
        Assert.True(items.PickUpItem(log.Id, creature.Id, pos, ItemCarryMode.Hauling));

        creature.Needs.Thirst.SetLevel(0f, 6f * 60f);
        sim.Tick(0.1f);

        var corpse = Assert.Single(items.GetItemsAt(pos).Where(item => item.DefId == ItemDefIds.Corpse));
        Assert.Contains(items.GetItemsInItem(corpse.Id), item => item.Id == log.Id);
    }

    [Fact]
    public void ItemSystem_MoveItem_Removes_Item_From_BoxContainer_When_Item_Leaves_Box()
    {
        var (_, _, er, _, items) = TestFixtures.BuildFullSim();

        var box = new Box(er.NextId(), new Vec3i(5, 5, 0));
        er.Register(box);

        var meal = items.CreateItem(ItemDefIds.Meal, MaterialIds.Food, box.Position.Position);
        items.StoreItemInBox(meal.Id, box);

        items.MoveItem(meal.Id, new Vec3i(6, 5, 0));

        Assert.DoesNotContain(meal.Id, box.Container.StoredItemIds);
        Assert.Equal(-1, meal.ContainerItemId);
        Assert.Equal(new Vec3i(6, 5, 0), meal.Position.Position);
    }

    [Fact]
    public void ItemSystem_InventoryPickup_Respects_Dwarf_Strength_Capacity()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var weakDwarf = new Dwarf(er.NextId(), "Kogan", new Vec3i(5, 5, 0));
        weakDwarf.Appearance.Height = 100f;
        weakDwarf.Attributes.SetLevel(AttributeIds.Strength, 1);
        er.Register(weakDwarf);

        var strongDwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(7, 5, 0));
        strongDwarf.Appearance.Height = 100f;
        strongDwarf.Attributes.SetLevel(AttributeIds.Strength, 5);
        er.Register(strongDwarf);

        var weakBoulderA = items.CreateItem(ItemDefIds.GraniteBoulder, MaterialIds.Granite, weakDwarf.Position.Position);
        var weakBoulderB = items.CreateItem(ItemDefIds.GraniteBoulder, MaterialIds.Granite, weakDwarf.Position.Position);
        var strongBoulderA = items.CreateItem(ItemDefIds.GraniteBoulder, MaterialIds.Granite, strongDwarf.Position.Position);
        var strongBoulderB = items.CreateItem(ItemDefIds.GraniteBoulder, MaterialIds.Granite, strongDwarf.Position.Position);

        Assert.True(items.PickUpItem(weakBoulderA.Id, weakDwarf.Id, weakDwarf.Position.Position));
        Assert.False(items.PickUpItem(weakBoulderB.Id, weakDwarf.Id, weakDwarf.Position.Position));

        Assert.True(items.PickUpItem(strongBoulderA.Id, strongDwarf.Id, strongDwarf.Position.Position));
        Assert.True(items.PickUpItem(strongBoulderB.Id, strongDwarf.Id, strongDwarf.Position.Position));
    }
}

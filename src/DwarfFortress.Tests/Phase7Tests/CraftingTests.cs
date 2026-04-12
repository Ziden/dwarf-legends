using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Jobs.Strategies;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.Tests.Fakes;
using DwarfFortress.GameLogic.World;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

/// <summary>
/// Tests for CraftItemStrategy and the RecipeSystem production queue.
/// </summary>
public sealed class CraftingTests
{
    // ── Strategy basics ────────────────────────────────────────────────────

    [Fact]
    public void CraftItemStrategy_Has_Correct_JobDefId()
    {
        var strategy = new CraftItemStrategy();
        Assert.Equal("craft_item", strategy.JobDefId);
    }

    [Fact]
    public void CraftItemStrategy_Reserves_And_Hauls_Inputs_To_Workshop()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();

        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(1, 1, 0));
        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(2, 1, 0));
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: "carpenter_workshop",
            Origin: new Vec3i(5, 5, 0)));

        var workshop = sim.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(5, 5, 0));
        Assert.NotNull(workshop);

        items.CreateItem(ItemDefIds.Plank, "wood", new Vec3i(3, 1, 0));
        items.CreateItem(ItemDefIds.Plank, "wood", new Vec3i(4, 1, 0));
        sim.Context.Commands.Dispatch(new SetProductionOrderCommand(
            WorkshopEntityId: workshop!.Id,
            RecipeDefId: "make_bed",
            Quantity: 1));

        var job = new Job(1, JobDefIds.Craft, workshop.Origin, priority: 3, entityId: workshop.Id);
        var steps = new CraftItemStrategy().GetSteps(job, dwarfId: 1, sim.Context);

        Assert.Equal(2, job.ReservedItemIds.Count);
        Assert.Contains(steps, step => step is PickUpItemStep);
        Assert.Contains(steps, step => step is PlaceItemStep place && place.Target == workshop.Origin && place.ContainerBuildingId == workshop.Id);
        Assert.IsType<WorkAtStep>(steps.Last());
    }

    [Fact]
    public void ItemSystem_Can_Store_Item_In_Workshop_Container()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();

        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(1, 1, 0));
        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(2, 1, 0));
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: "carpenter_workshop",
            Origin: new Vec3i(5, 5, 0)));

        var workshop = sim.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(5, 5, 0));
        Assert.NotNull(workshop);

        var item = items.CreateItem(ItemDefIds.Plank, "wood", new Vec3i(2, 2, 0));
        items.StoreItemInBuilding(item.Id, workshop!.Id, workshop.Origin);

        Assert.Equal(workshop.Id, item.ContainerBuildingId);
        Assert.Equal(workshop.Origin, item.Position.Position);
        Assert.Contains(items.GetItemsInBuilding(workshop.Id), stored => stored.Id == item.Id);

        var buildingView = sim.Context.Get<WorldQuerySystem>().GetBuildingView(workshop.Id);
        Assert.NotNull(buildingView);
        Assert.Equal(1, buildingView!.StoredItemCount);
    }

    // ── RecipeSystem queue ─────────────────────────────────────────────────

    [Fact]
    public void RecipeSystem_Accepts_Production_Order_Command()
    {
        var (sim, _, _, js, _) = TestFixtures.BuildFullSim();

        var ex = Record.Exception(() =>
            sim.Context.Commands.Dispatch(new SetProductionOrderCommand(
                WorkshopEntityId: 1,
                RecipeDefId:      "make_plank",
                Quantity:         3)));

        Assert.Null(ex);
    }

    [Fact]
    public void RecipeSystem_Creates_Craft_Job_After_Order_Submitted()
    {
        var (sim, map, _, js, items) = TestFixtures.BuildFullSim();

        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(0, 0, 0));
        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(1, 0, 0));

        // Place a carpenter workshop so RecipeSystem can resolve its position
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: "carpenter_workshop",
            Origin: new Vec3i(5, 5, 0)));

        // Find the placed building's ID
        var bs        = sim.Context.Get<BuildingSystem>();
        var workshop  = bs.GetByOrigin(new Vec3i(5, 5, 0));
        Assert.NotNull(workshop);

        // Place a log so the recipe's ingredient is available
        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(0, 0, 0));

        sim.Context.Commands.Dispatch(new SetProductionOrderCommand(
            WorkshopEntityId: workshop!.Id,
            RecipeDefId:      "make_plank",
            Quantity:         1));

        // Tick a few times so RecipeSystem can evaluate
        for (int i = 0; i < 10; i++) sim.Tick(0.1f);

        var craftJobs = js.GetAllJobs().Where(j => j.JobDefId == "craft_item").ToList();
        Assert.True(craftJobs.Count > 0,
            "Expected at least one 'craft_item' job to be created by RecipeSystem.");
    }

    [Fact]
    public void RecipeSystem_Queues_Recipe_In_Target_Workshop()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Crafter", new Vec3i(4, 4, 0));
        er.Register(dwarf);

        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(4, 5, 0));

        sim.Context.Commands.Dispatch(new SetProductionOrderCommand(
            WorkshopEntityId: 4,
            RecipeDefId:      "make_plank",
            Quantity:         1));

        var queue = sim.Context.Get<RecipeSystem>().GetOrCreateQueue(4);
        var order = queue.Peek();

        Assert.NotNull(order);
        Assert.Equal("make_plank", order!.RecipeId);
        Assert.Equal(1, order.Remaining);
    }

    [Fact]
    public void RecipeSystem_Does_Not_Assign_Craft_Job_When_Inputs_Are_Missing()
    {
        var (sim, _, _, js, items) = TestFixtures.BuildFullSim();

        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(5, 4, 0));
        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(5, 3, 0));

        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: "carpenter_workshop",
            Origin: new Vec3i(5, 5, 0)));

        var workshop = sim.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(5, 5, 0));
        Assert.NotNull(workshop);

        sim.Context.Commands.Dispatch(new SetProductionOrderCommand(
            WorkshopEntityId: workshop!.Id,
            RecipeDefId: "make_bed",
            Quantity: 1));

        for (int i = 0; i < 20; i++) sim.Tick(0.1f);

        Assert.DoesNotContain(js.GetAllJobs(), j => j.JobDefId == JobDefIds.Craft && j.EntityId == workshop.Id);

        var queue = sim.Context.Get<RecipeSystem>().GetOrCreateQueue(workshop.Id);
        Assert.NotNull(queue.Peek());
        Assert.Equal("make_bed", queue.Peek()!.RecipeId);
    }

    [Fact]
    public void RecipeSystem_Crafts_Output_When_Craft_Job_Completes()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Crafter", new Vec3i(5, 5, 0));
        er.Register(dwarf);

        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(3, 5, 0));
        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(3, 6, 0));

        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: "carpenter_workshop",
            Origin: new Vec3i(5, 5, 0)));

        var workshop = sim.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(5, 5, 0));
        Assert.NotNull(workshop);

        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(4, 5, 0));

        sim.Context.Commands.Dispatch(new SetProductionOrderCommand(
            WorkshopEntityId: workshop!.Id,
            RecipeDefId: "make_plank",
            Quantity: 1));

        for (int i = 0; i < 200; i++) sim.Tick(0.1f);

        Assert.Contains(items.GetAllItems(), item =>
            item.DefId == ItemDefIds.Plank &&
            item.MaterialId == "wood" &&
            item.Position.Position == workshop.Origin);

        var queue = sim.Context.Get<RecipeSystem>().GetOrCreateQueue(workshop.Id);
        Assert.Null(queue.Peek());
    }

    [Fact]
    public void RecipeSystem_Picks_Compatible_Materials_For_Derived_Wood_Output()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Carpenter", new Vec3i(5, 5, 0));
        er.Register(dwarf);
        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(1, 1, 0));
        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(2, 1, 0));

        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: "carpenter_workshop",
            Origin: new Vec3i(5, 5, 0)));

        var workshop = sim.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(5, 5, 0));
        Assert.NotNull(workshop);

        items.CreateItem(ItemDefIds.Plank, "oak", new Vec3i(4, 5, 0));
        items.CreateItem(ItemDefIds.Plank, "pine", new Vec3i(4, 6, 0));
        items.CreateItem(ItemDefIds.Plank, "oak", new Vec3i(4, 7, 0));

        sim.Context.Commands.Dispatch(new SetProductionOrderCommand(
            WorkshopEntityId: workshop!.Id,
            RecipeDefId: "make_bed",
            Quantity: 1));

        for (int i = 0; i < 250; i++) sim.Tick(0.1f);

        Assert.Contains(items.GetAllItems(), item =>
            item.DefId == ItemDefIds.Bed &&
            item.MaterialId == "oak" &&
            item.Position.Position == workshop.Origin);
        Assert.Contains(items.GetAllItems(), item => item.DefId == ItemDefIds.Plank && item.MaterialId == "pine");
    }

    [Fact]
    public void RecipeSystem_Consumes_All_Required_Input_Quantities()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Smelter", new Vec3i(6, 6, 0));
        er.Register(dwarf);

        items.CreateItem(ItemDefIds.GraniteBoulder, "granite", new Vec3i(7, 6, 0));
        items.CreateItem(ItemDefIds.GraniteBoulder, "granite", new Vec3i(8, 6, 0));
        items.CreateItem(ItemDefIds.GraniteBoulder, "granite", new Vec3i(9, 6, 0));
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: "smelter",
            Origin: new Vec3i(6, 6, 0)));

        var workshop = sim.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(6, 6, 0));
        Assert.NotNull(workshop);

        items.CreateItem(ItemDefIds.IronOre, "iron", new Vec3i(4, 6, 0));
        items.CreateItem(ItemDefIds.IronOre, "iron", new Vec3i(4, 7, 0));
        items.CreateItem(ItemDefIds.CoalOre, "coal", new Vec3i(4, 8, 0));

        sim.Context.Commands.Dispatch(new SetProductionOrderCommand(
            WorkshopEntityId: workshop!.Id,
            RecipeDefId: "make_iron_bar",
            Quantity: 1));

        sim.Context.EventBus.Emit(new JobCompletedEvent(
            JobId: 1,
            DwarfId: dwarf.Id,
            JobDefId: JobDefIds.Craft,
            EntityId: workshop.Id));

        Assert.Contains(items.GetAllItems(), item => item.DefId == ItemDefIds.IronBar && item.MaterialId == "iron");
        Assert.DoesNotContain(items.GetAllItems(), item => item.DefId == ItemDefIds.IronOre);
        Assert.DoesNotContain(items.GetAllItems(), item => item.DefId == ItemDefIds.CoalOre);
    }

    [Fact]
    public void BuildingPlacement_Consumes_Construction_Inputs()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();

        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(2, 2, 0));

        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: "carpenter_workshop",
            Origin: new Vec3i(5, 5, 0)));

        Assert.NotNull(sim.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(5, 5, 0)));
        Assert.DoesNotContain(items.GetAllItems(), item => item.DefId == ItemDefIds.Log);
    }

    [Fact]
    public void BuildingPlacement_Preserves_Consumed_Wood_Material_On_Footprint()
    {
        var (sim, map, _, _, items) = TestFixtures.BuildFullSim();

        items.CreateItem(ItemDefIds.Log, "oak_wood", new Vec3i(2, 2, 0));

        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: BuildingDefIds.CarpenterWorkshop,
            Origin: new Vec3i(5, 5, 0)));

        var building = sim.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(5, 5, 0));
        Assert.NotNull(building);
        Assert.Equal("oak_wood", building!.MaterialId);
        Assert.Equal("oak_wood", map.GetTile(new Vec3i(5, 5, 0)).MaterialId);
        Assert.Equal("oak_wood", map.GetTile(new Vec3i(6, 6, 0)).MaterialId);
    }

    [Fact]
    public void SleepStrategy_Uses_Placed_Bed_When_One_Exists()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Sleeper", new Vec3i(10, 10, 0));
        dwarf.Needs.Sleep.SetLevel(0.05f);
        er.Register(dwarf);

        items.CreateItem(ItemDefIds.Bed, "wood", new Vec3i(11, 10, 0));
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: "bed",
            Origin: new Vec3i(12, 10, 0)));

        var job = new Job(1, JobDefIds.Sleep, dwarf.Position.Position, priority: 100);
        var steps = new SleepStrategy().GetSteps(job, dwarf.Id, sim.Context);

        var move = Assert.IsType<MoveToStep>(steps[0]);
        var work = Assert.IsType<WorkAtStep>(steps[1]);
        Assert.Equal(new Vec3i(12, 10, 0), move.Target);
        Assert.Equal(171f, work.Duration, precision: 3);
    }

    [Fact]
    public void SleepStrategy_Ignores_Unreachable_Bed_When_Selecting_Target()
    {
        var (sim, map, er, _, items) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Sleeper", new Vec3i(10, 10, 0));
        dwarf.Needs.Sleep.SetLevel(0.05f);
        er.Register(dwarf);

        items.CreateItem(ItemDefIds.Bed, "wood", new Vec3i(13, 10, 0));
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: BuildingDefIds.Bed,
            Origin: new Vec3i(14, 10, 0)));

        foreach (var pos in new[]
                 {
                     new Vec3i(13, 10, 0),
                     new Vec3i(15, 10, 0),
                     new Vec3i(14, 9, 0),
                     new Vec3i(14, 11, 0),
                 })
        {
            map.SetTile(pos, new TileData
            {
                TileDefId = TileDefIds.Tree,
                MaterialId = MaterialIds.Granite,
                IsPassable = false,
            });
        }

        var job = new Job(1, JobDefIds.Sleep, dwarf.Position.Position, priority: 100);
        var steps = new SleepStrategy().GetSteps(job, dwarf.Id, sim.Context);

        var move = Assert.IsType<MoveToStep>(steps[0]);
        Assert.NotEqual(new Vec3i(14, 10, 0), move.Target);
    }

    [Fact]
    public void EatStrategy_Uses_Chair_And_Table_When_Available()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Diner", new Vec3i(10, 10, 0));
        er.Register(dwarf);

        var meal = items.CreateItem(ItemDefIds.Meal, "food", new Vec3i(9, 10, 0));
        meal.StockpileId = 1;

        items.CreateItem(ItemDefIds.Table, "wood", new Vec3i(11, 10, 0));
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: "table",
            Origin: new Vec3i(12, 10, 0)));

        items.CreateItem(ItemDefIds.Chair, "wood", new Vec3i(11, 11, 0));
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: "chair",
            Origin: new Vec3i(12, 11, 0)));

        var job = new Job(1, JobDefIds.Eat, dwarf.Position.Position, priority: 100);
        var steps = new EatStrategy().GetSteps(job, dwarf.Id, sim.Context);

        Assert.Equal(4, steps.Count);
        Assert.IsType<MoveToStep>(steps[0]);
        Assert.IsType<PickUpItemStep>(steps[1]);
        var dineMove = Assert.IsType<MoveToStep>(steps[2]);
        var work = Assert.IsType<WorkAtStep>(steps[3]);

        Assert.Equal(new Vec3i(12, 11, 0), dineMove.Target);
        Assert.Equal(1f, work.Duration);
    }

    [Fact]
    public void EatStrategy_Can_Use_Loose_Food_Not_In_A_Stockpile()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Forager", new Vec3i(10, 10, 0));
        er.Register(dwarf);

        var meal = items.CreateItem(ItemDefIds.Meal, "food", new Vec3i(9, 10, 0));
        meal.StockpileId = -1;

        var job = new Job(1, JobDefIds.Eat, dwarf.Position.Position, priority: 100);
        var steps = new EatStrategy().GetSteps(job, dwarf.Id, sim.Context);

        Assert.NotEmpty(steps);
        var move = Assert.IsType<MoveToStep>(steps[0]);
        var pickup = Assert.IsType<PickUpItemStep>(steps[1]);
        Assert.Equal(meal.Position.Position, move.Target);
        Assert.Equal(meal.Id, pickup.ItemEntityId);
    }

    [Fact]
    public void EatStrategy_Can_Forage_Harvestable_Plant_When_No_Food_Items_Exist()
    {
        var (sim, map, er, _, _) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Forager", new Vec3i(10, 10, 0));
        er.Register(dwarf);

        var plantPos = new Vec3i(12, 10, 0);
        var tile = map.GetTile(plantPos);
        tile.TileDefId = TileDefIds.Grass;
        tile.IsPassable = true;
        tile.PlantDefId = "berry_bush";
        tile.PlantGrowthStage = PlantGrowthStages.Mature;
        tile.PlantYieldLevel = 1;
        map.SetTile(plantPos, tile);

        var job = new Job(1, JobDefIds.Eat, plantPos, priority: 100);
        var steps = new EatStrategy().GetSteps(job, dwarf.Id, sim.Context);

        Assert.Equal(2, steps.Count);
        var move = Assert.IsType<MoveToStep>(steps[0]);
        var work = Assert.IsType<WorkAtStep>(steps[1]);
        Assert.Equal(plantPos, move.Target);
        Assert.Equal("gather_plants", work.AnimationHint);

        new EatStrategy().OnComplete(job, dwarf.Id, sim.Context);

        Assert.InRange(dwarf.Needs.Hunger.Level, 0.79f, 1f);
        Assert.Equal(0, map.GetTile(plantPos).PlantYieldLevel);
    }
}

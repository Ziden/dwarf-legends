using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Jobs.Strategies;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class PlantHarvestTests
{
    [Fact]
    public void Mature_Plants_Do_Not_AutoQueue_Harvest_Jobs()
    {
        var (sim, map, _, js, _) = TestFixtures.BuildFullSim();
        var plantPos = new Vec3i(18, 18, 0);
        var tile = map.GetTile(plantPos);
        tile.TileDefId = TileDefIds.Grass;
        tile.IsPassable = true;
        tile.PlantDefId = "berry_bush";
        tile.PlantGrowthStage = PlantGrowthStages.Mature;
        tile.PlantYieldLevel = 1;
        tile.PlantSeedLevel = 1;
        map.SetTile(plantPos, tile);

        sim.Tick(3.2f);

        Assert.DoesNotContain(js.GetAllJobs(), job => job.JobDefId == JobDefIds.HarvestPlant && job.TargetPos == plantPos);
    }

    [Fact]
    public void HarvestPlantStrategy_Creates_Food_And_Resets_Yield()
    {
        var (sim, map, er, _, items) = TestFixtures.BuildFullSim();
        var dwarf = new Dwarf(er.NextId(), "Harvester", new Vec3i(9, 10, 0));
        er.Register(dwarf);

        var plantPos = new Vec3i(10, 10, 0);
        var tile = map.GetTile(plantPos);
        tile.TileDefId = TileDefIds.Grass;
        tile.IsPassable = true;
        tile.PlantDefId = "sunroot";
        tile.PlantGrowthStage = PlantGrowthStages.Mature;
        tile.PlantYieldLevel = 1;
        tile.PlantSeedLevel = 1;
        map.SetTile(plantPos, tile);

        var job = new Job(1, JobDefIds.HarvestPlant, plantPos, priority: 8);
        var strategy = new DwarfFortress.GameLogic.Jobs.Strategies.HarvestPlantStrategy();

        Assert.True(strategy.CanExecute(job, dwarf.Id, sim.Context));
        strategy.OnComplete(job, dwarf.Id, sim.Context);

        var bulb = Assert.Single(items.GetAllItems().Where(item => item.DefId == ItemDefIds.SunrootBulb));
        var seed = Assert.Single(items.GetAllItems().Where(item => item.DefId == ItemDefIds.SunrootSeed));
        Assert.Equal(dwarf.Id, bulb.CarriedByEntityId);
        Assert.Equal(dwarf.Id, seed.CarriedByEntityId);
        Assert.Contains(bulb.Id, dwarf.Inventory.CarriedItemIds);
        Assert.Contains(seed.Id, dwarf.Inventory.CarriedItemIds);

        var harvestedTile = map.GetTile(plantPos);
        Assert.Equal(0, harvestedTile.PlantYieldLevel);
        Assert.Equal(0, harvestedTile.PlantSeedLevel);
        Assert.True(harvestedTile.PlantGrowthStage < PlantGrowthStages.Mature);
        Assert.False(harvestedTile.IsDesignated);

        Assert.False(DwarfFortress.GameLogic.Systems.PlantHarvesting.TryHarvestPlant(
            sim.Context,
            plantPos,
            dropHarvestItem: true,
            dropSeedItem: true,
            out _));
    }

    [Fact]
    public void Critical_Hunger_Can_Target_Harvestable_Plant_When_No_Food_Items_Exist()
    {
        var (sim, map, er, js, _) = TestFixtures.BuildFullSim();
        var dwarf = new Dwarf(er.NextId(), "HungryForager", new Vec3i(9, 10, 0));
        er.Register(dwarf);

        var plantPos = new Vec3i(10, 10, 0);
        var tile = map.GetTile(plantPos);
        tile.TileDefId = TileDefIds.Grass;
        tile.IsPassable = true;
        tile.PlantDefId = "sunroot";
        tile.PlantGrowthStage = PlantGrowthStages.Mature;
        tile.PlantYieldLevel = 1;
        tile.PlantSeedLevel = 1;
        tile.IsDesignated = true;
        map.SetTile(plantPos, tile);

        dwarf.Needs.Hunger.SetLevel(0.01f);

        sim.Tick(0.1f);

        var eatJob = Assert.Single(js.GetAllJobs().Where(job => job.JobDefId == JobDefIds.Eat));
        Assert.Equal(plantPos, eatJob.TargetPos);

        var eatStrategy = new EatStrategy();
        Assert.True(eatStrategy.CanExecute(eatJob, dwarf.Id, sim.Context));

        var steps = eatStrategy.GetSteps(eatJob, dwarf.Id, sim.Context);
        Assert.DoesNotContain(steps, step => step is PickUpItemStep);
        Assert.Contains(steps, step => step is WorkAtStep work && work.AnimationHint == "gather_plants");
    }

    [Fact]
    public void EatStrategy_Consumes_Carried_Harvested_Food()
    {
        var (sim, map, er, _, items) = TestFixtures.BuildFullSim();
        var dwarf = new Dwarf(er.NextId(), "Forager", new Vec3i(9, 10, 0));
        er.Register(dwarf);

        var plantPos = new Vec3i(10, 10, 0);
        var tile = map.GetTile(plantPos);
        tile.TileDefId = TileDefIds.Grass;
        tile.IsPassable = true;
        tile.PlantDefId = "sunroot";
        tile.PlantGrowthStage = PlantGrowthStages.Mature;
        tile.PlantYieldLevel = 1;
        tile.PlantSeedLevel = 1;
        map.SetTile(plantPos, tile);

        var harvestStrategy = new HarvestPlantStrategy();
        harvestStrategy.OnComplete(new Job(1, JobDefIds.HarvestPlant, plantPos, priority: 8), dwarf.Id, sim.Context);

        dwarf.Needs.Hunger.SetLevel(0.01f);

        var eatJob = new Job(2, JobDefIds.Eat, dwarf.Position.Position, priority: 100);
        var eatStrategy = new EatStrategy();

        Assert.True(eatStrategy.CanExecute(eatJob, dwarf.Id, sim.Context));
        var steps = eatStrategy.GetSteps(eatJob, dwarf.Id, sim.Context);
        Assert.DoesNotContain(steps, step => step is PickUpItemStep);
        Assert.NotEmpty(eatJob.ReservedItemIds);

        eatStrategy.OnComplete(eatJob, dwarf.Id, sim.Context);

        Assert.True(dwarf.Needs.Hunger.Level > 0.01f);
        Assert.DoesNotContain(items.GetAllItems(), item => item.DefId == ItemDefIds.SunrootBulb);
        Assert.DoesNotContain(ItemDefIds.SunrootBulb, dwarf.Inventory.CarriedItemIds
            .Select(itemId => items.TryGetItem(itemId, out var item) ? item?.DefId : null)
            .Where(defId => defId is not null)!);
    }

    [Fact]
    public void EatStrategy_Can_Forage_When_Inventory_Is_Full_Even_If_Food_Items_Exist()
    {
        var (sim, map, er, _, items) = TestFixtures.BuildFullSim();
        var dwarf = new Dwarf(er.NextId(), "PackedForager", new Vec3i(9, 10, 0));
        er.Register(dwarf);

        dwarf.Inventory.AddCarriedItem(1001);
        dwarf.Inventory.AddCarriedItem(1002);
        dwarf.Inventory.AddCarriedItem(1003);
        dwarf.Inventory.AddCarriedItem(1004);

        items.CreateItem(ItemDefIds.Meal, MaterialIds.Food, new Vec3i(20, 20, 0));

        var plantPos = new Vec3i(10, 10, 0);
        var tile = map.GetTile(plantPos);
        tile.TileDefId = TileDefIds.Grass;
        tile.IsPassable = true;
        tile.PlantDefId = "sunroot";
        tile.PlantGrowthStage = PlantGrowthStages.Mature;
        tile.PlantYieldLevel = 1;
        tile.PlantSeedLevel = 1;
        map.SetTile(plantPos, tile);

        var eatJob = new Job(2, JobDefIds.Eat, dwarf.Position.Position, priority: 100);
        var eatStrategy = new EatStrategy();

        Assert.True(eatStrategy.CanExecute(eatJob, dwarf.Id, sim.Context));
        var steps = eatStrategy.GetSteps(eatJob, dwarf.Id, sim.Context);
        Assert.DoesNotContain(steps, step => step is PickUpItemStep);
        Assert.Contains(steps, step => step is WorkAtStep work && work.AnimationHint == "gather_plants");
    }

    [Fact]
    public void IdleJob_Batch_Unloads_Harvested_Items_Using_Preferred_Stockpiles()
    {
        var (sim, map, er, _, items) = TestFixtures.BuildFullSim();
        var dwarf = new Dwarf(er.NextId(), "Storekeeper", new Vec3i(9, 10, 0));
        er.Register(dwarf);

        var completedIdleJobs = 0;
        sim.Context.EventBus.On<JobCompletedEvent>(e =>
        {
            if (e.JobDefId == JobDefIds.Idle)
                completedIdleJobs++;
        });

        sim.Context.Commands.Dispatch(new CreateStockpileCommand(
            new Vec3i(12, 9, 0),
            new Vec3i(12, 9, 0),
            [TagIds.Food]));
        sim.Context.Commands.Dispatch(new CreateStockpileCommand(
            new Vec3i(13, 9, 0),
            new Vec3i(13, 9, 0),
            [TagIds.Seed]));

        var plantPos = new Vec3i(10, 10, 0);
        var tile = map.GetTile(plantPos);
        tile.TileDefId = TileDefIds.Grass;
        tile.IsPassable = true;
        tile.PlantDefId = "sunroot";
        tile.PlantGrowthStage = PlantGrowthStages.Mature;
        tile.PlantYieldLevel = 1;
        tile.PlantSeedLevel = 1;
        map.SetTile(plantPos, tile);

        var harvestStrategy = new HarvestPlantStrategy();
        harvestStrategy.OnComplete(new Job(1, JobDefIds.HarvestPlant, plantPos, priority: 8), dwarf.Id, sim.Context);

        Assert.NotEmpty(dwarf.Inventory.CarriedItemIds);

        for (var i = 0; i < 60; i++)
        {
            sim.Tick(0.5f);

            var trackedItems = items.GetAllItems()
                .Where(item => item.DefId is ItemDefIds.SunrootBulb or ItemDefIds.SunrootSeed)
                .ToList();

            if (dwarf.Inventory.CarriedItemIds.Count == 0 && trackedItems.Count == 2 && trackedItems.All(item => item.StockpileId >= 0))
                break;
        }

        Assert.Empty(dwarf.Inventory.CarriedItemIds);

        var storedItems = items.GetAllItems()
            .Where(item => item.DefId is ItemDefIds.SunrootBulb or ItemDefIds.SunrootSeed)
            .ToList();

        Assert.Equal(2, storedItems.Count);
        Assert.All(storedItems, item => Assert.True(item.StockpileId >= 0));
        Assert.Equal(1, completedIdleJobs);

        var storedBulb = Assert.Single(storedItems.Where(item => item.DefId == ItemDefIds.SunrootBulb));
        var storedSeed = Assert.Single(storedItems.Where(item => item.DefId == ItemDefIds.SunrootSeed));
        Assert.Equal(new Vec3i(12, 9, 0), storedBulb.Position.Position);
        Assert.Equal(new Vec3i(13, 9, 0), storedSeed.Position.Position);
    }
}

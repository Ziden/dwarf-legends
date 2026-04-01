using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class PlantHarvestTests
{
    [Fact]
    public void PlantHarvestSystem_Queues_Harvest_Job_For_Mature_Plant()
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

        Assert.Contains(js.GetAllJobs(), job => job.JobDefId == JobDefIds.HarvestPlant && job.TargetPos == plantPos);
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

        Assert.Contains(items.GetAllItems(), item => item.DefId == ItemDefIds.SunrootBulb && item.Position.Position == plantPos);
        Assert.Contains(items.GetAllItems(), item => item.DefId == ItemDefIds.SunrootSeed && item.Position.Position == plantPos);

        var harvestedTile = map.GetTile(plantPos);
        Assert.Equal(0, harvestedTile.PlantYieldLevel);
        Assert.Equal(0, harvestedTile.PlantSeedLevel);
        Assert.True(harvestedTile.PlantGrowthStage < PlantGrowthStages.Mature);
    }
}
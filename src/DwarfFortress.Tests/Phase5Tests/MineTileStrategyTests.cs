using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Jobs.Strategies;
using DwarfFortress.GameLogic.Tests;
using DwarfFortress.GameLogic.Tests.Fakes;
using DwarfFortress.GameLogic.World;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase5Tests;

public sealed class MineTileStrategyTests
{
    [Fact]
    public void OnComplete_Tree_Uses_Same_Cleared_Terrain_Rules_As_Chopping()
    {
        var (sim, map, items) = CreateSimulation("""
            [
              { "id": "granite", "displayName": "Granite", "tags": ["stone"] },
              { "id": "mud", "displayName": "Mud", "tags": ["dirt"] },
              { "id": "wood", "displayName": "Wood", "tags": ["organic"] }
            ]
            """);

        var target = new Vec3i(2, 2, 0);
        map.SetTile(target, new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = "wood",
            TreeSpeciesId = "oak",
            IsDesignated = true,
            IsPassable = false,
        });
        map.SetTile(target + new Vec3i(0, 0, 1), new TileData
        {
            TileDefId = TileDefIds.SoilWall,
            MaterialId = "mud",
            IsPassable = false,
        });

        var strategy = new MineTileStrategy();
        strategy.OnComplete(new Job(1, JobDefIds.MineTile, target), dwarfId: 1, sim.Context);

        var mined = map.GetTile(target);
        Assert.Equal(TileDefIds.Soil, mined.TileDefId);
        Assert.Equal("mud", mined.MaterialId);
        Assert.Null(mined.TreeSpeciesId);
        Assert.True(mined.IsPassable);
        Assert.False(mined.IsDesignated);
        Assert.Empty(items.GetItemsAt(target));
    }

    [Fact]
    public void OnComplete_AquiferOreTile_SpawnsOreAndStartsWaterSeep()
    {
        var ds = new InMemoryDataSource();
        TestFixtures.AddFullData(ds);
        var (sim, map, _, _, items) = TestFixtures.BuildFullSim(ds);

        var target = new Vec3i(10, 10, 1);
        map.SetTile(target, new TileData
        {
            TileDefId = TileDefIds.GraniteWall,
            MaterialId = "granite",
            OreItemDefId = "iron_ore",
            IsAquifer = true,
            IsDesignated = true,
            IsPassable = false,
        });

        var strategy = new MineTileStrategy();
        var job = new Job(1, JobDefIds.MineTile, target);
        strategy.OnComplete(job, dwarfId: 1, sim.Context);

        var mined = map.GetTile(target);
        Assert.Equal(TileDefIds.StoneFloor, mined.TileDefId);
        Assert.True(mined.IsPassable);
        Assert.False(mined.IsDesignated);
        Assert.Null(mined.OreItemDefId);
        Assert.False(mined.IsAquifer);
        Assert.Equal(FluidType.Water, mined.FluidType);
        Assert.True(mined.FluidLevel >= 3);

        var drops = items.GetItemsAt(target).ToList();
        Assert.Contains(drops, i => i.DefId == "iron_ore");
        Assert.DoesNotContain(drops, i => i.DefId == "granite_boulder");
    }

    private static (GameSimulation Sim, WorldMap Map, ItemSystem Items) CreateSimulation(string materialsJson)
    {
        var logger = new TestLogger();
        var ds = new InMemoryDataSource();
        ds.AddFile("data/ConfigBundle/materials.json", materialsJson);
        ds.AddFile("data/ConfigBundle/tiles.json", TestFixtures.CoreTilesJson);
        ds.AddFile("data/ConfigBundle/items.json", TestFixtures.CoreItemsJson);
        ds.AddFile("data/ConfigBundle/jobs.json", TestFixtures.CoreJobsJson);
        ds.AddFile("data/ConfigBundle/creatures.json", TestFixtures.CoreCreaturesJson);

        var map = new WorldMap();
        var items = new ItemSystem();
        var sim = TestFixtures.CreateSimulation(
            logger,
            ds,
            new DataManager(),
            new DwarfFortress.GameLogic.Entities.EntityRegistry(),
            map,
            items);

        map.SetDimensions(8, 8, 2);
        return (sim, map, items);
    }
}

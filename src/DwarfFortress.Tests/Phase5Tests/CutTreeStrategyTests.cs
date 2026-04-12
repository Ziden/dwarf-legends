using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Jobs.Strategies;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.Tests.Fakes;
using DwarfFortress.GameLogic.World;
using DwarfFortress.WorldGen.Ids;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase5Tests;

public sealed class CutTreeStrategyTests
{
    [Fact]
    public void GetSteps_Uses_Reachable_Adjacent_Tile_Not_Always_South()
    {
        var (sim, map, _) = CreateSimulation("""
            [
              { "id": "granite", "displayName": "Granite", "tags": ["stone"] },
              { "id": "wood", "displayName": "Wood", "tags": ["organic"] }
            ]
            """);

        var pos = new Vec3i(2, 2, 0);
        map.SetTile(pos, new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = "wood",
            TreeSpeciesId = "oak",
            IsDesignated = true,
            IsPassable = false,
        });
        map.SetTile(pos + Vec3i.South, new TileData
        {
            TileDefId = TileDefIds.GraniteWall,
            MaterialId = "granite",
            IsPassable = false,
        });
        map.SetTile(pos + Vec3i.East, new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            MaterialId = "granite",
            IsPassable = true,
        });

        var strategy = new CutTreeStrategy();
        var steps = strategy.GetSteps(new Job(1, JobDefIds.CutTree, pos), dwarfId: 0, sim.Context);

        var move = Assert.IsType<MoveToStep>(steps[0]);
        Assert.NotEqual(pos + Vec3i.South, move.Target);
        Assert.Contains(move.Target, new[] { pos + Vec3i.North, pos + Vec3i.East, pos + Vec3i.West });
    }

    [Fact]
    public void OnComplete_Falls_Back_To_Generic_Wood_When_Species_Material_Is_Missing()
    {
        var (sim, map, items) = CreateSimulation("""
            [
              { "id": "granite", "displayName": "Granite", "tags": ["stone"] },
              { "id": "wood", "displayName": "Wood", "tags": ["organic"] }
            ]
            """);

        var pos = new Vec3i(2, 2, 0);
        map.SetTile(pos, new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = "wood",
            TreeSpeciesId = "oak",
            IsPassable = false,
        });

        var strategy = new CutTreeStrategy();
        strategy.OnComplete(new Job(1, JobDefIds.CutTree, pos), dwarfId: 0, sim.Context);

        var log = items.GetAllItems().Single();
        var tile = map.GetTile(pos);

        Assert.Equal("wood", log.MaterialId);
        Assert.Equal(TileDefIds.StoneFloor, tile.TileDefId);
        Assert.Equal(MaterialIds.Granite, tile.MaterialId);
        Assert.Null(tile.TreeSpeciesId);
        Assert.False(tile.IsDesignated);
        Assert.True(tile.IsPassable);
    }

    [Fact]
    public void OnComplete_Uses_SpeciesWood_Material_When_Configured()
    {
        var (sim, map, items) = CreateSimulation("""
            [
              { "id": "granite", "displayName": "Granite", "tags": ["stone"] },
              { "id": "wood", "displayName": "Wood", "tags": ["organic"] },
              { "id": "oak_wood", "displayName": "Oak Wood", "tags": ["organic"] }
            ]
            """);

        var pos = new Vec3i(3, 2, 0);
        map.SetTile(pos, new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = "wood",
            TreeSpeciesId = "oak",
            IsPassable = false,
        });

        var strategy = new CutTreeStrategy();
        strategy.OnComplete(new Job(1, JobDefIds.CutTree, pos), dwarfId: 0, sim.Context);

        var log = items.GetAllItems().Single();
        Assert.Equal("oak_wood", log.MaterialId);
    }

    [Fact]
    public void OnComplete_Uses_TreeSpecies_Content_Wood_Material_When_Configured()
    {
        var logger = new TestLogger();
        var ds = new InMemoryDataSource();
        ds.AddFile("data/ConfigBundle/materials.json", """
            [
              { "id": "granite", "displayName": "Granite", "tags": ["stone"] },
              { "id": "wood", "displayName": "Wood", "tags": ["organic"] },
              { "id": "glowwood_wood", "displayName": "Glowwood", "tags": ["organic"] }
            ]
            """);
        ds.AddFile("data/ConfigBundle/tiles.json", TestFixtures.CoreTilesJson);
        ds.AddFile("data/ConfigBundle/items.json", TestFixtures.CoreItemsJson);
        ds.AddFile("data/ConfigBundle/jobs.json", TestFixtures.CoreJobsJson);
        TestFixtures.AddCoreCreatureBundles(ds);
        ds.AddFile("data/Content/Core/tree_species/glowwood.json", """
            {
              "id": "glowwood",
              "displayName": "Glowwood",
              "woodMaterialId": "glowwood_wood"
            }
            """);

        var map = new WorldMap();
        var items = new ItemSystem();
        var sim = TestFixtures.CreateSimulation(
            logger,
            ds,
            new DataManager(),
            new EntityRegistry(),
            map,
            items);

        map.SetDimensions(8, 8, 2);

        var pos = new Vec3i(3, 2, 0);
        map.SetTile(pos, new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = "wood",
            TreeSpeciesId = "glowwood",
            IsPassable = false,
        });

        var strategy = new CutTreeStrategy();
        strategy.OnComplete(new Job(1, JobDefIds.CutTree, pos), dwarfId: 0, sim.Context);

        var log = items.GetAllItems().Single();
        Assert.Equal("glowwood_wood", log.MaterialId);
    }

    [Fact]
    public void OnComplete_Uses_ContentDefined_Log_Form_Item_When_Configured()
    {
        var logger = new TestLogger();
        var ds = new InMemoryDataSource();
        ds.AddFile("data/ConfigBundle/materials.json", """
            [
              { "id": "granite", "displayName": "Granite", "tags": ["stone"] },
              { "id": "wood", "displayName": "Wood", "tags": ["organic"] },
              { "id": "glowwood_wood", "displayName": "Glowwood", "tags": ["organic"] }
            ]
            """);
        ds.AddFile("data/ConfigBundle/tiles.json", TestFixtures.CoreTilesJson);
        ds.AddFile("data/ConfigBundle/items.json", TestFixtures.CoreItemsJson);
        ds.AddFile("data/ConfigBundle/jobs.json", TestFixtures.CoreJobsJson);
        TestFixtures.AddCoreCreatureBundles(ds);
        ds.AddFile("data/Content/Core/tree_species/glowwood.json", """
            {
              "id": "glowwood",
              "displayName": "Glowwood",
              "woodMaterialId": "glowwood_wood"
            }
            """);
        ds.AddFile("data/Content/Core/materials/glowwood_wood.json", """
            {
              "id": "glowwood_wood",
              "displayName": "Glowwood",
              "tags": ["wood", "organic"],
              "forms": [
                {
                  "role": "log",
                  "item": {
                    "id": "glowwood_log",
                    "displayName": "Glowwood Log",
                    "tags": ["wood", "log"],
                    "weight": 12.0
                  }
                }
              ]
            }
            """);

        var map = new WorldMap();
        var items = new ItemSystem();
        var sim = TestFixtures.CreateSimulation(
            logger,
            ds,
            new DataManager(),
            new EntityRegistry(),
            map,
            items);

        map.SetDimensions(8, 8, 2);

        var pos = new Vec3i(3, 2, 0);
        map.SetTile(pos, new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = "wood",
            TreeSpeciesId = "glowwood",
            IsPassable = false,
        });

        var strategy = new CutTreeStrategy();
        strategy.OnComplete(new Job(1, JobDefIds.CutTree, pos), dwarfId: 0, sim.Context);

        var log = items.GetAllItems().Single();
        Assert.Equal("glowwood_log", log.DefId);
        Assert.Equal("glowwood_wood", log.MaterialId);
    }

    [Fact]
    public void OnComplete_Prefers_Nearby_Ground_Material_For_Cleared_Tile()
    {
        var (sim, map, items) = CreateSimulation("""
            [
              { "id": "granite", "displayName": "Granite", "tags": ["stone"] },
              { "id": "limestone", "displayName": "Limestone", "tags": ["stone"] },
              { "id": "wood", "displayName": "Wood", "tags": ["organic"] }
            ]
            """);

        var pos = new Vec3i(2, 2, 0);
        map.SetTile(pos, new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = "wood",
            TreeSpeciesId = "oak",
            IsPassable = false,
        });
        map.SetTile(pos + Vec3i.East, new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            MaterialId = "limestone",
            IsPassable = true,
        });

        var strategy = new CutTreeStrategy();
        strategy.OnComplete(new Job(1, JobDefIds.CutTree, pos), dwarfId: 0, sim.Context);

        var tile = map.GetTile(pos);
        Assert.Equal("limestone", tile.MaterialId);
        Assert.Equal(TileDefIds.StoneFloor, tile.TileDefId);
    }

    [Fact]
    public void OnComplete_Uses_Soil_Tile_When_Nearby_Ground_Is_Dirt()
    {
        var (sim, map, items) = CreateSimulation("""
            [
              { "id": "granite", "displayName": "Granite", "tags": ["stone"] },
              { "id": "mud", "displayName": "Mud", "tags": ["dirt"] },
              { "id": "wood", "displayName": "Wood", "tags": ["organic"] }
            ]
            """);

        var pos = new Vec3i(2, 2, 0);
        map.SetTile(pos, new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = "wood",
            TreeSpeciesId = "oak",
            IsPassable = false,
        });
        map.SetTile(pos + Vec3i.West, new TileData
        {
            TileDefId = TileDefIds.Soil,
            MaterialId = "mud",
            IsPassable = true,
        });

        var strategy = new CutTreeStrategy();
        strategy.OnComplete(new Job(1, JobDefIds.CutTree, pos), dwarfId: 0, sim.Context);

        var tile = map.GetTile(pos);
        Assert.Equal(TileDefIds.Soil, tile.TileDefId);
        Assert.Equal("mud", tile.MaterialId);
    }

    [Fact]
    public void OnComplete_Prefers_Below_Layer_Terrain_For_Cleared_Ground()
    {
        var (sim, map, items) = CreateSimulation("""
            [
              { "id": "granite", "displayName": "Granite", "tags": ["stone"] },
              { "id": "mud", "displayName": "Mud", "tags": ["dirt"] },
              { "id": "wood", "displayName": "Wood", "tags": ["organic"] }
            ]
            """);

        var pos = new Vec3i(2, 2, 0);
        map.SetTile(pos, new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = "wood",
            TreeSpeciesId = "oak",
            IsPassable = false,
            IsDesignated = true,
        });
        map.SetTile(pos + new Vec3i(0, 0, 1), new TileData
        {
            TileDefId = TileDefIds.SoilWall,
            MaterialId = "mud",
            IsPassable = false,
        });
        map.SetTile(pos + Vec3i.East, new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            MaterialId = "granite",
            IsPassable = true,
        });

        var strategy = new CutTreeStrategy();
        strategy.OnComplete(new Job(1, JobDefIds.CutTree, pos), dwarfId: 0, sim.Context);

        var tile = map.GetTile(pos);
        Assert.Equal(TileDefIds.Soil, tile.TileDefId);
        Assert.Equal("mud", tile.MaterialId);
        Assert.False(tile.IsDesignated);
    }

    [Fact]
    public void OnComplete_Drops_Ripe_Fruit_For_FruitTrees_And_Clears_Canopy_State_Immediately()
    {
        var (sim, map, _, _, items) = TestFixtures.BuildFullSim();
        var data = sim.Context.Get<DataManager>();
        var expectedLogMaterialId = data.ContentQueries!.ResolveTreeWoodMaterialId(TreeSpeciesIds.Apple);
        var appleCanopy = data.Plants.GetOrNull(PlantSpeciesIds.AppleCanopy);
        var figCanopy = data.Plants.GetOrNull(PlantSpeciesIds.FigCanopy);

        Assert.NotNull(appleCanopy);
        Assert.NotNull(figCanopy);
        Assert.True(appleCanopy!.DropYieldOnHostRemoval);
        Assert.True(figCanopy!.DropYieldOnHostRemoval);
        Assert.False(string.IsNullOrWhiteSpace(expectedLogMaterialId));

        var pos = new Vec3i(Math.Min(map.Width - 3, 24), Math.Min(map.Height - 3, 24), 0);
        var existingItemIds = items.GetAllItems().Select(item => item.Id).ToHashSet();

        var tile = map.GetTile(pos);
        tile.TileDefId = TileDefIds.Tree;
        tile.MaterialId = MaterialIds.Wood;
        tile.TreeSpeciesId = TreeSpeciesIds.Apple;
        tile.PlantDefId = PlantSpeciesIds.AppleCanopy;
        tile.PlantGrowthStage = PlantGrowthStages.Mature;
        tile.PlantGrowthProgressSeconds = 0f;
        tile.PlantYieldLevel = 1;
        tile.PlantSeedLevel = 1;
        tile.IsPassable = false;
        tile.IsDesignated = true;
        map.SetTile(pos, tile);

        var strategy = new CutTreeStrategy();
        strategy.OnComplete(new Job(1, JobDefIds.CutTree, pos), dwarfId: 0, sim.Context);

        var newItems = items.GetAllItems()
            .Where(item => !existingItemIds.Contains(item.Id) && item.Position.Position == pos)
            .ToArray();

        Assert.Single(newItems.Where(item => item.DefId == ItemDefIds.Apple));
        Assert.Single(newItems.Where(item => item.MaterialId == expectedLogMaterialId));

        var clearedTile = map.GetTile(pos);
        Assert.Null(clearedTile.TreeSpeciesId);
        Assert.Null(clearedTile.PlantDefId);
        Assert.Equal(0, clearedTile.PlantGrowthStage);
        Assert.Equal(0f, clearedTile.PlantGrowthProgressSeconds);
        Assert.Equal(0, clearedTile.PlantYieldLevel);
        Assert.Equal(0, clearedTile.PlantSeedLevel);
        Assert.False(clearedTile.IsDesignated);
        Assert.True(clearedTile.IsPassable);
    }

    [Fact]
    public void OnComplete_Does_Not_Drop_Unripe_Fruit_When_Felling_FruitTree()
    {
        var (sim, map, _, _, items) = TestFixtures.BuildFullSim();
        var data = sim.Context.Get<DataManager>();
        var expectedLogMaterialId = data.ContentQueries!.ResolveTreeWoodMaterialId(TreeSpeciesIds.Apple);
        var pos = new Vec3i(Math.Min(map.Width - 4, 25), Math.Min(map.Height - 4, 25), 0);
        var existingItemIds = items.GetAllItems().Select(item => item.Id).ToHashSet();

        var tile = map.GetTile(pos);
        tile.TileDefId = TileDefIds.Tree;
        tile.MaterialId = MaterialIds.Wood;
        tile.TreeSpeciesId = TreeSpeciesIds.Apple;
        tile.PlantDefId = PlantSpeciesIds.AppleCanopy;
        tile.PlantGrowthStage = PlantGrowthStages.Mature;
        tile.PlantGrowthProgressSeconds = 0f;
        tile.PlantYieldLevel = 0;
        tile.PlantSeedLevel = 0;
        tile.IsPassable = false;
        tile.IsDesignated = true;
        map.SetTile(pos, tile);

        var strategy = new CutTreeStrategy();
        strategy.OnComplete(new Job(1, JobDefIds.CutTree, pos), dwarfId: 0, sim.Context);

        var newItems = items.GetAllItems()
            .Where(item => !existingItemIds.Contains(item.Id) && item.Position.Position == pos)
            .ToArray();

        Assert.DoesNotContain(newItems, item => item.DefId == ItemDefIds.Apple);
        Assert.Single(newItems.Where(item => item.MaterialId == expectedLogMaterialId));

        var clearedTile = map.GetTile(pos);
        Assert.Null(clearedTile.PlantDefId);
        Assert.Equal(0, clearedTile.PlantYieldLevel);
        Assert.Equal(0, clearedTile.PlantSeedLevel);
    }

    private static (GameSimulation Sim, WorldMap Map, ItemSystem Items) CreateSimulation(string materialsJson)
    {
        var logger = new TestLogger();
        var ds = new InMemoryDataSource();
        ds.AddFile("data/ConfigBundle/materials.json", materialsJson);
        ds.AddFile("data/ConfigBundle/tiles.json", TestFixtures.CoreTilesJson);
        ds.AddFile("data/ConfigBundle/items.json", TestFixtures.CoreItemsJson);
        ds.AddFile("data/ConfigBundle/jobs.json", TestFixtures.CoreJobsJson);
        TestFixtures.AddCoreCreatureBundles(ds);

        var map = new WorldMap();
        var items = new ItemSystem();
        var sim = TestFixtures.CreateSimulation(
            logger,
            ds,
            new DataManager(),
            new EntityRegistry(),
            map,
            items);

        map.SetDimensions(8, 8, 2);
        return (sim, map, items);
    }
}

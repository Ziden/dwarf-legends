using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.Tests.Fakes;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Tests;

/// <summary>
/// Shared factory for test GameContext and simulation instances.
/// Keeps test setup code minimal and consistent.
/// </summary>
public static class TestFixtures
{
    public static (GameContext ctx, TestLogger logger, InMemoryDataSource ds) CreateContext()
    {
        var logger   = new TestLogger();
        var ds       = new InMemoryDataSource();
        var bus      = new EventBus();
        var commands = new CommandDispatcher(logger);
        var ctx      = new GameContext(bus, commands, logger, ds);
        return (ctx, logger, ds);
    }

    public static GameSimulation CreateSimulation(
        TestLogger logger,
        InMemoryDataSource ds,
        params IGameSystem[] systems)
    {
        var sim = new GameSimulation(logger, ds);
        foreach (var sys in systems)
            sim.RegisterSystem(sys);
        sim.Initialize();
        return sim;
    }

    /// <summary>Minimal JSON for a single material.</summary>
    public static string GraniteMaterialJson => """
        [{ "id": "granite", "displayName": "Granite", "tags": ["stone", "hard"], "hardness": 8.0 }]
        """;

    /// <summary>Minimal JSON for core tiles.</summary>
    public static string CoreTilesJson => """
        [
          { "id": "empty",       "displayName": "Empty",         "isPassable": true,  "isOpaque": false },
          { "id": "stone_floor", "displayName": "Stone Floor",   "isPassable": true,  "isOpaque": false },
                    { "id": "stone_wall",  "displayName": "Stone Wall",    "isPassable": false, "isOpaque": true,  "isMineable": true },
          { "id": "tree",        "displayName": "Tree",          "isPassable": false, "isOpaque": false }
        ]
        """;

    /// <summary>Minimal JSON for core items.</summary>
    public static string CoreItemsJson => """
        [
          { "id": "granite_boulder", "displayName": "Granite Boulder", "tags": ["stone", "boulder"], "weight": 20.0 },
          { "id": "log",             "displayName": "Log",             "tags": ["wood", "log"],     "weight": 10.0 },
          { "id": "meal",            "displayName": "Meal",            "tags": ["food"],            "weight": 1.0  },
                    { "id": "drink",           "displayName": "Drink",           "tags": ["drink"],           "weight": 0.5  },
                    { "id": "corpse",          "displayName": "Corpse",          "tags": ["corpse", "refuse", "container"], "weight": 35.0 }
        ]
        """;

    /// <summary>Minimal JSON for core jobs.</summary>
    public static string CoreJobsJson => """
        [
                    { "id": "engage_hostile", "displayName": "Engage Hostile", "labor": "misc",        "workTime": 1.0 },
          { "id": "mine_tile",  "displayName": "Mine",     "labor": "mining",      "workTime": 5.0 },
          { "id": "cut_tree",   "displayName": "Cut Tree", "labor": "wood_cutting","workTime": 6.0 },
          { "id": "haul_item",  "displayName": "Haul",     "labor": "hauling",     "workTime": 1.0 }
        ]
        """;

    public static string CoreDwarfCreatureBundleJson => """
        {
          "id": "dwarf",
          "displayName": "Dwarf",
          "tags": ["sapient", "playable"],
          "isPlayable": true,
          "isSapient": true,
          "maxHealth": 100,
          "society": {
            "factionRoles": [
              { "id": "civilized_primary", "weight": 1.0 }
            ]
          }
        }
        """;

    public static void AddCoreCreatureBundles(InMemoryDataSource ds)
    {
        ds.AddFile("data/Content/Game/creatures/sapients/dwarf/creature.json", CoreDwarfCreatureBundleJson);
    }

    public static void AddFullCreatureBundles(InMemoryDataSource ds)
    {
        AddCoreCreatureBundles(ds);
        ds.AddFile("data/Content/Game/creatures/hostile/goblin/creature.json", """
            {
              "id": "goblin",
              "displayName": "Goblin",
              "tags": ["hostile", "carnivore"],
              "isHostile": true,
              "diet": "carnivore",
              "maxHealth": 60,
              "society": {
                "factionRoles": [
                  { "id": "hostile_primary", "weight": 1.0 }
                ]
              }
            }
            """);
        ds.AddFile("data/Content/Game/creatures/wildlife/elk/creature.json", """
            {
              "id": "elk",
              "displayName": "Elk",
              "tags": ["animal", "grazer", "herbivore"],
              "diet": "herbivore",
              "maxHealth": 85
            }
            """);
        ds.AddFile("data/Content/Game/creatures/pets/cat/creature.json", """
            {
              "id": "cat",
              "displayName": "Cat",
              "tags": ["animal", "pet", "groomer", "carnivore"],
              "canGroom": true,
              "diet": "carnivore",
              "maxHealth": 20
            }
            """);
        ds.AddFile("data/Content/Game/creatures/pets/dog/creature.json", """
            {
              "id": "dog",
              "displayName": "Dog",
              "tags": ["animal", "pet", "groomer", "omnivore"],
              "canGroom": true,
              "diet": "omnivore",
              "maxHealth": 30
            }
            """);
        ds.AddFile("data/Content/Game/creatures/aquatic/giant_carp/creature.json", """
            {
              "id": "giant_carp",
              "displayName": "Giant Carp",
              "tags": ["animal", "aquatic", "fish", "aquatic_grazer"],
              "diet": "aquatic_grazer",
              "movementMode": "aquatic",
              "maxHealth": 55
            }
            """);
    }

    /// <summary>Adds all minimal data files to the data source.</summary>
    public static void AddCoreData(InMemoryDataSource ds)
    {
        ds.AddFile("data/ConfigBundle/materials.json", GraniteMaterialJson);
        ds.AddFile("data/ConfigBundle/tiles.json",      CoreTilesJson);
        ds.AddFile("data/ConfigBundle/items.json",      CoreItemsJson);
        ds.AddFile("data/ConfigBundle/jobs.json",       CoreJobsJson);
        AddCoreCreatureBundles(ds);
    }

    /// <summary>
    /// Full data set matching what the game actually needs — use for integration tests.
    /// Includes recipes, reactions, world_events, and extended creature/item defs.
    /// </summary>
    public static void AddFullData(InMemoryDataSource ds)
    {
        ds.AddFile("data/ConfigBundle/materials.json", """
            [
              { "id": "granite",   "displayName": "Granite",   "tags": ["stone","hard"], "hardness": 8.0 },
              { "id": "limestone", "displayName": "Limestone", "tags": ["stone"],        "hardness": 5.0 },
              { "id": "wood",      "displayName": "Wood",      "tags": ["organic"],      "hardness": 2.0 },
              { "id": "food",      "displayName": "Food",      "tags": ["organic"],      "hardness": 0.1 },
              { "id": "drink",     "displayName": "Drink",     "tags": ["liquid"],       "hardness": 0.1 }
            ]
            """);

        ds.AddFile("data/ConfigBundle/tiles.json", """
            [
              { "id": "empty",        "displayName": "Empty",        "isPassable": true,  "isOpaque": false },
              { "id": "stone_floor",  "displayName": "Stone Floor",  "isPassable": true,  "isOpaque": false },
                            { "id": "stone_wall",   "displayName": "Stone Wall",   "isPassable": false, "isOpaque": true,  "isMineable": true },
              { "id": "soil",         "displayName": "Soil",         "isPassable": true,  "isOpaque": false },
              { "id": "tree",         "displayName": "Tree",         "isPassable": false, "isOpaque": false },
              { "id": "staircase",    "displayName": "Staircase",    "isPassable": true,  "isOpaque": false },
              { "id": "water_tile",   "displayName": "Water",        "isPassable": true,  "isOpaque": false }
            ]
            """);

        ds.AddFile("data/ConfigBundle/items.json", """
            [
              { "id": "granite_boulder", "displayName": "Granite Boulder", "tags": ["stone","boulder"], "weight": 20.0 },
              { "id": "log",             "displayName": "Log",             "tags": ["wood","log"],      "weight": 10.0 },
              { "id": "meal",            "displayName": "Meal",            "tags": ["food"],            "weight": 1.0  },
              { "id": "drink",           "displayName": "Drink",           "tags": ["drink"],           "weight": 0.5  },
                            { "id": "corpse",          "displayName": "Corpse",          "tags": ["corpse","refuse","container"], "weight": 35.0 },
              { "id": "plank",           "displayName": "Plank",           "tags": ["wood","plank"],    "weight": 5.0  },
              { "id": "iron_ore",        "displayName": "Iron Ore",        "tags": ["ore","stone"],     "weight": 15.0 }
            ]
            """);

        ds.AddFile("data/ConfigBundle/jobs.json", """
            [
                            { "id": "engage_hostile",     "displayName": "Engage Hostile", "labor": "misc",         "workTime": 1.0  },
              { "id": "mine_tile",        "displayName": "Mine",      "labor": "mining",       "workTime": 5.0  },
              { "id": "cut_tree",         "displayName": "Cut Tree",  "labor": "wood_cutting", "workTime": 6.0  },
              { "id": "haul_item",        "displayName": "Haul",      "labor": "hauling",      "workTime": 1.0  },
              { "id": "construct_building","displayName": "Construct", "labor": "construction","workTime": 10.0 },
              { "id": "craft_item",       "displayName": "Craft",     "labor": "crafting",     "workTime": 8.0  },
              { "id": "eat",              "displayName": "Eat",       "labor": "misc",         "workTime": 2.0  },
              { "id": "drink",            "displayName": "Drink",     "labor": "misc",         "workTime": 1.0  },
              { "id": "sleep",            "displayName": "Sleep",     "labor": "misc",         "workTime": 8.0  }
            ]
            """);

        AddFullCreatureBundles(ds);

        ds.AddFile("data/ConfigBundle/recipes.json", """
            [
              { "id": "craft_plank", "displayName": "Craft Plank",
                "workshop": "carpenter_workshop",
                "inputs":  [{ "tags": ["log"], "qty": 1 }],
                "outputs": [{ "qty": 2, "materialFrom": "tag:log", "formRole": "plank" }],
                "workTime": 8.0, "labor": "carpentry" }
            ]
            """);

        ds.AddFile("data/ConfigBundle/reactions.json",    "[]");
        ds.AddFile("data/ConfigBundle/world_events.json", "[]");
    }

    /// <summary>Build a fully wired GameBootstrapper simulation with a 32×32×8 open/wall map.
    /// When <paramref name="ds"/> is null the real game data files are loaded via
    /// <see cref="FolderDataSource"/> (same JSON the Godot client will use).
    /// Pass an explicit <see cref="InMemoryDataSource"/> for isolated unit tests.</summary>
    public static (GameSimulation sim, WorldMap map, EntityRegistry er, JobSystem js, ItemSystem items)
        BuildFullSim(InMemoryDataSource? ds = null)
    {
        var logger  = new Fakes.TestLogger();
        IDataSource source = ds ?? (IDataSource)new FolderDataSource("data");

        var sim   = GameBootstrapper.Build(logger, source);
        var map   = sim.Context.Get<WorldMap>();
        var er    = sim.Context.Get<EntityRegistry>();
        var js    = sim.Context.Get<JobSystem>();
        var items = sim.Context.Get<ItemSystem>();

        map.SetDimensions(32, 32, 8);
        for (int x = 0; x < 32; x++)
        for (int y = 0; y < 32; y++)
        {
            map.SetTile(new Vec3i(x, y, 0), new TileData
            {
                TileDefId  = TileDefIds.StoneFloor,
                MaterialId = "granite",
                IsPassable = true,
            });
            for (int z = 1; z < 8; z++)
                map.SetTile(new Vec3i(x, y, z), new TileData
                {
                    TileDefId  = TileDefIds.GraniteWall,
                    MaterialId = "granite",
                    IsPassable = false,
                });
        }

        return (sim, map, er, js, items);
    }

    public static PlacedBuildingData PlaceBuildingWithMaterials(
        GameSimulation sim,
        string buildingDefId,
        Vec3i origin,
        BuildingRotation rotation = BuildingRotation.None,
        Vec3i? materialStart = null,
        string materialId = MaterialIds.Wood,
        bool complete = true)
    {
        var data = sim.Context.Get<DataManager>();
        var items = sim.Context.Get<ItemSystem>();
        var buildings = sim.Context.Get<BuildingSystem>();
        var definition = data.Buildings.Get(buildingDefId);

        AddBuildingConstructionInputs(items, definition.ConstructionInputs, materialStart ?? new Vec3i(1, 1, 0), materialId);
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(buildingDefId, origin, rotation));

        var building = buildings.GetByOrigin(origin)
            ?? throw new InvalidOperationException($"Building '{buildingDefId}' was not placed at {origin}.");

        if (complete)
            buildings.CompleteConstruction(building.Id);

        return building;
    }

    public static void AddBuildingConstructionInputs(
        ItemSystem items,
        IReadOnlyList<RecipeInput> inputs,
        Vec3i start,
        string materialId = MaterialIds.Wood)
    {
        var offset = 0;
        foreach (var input in inputs)
        {
            var itemDefId = ResolveInputItemDefId(input);
            var inputMaterialId = ResolveInputMaterialId(input, itemDefId, materialId);
            for (var i = 0; i < input.Quantity; i++)
            {
                items.CreateItem(itemDefId, inputMaterialId, new Vec3i(start.X + offset, start.Y, start.Z));
                offset++;
            }
        }
    }

    private static string ResolveInputItemDefId(RecipeInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.ItemDefId))
            return input.ItemDefId!;

        if (input.RequiredTags.Contains(TagIds.Stone) || input.RequiredTags.Contains(TagIds.Boulder))
            return ItemDefIds.GraniteBoulder;
        if (input.RequiredTags.Contains(TagIds.Bed))
            return ItemDefIds.Bed;
        if (input.RequiredTags.Contains(TagIds.Table))
            return ItemDefIds.Table;
        if (input.RequiredTags.Contains(TagIds.Chair))
            return ItemDefIds.Chair;
        if (input.RequiredTags.Contains(TagIds.Bucket))
            return ItemDefIds.Bucket;
        if (input.RequiredTags.Contains(TagIds.Barrel))
            return ItemDefIds.Barrel;

        return ItemDefIds.Log;
    }

    private static string ResolveInputMaterialId(RecipeInput input, string itemDefId, string fallbackMaterialId)
    {
        if (!string.IsNullOrWhiteSpace(input.MaterialId))
            return input.MaterialId!;

        return itemDefId == ItemDefIds.GraniteBoulder
            ? MaterialIds.Granite
            : fallbackMaterialId;
    }
}

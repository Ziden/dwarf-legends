using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Tests.Fakes;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.WorldGen.Content;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class SharedContentIntegrationTests
{
    [Fact]
    public void DataManager_LoadsPlantBundlesAndEmbeddedItemDefs()
    {
        var logger = new TestLogger();
        var ds = new InMemoryDataSource();
        ds.AddFile("data/Content/Game/plants/glow_cap/plant.json", """
            {
              "id": "glow_cap",
              "displayName": "Glow Cap",
              "hostKind": "ground",
              "allowedBiomes": ["fungal_grove"],
              "allowedGroundTiles": ["soil", "mud"],
              "minMoisture": 0.55,
              "maxMoisture": 1.00,
              "minTerrain": 0.00,
              "maxTerrain": 0.52,
              "harvestItemDefId": "glow_cap_crop",
              "harvestItem": {
                "id": "glow_cap_crop",
                "displayName": "Glow Cap Crop",
                "tags": ["plant", "food"],
                "weight": 0.2
              },
              "seedItemDefId": "glow_cap_spore",
              "seedItem": {
                "id": "glow_cap_spore",
                "displayName": "Glow Cap Spore",
                "tags": ["plant", "seed"],
                "weight": 0.05
              }
            }
            """);

        var sim = TestFixtures.CreateSimulation(logger, ds, new DataManager());
        var data = sim.Context.Get<DataManager>();

        Assert.NotNull(data.Plants.GetOrNull("glow_cap"));
        Assert.NotNull(data.Items.GetOrNull("glow_cap_crop"));
        Assert.NotNull(data.Items.GetOrNull("glow_cap_spore"));
        Assert.Equal("glow_cap_spore", data.ContentQueries!.ResolveSeedItemDefId("glow_cap"));
    }

    [Fact]
    public void DataManager_LoadsMaterialFormItemsFromSharedContent()
    {
        var logger = new TestLogger();
        var ds = new InMemoryDataSource();
        ds.AddFile("data/ConfigBundle/materials.json", """
            [
              { "id": "glowwood_wood", "displayName": "Glowwood", "tags": ["organic"] }
            ]
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
                    "weight": 11.0
                  }
                },
                {
                  "role": "plank",
                  "item": {
                    "id": "glowwood_plank",
                    "displayName": "Glowwood Plank",
                    "tags": ["wood", "plank"],
                    "weight": 5.5
                  }
                }
              ]
            }
            """);

        var sim = TestFixtures.CreateSimulation(logger, ds, new DataManager());
        var data = sim.Context.Get<DataManager>();

        Assert.NotNull(data.Items.GetOrNull("glowwood_log"));
        Assert.NotNull(data.Items.GetOrNull("glowwood_plank"));
        Assert.Equal("glowwood_log", data.ContentQueries!.ResolveLogItemDefId("glowwood_wood"));
        Assert.Equal("glowwood_plank", data.ContentQueries.ResolvePlankItemDefId("glowwood_wood"));
    }

    [Fact]
    public void DataManager_LoadsCreatureBundlesAndPreservesCreatureFields()
    {
        var logger = new TestLogger();
        var ds = new InMemoryDataSource();
        ds.AddFile("data/ConfigBundle/items.json", """
            [
              { "id": "raw_meat", "displayName": "Raw Meat", "tags": ["meat", "food"], "weight": 0.8 },
              { "id": "bone", "displayName": "Bone", "tags": ["bone"], "weight": 1.5 }
            ]
            """);
        ds.AddFile("data/Content/Game/creatures/cavern/glow_lizard/creature.json", """
            {
              "id": "glow_lizard",
              "displayName": "Glow Lizard",
              "tags": ["animal", "carnivore"],
              "isHostile": true,
              "canGroom": true,
              "diet": "carnivore",
              "movementMode": "swimmer",
              "speed": 1.30,
              "strength": 1.10,
              "toughness": 0.80,
              "maxHealth": 45,
              "bodyParts": [
                { "id": "head", "displayName": "Head", "hitWeight": 1.4, "isVital": true },
                { "id": "tail", "displayName": "Tail", "hitWeight": 0.8, "isVital": false }
              ],
              "naturalLabors": ["hauling", "hunting"],
              "deathDrops": [
                { "itemDefId": "raw_meat", "quantity": 2 },
                { "itemDefId": "bone", "quantity": 1 }
              ]
            }
            """);

        var sim = TestFixtures.CreateSimulation(logger, ds, new DataManager());
        var data = sim.Context.Get<DataManager>();
        var creature = data.Creatures.Get("glow_lizard");

        Assert.Equal("Glow Lizard", creature.DisplayName);
        Assert.True(creature.AuthoredIsHostile);
        Assert.True(creature.AuthoredCanGroom);
        Assert.True(creature.IsGroomer());
        Assert.True(creature.IsHostile());
        Assert.Equal(CreatureDiet.Carnivore, creature.AuthoredDiet);
        Assert.Equal(CreatureMovementMode.Swimmer, creature.AuthoredMovementMode);
        Assert.Equal(1.30f, creature.BaseSpeed);
        Assert.Equal(2, creature.BodyParts!.Count);
        Assert.Equal("head", creature.BodyParts[0].Id);
        Assert.True(creature.BodyParts[0].IsVital);
        Assert.Equal(new[] { "hauling", "hunting" }, creature.NaturalLabors);
        Assert.Equal(2, creature.DeathDrops![0].Quantity);
        Assert.Equal("raw_meat", creature.DeathDrops[0].ItemDefId);
    }

    [Fact]
    public void DataManager_DoesNotLoadLegacyCreatureFile_WhenNoBundleExists()
    {
        var logger = new TestLogger();
        var ds = new InMemoryDataSource();
        ds.AddFile("data/ConfigBundle/creatures.json", """
            [
              {
                "id": "legacy_lizard",
                "displayName": "Legacy Lizard",
                "tags": ["animal"],
                "speed": 0.9,
                "maxHealth": 20
              }
            ]
            """);

        var sim = TestFixtures.CreateSimulation(logger, ds, new DataManager());
        var data = sim.Context.Get<DataManager>();

        Assert.Null(data.Creatures.GetOrNull("legacy_lizard"));
    }

    [Fact]
    public void DataManager_ResolvesDistinctWoodMaterialsForShippedTreeSpecies()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();
        var data = sim.Context.Get<DataManager>();

        Assert.Equal("oak_wood", data.ContentQueries!.ResolveTreeWoodMaterialId("oak"));
        Assert.Equal("pine_wood", data.ContentQueries.ResolveTreeWoodMaterialId("pine"));
        Assert.NotNull(data.Materials.GetOrNull("oak_wood"));
        Assert.NotNull(data.Materials.GetOrNull("pine_wood"));
        Assert.Equal("log", data.ContentQueries.ResolveLogItemDefId("oak_wood"));
        Assert.Equal("plank", data.ContentQueries.ResolvePlankItemDefId("pine_wood"));
    }

    [Fact]
    public void RecipeOutputQuery_ResolvesDerivedOutputsForShippedRecipes()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();
        var data = sim.Context.Get<DataManager>();

        var plankRecipe = data.Recipes.Get("make_plank");
        var ironBarRecipe = data.Recipes.Get("make_iron_bar");

        Assert.Contains("plank", RecipeOutputQuery.ResolveItemDefIds(data, plankRecipe));
        Assert.Contains("iron_bar", RecipeOutputQuery.ResolveItemDefIds(data, ironBarRecipe));
    }

    [Fact]
    public void BuildFullSim_LoadsShippedCreatureBundlesThroughSharedContent()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();
        var data = sim.Context.Get<DataManager>();

        Assert.Equal(ContentRoots.Game, data.SharedContent!.Creatures["dwarf"].SourceRoot);
        Assert.True(data.Creatures.Contains("dwarf"));
        Assert.True(data.Creatures.Contains("goblin"));
    }
}

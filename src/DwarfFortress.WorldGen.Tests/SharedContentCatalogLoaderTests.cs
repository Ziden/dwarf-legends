using DwarfFortress.WorldGen.Content;

namespace DwarfFortress.WorldGen.Tests;

public sealed class SharedContentCatalogLoaderTests
{
    [Fact]
    public void Load_RecursivelyDiscoversBundlePlantsAndBuildsEmbeddedItems()
    {
        var source = new MemoryContentFileSource();
        source.AddFile("data/Content/Core/tree_species/glowwood.json", """
            { "id": "glowwood", "displayName": "Glowwood" }
            """);
        source.AddFile("data/Content/Game/plants/fungal/glow_cap/plant.json", """
            {
              "id": "glow_cap",
              "displayName": "Glow Cap",
              "hostKind": "ground",
              "allowedBiomes": ["fungal_grove"],
              "allowedGroundTiles": ["soil", "mud"],
              "minMoisture": 0.50,
              "maxMoisture": 1.00,
              "minTerrain": 0.00,
              "maxTerrain": 0.60,
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

        var catalog = SharedContentCatalogLoader.Load(source);

        Assert.Contains("glow_cap", catalog.Plants.Keys);
        Assert.Contains("glow_cap_crop", catalog.Items.Keys);
        Assert.Contains("glow_cap_spore", catalog.Items.Keys);
        Assert.Contains("glowwood", catalog.TreeSpecies.Keys);
    }

    [Fact]
    public void Load_RecursivelyDiscoversBundleCreaturesAndPreservesCreatureFields()
    {
        var source = new MemoryContentFileSource();
        source.AddFile("data/ConfigBundle/items.json", """
            [
              { "id": "raw_meat", "displayName": "Raw Meat", "tags": ["meat", "food"], "weight": 0.8 },
              { "id": "bone", "displayName": "Bone", "tags": ["bone"], "weight": 1.5 }
            ]
            """);
        source.AddFile("data/Content/Game/creatures/cavern/glow_lizard/creature.json", """
            {
              "id": "glow_lizard",
              "displayName": "Glow Lizard",
              "tags": ["animal", "carnivore"],
              "isHostile": true,
              "canGroom": true,
              "diet": "carnivore",
              "movementMode": "swimmer",
              "speed": 1.3,
              "strength": 1.1,
              "toughness": 0.8,
              "maxHealth": 45,
              "visuals": {
                "proceduralProfile": "goblin",
                "waterEffectStyle": "aquatic",
                "viewerColor": "#44AA66"
              },
              "bodyParts": [
                { "id": "head", "displayName": "Head", "hitWeight": 1.4, "isVital": true },
                { "id": "tail", "displayName": "Tail", "hitWeight": 0.8, "isVital": false }
              ],
              "naturalLabors": ["hauling", "hunting"],
              "ecology": {
                "surfaceWildlife": [
                  { "biomes": ["fungal_grove"], "weight": 0.8, "minGroup": 1, "maxGroup": 2 }
                ],
                "caveWildlife": [
                  { "layers": [2, 3], "weight": 0.4, "minGroup": 1, "maxGroup": 1 }
                ]
              },
              "history": {
                "figureNamePool": ["Skrit", "Narak"],
                "defaultProfessionIds": ["militia"],
                "professionRules": [
                  { "siteKindContains": "cave", "memberIndex": 0, "professionIds": ["militia"] }
                ]
              },
              "deathDrops": [
                { "itemDefId": "raw_meat", "quantity": 2 },
                { "itemDefId": "bone", "quantity": 1 }
              ],
              "society": {
                "factionRoles": [
                  { "id": "hostile_primary", "weight": 0.7 },
                  { "id": "hostile_alternate", "weight": 0.3 }
                ]
              }
            }
            """);

        var catalog = SharedContentCatalogLoader.Load(source);

        Assert.Contains("glow_lizard", catalog.Creatures.Keys);
        var creature = catalog.Creatures["glow_lizard"];

        Assert.Equal("Glow Lizard", creature!.DisplayName);
        Assert.True(creature.IsHostile);
        Assert.True(creature.CanGroom);
        Assert.Equal(ContentCreatureDietIds.Carnivore, creature.DietId);
        Assert.Equal(ContentCreatureMovementModeIds.Swimmer, creature.MovementModeId);
        Assert.Equal(ContentCreatureVisualProfileIds.Goblin, creature.Visuals!.ProceduralProfileId);
        Assert.Equal(ContentCreatureWaterEffectStyleIds.Aquatic, creature.Visuals.WaterEffectStyleId);
        Assert.Equal("#44AA66", creature.Visuals.ViewerColor);
        Assert.Equal(2, creature.BodyParts!.Count);
        Assert.Equal("head", creature.BodyParts[0].Id);
        Assert.True(creature.BodyParts[0].IsVital);
        Assert.Equal(new[] { "hauling", "hunting" }, creature.NaturalLabors);
        Assert.Single(creature.Ecology!.SurfaceWildlife!);
        Assert.Equal("fungal_grove", creature.Ecology.SurfaceWildlife![0].BiomeIds[0]);
        Assert.Single(creature.Ecology.CaveWildlife!);
        Assert.Contains(2, creature.Ecology.CaveWildlife![0].Layers);
        Assert.Equal(new[] { "Skrit", "Narak" }, creature.History!.FigureNamePool);
        Assert.Equal(new[] { "militia" }, creature.History.DefaultProfessionIds);
        Assert.Equal("cave", creature.History.ProfessionRules![0].SiteKindContains);
        Assert.Equal(2, creature.DeathDrops![0].Quantity);
        Assert.Equal("raw_meat", creature.DeathDrops[0].ItemDefId);
        Assert.Equal("hostile_primary", creature.Society!.FactionRoles![0].Id);
        Assert.Equal(0.7f, creature.Society.FactionRoles[0].Weight);
        Assert.Equal(ContentRoots.Game, creature.SourceRoot);
    }

    [Fact]
    public void Load_GamePlantOverridesLegacyPlantAndReportsShadow()
    {
        var source = new MemoryContentFileSource();
        source.AddFile("data/ConfigBundle/plants.json", """
            [
              {
                "id": "sunmoss",
                "displayName": "Legacy Sunmoss",
                "hostKind": "ground",
                "allowedBiomes": ["cavern"],
                "allowedGroundTiles": ["soil"]
              }
            ]
            """);
        source.AddFile("data/Content/Game/plants/sunmoss/plant.json", """
            {
              "id": "sunmoss",
              "displayName": "Source Sunmoss",
              "hostKind": "ground",
              "allowedBiomes": ["fungal_grove"],
              "allowedGroundTiles": ["mud"]
            }
            """);

        var catalog = SharedContentCatalogLoader.Load(source);

        Assert.Equal("Source Sunmoss", catalog.Plants["sunmoss"].DisplayName);
        Assert.Contains(catalog.Report.ShadowedEntries, shadow => shadow.Family == ContentFamilies.Plants && shadow.Id == "sunmoss");
    }

    [Fact]
    public void Load_GameCreatureOverridesCoreCreatureAndReportsShadow()
    {
        var source = new MemoryContentFileSource();
        source.AddFile("data/Content/Core/creatures/glow_lizard/creature.json", """
            {
              "id": "glow_lizard",
              "displayName": "Core Glow Lizard",
              "tags": ["animal"],
              "maxHealth": 30
            }
            """);
        source.AddFile("data/Content/Game/creatures/glow_lizard/creature.json", """
            {
              "id": "glow_lizard",
              "displayName": "Source Glow Lizard",
              "tags": ["animal", "carnivore"],
              "maxHealth": 45
            }
            """);

        var catalog = SharedContentCatalogLoader.Load(source);

        Assert.Equal("Source Glow Lizard", catalog.Creatures["glow_lizard"].DisplayName);
        Assert.Contains(catalog.Report.ShadowedEntries, shadow => shadow.Family == ContentFamilies.Creatures && shadow.Id == "glow_lizard");
    }

    [Fact]
    public void Load_RejectsPlantsReferencingUnknownTreeSpecies()
    {
        var source = new MemoryContentFileSource();
        source.AddFile("data/Content/Game/plants/hanging_orchard/plant.json", """
            {
              "id": "hanging_orchard",
              "displayName": "Hanging Orchard",
              "hostKind": "tree",
              "supportedTreeSpecies": ["missing_species"]
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => SharedContentCatalogLoader.Load(source));
        Assert.Contains("missing_species", ex.Message);
    }

    [Fact]
    public void Load_RejectsTreeSpeciesReferencingUnknownWoodMaterial()
    {
        var source = new MemoryContentFileSource();
        source.AddFile("data/Content/Core/tree_species/crystalwood.json", """
            {
              "id": "crystalwood",
              "displayName": "Crystalwood",
              "woodMaterialId": "crystalwood_wood"
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => SharedContentCatalogLoader.Load(source));
        Assert.Contains("crystalwood_wood", ex.Message);
    }

    [Fact]
    public void Load_RejectsCreaturesWithDuplicateBodyParts()
    {
        var source = new MemoryContentFileSource();
        source.AddFile("data/Content/Game/creatures/twoheaded/creature.json", """
            {
              "id": "twoheaded_lizard",
              "displayName": "Twoheaded Lizard",
              "bodyParts": [
                { "id": "head", "displayName": "Head", "hitWeight": 1.0, "isVital": true },
                { "id": "head", "displayName": "Other Head", "hitWeight": 1.0, "isVital": true }
              ]
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => SharedContentCatalogLoader.Load(source));
        Assert.Contains("duplicate body part", ex.Message);
    }

    [Fact]
    public void Load_RejectsCreaturesWithBlankHistoryNames()
    {
        var source = new MemoryContentFileSource();
        source.AddFile("data/Content/Game/creatures/twoheaded/creature.json", """
            {
              "id": "story_lizard",
              "displayName": "Story Lizard",
              "history": {
                "figureNamePool": ["Skrit", ""]
              }
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => SharedContentCatalogLoader.Load(source));
        Assert.Contains("history figure name pool", ex.Message);
    }

    [Fact]
    public void Load_RejectsCreaturesWithInvalidSocietyRoleWeight()
    {
        var source = new MemoryContentFileSource();
        source.AddFile("data/Content/Game/creatures/story_lizard/creature.json", """
            {
              "id": "story_lizard",
              "displayName": "Story Lizard",
              "society": {
                "factionRoles": [
                  { "id": "hostile_primary", "weight": 0.0 }
                ]
              }
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => SharedContentCatalogLoader.Load(source));
        Assert.Contains("non-positive society faction role weight", ex.Message);
    }

    [Fact]
    public void Load_RejectsCreaturesWithBlankSocietyRoleId()
    {
        var source = new MemoryContentFileSource();
        source.AddFile("data/Content/Game/creatures/story_lizard/creature.json", """
            {
              "id": "story_lizard",
              "displayName": "Story Lizard",
              "society": {
                "factionRoles": [
                  { "id": "", "weight": 1.0 }
                ]
              }
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => SharedContentCatalogLoader.Load(source));
        Assert.Contains("society faction role with an empty id", ex.Message);
    }

    [Fact]
    public void Load_RejectsCreaturesReferencingUnknownDeathDropItems()
    {
        var source = new MemoryContentFileSource();
        source.AddFile("data/Content/Game/creatures/story_lizard/creature.json", """
            {
              "id": "story_lizard",
              "displayName": "Story Lizard",
              "deathDrops": [
                { "itemDefId": "missing_drop", "quantity": 1 }
              ]
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => SharedContentCatalogLoader.Load(source));
        Assert.Contains("unknown death drop item", ex.Message);
    }

    [Fact]
    public void Load_RejectsCreaturesWithUnknownDiet()
    {
        var source = new MemoryContentFileSource();
        source.AddFile("data/Content/Game/creatures/story_lizard/creature.json", """
            {
              "id": "story_lizard",
              "displayName": "Story Lizard",
              "diet": "sunlight"
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => SharedContentCatalogLoader.Load(source));
        Assert.Contains("unknown diet", ex.Message);
    }

    [Fact]
    public void Load_RejectsCreaturesWithUnknownMovementMode()
    {
        var source = new MemoryContentFileSource();
        source.AddFile("data/Content/Game/creatures/story_lizard/creature.json", """
            {
              "id": "story_lizard",
              "displayName": "Story Lizard",
              "movementMode": "teleport"
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => SharedContentCatalogLoader.Load(source));
        Assert.Contains("unknown movement mode", ex.Message);
    }

    [Fact]
    public void Load_BuildsLegacyWoodFormsForWoodMaterials()
    {
        var source = new MemoryContentFileSource();
        source.AddFile("data/ConfigBundle/materials.json", """
            [
              { "id": "wood", "displayName": "Wood", "tags": ["organic"] },
              { "id": "oak_wood", "displayName": "Oak Wood", "tags": ["organic"] }
            ]
            """);

        var catalog = SharedContentCatalogLoader.Load(source);
        var queries = new ContentQueryService(catalog);

        Assert.Equal("log", queries.ResolveLogItemDefId("wood"));
        Assert.Equal("plank", queries.ResolvePlankItemDefId("wood"));
        Assert.Equal("log", queries.ResolveLogItemDefId("oak_wood"));
        Assert.Equal("plank", queries.ResolvePlankItemDefId("oak_wood"));
        Assert.Contains("wood", queries.ResolveMaterialIdsForFormItemDefId("log", ContentFormRoles.Log));
        Assert.Contains("oak_wood", queries.ResolveMaterialIdsForFormItemDefId("log", ContentFormRoles.Log));
    }

    [Fact]
    public void Load_ResolvesGenericWoodFormsForContentWoodMaterialsWithoutExplicitForms()
    {
        var source = new MemoryContentFileSource();
        source.AddFile("data/ConfigBundle/items.json", """
            [
              { "id": "log", "displayName": "Log", "tags": ["wood", "log"], "weight": 10.0 },
              { "id": "plank", "displayName": "Plank", "tags": ["wood", "plank"], "weight": 5.0 }
            ]
            """);
        source.AddFile("data/Content/Core/materials/cedar_wood.json", """
            {
              "id": "cedar_wood",
              "displayName": "Cedar Wood",
              "tags": ["wood", "organic", "conifer"]
            }
            """);

        var catalog = SharedContentCatalogLoader.Load(source);
        var queries = new ContentQueryService(catalog);

        Assert.Equal("log", queries.ResolveLogItemDefId("cedar_wood"));
        Assert.Equal("plank", queries.ResolvePlankItemDefId("cedar_wood"));
        Assert.Contains("cedar_wood", queries.ResolveMaterialIdsForFormItemDefId("log", ContentFormRoles.Log));
        Assert.Contains("cedar_wood", queries.ResolveMaterialIdsForFormItemDefId("plank", ContentFormRoles.Plank));
    }
}

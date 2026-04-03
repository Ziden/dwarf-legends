using DwarfFortress.WorldGen.Config;
using DwarfFortress.WorldGen.Content;
using DwarfFortress.WorldGen.Creatures;
using DwarfFortress.WorldGen.Geology;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Story;
using System.Linq;

namespace DwarfFortress.WorldGen.Tests;

public sealed class WorldGenContentConfigLoaderTests
{
    [Fact]
    public void Catalog_FromCustomConfig_SupportsNewBiomeTreeRockOreAndCreatureIds()
    {
        const string json =
            """
            {
              "geologyProfiles": [
                {
                  "id": "crystal_spine",
                  "seedSalt": 777,
                  "aquiferDepthFraction": 0.05,
                  "layers": [
                    { "rockTypeId": "gneiss", "thicknessMin": 2, "thicknessMax": 4 },
                    { "rockTypeId": "quartzite", "thicknessMin": 1, "thicknessMax": 2 }
                  ],
                  "mineralVeins": [
                    { "oreId": "mythril_ore", "shape": "Vein", "frequency": 0.25, "requiredRockTypeId": "gneiss", "sizeMin": 6, "sizeMax": 12 }
                  ]
                }
              ],
              "treeProfiles": [
                {
                  "biomeId": "fogwood",
                  "subsurfaceMaterialId": "peat",
                  "rules": [
                    {
                      "minRiparianBoost": 0.8,
                      "species": [
                        { "speciesId": "mangrove", "weight": 1.0 }
                      ]
                    }
                  ],
                  "defaultSpecies": [
                    { "speciesId": "cedar", "weight": 1.0 }
                  ]
                }
              ],
              "biomeProfiles": [
                {
                  "id": "fogwood",
                  "groundPlantDensity": 0.37,
                  "terrainRuggedness": 0.61,
                  "baseMoisture": 0.73,
                  "treeCoverageBoost": 0.19,
                  "treeSuitabilityFloor": 0.17,
                  "denseForest": true,
                  "surfaceCreatureGroupBias": 5,
                  "treeCoverMin": 0.40,
                  "treeCoverMax": 0.72,
                  "outcropMin": 2,
                  "outcropMax": 6,
                  "streamBands": 3,
                  "marshPoolCount": 2,
                  "stoneSurface": false,
                  "surfaceWildlife": [
                    { "creatureDefId": "moss_hare", "weight": 1.0, "minGroup": 2, "maxGroup": 4 }
                  ]
                }
              ],
              "caveWildlifeLayers": [
                {
                  "layer": 4,
                  "spawns": [
                    { "creatureDefId": "crystal_troll", "weight": 1.0, "minGroup": 1, "maxGroup": 1 }
                  ]
                }
              ]
            }
            """;

        var content = new MemoryContentFileSource();
        content.AddFile("data/Content/Core/materials/gneiss.json", """
            { "id": "gneiss", "displayName": "Gneiss", "tags": ["stone"] }
            """);
        content.AddFile("data/Content/Core/materials/quartzite.json", """
            { "id": "quartzite", "displayName": "Quartzite", "tags": ["stone"] }
            """);
        content.AddFile("data/Content/Core/materials/peat.json", """
            { "id": "peat", "displayName": "Peat", "tags": ["dirt"] }
            """);
        content.AddFile("data/Content/Core/materials/mythril.json", """
            {
              "id": "mythril",
              "displayName": "Mythril",
              "tags": ["metal"],
              "forms": [
                {
                  "role": "ore",
                  "item": {
                    "id": "mythril_ore",
                    "displayName": "Mythril Ore",
                    "tags": ["ore", "stone"],
                    "weight": 12.0
                  }
                }
              ]
            }
            """);
        content.AddFile("data/Content/Core/tree_species/mangrove.json", """
            { "id": "mangrove", "displayName": "Mangrove", "woodMaterialId": "peat" }
            """);
        content.AddFile("data/Content/Core/tree_species/cedar.json", """
            { "id": "cedar", "displayName": "Cedar", "woodMaterialId": "peat" }
            """);

        var shared = SharedContentCatalogLoader.Load(content);
        var config = WorldGenContentConfigLoader.LoadFromJson(json);
        var catalog = WorldGenContentCatalog.FromConfig(config, shared);

        var profile = catalog.ResolveStrataProfile("crystal_spine");
        var veins = catalog.ResolveMineralVeins("crystal_spine");
        var biome = catalog.ResolveBiomePreset("fogwood", seed: 11);
        var surfaceWildlife = catalog.ResolveSurfaceWildlife("fogwood", seed: 11);
        var caveWildlife = catalog.ResolveCaveWildlife(4);

        Assert.Equal("crystal_spine", profile.GeologyProfileId);
        Assert.Contains(profile.Layers, layer => layer.RockTypeId == "gneiss");
        Assert.Single(veins);
        Assert.Equal("mythril_ore", veins[0].OreId);
        Assert.True(catalog.IsOreCompatible("mythril_ore", "gneiss"));
        Assert.False(catalog.IsOreCompatible("mythril_ore", "granite"));
        Assert.Equal("peat", catalog.ResolveTreeSubsurfaceMaterialId("fogwood"));
        Assert.Equal("cedar", catalog.ResolveTreeSpeciesId("fogwood", moisture: 0.4f, terrain: 0.5f, riparianBoost: 0.1f, new Random(1)));
        Assert.Equal("mangrove", catalog.ResolveTreeSpeciesId("fogwood", moisture: 0.6f, terrain: 0.4f, riparianBoost: 0.9f, new Random(1)));
        Assert.Equal("fogwood", biome.Id);
        Assert.Equal(0.37f, biome.GroundPlantDensity);
        Assert.Equal(0.61f, biome.TerrainRuggedness);
        Assert.Equal(0.73f, biome.BaseMoisture);
        Assert.Equal(0.19f, biome.TreeCoverageBoost);
        Assert.Equal(0.17f, biome.TreeSuitabilityFloor);
        Assert.True(biome.DenseForest);
        Assert.Equal(5, biome.SurfaceCreatureGroupBias);
        Assert.Equal(0.37f, catalog.ResolveGroundPlantDensity("fogwood"));
        Assert.Equal(5, catalog.ResolveSurfaceCreatureGroupBias("fogwood"));
        Assert.Equal(3, biome.StreamBands);
        Assert.Equal(2, biome.OutcropMin);
        Assert.Single(surfaceWildlife);
        Assert.Equal("moss_hare", surfaceWildlife[0].CreatureDefId);
        Assert.Single(caveWildlife);
        Assert.Equal("crystal_troll", caveWildlife[0].CreatureDefId);
    }

    [Fact]
    public void Catalog_FromCustomConfig_ResolvesMaterialDrivenVeinsFromSharedContent()
    {
        const string json =
            """
            {
              "geologyProfiles": [
                {
                  "id": "crystal_spine",
                  "seedSalt": 777,
                  "aquiferDepthFraction": 0.05,
                  "layers": [
                    { "rockTypeId": "gneiss", "thicknessMin": 2, "thicknessMax": 4 }
                  ],
                  "mineralVeins": [
                    { "materialId": "mythril", "resourceFormRole": "ore", "shape": "Vein", "frequency": 0.25, "requiredRockTypeId": "gneiss", "sizeMin": 6, "sizeMax": 12 }
                  ]
                }
              ],
              "treeProfiles": [
                {
                  "biomeId": "fogwood",
                  "subsurfaceMaterialId": "gneiss",
                  "defaultSpecies": [
                    { "speciesId": "cedar", "weight": 1.0 }
                  ]
                }
              ],
              "biomeProfiles": [
                {
                  "id": "fogwood",
                  "treeCoverMin": 0.20,
                  "treeCoverMax": 0.40
                }
              ]
            }
            """;

        var content = new MemoryContentFileSource();
        content.AddFile("data/Content/Core/materials/gneiss.json", """
            { "id": "gneiss", "displayName": "Gneiss", "tags": ["stone"] }
            """);
        content.AddFile("data/Content/Core/materials/mythril.json", """
            {
              "id": "mythril",
              "displayName": "Mythril",
              "tags": ["metal"],
              "forms": [
                {
                  "role": "ore",
                  "item": {
                    "id": "mythril_ore_chunk",
                    "displayName": "Mythril Ore Chunk",
                    "tags": ["ore", "stone"],
                    "weight": 12.0
                  }
                },
                {
                  "role": "bar",
                  "item": {
                    "id": "mythril_bar",
                    "displayName": "Mythril Bar",
                    "tags": ["metal", "bar"],
                    "weight": 6.0
                  }
                }
              ]
            }
            """);
        content.AddFile("data/Content/Core/tree_species/cedar.json", """
            { "id": "cedar", "displayName": "Cedar", "woodMaterialId": "mythril" }
            """);

        var shared = SharedContentCatalogLoader.Load(content);
        var config = WorldGenContentConfigLoader.LoadFromJson(json);
        var catalog = WorldGenContentCatalog.FromConfig(config, shared);

        var vein = Assert.Single(catalog.ResolveMineralVeins("crystal_spine"));
        Assert.Equal("mythril", vein.MaterialId);
        Assert.Equal("ore", vein.ResourceFormRole);
        Assert.Equal("mythril_ore_chunk", vein.OreId);
        Assert.True(catalog.IsOreCompatible("mythril_ore_chunk", "gneiss"));
    }

    [Fact]
    public void Catalog_FromCustomConfig_MergesCreatureEcologyWildlifeWithoutCentralSpawnTables()
    {
        const string json =
            """
            {
              "geologyProfiles": [
                {
                  "id": "fungal_bedrock",
                  "seedSalt": 77,
                  "aquiferDepthFraction": 0.0,
                  "layers": [
                    { "rockTypeId": "granite", "thicknessMin": 2, "thicknessMax": 4 }
                  ]
                }
              ],
              "treeProfiles": [
                {
                  "biomeId": "fungal_grove",
                  "subsurfaceMaterialId": "soil",
                  "defaultSpecies": [
                    { "speciesId": "oak", "weight": 1.0 }
                  ]
                }
              ],
              "biomeProfiles": [
                {
                  "id": "fungal_grove",
                  "treeCoverMin": 0.10,
                  "treeCoverMax": 0.20,
                  "surfaceWildlife": [
                    { "creatureDefId": "stone_hare", "weight": 0.6, "minGroup": 2, "maxGroup": 3 }
                  ]
                }
              ]
            }
            """;

        var content = new MemoryContentFileSource();
        content.AddFile("data/Content/Core/materials/granite.json", """
            { "id": "granite", "displayName": "Granite", "tags": ["stone"] }
            """);
        content.AddFile("data/Content/Core/materials/soil.json", """
            { "id": "soil", "displayName": "Soil", "tags": ["dirt"] }
            """);
        content.AddFile("data/Content/Core/materials/oak_wood.json", """
            { "id": "oak_wood", "displayName": "Oak Wood", "tags": ["wood"] }
            """);
        content.AddFile("data/Content/Core/tree_species/oak.json", """
            { "id": "oak", "displayName": "Oak", "woodMaterialId": "oak_wood" }
            """);
        content.AddFile("data/Content/Game/creatures/fungal/glow_lizard/creature.json", """
            {
              "id": "glow_lizard",
              "displayName": "Glow Lizard",
              "tags": ["animal", "carnivore"],
              "ecology": {
                "surfaceWildlife": [
                  { "biomes": ["fungal_grove"], "weight": 0.8, "minGroup": 1, "maxGroup": 2 }
                ],
                "caveWildlife": [
                  { "layers": [3], "weight": 0.4, "minGroup": 1, "maxGroup": 1 }
                ]
              }
            }
            """);
        content.AddFile("data/Content/Game/creatures/fungal/stone_hare/creature.json", """
            {
              "id": "stone_hare",
              "displayName": "Stone Hare",
              "tags": ["animal", "herbivore"],
              "ecology": {
                "surfaceWildlife": [
                  { "biomes": ["fungal_grove"], "weight": 0.2, "minGroup": 1, "maxGroup": 1 }
                ]
              }
            }
            """);

        var shared = SharedContentCatalogLoader.Load(content);
        var config = WorldGenContentConfigLoader.LoadFromJson(json);
        var catalog = WorldGenContentCatalog.FromConfig(config, shared);

        var surfaceWildlife = catalog.ResolveSurfaceWildlife("fungal_grove", seed: 5);
        var caveWildlife = catalog.ResolveCaveWildlife(3);

        Assert.Contains(surfaceWildlife, spawn => spawn.CreatureDefId == "stone_hare" && spawn.Weight == 0.6f);
        Assert.Contains(surfaceWildlife, spawn => spawn.CreatureDefId == "glow_lizard" && spawn.MinGroup == 1 && spawn.MaxGroup == 2);
        Assert.Single(surfaceWildlife.Where(spawn => spawn.CreatureDefId == "stone_hare"));
        Assert.Contains(caveWildlife, spawn => spawn.CreatureDefId == "glow_lizard");
    }

    [Fact]
    public void Catalog_FromCustomConfig_RejectsCreatureEcologyReferencingUnknownBiome()
    {
        const string json =
            """
            {
              "geologyProfiles": [
                {
                  "id": "fungal_bedrock",
                  "seedSalt": 77,
                  "aquiferDepthFraction": 0.0,
                  "layers": [
                    { "rockTypeId": "granite", "thicknessMin": 2, "thicknessMax": 4 }
                  ]
                }
              ],
              "treeProfiles": [
                {
                  "biomeId": "fungal_grove",
                  "subsurfaceMaterialId": "soil",
                  "defaultSpecies": [
                    { "speciesId": "oak", "weight": 1.0 }
                  ]
                }
              ],
              "biomeProfiles": [
                {
                  "id": "fungal_grove",
                  "treeCoverMin": 0.10,
                  "treeCoverMax": 0.20
                }
              ]
            }
            """;

        var content = new MemoryContentFileSource();
        content.AddFile("data/Content/Core/materials/granite.json", """
            { "id": "granite", "displayName": "Granite", "tags": ["stone"] }
            """);
        content.AddFile("data/Content/Core/materials/soil.json", """
            { "id": "soil", "displayName": "Soil", "tags": ["dirt"] }
            """);
        content.AddFile("data/Content/Core/materials/oak_wood.json", """
            { "id": "oak_wood", "displayName": "Oak Wood", "tags": ["wood"] }
            """);
        content.AddFile("data/Content/Core/tree_species/oak.json", """
            { "id": "oak", "displayName": "Oak", "woodMaterialId": "oak_wood" }
            """);
        content.AddFile("data/Content/Game/creatures/fungal/glow_lizard/creature.json", """
            {
              "id": "glow_lizard",
              "displayName": "Glow Lizard",
              "tags": ["animal", "carnivore"],
              "ecology": {
                "surfaceWildlife": [
                  { "biomes": ["missing_biome"], "weight": 0.8, "minGroup": 1, "maxGroup": 2 }
                ]
              }
            }
            """);

        var shared = SharedContentCatalogLoader.Load(content);
        var config = WorldGenContentConfigLoader.LoadFromJson(json);

        var ex = Assert.Throws<InvalidOperationException>(() => WorldGenContentCatalog.FromConfig(config, shared));
        Assert.Contains("missing_biome", ex.Message);
    }

    [Fact]
    public void LoadDefaultOrFallback_ContainsBuiltInGeologyTreeBiomeAndWildlifeProfiles()
    {
        var config = WorldGenContentConfigLoader.LoadDefaultOrFallback();
        var catalog = WorldGenContentCatalog.FromConfig(config);

        var alluvial = catalog.ResolveStrataProfile(GeologyProfileIds.AlluvialBasin);
        var alluvialVeins = catalog.ResolveMineralVeins(GeologyProfileIds.AlluvialBasin);
        var coniferSpecies = catalog.ResolveTreeSpeciesId(MacroBiomeIds.ConiferForest, moisture: 0.5f, terrain: 0.4f, riparianBoost: 0.1f, new Random(2));
        var highland = catalog.ResolveBiomePreset(MacroBiomeIds.Highland, seed: 7);
        var oceanWildlife = catalog.ResolveSurfaceWildlife(MacroBiomeIds.OceanDeep, seed: 7);
        var caveWildlife = catalog.ResolveCaveWildlife(2);

        Assert.Equal(GeologyProfileIds.AlluvialBasin, alluvial.GeologyProfileId);
        Assert.NotEmpty(alluvial.Layers);
        Assert.NotEmpty(alluvialVeins);
        Assert.Contains(coniferSpecies, new[] { TreeSpeciesIds.Spruce, TreeSpeciesIds.Pine });
        Assert.Equal(0.16f, highland.GroundPlantDensity);
        Assert.Equal(1.00f, highland.TerrainRuggedness);
        Assert.Equal(0.44f, highland.BaseMoisture);
        Assert.Equal(0.32f, highland.TreeSuitabilityFloor);
        Assert.Equal(-1, highland.SurfaceCreatureGroupBias);
        Assert.True(highland.StoneSurface);
        Assert.NotEmpty(oceanWildlife);
        Assert.All(oceanWildlife, spawn => Assert.Equal(CreatureDefIds.GiantCarp, spawn.CreatureDefId));
        Assert.NotEmpty(caveWildlife);
    }

    [Fact]
    public void LoadDefaultOrFallback_UsesMaterialDrivenVeinAuthoring()
    {
        var config = WorldGenContentConfigLoader.LoadDefaultOrFallback();
        var veins = config.GeologyProfiles.SelectMany(profile => profile.MineralVeins).ToArray();

        Assert.NotEmpty(veins);
        Assert.All(veins, vein =>
        {
            Assert.False(string.IsNullOrWhiteSpace(vein.MaterialId));
            Assert.Equal(ContentFormRoles.Ore, vein.ResourceFormRole);
            Assert.True(string.IsNullOrWhiteSpace(vein.OreId));
        });
    }

    [Fact]
    public void Catalog_FromCustomConfig_Supports_CustomHistoryFigureHooks()
    {
        const string json =
            """
            {
              "geologyProfiles": [
                {
                  "id": "custom_history_geology",
                  "seedSalt": 321,
                  "aquiferDepthFraction": 0.10,
                  "layers": [
                    { "rockTypeId": "granite", "thicknessMin": 2, "thicknessMax": 3 }
                  ]
                }
              ],
              "historyFigures": {
                "professionProfiles": [
                  {
                    "id": "scribe",
                    "laborIds": ["hauling"],
                    "skillLevels": { "writing": 4 },
                    "attributeLevels": { "focus": 4 },
                    "likedFoodId": "fig",
                    "dislikedFoodId": "drink"
                  }
                ],
                "professionSelectionRules": [
                  {
                    "speciesDefId": "dwarf",
                    "memberIndex": 0,
                    "founderBias": true,
                    "professionIds": ["scribe"]
                  }
                ],
                "defaultProfessionIds": ["scribe"],
                "defaultNonDwarfProfessionIds": ["scribe"],
                "defaultNamePool": ["Archivist"],
                "speciesNamePools": [
                  {
                    "speciesDefId": "dwarf",
                    "names": ["Led", "Deler"]
                  }
                ]
              }
            }
            """;

        var config = WorldGenContentConfigLoader.LoadFromJson(json);
        var catalog = WorldGenContentCatalog.FromConfig(config);

        var profession = catalog.ResolveHistoryFigureProfession("dwarf", "ridge_hamlet", memberIndex: 0, founderBias: true, new Random(7));
        var dwarfName = catalog.ResolveHistoryFigureName("dwarf", new Random(7));
        var unknownSpeciesName = catalog.ResolveHistoryFigureName("forgotten_beast", new Random(7));

        Assert.Equal("scribe", profession.ProfessionId);
        Assert.Equal(4, profession.SkillLevels["writing"]);
    Assert.Equal(4, profession.AttributeLevels["focus"]);
        Assert.Contains(dwarfName, new[] { "Led", "Deler" });
        Assert.Equal("Archivist", unknownSpeciesName);
    }

    [Fact]
    public void Catalog_FromSharedCreatureHistory_UsesCreatureNamePoolsAndProfessionDefaults()
    {
        const string json =
            """
            {
              "geologyProfiles": [
                {
                  "id": "custom_history_geology",
                  "seedSalt": 321,
                  "aquiferDepthFraction": 0.10,
                  "layers": [
                    { "rockTypeId": "granite", "thicknessMin": 2, "thicknessMax": 3 }
                  ]
                }
              ]
            }
            """;

        var content = new MemoryContentFileSource();
        content.AddFile("data/Content/Core/materials/granite.json", """
            { "id": "granite", "displayName": "Granite", "tags": ["stone"] }
            """);
        content.AddFile("data/Content/Game/creatures/sapients/dwarf/creature.json", """
            {
              "id": "dwarf",
              "displayName": "Dwarf",
              "tags": ["sapient", "playable"],
              "history": {
                "figureNamePool": ["Led", "Deler"],
                "defaultProfessionIds": ["hauler"],
                "professionRules": [
                  { "memberIndex": 0, "founderBias": true, "professionIds": ["crafter"] },
                  { "siteKindContains": "hamlet", "memberIndex": 0, "professionIds": ["farmer"] }
                ]
              }
            }
            """);
        content.AddFile("data/Content/Game/creatures/hostile/goblin/creature.json", """
            {
              "id": "goblin",
              "displayName": "Goblin",
              "tags": ["hostile"],
              "history": {
                "figureNamePool": ["Snaga", "Ghash"],
                "defaultProfessionIds": ["militia"]
              }
            }
            """);

        var shared = SharedContentCatalogLoader.Load(content);
        var config = WorldGenContentConfigLoader.LoadFromJson(json);
        var catalog = WorldGenContentCatalog.FromConfig(config, shared);

        var founderProfession = catalog.ResolveHistoryFigureProfession("dwarf", "watchtower", memberIndex: 0, founderBias: true, new Random(7));
        var regularProfession = catalog.ResolveHistoryFigureProfession("dwarf", "fortress", memberIndex: 2, founderBias: false, new Random(7));
        var goblinProfession = catalog.ResolveHistoryFigureProfession("goblin", "cave", memberIndex: 1, founderBias: false, new Random(7));
        var dwarfName = catalog.ResolveHistoryFigureName("dwarf", new Random(7));
        var goblinName = catalog.ResolveHistoryFigureName("goblin", new Random(7));

        Assert.Equal("crafter", founderProfession.ProfessionId);
        Assert.Equal("hauler", regularProfession.ProfessionId);
        Assert.Equal("militia", goblinProfession.ProfessionId);
        Assert.Contains(dwarfName, new[] { "Led", "Deler" });
        Assert.Contains(goblinName, new[] { "Snaga", "Ghash" });
    }

    [Fact]
    public void Catalog_FromSharedCreatureSociety_ResolvesFactionPrimaryUnitsFromRoles()
    {
        const string json =
            """
            {
              "geologyProfiles": [
                {
                  "id": "role_test_geology",
                  "seedSalt": 321,
                  "aquiferDepthFraction": 0.10,
                  "layers": [
                    { "rockTypeId": "granite", "thicknessMin": 2, "thicknessMax": 3 }
                  ]
                }
              ]
            }
            """;

        var content = new MemoryContentFileSource();
        content.AddFile("data/Content/Core/materials/granite.json", """
            { "id": "granite", "displayName": "Granite", "tags": ["stone"] }
            """);
        content.AddFile("data/Content/Game/creatures/sapients/molefolk/creature.json", """
            {
              "id": "molefolk",
              "displayName": "Molefolk",
              "tags": ["sapient"],
              "society": {
                "factionRoles": [
                  { "id": "civilized_primary", "weight": 1.0 }
                ]
              }
            }
            """);
        content.AddFile("data/Content/Game/creatures/hostile/orc/creature.json", """
            {
              "id": "orc",
              "displayName": "Orc",
              "tags": ["hostile", "sapient"],
              "society": {
                "factionRoles": [
                  { "id": "hostile_primary", "weight": 1.0 }
                ]
              }
            }
            """);
        content.AddFile("data/Content/Game/creatures/hostile/ogre/creature.json", """
            {
              "id": "ogre",
              "displayName": "Ogre",
              "tags": ["hostile"],
              "society": {
                "factionRoles": [
                  { "id": "hostile_alternate", "weight": 1.0 }
                ]
              }
            }
            """);

        var shared = SharedContentCatalogLoader.Load(content);
        var config = WorldGenContentConfigLoader.LoadFromJson(json);
        var catalog = WorldGenContentCatalog.FromConfig(config, shared);

        var civilized = catalog.ResolveFactionPrimaryUnit(new FactionTemplateConfig
        {
            Id = "civilized_test",
            IsHostile = false,
            PrimaryUnitRole = FactionUnitRoleIds.CivilizedPrimary,
            PrimaryUnitDefId = "dwarf",
        }, new Random(7));

        var hostileAlternate = catalog.ResolveFactionPrimaryUnit(new FactionTemplateConfig
        {
            Id = "hostile_test",
            IsHostile = true,
            PrimaryUnitRole = FactionUnitRoleIds.HostilePrimary,
            PrimaryUnitDefId = "goblin",
            AlternatePrimaryUnitRole = FactionUnitRoleIds.HostileAlternate,
            AlternatePrimaryUnitDefId = "troll",
            AlternatePrimaryChance = 1.0f,
        }, new Random(7));

        var civilizedDefault = catalog.ResolveDefaultCivilizationPrimaryUnit(hostile: false, new Random(7));
        var hostileDefault = catalog.ResolveDefaultCivilizationPrimaryUnit(hostile: true, new Random(7));

        Assert.Equal("molefolk", civilized);
        Assert.Equal("ogre", hostileAlternate);
        Assert.Equal("molefolk", civilizedDefault);
        Assert.Equal("orc", hostileDefault);
    }
}

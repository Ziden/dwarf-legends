using System.Linq;
using DwarfFortress.WorldGen.Content;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Story;

namespace DwarfFortress.WorldGen.Tests;

public sealed class WorldLoreGeneratorTests
{
    [Fact]
    public void Generate_SameInputs_ProducesDeterministicLore()
    {
        var loreA = WorldLoreGenerator.Generate(seed: 999, width: 48, height: 48, depth: 8);
        var loreB = WorldLoreGenerator.Generate(seed: 999, width: 48, height: 48, depth: 8);

        Assert.Equal(loreA.RegionName, loreB.RegionName);
        Assert.Equal(loreA.BiomeId, loreB.BiomeId);
        Assert.Equal(loreA.SimulatedYears, loreB.SimulatedYears);
        Assert.Equal(loreA.Factions.Select(f => f.Name), loreB.Factions.Select(f => f.Name));
        Assert.Equal(
            loreA.FactionRelations.Select(r => $"{r.FactionAId}|{r.FactionBId}:{r.Score:0.000}:{r.Stance}"),
            loreB.FactionRelations.Select(r => $"{r.FactionAId}|{r.FactionBId}:{r.Score:0.000}:{r.Stance}"));
        Assert.Equal(
            loreA.History.Take(20).Select(h => h.Summary),
            loreB.History.Take(20).Select(h => h.Summary));
    }

    [Fact]
    public void Generate_ProducesBoundedAndUsableLore()
    {
        var lore = WorldLoreGenerator.Generate(seed: 42, width: 48, height: 48, depth: 8);

        Assert.False(string.IsNullOrWhiteSpace(lore.RegionName));
        Assert.False(string.IsNullOrWhiteSpace(lore.BiomeId));
        Assert.NotEmpty(lore.Factions);
        Assert.NotEmpty(lore.FactionRelations);
        Assert.NotEmpty(lore.Sites);
        Assert.Contains(lore.Sites, site => !string.IsNullOrWhiteSpace(site.Status));
        Assert.NotEmpty(lore.History);
        Assert.InRange(lore.Threat, 0f, 1f);
        Assert.InRange(lore.Prosperity, 0f, 1f);
        Assert.InRange(lore.SimulatedYears, 120, 260);
    }

    [Fact]
    public void Generate_UsesCreatureFactionRolesForPrimaryUnits()
    {
        var content = new MemoryContentFileSource();
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
        var config = new WorldLoreConfig
        {
            Biomes = ["temperate_plains"],
            NameLeft = ["Stone"],
            NameRight = ["reach"],
            MottoFragments = ["endure"],
            SiteKinds =
            [
                new SiteKindConfig { Id = "hamlet", OwnerRule = SiteOwnerRules.Random, Summary = "Hamlet" },
            ],
            FactionTemplates =
            [
                new FactionTemplateConfig
                {
                    Id = "civilized_test",
                    NamePattern = "Civilized {left}",
                    IsHostile = false,
                    PrimaryUnitRole = FactionUnitRoleIds.CivilizedPrimary,
                    PrimaryUnitDefId = "dwarf",
                    InfluenceMin = 0.5f,
                    InfluenceMax = 0.5f,
                    MilitarismMin = 0.3f,
                    MilitarismMax = 0.3f,
                    TradeFocusMin = 0.7f,
                    TradeFocusMax = 0.7f,
                    SpawnChance = 1.0f,
                },
                new FactionTemplateConfig
                {
                    Id = "hostile_test",
                    NamePattern = "Hostile {left}",
                    IsHostile = true,
                    PrimaryUnitRole = FactionUnitRoleIds.HostilePrimary,
                    PrimaryUnitDefId = "goblin",
                    AlternatePrimaryUnitRole = FactionUnitRoleIds.HostileAlternate,
                    AlternatePrimaryUnitDefId = "troll",
                    AlternatePrimaryChance = 1.0f,
                    InfluenceMin = 0.6f,
                    InfluenceMax = 0.6f,
                    MilitarismMin = 0.8f,
                    MilitarismMax = 0.8f,
                    TradeFocusMin = 0.1f,
                    TradeFocusMax = 0.1f,
                    SpawnChance = 1.0f,
                },
            ],
            History = new LoreHistoryConfig
            {
                SimulatedYearsMin = 1,
                SimulatedYearsMax = 1,
                EventsPerYearMin = 1,
                EventsPerYearMax = 1,
            },
        };

        var lore = WorldLoreGenerator.Generate(seed: 5, width: 48, height: 48, depth: 8, config, shared);

        Assert.Contains(lore.Factions, faction => faction.Id == "civilized_test" && faction.PrimaryUnitDefId == "molefolk");
        Assert.Contains(lore.Factions, faction => faction.Id == "hostile_test" && faction.PrimaryUnitDefId == "ogre");
    }
}

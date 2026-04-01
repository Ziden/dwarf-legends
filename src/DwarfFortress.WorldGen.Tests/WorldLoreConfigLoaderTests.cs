using DwarfFortress.WorldGen.Story;

namespace DwarfFortress.WorldGen.Tests;

public sealed class WorldLoreConfigLoaderTests
{
    [Fact]
    public void LoadFromJson_MinimalConfig_MergesDefaults()
    {
        const string json =
            """
            {
              "biomes": ["ember_wastes"],
              "factionTemplates": [
                {
                  "id": "faction_custom",
                  "namePattern": "Custom {left}",
                  "isHostile": false,
                  "primaryUnitDefId": "dwarf",
                  "influenceMin": 0.5,
                  "influenceMax": 0.5,
                  "militarismMin": 0.3,
                  "militarismMax": 0.3,
                  "tradeFocusMin": 0.7,
                  "tradeFocusMax": 0.7,
                  "spawnChance": 1.0
                }
              ]
            }
            """;

        var config = WorldLoreConfigLoader.LoadFromJson(json);

        Assert.Equal("ember_wastes", Assert.Single(config.Biomes));
        Assert.NotEmpty(config.SiteKinds);
        Assert.NotNull(config.History);
    }

    [Fact]
    public void Generate_UsesCustomBiomeAndFactionTemplate()
    {
        const string json =
            """
            {
              "biomes": ["obsidian_badlands"],
              "nameLeft": ["Night"],
              "nameRight": ["wastes"],
              "mottoFragments": ["unyielding"],
              "siteKinds": [
                { "id": "ruin", "ownerRule": "random", "summary": "Custom ruin" }
              ],
              "factionTemplates": [
                {
                  "id": "faction_custom",
                  "namePattern": "Order of {left}",
                  "isHostile": false,
                  "primaryUnitDefId": "dwarf",
                  "influenceMin": 0.4,
                  "influenceMax": 0.4,
                  "militarismMin": 0.2,
                  "militarismMax": 0.2,
                  "tradeFocusMin": 0.8,
                  "tradeFocusMax": 0.8,
                  "spawnChance": 1.0,
                  "motto": "unyielding"
                }
              ],
              "history": {
                "simulatedYearsMin": 5,
                "simulatedYearsMax": 5,
                "eventsPerYearMin": 1,
                "eventsPerYearMax": 1,
                "eventWeightTreaty": 1.0,
                "eventWeightRaid": 0.0,
                "eventWeightFounding": 0.0,
                "eventWeightSkirmish": 0.0,
                "eventWeightCrisis": 0.0
              }
            }
            """;

        var config = WorldLoreConfigLoader.LoadFromJson(json);
        var lore = WorldLoreGenerator.Generate(seed: 1, width: 48, height: 48, depth: 8, config);

        Assert.Equal("obsidian_badlands", lore.BiomeId);
        Assert.Contains(lore.Factions, faction => faction.Id == "faction_custom");
        Assert.Equal(5, lore.SimulatedYears);
        Assert.All(lore.History, evt => Assert.Equal("treaty", evt.Type));
    }
}

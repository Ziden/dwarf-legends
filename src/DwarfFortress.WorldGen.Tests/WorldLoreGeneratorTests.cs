using System.Linq;
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
}

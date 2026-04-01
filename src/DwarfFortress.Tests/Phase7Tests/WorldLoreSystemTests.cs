using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Systems;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class WorldLoreSystemTests
{
    [Fact]
    public void GenerateWorldCommand_Creates_WorldLore_State()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new GenerateWorldCommand(Seed: 42, Width: 48, Height: 48, Depth: 8));

        var lore = sim.Context.Get<WorldLoreSystem>().Current;
        Assert.NotNull(lore);
        Assert.False(string.IsNullOrWhiteSpace(lore!.RegionName));
        Assert.NotEmpty(lore.Factions);
        Assert.NotEmpty(lore.Sites);
        Assert.NotEmpty(lore.History);
        Assert.InRange(lore.Threat, 0f, 1f);
        Assert.InRange(lore.Prosperity, 0f, 1f);
    }

    [Fact]
    public void GenerateWorldCommand_SameSeed_Produces_SameLore()
    {
        var (simA, _, _, _, _) = TestFixtures.BuildFullSim();
        var (simB, _, _, _, _) = TestFixtures.BuildFullSim();

        simA.Context.Commands.Dispatch(new GenerateWorldCommand(Seed: 1001, Width: 48, Height: 48, Depth: 8));
        simB.Context.Commands.Dispatch(new GenerateWorldCommand(Seed: 1001, Width: 48, Height: 48, Depth: 8));

        var loreA = simA.Context.Get<WorldLoreSystem>().Current!;
        var loreB = simB.Context.Get<WorldLoreSystem>().Current!;

        Assert.Equal(loreA.RegionName, loreB.RegionName);
        Assert.Equal(loreA.BiomeId, loreB.BiomeId);
        Assert.Equal(loreA.SimulatedYears, loreB.SimulatedYears);
        Assert.Equal(loreA.Factions.Select(f => f.Name), loreB.Factions.Select(f => f.Name));
        Assert.Equal(
            loreA.History.Take(20).Select(h => h.Summary),
            loreB.History.Take(20).Select(h => h.Summary));
    }

    [Fact]
    public void StartFortress_WorldQuery_Includes_LoreSummary()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 7, Width: 48, Height: 48, Depth: 8));
        var lore = sim.Context.Get<WorldQuerySystem>().GetLoreSummary();

        Assert.NotNull(lore);
        Assert.False(string.IsNullOrWhiteSpace(lore!.RegionName));
        Assert.False(string.IsNullOrWhiteSpace(lore.BiomeId));
        Assert.True(lore.SimulatedYears >= 120);
        Assert.NotEmpty(lore.RecentEvents);
    }

    [Fact]
    public void WorldLoreSystem_Scales_Event_Intensity()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();
        var lore = sim.Context.Get<WorldLoreSystem>();

        sim.Context.Commands.Dispatch(new GenerateWorldCommand(Seed: 99, Width: 48, Height: 48, Depth: 8));

        var migrants = lore.ScaleMigrantCount(3);
        var raids = lore.ScaleRaidCount(2);
        var tunedRaidProb = lore.TuneEventProbability(WorldEventIds.GoblinRaid, 0.3f);
        var tunedMigrantProb = lore.TuneEventProbability(WorldEventIds.MigrantWave, 0.5f);

        Assert.InRange(migrants, 1, 8);
        Assert.InRange(raids, 1, 8);
        Assert.InRange(tunedRaidProb, 0.05f, 0.95f);
        Assert.InRange(tunedMigrantProb, 0.05f, 0.95f);
    }
}

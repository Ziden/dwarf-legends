using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.Tests;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase6Tests;

public sealed class NeedsSystemTests
{
    private static (NeedsSystem ns, EntityRegistry er, JobSystem js, GameSimulation sim) CreateSim()
    {
        var logger = new Fakes.TestLogger();
        var ds     = new Fakes.InMemoryDataSource();
        TestFixtures.AddCoreData(ds);
        var er = new EntityRegistry();
        var js = new JobSystem();
        var ns = new NeedsSystem();
        var sim = TestFixtures.CreateSimulation(logger, ds, er, js, ns);
        return (ns, er, js, sim);
    }

    [Fact]
    public void Tick_Decays_Needs_Over_Time()
    {
        var (_, er, _, sim) = CreateSim();
        var dwarf = new Dwarf(1, "Urist", new Vec3i(0, 0, 0));
        er.Register(dwarf);
        float initialHunger = dwarf.Needs.Hunger.Level;

        sim.Tick(10f);

        Assert.True(dwarf.Needs.Hunger.Level < initialHunger);
    }

    [Fact]
    public void Tick_Emits_NeedCriticalEvent_When_Need_Is_Critical()
    {
        var (_, er, _, sim) = CreateSim();
        var dwarf = new Dwarf(1, "Urist", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        // Force hunger to critical level
        dwarf.Needs.Hunger.SetLevel(0.05f);

        NeedCriticalEvent? received = null;
        sim.Context.EventBus.On<NeedCriticalEvent>(e => received = e);

        sim.Tick(0.1f);

        Assert.NotNull(received);
        Assert.Equal(1, received!.Value.EntityId);
        Assert.Equal(NeedIds.Hunger, received.Value.NeedId);
    }

    [Fact]
    public void Tick_Does_Not_Emit_NeedCriticalEvent_When_Need_Is_Fine()
    {
        var (_, er, _, sim) = CreateSim();
        var dwarf = new Dwarf(1, "Urist", new Vec3i(0, 0, 0));
        er.Register(dwarf);
        // Needs start at 1.0 — above critical threshold

        bool eventReceived = false;
        sim.Context.EventBus.On<NeedCriticalEvent>(_ => eventReceived = true);

        sim.Tick(0.01f);

        Assert.False(eventReceived);
    }

    [Fact]
    public void Tick_Creates_Eat_Job_When_Hunger_Is_Critical()
    {
        var (_, er, js, sim) = CreateSim();
        var dwarf = new Dwarf(1, "Urist", new Vec3i(2, 3, 0));
        er.Register(dwarf);
        dwarf.Needs.Hunger.SetLevel(0.05f);

        sim.Tick(0.1f);

        var eatJobs = js.GetPendingJobs().Where(j => j.JobDefId == JobDefIds.Eat).ToList();
        Assert.NotEmpty(eatJobs);
    }

    [Fact]
    public void Tick_Does_Not_Duplicate_Survival_Jobs()
    {
        var (_, er, js, sim) = CreateSim();
        var dwarf = new Dwarf(1, "Urist", new Vec3i(0, 0, 0));
        er.Register(dwarf);
        dwarf.Needs.Hunger.SetLevel(0.05f);

        sim.Tick(0.1f);
        sim.Tick(0.1f);
        sim.Tick(0.1f);

        var eatJobs = js.GetPendingJobs().Where(j => j.JobDefId == JobDefIds.Eat).ToList();
        Assert.Single(eatJobs);
    }

    [Fact]
    public void Tick_Creates_Drink_Job_When_Thirst_Is_Critical()
    {
        var (_, er, js, sim) = CreateSim();
        var dwarf = new Dwarf(1, "Urist", new Vec3i(0, 0, 0));
        er.Register(dwarf);
        dwarf.Needs.Thirst.SetLevel(0.05f);

        sim.Tick(0.1f);

        var drinkJobs = js.GetPendingJobs().Where(j => j.JobDefId == JobDefIds.Drink).ToList();
        Assert.NotEmpty(drinkJobs);
    }

    [Fact]
    public void Tick_Decays_Creature_Needs_Using_Same_Component_Path()
    {
        var (_, er, _, sim) = CreateSim();
        var creature = new Creature(2, DefIds.Elk, new Vec3i(1, 1, 0), maxHealth: 80f);
        er.Register(creature);
        var initialHunger = creature.Needs.Hunger.Level;

        sim.Tick(10f);

        Assert.True(creature.Needs.Hunger.Level < initialHunger);
    }
}

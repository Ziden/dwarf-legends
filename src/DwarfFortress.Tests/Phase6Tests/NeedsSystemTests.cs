using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Jobs.Strategies;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.Tests;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase6Tests;

public sealed class NeedsSystemTests
{
    private sealed class LongRunningStrategy : IJobStrategy
    {
        public const string DefId = "test_long_running";

        public bool Interrupted { get; private set; }

        public string JobDefId => DefId;

        public bool CanExecute(Job job, int dwarfId, GameContext ctx) => true;

        public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
            => [new WaitStep(10f)];

        public void OnInterrupt(Job job, int dwarfId, GameContext ctx)
        {
            Interrupted = true;
        }

        public void OnComplete(Job job, int dwarfId, GameContext ctx)
        {
        }
    }

    private sealed class InterruptibleSleepStrategy : IJobStrategy
    {
        public bool Interrupted { get; private set; }

        public string JobDefId => JobDefIds.Sleep;

        public bool CanExecute(Job job, int dwarfId, GameContext ctx) => true;

        public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
            => [new WaitStep(10f)];

        public void OnInterrupt(Job job, int dwarfId, GameContext ctx)
        {
            Interrupted = true;
        }

        public void OnComplete(Job job, int dwarfId, GameContext ctx)
        {
        }
    }

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
    public void Tick_Emits_NeedCriticalEvent_Only_Once_While_Need_Remains_Critical()
    {
        var (_, er, _, sim) = CreateSim();
        var dwarf = new Dwarf(1, "Urist", new Vec3i(0, 0, 0));
        er.Register(dwarf);
        dwarf.Needs.Hunger.SetLevel(0.05f);

        var events = new List<NeedCriticalEvent>();
        sim.Context.EventBus.On<NeedCriticalEvent>(e => events.Add(e));

        sim.Tick(0.1f);
        sim.Tick(0.1f);
        sim.Tick(0.1f);

        var hungerEvents = events.Where(e => e.EntityId == dwarf.Id && e.NeedId == NeedIds.Hunger).ToList();
        Assert.Single(hungerEvents);
    }

    [Fact]
    public void Tick_Emits_NeedCriticalEvent_Again_When_Need_Recovers_Then_Becomes_Critical_Again()
    {
        var (_, er, _, sim) = CreateSim();
        var dwarf = new Dwarf(1, "Urist", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        var events = new List<NeedCriticalEvent>();
        sim.Context.EventBus.On<NeedCriticalEvent>(e => events.Add(e));

        dwarf.Needs.Hunger.SetLevel(0.05f);
        sim.Tick(0.1f);

        dwarf.Needs.Hunger.SetLevel(0.5f);
        sim.Tick(0.1f);

        dwarf.Needs.Hunger.SetLevel(0.05f);
        sim.Tick(0.1f);

        var hungerEvents = events.Where(e => e.EntityId == dwarf.Id && e.NeedId == NeedIds.Hunger).ToList();
        Assert.Equal(2, hungerEvents.Count);
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
    public void Tick_Gives_Drink_Higher_Priority_Than_Sleep_When_Both_Needs_Are_Critical()
    {
        var (_, er, js, sim) = CreateSim();
        var dwarf = new Dwarf(1, "Urist", new Vec3i(0, 0, 0));
        er.Register(dwarf);
        dwarf.Needs.Thirst.SetLevel(0.05f);
        dwarf.Needs.Sleep.SetLevel(0.05f);

        sim.Tick(0.1f);

        var drinkJob = Assert.Single(js.GetPendingJobs().Where(job => job.JobDefId == JobDefIds.Drink));
        var sleepJob = Assert.Single(js.GetPendingJobs().Where(job => job.JobDefId == JobDefIds.Sleep));
        Assert.True(drinkJob.Priority > sleepJob.Priority);
    }

    [Fact]
    public void Tick_Cancels_Active_NonSurvival_Work_When_Hunger_Becomes_Critical()
    {
        var (_, er, js, sim) = CreateSim();
        var strategy = new LongRunningStrategy();
        js.RegisterStrategy(strategy);

        var dwarf = new Dwarf(1, "Urist", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        var workJob = js.CreateJob(LongRunningStrategy.DefId, dwarf.Position.Position, priority: 5);

        sim.Tick(0.1f);

        Assert.Equal(workJob.Id, js.GetAssignedJob(dwarf.Id)?.Id);

        dwarf.Needs.Hunger.SetLevel(0.05f);

        sim.Tick(0.1f);

        Assert.True(strategy.Interrupted);
        Assert.Null(js.GetJob(workJob.Id));
        Assert.Contains(js.GetAllJobs(), job => job.JobDefId == JobDefIds.Eat);
    }

    [Fact]
    public void Tick_Cancels_Active_Sleep_Job_When_Thirst_Becomes_Critical()
    {
        var (_, er, js, sim) = CreateSim();
        var sleepStrategy = new InterruptibleSleepStrategy();
        js.RegisterStrategy(sleepStrategy);

        var dwarf = new Dwarf(1, "Urist", new Vec3i(0, 0, 0));
        er.Register(dwarf);
        dwarf.Needs.Sleep.SetLevel(0.05f);

        sim.Tick(0.1f);

        Assert.Equal(JobDefIds.Sleep, js.GetAssignedJob(dwarf.Id)?.JobDefId);

        dwarf.Needs.Thirst.SetLevel(0.05f);

        sim.Tick(0.1f);

        Assert.True(sleepStrategy.Interrupted);
        var drinkJob = Assert.Single(js.GetPendingJobs().Where(job => job.JobDefId == JobDefIds.Drink));
        Assert.Equal(dwarf.Id, drinkJob.AssignedDwarfId);
    }

    [Fact]
    public void Tick_Creates_Preassigned_Drink_Job_For_Thirsty_Dwarf()
    {
        var (_, er, js, sim) = CreateSim();
        var thirstyDwarf = new Dwarf(1, "Urist", new Vec3i(0, 0, 0));
        var fullDwarf = new Dwarf(2, "Rigoth", new Vec3i(1, 0, 0));
        er.Register(thirstyDwarf);
        er.Register(fullDwarf);
        thirstyDwarf.Needs.Thirst.SetLevel(0.05f);

        sim.Tick(0.1f);

        var drinkJob = js.GetPendingJobs().Single(j => j.JobDefId == JobDefIds.Drink);
        Assert.Equal(thirstyDwarf.Id, drinkJob.AssignedDwarfId);
    }

    [Fact]
    public void Tick_Cancels_Pending_Drink_Job_When_Thirst_Recovers()
    {
        var (_, er, js, sim) = CreateSim();
        var dwarf = new Dwarf(1, "Urist", new Vec3i(0, 0, 0));
        er.Register(dwarf);
        dwarf.Needs.Thirst.SetLevel(0.05f);

        sim.Tick(0.1f);
        Assert.Contains(js.GetPendingJobs(), job => job.JobDefId == JobDefIds.Drink && job.AssignedDwarfId == dwarf.Id);

        dwarf.Needs.Thirst.SetLevel(1.0f);
        sim.Tick(0.1f);

        Assert.DoesNotContain(js.GetAllJobs(), job => job.JobDefId == JobDefIds.Drink && job.AssignedDwarfId == dwarf.Id);
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

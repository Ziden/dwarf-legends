using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Jobs.Strategies;
using DwarfFortress.GameLogic.World;
using DwarfFortress.GameLogic.Tests;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase5Tests;

public sealed class JobSystemTests
{
    private sealed class WaitOnlyStrategy : IJobStrategy
    {
        public const string DefId = "test_wait_only";

        public bool Completed { get; private set; }

        public string JobDefId => DefId;

        public bool CanExecute(Job job, int dwarfId, GameContext ctx) => true;

        public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
            => [new WaitStep(0.5f)];

        public void OnInterrupt(Job job, int dwarfId, GameContext ctx)
        {
        }

        public void OnComplete(Job job, int dwarfId, GameContext ctx)
        {
            Completed = true;
        }
    }

    private static (JobSystem js, EntityRegistry er, WorldMap wm, GameSimulation sim) CreateSim()
    {
        var logger = new Fakes.TestLogger();
        var ds     = new Fakes.InMemoryDataSource();
        TestFixtures.AddCoreData(ds);
        var er = new EntityRegistry();
        var js = new JobSystem();
        var wm = new WorldMap();
        var sim = TestFixtures.CreateSimulation(logger, ds, er, js, wm);
        wm.SetDimensions(16, 16, 4);
        return (js, er, wm, sim);
    }

    [Fact]
    public void CreateJob_Returns_Job_With_Pending_Status()
    {
        var (js, _, _, _) = CreateSim();

        var job = js.CreateJob(JobDefIds.MineTile, new Vec3i(5, 5, 0));

        Assert.Equal(JobStatus.Pending, job.Status);
    }

    [Fact]
    public void CreateJob_Returns_Job_With_Correct_DefId()
    {
        var (js, _, _, _) = CreateSim();

        var job = js.CreateJob(JobDefIds.HaulItem, new Vec3i(0, 0, 0));

        Assert.Equal(JobDefIds.HaulItem, job.JobDefId);
    }

    [Fact]
    public void CreateJob_Emits_JobCreatedEvent()
    {
        var (js, _, _, sim) = CreateSim();
        string? createdDefId = null;
        sim.Context.EventBus.On<JobCreatedEvent>(e => createdDefId = e.JobDefId);

        js.CreateJob(JobDefIds.MineTile, new Vec3i(0, 0, 0));

        Assert.Equal(JobDefIds.MineTile, createdDefId);
    }

    [Fact]
    public void GetPendingJobs_Contains_New_Jobs()
    {
        var (js, _, _, _) = CreateSim();

        js.CreateJob(JobDefIds.MineTile, new Vec3i(0, 0, 0));
        js.CreateJob(JobDefIds.CutTree,  new Vec3i(1, 0, 0));

        Assert.Equal(2, js.GetPendingJobs().Count());
    }

    [Fact]
    public void CancelJob_Removes_Job_From_AllJobs()
    {
        var (js, _, _, _) = CreateSim();
        var job = js.CreateJob(JobDefIds.MineTile, new Vec3i(0, 0, 0));

        js.CancelJob(job.Id);

        Assert.Null(js.GetJob(job.Id));
    }

    [Fact]
    public void CancelJob_Emits_JobCancelledEvent()
    {
        var (js, _, _, sim) = CreateSim();
        int? cancelledId = null;
        sim.Context.EventBus.On<JobCancelledEvent>(e => cancelledId = e.JobId);
        var job = js.CreateJob(JobDefIds.MineTile, new Vec3i(0, 0, 0));

        js.CancelJob(job.Id);

        Assert.Equal(job.Id, cancelledId);
    }

    [Fact]
    public void GetJob_Returns_Null_For_Unknown_Id()
    {
        var (js, _, _, _) = CreateSim();

        Assert.Null(js.GetJob(99999));
    }

    [Fact]
    public void JobSystem_Completes_Wait_Step_Via_Action_Executor()
    {
        var (js, er, _, sim) = CreateSim();
        var strategy = new WaitOnlyStrategy();
        js.RegisterStrategy(strategy);

        var dwarf = new Dwarf(er.NextId(), "Waiter", new Vec3i(1, 1, 0));
        er.Register(dwarf);

        JobCompletedEvent? completed = null;
        sim.Context.EventBus.On<JobCompletedEvent>(e =>
        {
            if (e.JobDefId == WaitOnlyStrategy.DefId)
                completed = e;
        });

        js.CreateJob(WaitOnlyStrategy.DefId, dwarf.Position.Position, priority: 5);

        sim.Tick(0.25f);
        sim.Tick(0.25f);
        sim.Tick(0.25f);

        Assert.True(strategy.Completed);
        Assert.Equal(dwarf.Id, completed?.DwarfId);
    }

    [Fact]
    public void DesignateMineCommand_Creates_MineTile_Jobs_For_Designated_Tiles()
    {
        var (js, _, wm, sim) = CreateSim();

        // Tiles must be solid walls; the command sets IsDesignated and creates jobs
        var pos1 = new Vec3i(3, 3, 0);
        var pos2 = new Vec3i(4, 3, 0);
        wm.SetTile(pos1, new TileData { TileDefId = TileDefIds.GraniteWall });
        wm.SetTile(pos2, new TileData { TileDefId = TileDefIds.GraniteWall });

        sim.Context.Commands.Dispatch(new DesignateMineCommand(pos1, pos2));

        var pending = js.GetPendingJobs().ToList();
        Assert.Equal(2, pending.Count);
        Assert.All(pending, j => Assert.Equal(JobDefIds.MineTile, j.JobDefId));
    }

    [Fact]
    public void DesignateMineCommand_Does_Not_Create_Mine_Jobs_For_Trees()
    {
        var (js, _, wm, sim) = CreateSim();

        var pos = new Vec3i(3, 3, 0);
        wm.SetTile(pos, new TileData { TileDefId = TileDefIds.Tree, MaterialId = "wood", IsPassable = false });

        sim.Context.Commands.Dispatch(new DesignateMineCommand(pos, pos));

        Assert.DoesNotContain(js.GetPendingJobs(), j => j.JobDefId == JobDefIds.MineTile && j.TargetPos == pos);
        Assert.False(wm.GetTile(pos).IsDesignated);
    }

    [Fact]
    public void DesignateMineCommand_Does_Not_Create_Mine_Jobs_For_Hidden_Walls()
    {
        var (js, _, wm, sim) = CreateSim();
        var target = new Vec3i(6, 6, 0);

        wm.SetTile(target, new TileData { TileDefId = TileDefIds.GraniteWall, IsPassable = false });
        wm.SetTile(target + Vec3i.North, new TileData { TileDefId = TileDefIds.GraniteWall, IsPassable = false });
        wm.SetTile(target + Vec3i.South, new TileData { TileDefId = TileDefIds.GraniteWall, IsPassable = false });
        wm.SetTile(target + Vec3i.East, new TileData { TileDefId = TileDefIds.GraniteWall, IsPassable = false });
        wm.SetTile(target + Vec3i.West, new TileData { TileDefId = TileDefIds.GraniteWall, IsPassable = false });

        sim.Context.Commands.Dispatch(new DesignateMineCommand(target, target));

        Assert.DoesNotContain(js.GetPendingJobs(), j => j.JobDefId == JobDefIds.MineTile && j.TargetPos == target);
        Assert.False(wm.GetTile(target).IsDesignated);
    }

    [Fact]
    public void DesignateMineCommand_Allows_Chain_From_Exposed_Wall_Through_Designation()
    {
        var (js, _, wm, sim) = CreateSim();
        var start = new Vec3i(3, 4, 0);
        var end = new Vec3i(5, 4, 0);

        wm.SetTile(new Vec3i(2, 4, 0), new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            MaterialId = "granite",
            IsPassable = true,
        });
        wm.SetTile(start, new TileData { TileDefId = TileDefIds.GraniteWall, IsPassable = false });
        wm.SetTile(new Vec3i(4, 4, 0), new TileData { TileDefId = TileDefIds.GraniteWall, IsPassable = false });
        wm.SetTile(end, new TileData { TileDefId = TileDefIds.GraniteWall, IsPassable = false });

        sim.Context.Commands.Dispatch(new DesignateMineCommand(start, end));

        var targets = js.GetPendingJobs()
            .Where(j => j.JobDefId == JobDefIds.MineTile)
            .Select(j => j.TargetPos)
            .ToHashSet();

        Assert.Contains(start, targets);
        Assert.Contains(new Vec3i(4, 4, 0), targets);
        Assert.Contains(end, targets);
    }

    [Fact]
    public void Newly_Exposed_Damp_Wall_Auto_Cancels_Mining_Designation()
    {
        var (js, _, wm, sim) = CreateSim();
        var open = new Vec3i(2, 4, 1);
        var firstWall = new Vec3i(3, 4, 1);
        var dampWall = new Vec3i(4, 4, 1);

        wm.SetTile(open, new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            MaterialId = "granite",
            IsPassable = true,
        });
        wm.SetTile(firstWall, new TileData { TileDefId = TileDefIds.GraniteWall, MaterialId = "granite", IsPassable = false });
        wm.SetTile(dampWall, new TileData { TileDefId = TileDefIds.GraniteWall, MaterialId = "limestone", IsPassable = false, IsAquifer = true });
        wm.SetTile(dampWall + Vec3i.North, new TileData { TileDefId = TileDefIds.GraniteWall, MaterialId = "granite", IsPassable = false });
        wm.SetTile(dampWall + Vec3i.South, new TileData { TileDefId = TileDefIds.GraniteWall, MaterialId = "granite", IsPassable = false });
        wm.SetTile(dampWall + Vec3i.East, new TileData { TileDefId = TileDefIds.GraniteWall, MaterialId = "granite", IsPassable = false });

        MiningDesignationSafetyCancelledEvent? cancelled = null;
        sim.Context.EventBus.On<MiningDesignationSafetyCancelledEvent>(e => cancelled = e);

        sim.Context.Commands.Dispatch(new DesignateMineCommand(firstWall, dampWall));

        Assert.Contains(js.GetPendingJobs(), job => job.TargetPos == dampWall);
        Assert.True(wm.GetTile(dampWall).IsDesignated);

        wm.SetTile(firstWall, new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            MaterialId = "granite",
            IsPassable = true,
        });

        Assert.DoesNotContain(js.GetAllJobs(), job => job.TargetPos == dampWall && job.Status is JobStatus.Pending or JobStatus.InProgress);
        Assert.False(wm.GetTile(dampWall).IsDesignated);
        Assert.Equal(dampWall, cancelled?.Position);
    }

    [Fact]
    public void Newly_Exposed_Warm_Wall_Auto_Cancels_Mining_Designation()
    {
        var (js, _, wm, sim) = CreateSim();
        var open = new Vec3i(2, 4, 1);
        var firstWall = new Vec3i(3, 4, 1);
        var warmWall = new Vec3i(4, 4, 1);
        var magma = new Vec3i(4, 4, 2);

        wm.SetTile(open, new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            MaterialId = "granite",
            IsPassable = true,
        });
        wm.SetTile(firstWall, new TileData { TileDefId = TileDefIds.GraniteWall, MaterialId = "granite", IsPassable = false });
        wm.SetTile(warmWall, new TileData { TileDefId = TileDefIds.GraniteWall, MaterialId = "granite", IsPassable = false });
        wm.SetTile(warmWall + Vec3i.North, new TileData { TileDefId = TileDefIds.GraniteWall, MaterialId = "granite", IsPassable = false });
        wm.SetTile(warmWall + Vec3i.South, new TileData { TileDefId = TileDefIds.GraniteWall, MaterialId = "granite", IsPassable = false });
        wm.SetTile(warmWall + Vec3i.East, new TileData { TileDefId = TileDefIds.GraniteWall, MaterialId = "granite", IsPassable = false });
        wm.SetTile(magma, new TileData
        {
            TileDefId = TileDefIds.Magma,
            MaterialId = "magma",
            IsPassable = true,
            FluidType = FluidType.Magma,
            FluidLevel = 7,
        });

        MiningDesignationSafetyCancelledEvent? cancelled = null;
        sim.Context.EventBus.On<MiningDesignationSafetyCancelledEvent>(e => cancelled = e);

        sim.Context.Commands.Dispatch(new DesignateMineCommand(firstWall, warmWall));

        Assert.Contains(js.GetPendingJobs(), job => job.TargetPos == warmWall);
        Assert.True(wm.GetTile(warmWall).IsDesignated);

        wm.SetTile(firstWall, new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            MaterialId = "granite",
            IsPassable = true,
        });

        Assert.DoesNotContain(js.GetAllJobs(), job => job.TargetPos == warmWall && job.Status is JobStatus.Pending or JobStatus.InProgress);
        Assert.False(wm.GetTile(warmWall).IsDesignated);
        Assert.Equal(warmWall, cancelled?.Position);
        Assert.Equal("warm", cancelled?.HazardKind);
    }

    [Fact]
    public void Jobs_Are_Assigned_Unique_Ids()
    {
        var (js, _, _, _) = CreateSim();

        var job1 = js.CreateJob(JobDefIds.MineTile, new Vec3i(0, 0, 0));
        var job2 = js.CreateJob(JobDefIds.MineTile, new Vec3i(1, 0, 0));

        Assert.NotEqual(job1.Id, job2.Id);
    }

    [Fact]
    public void CutTree_Jobs_Fall_Back_To_Any_Idle_Dwarf_When_No_Woodcutter_Is_Idle()
    {
        var (js, er, wm, sim) = CreateSim();
        js.RegisterStrategy(new CutTreeStrategy());

        var treePos = new Vec3i(5, 5, 0);
        wm.SetTile(treePos, new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = "wood",
            TreeSpeciesId = "oak",
            IsPassable = false,
        });
        wm.SetTile(treePos + Vec3i.South, new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            MaterialId = "granite",
            IsPassable = true,
        });

        var dwarf = new Dwarf(er.NextId(), "Idler", treePos + new Vec3i(-2, 0, 0));
        dwarf.Labors.DisableAll();
        dwarf.Labors.Enable(LaborIds.Hauling);
        er.Register(dwarf);

        sim.Context.Commands.Dispatch(new DesignateCutTreesCommand(treePos, treePos));
        js.Tick(0.1f);

        var job = Assert.Single(js.GetAllJobs().Where(j => j.JobDefId == JobDefIds.CutTree));
        Assert.Equal(JobStatus.InProgress, job.Status);
        Assert.Equal(dwarf.Id, job.AssignedDwarfId);
    }

    [Fact]
    public void CutTree_Jobs_Stay_Pending_Until_Front_Trees_Open_A_Route()
    {
        var (js, er, wm, sim) = CreateSim();
        js.RegisterStrategy(new CutTreeStrategy());

        var failures = new List<JobFailedEvent>();
        sim.Context.EventBus.On<JobFailedEvent>(failure => failures.Add(failure));

        for (var x = 0; x < wm.Width; x++)
        for (var y = 0; y < wm.Height; y++)
        {
            wm.SetTile(new Vec3i(x, y, 0), new TileData
            {
                TileDefId = TileDefIds.GraniteWall,
                MaterialId = "granite",
                IsPassable = false,
            });
        }

        foreach (var floorPos in new[]
                 {
                     new Vec3i(3, 6, 0),
                     new Vec3i(4, 6, 0),
                     new Vec3i(6, 5, 0),
                     new Vec3i(6, 6, 0),
                     new Vec3i(6, 7, 0),
                     new Vec3i(7, 5, 0),
                     new Vec3i(7, 7, 0),
                 })
        {
            wm.SetTile(floorPos, new TileData
            {
                TileDefId = TileDefIds.StoneFloor,
                MaterialId = "granite",
                IsPassable = true,
            });
        }

        var frontTreePos = new Vec3i(5, 6, 0);
        var blockedTreePos = new Vec3i(7, 6, 0);

        foreach (var treePos in new[] { frontTreePos, blockedTreePos })
        {
            wm.SetTile(treePos, new TileData
            {
                TileDefId = TileDefIds.Tree,
                MaterialId = "wood",
                TreeSpeciesId = "oak",
                IsPassable = false,
                IsDesignated = true,
            });
        }

        var dwarf = new Dwarf(er.NextId(), "Woodcutter", new Vec3i(3, 6, 0));
        dwarf.Labors.DisableAll();
        dwarf.Labors.Enable(LaborIds.WoodCutting);
        er.Register(dwarf);

        js.CreateJob(JobDefIds.CutTree, blockedTreePos, priority: 5);
        js.CreateJob(JobDefIds.CutTree, frontTreePos, priority: 4);

        sim.Tick(0.1f);

        Assert.Contains(js.GetPendingJobs(), job => job.JobDefId == JobDefIds.CutTree && job.TargetPos == blockedTreePos);
        Assert.Contains(js.GetAllJobs(), job => job.JobDefId == JobDefIds.CutTree && job.TargetPos == frontTreePos && job.Status == JobStatus.InProgress);

        for (var tick = 0; tick < 400; tick++)
        {
            sim.Tick(0.1f);
            if (wm.GetTile(frontTreePos).TileDefId != TileDefIds.Tree
                && wm.GetTile(blockedTreePos).TileDefId != TileDefIds.Tree)
                break;
        }

        Assert.Empty(failures.Where(failure => failure.Reason == "no_path"));
        Assert.NotEqual(TileDefIds.Tree, wm.GetTile(frontTreePos).TileDefId);
        Assert.NotEqual(TileDefIds.Tree, wm.GetTile(blockedTreePos).TileDefId);
        Assert.DoesNotContain(js.GetAllJobs(), job => job.JobDefId == JobDefIds.CutTree && job.Status is JobStatus.Pending or JobStatus.InProgress);
    }
}

using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.Tests.Fakes;
using DwarfFortress.GameLogic.World;
using DwarfFortress.WorldGen.Ids;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

/// <summary>
/// Tests for PathFollowSystem real movement and auto idle-job creation.
/// </summary>
public sealed class MovementTests
{
    private sealed class PickupAndPlaceStrategy : IJobStrategy
    {
        public const string DefId = "test_pickup_place";

        public string JobDefId => DefId;

        public bool CanExecute(Job job, int dwarfId, GameContext ctx) => job.EntityId >= 0;

        public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
            =>
            [
                new PickUpItemStep(job.EntityId),
                new MoveToStep(job.TargetPos),
                new PlaceItemStep(job.EntityId, job.TargetPos),
            ];

        public void OnInterrupt(Job job, int dwarfId, GameContext ctx)
        {
        }

        public void OnComplete(Job job, int dwarfId, GameContext ctx)
        {
        }
    }

    private static (GameSimulation sim, EntityRegistry er, JobSystem js, WorldMap map) Build()
    {
        var (sim, map, er, js, _) = TestFixtures.BuildFullSim();
        return (sim, er, js, map);
    }

    [Fact]
    public void Dwarf_Position_Changes_Over_Ticks_When_Craft_Job_Assigned()
    {
        var (sim, er, js, _) = Build();

        var dwarf = new Dwarf(er.NextId(), "Walker", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        js.CreateJob(JobDefIds.Craft, new Vec3i(10, 10, 0));

        Vec3i startPos = dwarf.Position.Position;

        // Run enough ticks for pathfinding to kick in and move the dwarf
        for (int i = 0; i < 200; i++) sim.Tick(0.1f);

        Assert.NotEqual(startPos, dwarf.Position.Position);
    }

    [Fact]
    public void Dwarf_Reaches_Target_After_Enough_Ticks()
    {
        var (sim, er, js, _) = Build();

        var target = new Vec3i(5, 5, 0);

        var dwarf = new Dwarf(er.NextId(), "Traveler", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        js.CreateJob(JobDefIds.Craft, target);

        for (int i = 0; i < 150; i++) sim.Tick(0.1f);

        Assert.Equal(target, dwarf.Position.Position);
    }

    [Fact]
    public void JobSystem_Creates_Idle_Job_For_Dwarf_With_No_Work()
    {
        var (sim, er, js, _) = Build();

        var dwarf = new Dwarf(er.NextId(), "Idle", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        // Tick once — JobSystem should auto-assign an idle job
        sim.Tick(0.1f);

        var assignedJobs = js.GetAllJobs()
            .Where(j => j.AssignedDwarfId == dwarf.Id)
            .ToList();

        Assert.True(assignedJobs.Any(j => j.JobDefId == "idle"),
            "Expected at least one idle job assigned to the dwarf, but none was found.");
    }

    [Fact]
    public void JobSystem_Emits_JobFailed_When_Target_Is_Unreachable()
    {
        var (sim, er, js, map) = Build();

        // Surround the target with solid walls so pathfinding fails after assignment.
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            map.SetTile(new Vec3i(1 + dx, 1 + dy, 0), new TileData
            {
                TileDefId  = TileDefIds.GraniteWall,
                MaterialId = "granite",
                IsPassable = false,
            });
        }

        var dwarf = new Dwarf(er.NextId(), "Miner", new Vec3i(5, 5, 0));
        er.Register(dwarf);

        JobFailedEvent? failedEv = null;
        sim.Context.EventBus.On<JobFailedEvent>(ev => failedEv = ev);

        js.CreateJob(JobDefIds.Craft, new Vec3i(1, 1, 0));

        for (int i = 0; i < 50; i++) sim.Tick(0.1f);

        Assert.NotNull(failedEv);
    }

    [Fact]
    public void JobSystem_Executes_Pickup_And_Place_Steps_Via_Action_Executor()
    {
        var (sim, er, js, _) = Build();
        js.RegisterStrategy(new PickupAndPlaceStrategy());

        var dwarf = new Dwarf(er.NextId(), "Carrier", new Vec3i(5, 5, 0));
        er.Register(dwarf);

        var items = sim.Context.Get<ItemSystem>();
        var item = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, dwarf.Position.Position);
        var dropPos = dwarf.Position.Position + Vec3i.East;

        js.CreateJob(PickupAndPlaceStrategy.DefId, dropPos, priority: 5, entityId: item.Id);

        for (var i = 0; i < 6; i++)
            sim.Tick(0.5f);

        Assert.True(items.TryGetItem(item.Id, out var movedItem));
        Assert.NotNull(movedItem);
        Assert.Equal(dropPos, movedItem!.Position.Position);
        Assert.Equal(-1, movedItem.CarriedByEntityId);
        Assert.Equal(ItemCarryMode.None, movedItem.CarryMode);
    }

    [Fact]
    public void Dwarf_Does_Not_Enter_Tile_Occupied_By_Another_Dwarf()
    {
        var (sim, er, js, map) = Build();

        var mover = new Dwarf(er.NextId(), "Mover", new Vec3i(0, 1, 0));
        var blocker = new Dwarf(er.NextId(), "Blocker", new Vec3i(1, 1, 0));
        er.Register(mover);
        er.Register(blocker);

        for (var x = 0; x <= 2; x++)
        {
            map.SetTile(new Vec3i(x, 0, 0), new TileData { TileDefId = TileDefIds.GraniteWall, MaterialId = "granite", IsPassable = false });
            map.SetTile(new Vec3i(x, 2, 0), new TileData { TileDefId = TileDefIds.GraniteWall, MaterialId = "granite", IsPassable = false });
        }

        js.CreateJob(JobDefIds.Craft, new Vec3i(2, 1, 0));

        for (var i = 0; i < 40; i++)
            sim.Tick(0.25f);

        Assert.Equal(new Vec3i(0, 1, 0), mover.Position.Position);
        Assert.NotEqual(blocker.Position.Position, mover.Position.Position);
    }

    [Fact]
    public void JobSystem_Replans_From_Current_Position_After_External_Move()
    {
        var (sim, er, js, _) = Build();

        var dwarf = new Dwarf(er.NextId(), "Walker", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        js.CreateJob(JobDefIds.Craft, new Vec3i(6, 0, 0));
        sim.Tick(0.1f);

        var displacedFrom = dwarf.Position.Position;
        var displacedTo = new Vec3i(0, 5, 0);
        dwarf.Position.Position = displacedTo;
        sim.EventBus.Emit(new EntityMovedEvent(dwarf.Id, displacedFrom, displacedTo));

        EntityMovedEvent? resumedMove = null;
        sim.Context.EventBus.On<EntityMovedEvent>(ev =>
        {
            if (ev.EntityId == dwarf.Id && ev.OldPos == displacedTo && resumedMove is null)
                resumedMove = ev;
        });

        for (var i = 0; i < 20 && resumedMove is null; i++)
            sim.Tick(0.5f);

        Assert.NotNull(resumedMove);
        Assert.Equal(1, resumedMove!.Value.OldPos.ManhattanDistanceTo(resumedMove.Value.NewPos));
    }

    [Fact]
    public void JobSystem_Repaths_Back_To_Work_Tile_When_Displaced_During_Work()
    {
        var (sim, er, js, _) = Build();

        var dwarf = new Dwarf(er.NextId(), "Crafter", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        var job = js.CreateJob(JobDefIds.Craft, new Vec3i(0, 0, 0));

        sim.Tick(0.05f);

        var displacedTo = new Vec3i(0, 3, 0);
        dwarf.Position.Position = displacedTo;
        sim.EventBus.Emit(new EntityMovedEvent(dwarf.Id, new Vec3i(0, 0, 0), displacedTo));

        sim.Tick(0.05f);

        Assert.NotEqual(JobStatus.Complete, job.Status);
        Assert.Equal(displacedTo, dwarf.Position.Position);

        for (var i = 0; i < 20 && dwarf.Position.Position != job.TargetPos; i++)
            sim.Tick(0.5f);

        Assert.Equal(job.TargetPos, dwarf.Position.Position);
    }

    [Fact]
    public void JobSystem_Records_Exact_Move_Segment_Duration_From_Speed()
    {
        var (sim, er, js, _) = Build();
        var movementPresentation = sim.Context.Get<MovementPresentationSystem>();

        var dwarf = new Dwarf(er.NextId(), "Stride", new Vec3i(0, 0, 0));
        dwarf.Stats.Speed.BaseValue = 2f;
        er.Register(dwarf);

        js.CreateJob(JobDefIds.Craft, new Vec3i(2, 0, 0));

        sim.Tick(0.25f);
        Assert.False(movementPresentation.TryGetEntitySegment(dwarf.Id, out _));

        sim.Tick(0.25f);

        Assert.True(movementPresentation.TryGetEntitySegment(dwarf.Id, out var segment));
        Assert.Equal(new Vec3i(0, 0, 0), segment.OldPos);
        Assert.Equal(new Vec3i(1, 0, 0), segment.NewPos);
        Assert.Equal(0.5f, segment.DurationSeconds, 3);
    }

    [Fact]
    public void JobSystem_Preserves_Carry_Without_Changing_Segment_Duration()
    {
        var (sim, er, js, _) = Build();
        var movementPresentation = sim.Context.Get<MovementPresentationSystem>();

        var dwarf = new Dwarf(er.NextId(), "Cadence", new Vec3i(0, 0, 0));
        dwarf.Stats.Speed.BaseValue = 2f;
        er.Register(dwarf);

        js.CreateJob(JobDefIds.Craft, new Vec3i(3, 0, 0));

        sim.Tick(0.6f);

        Assert.True(movementPresentation.TryGetEntitySegment(dwarf.Id, out var firstSegment));
        Assert.Equal(new Vec3i(0, 0, 0), firstSegment.OldPos);
        Assert.Equal(new Vec3i(1, 0, 0), firstSegment.NewPos);
        Assert.Equal(0.5f, firstSegment.DurationSeconds, 3);

        sim.Tick(0.4f);

        Assert.True(movementPresentation.TryGetEntitySegment(dwarf.Id, out var secondSegment));
        Assert.True(secondSegment.Sequence > firstSegment.Sequence);
        Assert.Equal(new Vec3i(1, 0, 0), secondSegment.OldPos);
        Assert.Equal(new Vec3i(2, 0, 0), secondSegment.NewPos);
        Assert.Equal(0.5f, secondSegment.DurationSeconds, 3);
    }

    [Fact]
    public void MovementPresentationSystem_Does_Not_Animate_NonAdjacent_Fallback_Moves()
    {
        var (sim, er, _, _) = Build();
        var movementPresentation = sim.Context.Get<MovementPresentationSystem>();

        var dwarf = new Dwarf(er.NextId(), "Blink", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        sim.Context.EventBus.Emit(new EntityMovedEvent(dwarf.Id, new Vec3i(0, 0, 0), new Vec3i(0, 3, 0)));

        Assert.True(movementPresentation.TryGetEntitySegment(dwarf.Id, out var segment));
        Assert.Equal(0f, segment.DurationSeconds);
    }

    [Fact]
    public void MovementPresentationSystem_Records_Item_Pickup_As_Jump_To_Carrier()
    {
        var (sim, er, _, _) = Build();
        var movementPresentation = sim.Context.Get<MovementPresentationSystem>();
        var items = sim.Context.Get<ItemSystem>();

        var dwarf = new Dwarf(er.NextId(), "Carrier", new Vec3i(4, 4, 0));
        er.Register(dwarf);
        var log = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, dwarf.Position.Position);

        Assert.True(items.PickUpItem(log.Id, dwarf.Id, dwarf.Position.Position, ItemCarryMode.Hauling));
        Assert.True(movementPresentation.TryGetItemSegment(log.Id, out var segment));

        Assert.Equal(MovementPresentationMotionKind.Jump, segment.MotionKind);
        Assert.Equal(MovementPresentationAnchorKind.Tile, segment.StartAnchor);
        Assert.Equal(MovementPresentationAnchorKind.Carrier, segment.EndAnchor);
        Assert.Equal(dwarf.Id, segment.EndAnchorEntityId);
        Assert.Equal(dwarf.Position.Position, segment.OldPos);
        Assert.Equal(dwarf.Position.Position, segment.NewPos);
        Assert.True(segment.DurationSeconds > 0f);
        Assert.True(segment.ArcHeight > 0f);
    }

    [Fact]
    public void MovementPresentationSystem_Records_Item_Drop_As_Jump_From_Carrier()
    {
        var (sim, er, _, _) = Build();
        var movementPresentation = sim.Context.Get<MovementPresentationSystem>();
        var items = sim.Context.Get<ItemSystem>();

        var dwarf = new Dwarf(er.NextId(), "Dropper", new Vec3i(5, 5, 0));
        er.Register(dwarf);
        var log = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, dwarf.Position.Position);

        Assert.True(items.PickUpItem(log.Id, dwarf.Id, dwarf.Position.Position, ItemCarryMode.Hauling));
        Assert.True(movementPresentation.TryGetItemSegment(log.Id, out var pickupSegment));

        items.MoveItem(log.Id, dwarf.Position.Position);

        Assert.True(movementPresentation.TryGetItemSegment(log.Id, out var dropSegment));
        Assert.True(dropSegment.Sequence > pickupSegment.Sequence);
        Assert.Equal(MovementPresentationMotionKind.Jump, dropSegment.MotionKind);
        Assert.Equal(MovementPresentationAnchorKind.Carrier, dropSegment.StartAnchor);
        Assert.Equal(dwarf.Id, dropSegment.StartAnchorEntityId);
        Assert.Equal(MovementPresentationAnchorKind.Tile, dropSegment.EndAnchor);
        Assert.Equal(dwarf.Position.Position, dropSegment.OldPos);
        Assert.Equal(dwarf.Position.Position, dropSegment.NewPos);
        Assert.True(dropSegment.DurationSeconds > 0f);
        Assert.True(dropSegment.ArcHeight > 0f);
    }

    [Fact]
    public void CutTreeStrategy_Records_New_Log_Fall_Segment()
    {
        var (sim, _, _, map) = Build();
        var movementPresentation = sim.Context.Get<MovementPresentationSystem>();
        var items = sim.Context.Get<ItemSystem>();
        var strategy = new DwarfFortress.GameLogic.Jobs.Strategies.CutTreeStrategy();
        var treePos = new Vec3i(8, 8, 0);

        map.SetTile(treePos, new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = MaterialIds.Wood,
            TreeSpeciesId = "oak",
            IsDesignated = true,
            IsPassable = false,
        });

        strategy.OnComplete(new Job(1, JobDefIds.CutTree, treePos), dwarfId: -1, sim.Context);

        var log = items.GetAllItems().Single(item => item.Position.Position == treePos);
        Assert.True(movementPresentation.TryGetItemSegment(log.Id, out var segment));
        Assert.Equal(MovementPresentationMotionKind.Linear, segment.MotionKind);
        Assert.Equal(treePos + Vec3i.Up, segment.OldPos);
        Assert.Equal(treePos, segment.NewPos);
        Assert.True(segment.DurationSeconds > 0f);
    }

    [Fact]
    public void CutTreeStrategy_Records_Fruit_Fall_Segment_For_Ripe_FruitTree()
    {
        var (sim, _, _, map) = Build();
        var movementPresentation = sim.Context.Get<MovementPresentationSystem>();
        var items = sim.Context.Get<ItemSystem>();
        var strategy = new DwarfFortress.GameLogic.Jobs.Strategies.CutTreeStrategy();
        var treePos = new Vec3i(9, 8, 0);

        map.SetTile(treePos, new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = MaterialIds.Wood,
            TreeSpeciesId = TreeSpeciesIds.Apple,
            PlantDefId = PlantSpeciesIds.AppleCanopy,
            PlantGrowthStage = PlantGrowthStages.Mature,
            PlantYieldLevel = 1,
            PlantSeedLevel = 1,
            IsDesignated = true,
            IsPassable = false,
        });

        strategy.OnComplete(new Job(1, JobDefIds.CutTree, treePos), dwarfId: -1, sim.Context);

        var apple = items.GetAllItems().Single(item => item.DefId == ItemDefIds.Apple && item.Position.Position == treePos);
        Assert.True(movementPresentation.TryGetItemSegment(apple.Id, out var segment));
        Assert.Equal(MovementPresentationMotionKind.Jump, segment.MotionKind);
        Assert.Equal(treePos + Vec3i.Up, segment.OldPos);
        Assert.Equal(treePos, segment.NewPos);
        Assert.True(segment.DurationSeconds > 0f);
        Assert.True(segment.ArcHeight > 0f);
    }

    [Fact]
    public void Fearful_Dwarf_Flees_One_Tile_Instead_Of_Teleporting()
    {
        var (sim, er, _, _) = Build();

        var dwarf = new Dwarf(er.NextId(), "Nervous", new Vec3i(10, 10, 0));
        dwarf.Attributes.SetLevel(AttributeIds.Courage, 1);
        er.Register(dwarf);

        var cat = new Creature(er.NextId(), DefIds.Cat, new Vec3i(11, 10, 0), 20f);
        er.Register(cat);

        DwarfFledFromAnimalEvent? fleeEvent = null;
        sim.Context.EventBus.On<DwarfFledFromAnimalEvent>(ev => fleeEvent = ev);

        for (var i = 0; i < 20 && fleeEvent is null; i++)
            sim.Tick(5f);

        Assert.NotNull(fleeEvent);
        Assert.Equal(1, fleeEvent!.Value.From.ManhattanDistanceTo(fleeEvent.Value.To));
    }

    [Fact]
    public void Fearful_Dwarf_Flee_Emits_Movement_Event_And_Updates_Spatial_Index()
    {
        var (sim, er, _, _) = Build();
        var spatial = sim.Context.Get<SpatialIndexSystem>();

        var dwarf = new Dwarf(er.NextId(), "Skittish", new Vec3i(10, 10, 0));
        dwarf.Attributes.SetLevel(AttributeIds.Courage, 1);
        er.Register(dwarf);

        var cat = new Creature(er.NextId(), DefIds.Cat, new Vec3i(11, 10, 0), 20f);
        er.Register(cat);

        EntityMovedEvent? movedEvent = null;
        sim.Context.EventBus.On<EntityMovedEvent>(ev =>
        {
            if (ev.EntityId == dwarf.Id)
                movedEvent = ev;
        });

        for (var i = 0; i < 20 && movedEvent is null; i++)
            sim.Tick(5f);

        Assert.NotNull(movedEvent);
        Assert.DoesNotContain(dwarf.Id, spatial.GetDwarvesAt(movedEvent!.Value.OldPos));
        Assert.Contains(dwarf.Id, spatial.GetDwarvesAt(movedEvent.Value.NewPos));
    }
}

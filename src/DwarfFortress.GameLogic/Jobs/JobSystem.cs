using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Jobs;

// ── Events ─────────────────────────────────────────────────────────────────

public record struct JobCreatedEvent    (int JobId, string JobDefId);
public record struct JobAssignedEvent   (int JobId, int DwarfId);
public record struct JobWorkStartedEvent(int JobId, int DwarfId, string JobDefId, string AnimationHint, int EntityId = -1, Vec3i TargetPos = default);
public record struct JobWorkStoppedEvent(int JobId, int DwarfId, string JobDefId, string AnimationHint, int EntityId = -1, Vec3i TargetPos = default);
public record struct JobCompletedEvent  (int JobId, int DwarfId, string JobDefId, int EntityId = -1, Vec3i TargetPos = default, int[]? ReservedItemIds = null);
public record struct JobFailedEvent     (int JobId, string Reason);
public record struct JobCancelledEvent  (int JobId);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages all jobs: creation, assignment, step execution, and completion.
/// Order 10 — after world and entity systems.
/// </summary>
public sealed class JobSystem : IGameSystem
{
    private enum MoveStepState : byte
    {
        Ready,
        Wait,
        Fail,
    }

    // ── IGameSystem ────────────────────────────────────────────────────────
    public string SystemId    => SystemIds.JobSystem;
    public int    UpdateOrder => 10;
    public bool   IsEnabled   { get; set; } = true;

    private readonly Dictionary<string, IJobStrategy>   _strategies   = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, Job>               _jobs         = new();
    private readonly Dictionary<int, Queue<ActionStep>> _stepQueues   = new();
    private readonly Dictionary<int, Queue<Vec3i>>      _pathQueues   = new();
    private readonly Dictionary<int, float>             _moveProgress = new();
    private readonly Dictionary<int, string>            _activeWorkAnimations = new();

    private int       _nextJobId = 1;
    private EventBus? _eventBus;
    private GameContext? _ctx;

    // ── IGameSystem ────────────────────────────────────────────────────────

    public void Initialize(GameContext ctx)
    {
        _eventBus = ctx.EventBus;
        _ctx      = ctx;

        ctx.Commands.Register<DesignateMineCommand>(OnDesignateMine);
        ctx.Commands.Register<DesignateCutTreesCommand>(OnDesignateCutTrees);
        ctx.Commands.Register<DesignateHarvestCommand>(OnDesignateHarvest);
        ctx.Commands.Register<CancelDesignationCommand>(OnCancelDesignation);
    }

    public void Tick(float delta)
    {
        var entityRegistry = _ctx!.Get<EntityRegistry>();

        // Assign pending jobs to idle dwarves
        AssignJobs(entityRegistry);

        // Progress in-progress jobs
        TickActiveJobs(delta, entityRegistry);
    }

    public void OnSave(SaveWriter w)
    {
        w.Write("nextJobId", _nextJobId);
        w.Write("jobs", _jobs.Values
            .Where(j => j.Status is JobStatus.Pending or JobStatus.InProgress)
            .Select(j => new JobDto
            {
                Id       = j.Id,
                JobDefId = j.JobDefId,
                X        = j.TargetPos.X,
                Y        = j.TargetPos.Y,
                Z        = j.TargetPos.Z,
                Priority = j.Priority,
                EntityId = j.EntityId,
            }).ToList());
    }

    public void OnLoad(SaveReader r)
    {
        _nextJobId = r.TryRead<int>("nextJobId");
        if (_nextJobId <= 0) _nextJobId = 1;

        _jobs.Clear();
        _stepQueues.Clear();
        _pathQueues.Clear();
        _moveProgress.Clear();
        _activeWorkAnimations.Clear();

        var jobs = r.TryRead<List<JobDto>>("jobs");
        if (jobs is null) return;

        foreach (var dto in jobs)
        {
            var job = new Job(dto.Id, dto.JobDefId, new Vec3i(dto.X, dto.Y, dto.Z), dto.Priority, dto.EntityId);
            _jobs[job.Id] = job;
        }
    }

    private sealed class JobDto
    {
        public int    Id       { get; set; }
        public string JobDefId { get; set; } = "";
        public int    X        { get; set; }
        public int    Y        { get; set; }
        public int    Z        { get; set; }
        public int    Priority { get; set; }
        public int    EntityId { get; set; } = -1;
    }

    // ── Strategy registration ──────────────────────────────────────────────

    public void RegisterStrategy(IJobStrategy strategy)
        => _strategies[strategy.JobDefId] = strategy;

    // ── Job creation ───────────────────────────────────────────────────────

    public Job CreateJob(string jobDefId, Vec3i targetPos, int priority = 0, int entityId = -1)
    {
        var job = new Job(_nextJobId++, jobDefId, targetPos, priority, entityId);
        _jobs[job.Id] = job;
        _eventBus?.Emit(new JobCreatedEvent(job.Id, job.JobDefId));
        return job;
    }

    public Job? GetJob(int jobId) =>
        _jobs.TryGetValue(jobId, out var j) ? j : null;

    public Job? GetAssignedJob(int dwarfId)
        => _jobs.Values
            .Where(j => j.AssignedDwarfId == dwarfId && j.Status == JobStatus.InProgress)
            .OrderByDescending(j => j.Priority)
            .FirstOrDefault();

    public string DescribeCurrentStep(int jobId)
    {
        if (!_stepQueues.TryGetValue(jobId, out var steps) || steps.Count == 0)
            return "finishing";

        return DescribeStep(steps.Peek());
    }

    public void CancelJob(int jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return;

        if (job.IsAssigned && _ctx is not null)
        {
            if (_strategies.TryGetValue(job.JobDefId, out var strat))
                strat.OnInterrupt(job, job.AssignedDwarfId, _ctx);
        }

        StopWorkAnimation(job);
        job.Status = JobStatus.Cancelled;
        _stepQueues.Remove(jobId);
        _pathQueues.Remove(jobId);
        _moveProgress.Remove(jobId);
        _jobs.Remove(jobId);
        _eventBus?.Emit(new JobCancelledEvent(jobId));
    }

    public IEnumerable<Job> GetPendingJobs() =>
        _jobs.Values.Where(j => j.Status == JobStatus.Pending);

    public IEnumerable<Job> GetAllJobs() => _jobs.Values;

    private static string DescribeStep(ActionStep step) => step switch
    {
        MoveToStep move   => $"moving to {move.Target.X},{move.Target.Y},{move.Target.Z}",
        WorkAtStep work   => string.IsNullOrWhiteSpace(work.AnimationHint)
            ? "working"
            : work.AnimationHint.Replace('_', ' '),
        WaitStep          => "waiting",
        PickUpItemStep    => "picking up item",
        PlaceItemStep put => $"placing item at {put.Target.X},{put.Target.Y},{put.Target.Z}",
        _                 => "processing",
    };

    // ── Private: assignment ────────────────────────────────────────────────

    // Reusable list for collecting pending jobs — avoids per-tick allocations
    private readonly List<Job> _pendingJobBuffer = new();
    // Reusable list for collecting idle dwarves — avoids per-tick allocations
    private readonly List<Dwarf> _idleDwarfBuffer = new();

    private void AssignJobs(EntityRegistry registry)
    {
        // Collect idle dwarves without LINQ allocations
        _idleDwarfBuffer.Clear();
        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            if (!dwarf.Health.IsConscious) continue;
            if (IsDwarfWorking(dwarf.Id)) continue;
            _idleDwarfBuffer.Add(dwarf);
        }

        // Collect pending jobs sorted by priority without LINQ allocations
        _pendingJobBuffer.Clear();
        foreach (var job in _jobs.Values)
        {
            if (job.Status == JobStatus.Pending)
                _pendingJobBuffer.Add(job);
        }
        _pendingJobBuffer.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        foreach (var job in _pendingJobBuffer)
        {
            if (!_strategies.TryGetValue(job.JobDefId, out var strat)) continue;

            var candidate = FindCandidateDwarf(job, strat, _idleDwarfBuffer);

            if (candidate is null) continue;

            // Check Fears Water trait - refuse job if target is near water
            if (candidate.Traits.HasTrait(TraitIds.FearsWater) && IsJobNearWater(job))
            {
                _eventBus?.Emit(new Systems.JobRefusedEvent(candidate.Id, job.Id, TraitIds.FearsWater,
                    $"{candidate.FirstName} refuses to work near water."));
                continue;
            }

            var steps = strat.GetSteps(job, candidate.Id, _ctx!);
            _stepQueues[job.Id] = new Queue<ActionStep>(steps);

            job.Status          = JobStatus.InProgress;
            job.AssignedDwarfId = candidate.Id;
            _idleDwarfBuffer.Remove(candidate);

            _eventBus?.Emit(new JobAssignedEvent(job.Id, candidate.Id));
        }

        // Any dwarf still idle and with no idle job gets a new idle job
        foreach (var dwarf in _idleDwarfBuffer)
        {
            if (HasActiveJob(dwarf.Id, JobDefIds.Idle)) continue;

            var idleJob = CreateJob(JobDefIds.Idle, dwarf.Position.Position, priority: -100);

            if (_strategies.TryGetValue(JobDefIds.Idle, out var idleStrat))
            {
                var steps = idleStrat.GetSteps(idleJob, dwarf.Id, _ctx!);
                _stepQueues[idleJob.Id] = new Queue<ActionStep>(steps);
                idleJob.Status          = JobStatus.InProgress;
                idleJob.AssignedDwarfId = dwarf.Id;
                _eventBus?.Emit(new JobAssignedEvent(idleJob.Id, dwarf.Id));
            }
        }
    }

    /// <summary>Check if a dwarf currently has an in-progress job.</summary>
    private bool IsDwarfWorking(int dwarfId)
    {
        foreach (var job in _jobs.Values)
        {
            if (job.AssignedDwarfId == dwarfId && job.Status == JobStatus.InProgress)
                return true;
        }
        return false;
    }

    /// <summary>Check if a dwarf has a job with the given definition in pending or in-progress state.</summary>
    private bool HasActiveJob(int dwarfId, string jobDefId)
    {
        foreach (var job in _jobs.Values)
        {
            if (job.AssignedDwarfId == dwarfId && job.JobDefId == jobDefId
                && (job.Status is JobStatus.Pending or JobStatus.InProgress))
                return true;
        }
        return false;
    }

    private Dwarf? FindCandidateDwarf(Job job, IJobStrategy strat, IReadOnlyList<Dwarf> idleDwarves)
    {
        // Survival jobs are basic self-preservation and should never be blocked by labor toggles.
        if (IsSurvivalJob(job.JobDefId))
            return idleDwarves.FirstOrDefault(d => strat.CanExecute(job, d.Id, _ctx!));

        var requiredLabor = GetRequiredLabor(job.JobDefId);
        var skilledCandidate = idleDwarves.FirstOrDefault(d =>
            d.Labors.IsEnabled(requiredLabor) &&
            strat.CanExecute(job, d.Id, _ctx!));

        if (skilledCandidate is not null)
            return skilledCandidate;

        if (!string.Equals(job.JobDefId, JobDefIds.CutTree, StringComparison.OrdinalIgnoreCase))
            return null;

        return idleDwarves.FirstOrDefault(d => strat.CanExecute(job, d.Id, _ctx!));
    }

    private static bool IsSurvivalJob(string jobDefId) =>
        string.Equals(jobDefId, JobDefIds.Eat, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(jobDefId, JobDefIds.Drink, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(jobDefId, JobDefIds.Sleep, StringComparison.OrdinalIgnoreCase);

    private bool IsJobNearWater(Job job)
    {
        var map = _ctx!.Get<WorldMap>();
        var target = job.TargetPos;

        // Check if target tile or any adjacent tile has water
        if (TileHasWater(map, target)) return true;

        foreach (var neighbour in target.Neighbours6())
        {
            if (map.IsInBounds(neighbour) && TileHasWater(map, neighbour))
                return true;
        }

        return false;
    }

    private static bool TileHasWater(WorldMap map, Vec3i pos)
    {
        var tile = map.GetTile(pos);
        return tile.FluidType == FluidType.Water || tile.TileDefId == World.TileDefIds.Water;
    }

    private void TickActiveJobs(float delta, EntityRegistry registry)
    {
        var active = _jobs.Values
            .Where(j => j.Status == JobStatus.InProgress)
            .ToList();

        foreach (var job in active)
        {
            if (!_stepQueues.TryGetValue(job.Id, out var steps) || steps.Count == 0)
            {
                CompleteJob(job);
                continue;
            }

            var step = steps.Peek();

            switch (step)
            {
                case WorkAtStep work:
                    if (!EnsureWorkPosition(job, work, steps, registry))
                    {
                        StopWorkAnimation(job);
                        break;
                    }

                    StartWorkAnimation(job, work);
                    job.WorkProgress += delta;
                    if (job.WorkProgress >= work.Duration)
                    {
                        StopWorkAnimation(job);
                        steps.Dequeue();
                        job.WorkProgress = 0;
                    }
                    break;

                case WaitStep wait:
                    StopWorkAnimation(job);
                    job.WorkProgress += delta;
                    if (job.WorkProgress >= wait.Duration)
                    {
                        steps.Dequeue();
                        job.WorkProgress = 0;
                    }
                    break;

                case MoveToStep move:
                    StopWorkAnimation(job);
                    TickMoveStep(job, move, delta, steps, registry);
                    break;

                case PickUpItemStep pickup:
                    StopWorkAnimation(job);
                    var pickupItemSystem = _ctx!.TryGet<ItemSystem>();
                    var pickupEntity = registry.TryGetById(job.AssignedDwarfId);
                    if (pickupItemSystem is not null && pickupEntity is not null)
                    {
                        var carrierPos = pickupEntity.Components.Get<PositionComponent>().Position;
                        pickupItemSystem.PickUpItem(pickup.ItemEntityId, job.AssignedDwarfId, carrierPos);
                    }
                    steps.Dequeue();
                    break;

                case PlaceItemStep place:
                    StopWorkAnimation(job);
                    var itemSys = _ctx!.TryGet<ItemSystem>();
                    if (itemSys is not null)
                    {
                        if (place.ContainerBuildingId >= 0)
                            itemSys.StoreItemInBuilding(place.ItemEntityId, place.ContainerBuildingId, place.Target);
                        else
                            itemSys.MoveItem(place.ItemEntityId, place.Target);
                    }
                    steps.Dequeue();
                    break;

                default:
                    StopWorkAnimation(job);
                    steps.Dequeue();
                    break;
            }
        }
    }

    private void StartWorkAnimation(Job job, WorkAtStep work)
    {
        if (job.AssignedDwarfId < 0 || string.IsNullOrWhiteSpace(work.AnimationHint))
            return;

        if (_activeWorkAnimations.TryGetValue(job.Id, out var currentHint) && string.Equals(currentHint, work.AnimationHint, StringComparison.OrdinalIgnoreCase))
            return;

        StopWorkAnimation(job);
        _activeWorkAnimations[job.Id] = work.AnimationHint;
        _eventBus?.Emit(new JobWorkStartedEvent(job.Id, job.AssignedDwarfId, job.JobDefId, work.AnimationHint, job.EntityId, job.TargetPos));
    }

    private void StopWorkAnimation(Job job)
    {
        if (!_activeWorkAnimations.Remove(job.Id, out var currentHint) || job.AssignedDwarfId < 0)
            return;

        _eventBus?.Emit(new JobWorkStoppedEvent(job.Id, job.AssignedDwarfId, job.JobDefId, currentHint, job.EntityId, job.TargetPos));
    }

    private void TickMoveStep(Job job, MoveToStep move, float delta,
                               Queue<ActionStep> steps, EntityRegistry registry)
    {
        var entity = registry.TryGetById(job.AssignedDwarfId);
        if (entity is null) { FailJob(job, "entity_missing"); return; }

        var map = _ctx!.Get<WorldMap>();
        var spatial = _ctx.TryGet<SpatialIndexSystem>();
        var posComp = entity.Components.Get<PositionComponent>();
        if (posComp.Position == move.Target)
        {
            steps.Dequeue();
            _pathQueues.Remove(job.Id);
            _moveProgress.Remove(job.Id);
            return;
        }

        // Compute or reuse existing path
        if (!_pathQueues.TryGetValue(job.Id, out var pathQ))
        {
            var path = Pathfinder.FindPath(map, posComp.Position, move.Target);
            if (path.Count == 0) { FailJob(job, "no_path"); return; }

            // Skip index 0 (current position)
            pathQ = new Queue<Vec3i>(path.Skip(1));
            _pathQueues[job.Id] = pathQ;
        }

        if (pathQ.Count == 0)
        {
            steps.Dequeue();
            _pathQueues.Remove(job.Id);
            _moveProgress.Remove(job.Id);
            return;
        }

        // Accumulate movement progress (speed = tiles per second)
        float speed = entity.Components.Has<StatComponent>()
            ? entity.Components.Get<StatComponent>().Speed.Value
            : 1f;

        _moveProgress.TryGetValue(job.Id, out var prog);
        prog += delta * speed;

        if (prog >= 1.0f)
        {
            var oldPos = posComp.Position;

            var stepState = PrepareNextMoveStep(map, move, oldPos, job, entity.Id, spatial, ref pathQ);
            if (stepState == MoveStepState.Fail)
            {
                FailJob(job, "path_blocked");
                return;
            }

            if (stepState == MoveStepState.Wait)
            {
                _moveProgress[job.Id] = MathF.Min(prog, 1.0f);
                return;
            }

            if (pathQ.Count == 0)
            {
                steps.Dequeue();
                _pathQueues.Remove(job.Id);
                _moveProgress.Remove(job.Id);
                return;
            }

            var newPos = pathQ.Dequeue();
            posComp.Position = newPos;
            _ctx!.TryGet<ItemSystem>()?.UpdateCarriedItemsPosition(entity.Id, newPos);
            _eventBus?.Emit(new EntityMovedEvent(entity.Id, oldPos, newPos));
            prog -= 1.0f;
        }

        _moveProgress[job.Id] = prog;
    }

    private bool EnsureWorkPosition(Job job, WorkAtStep work, Queue<ActionStep> steps, EntityRegistry registry)
    {
        if (!work.RequiredPosition.HasValue)
            return true;

        var entity = registry.TryGetById(job.AssignedDwarfId);
        if (entity is null)
        {
            FailJob(job, "entity_missing");
            return false;
        }

        var currentPos = entity.Components.Get<PositionComponent>().Position;
        if (currentPos == work.RequiredPosition.Value)
            return true;

        job.WorkProgress = 0f;
        ResetMovementState(job.Id);

        var remainingSteps = steps.ToArray();
        steps.Clear();
        steps.Enqueue(new MoveToStep(work.RequiredPosition.Value));
        foreach (var remainingStep in remainingSteps)
            steps.Enqueue(remainingStep);

        return false;
    }

    private void ResetMovementState(int jobId)
    {
        _pathQueues.Remove(jobId);
        _moveProgress.Remove(jobId);
    }

    private MoveStepState PrepareNextMoveStep(
        WorldMap map,
        MoveToStep move,
        Vec3i origin,
        Job job,
        int entityId,
        SpatialIndexSystem? spatial,
        ref Queue<Vec3i> pathQ)
    {
        if (pathQ.Count == 0)
            return MoveStepState.Ready;

        var next = pathQ.Peek();
        if (IsStepAvailable(map, origin, next, entityId, spatial))
            return MoveStepState.Ready;

        var reroute = FindPathAvoidingOccupiedTiles(map, origin, move.Target, entityId, spatial);
        if (reroute.Count > 1)
        {
            pathQ = new Queue<Vec3i>(reroute.Skip(1));
            _pathQueues[job.Id] = pathQ;
            return pathQ.Count == 0 || IsStepAvailable(map, origin, pathQ.Peek(), entityId, spatial)
                ? MoveStepState.Ready
                : MoveStepState.Wait;
        }

        var terrainOnlyPath = Pathfinder.FindPath(map, origin, move.Target);
        return terrainOnlyPath.Count > 1
            ? MoveStepState.Wait
            : MoveStepState.Fail;
    }

    private IReadOnlyList<Vec3i> FindPathAvoidingOccupiedTiles(
        WorldMap map,
        Vec3i origin,
        Vec3i target,
        int entityId,
        SpatialIndexSystem? spatial)
    {
        if (spatial is null)
            return Pathfinder.FindPath(map, origin, target);

        return Pathfinder.FindPath(map, origin, target, pos => IsOccupiedByOtherEntity(spatial, pos, entityId));
    }

    private static bool IsStepAvailable(
        WorldMap map,
        Vec3i origin,
        Vec3i next,
        int entityId,
        SpatialIndexSystem? spatial)
    {
        if (origin.ManhattanDistanceTo(next) != 1)
            return false;

        if (!map.IsWalkable(next))
            return false;

        return spatial is null || !IsOccupiedByOtherEntity(spatial, next, entityId);
    }

    private static bool IsOccupiedByOtherEntity(SpatialIndexSystem spatial, Vec3i pos, int entityId)
    {
        foreach (var id in spatial.GetDwarvesAt(pos))
            if (id != entityId)
                return true;

        foreach (var id in spatial.GetCreaturesAt(pos))
            if (id != entityId)
                return true;

        return false;
    }

    private void FailJob(Job job, string reason)
    {
        if (job.IsAssigned && _ctx is not null)
        {
            if (_strategies.TryGetValue(job.JobDefId, out var strat))
                strat.OnInterrupt(job, job.AssignedDwarfId, _ctx);
        }

        StopWorkAnimation(job);
        job.Status = JobStatus.Failed;
        CleanupJob(job.Id);
        _eventBus?.Emit(new JobFailedEvent(job.Id, reason));
    }

    private void CompleteJob(Job job)
    {
        StopWorkAnimation(job);
        if (_strategies.TryGetValue(job.JobDefId, out var strat))
            strat.OnComplete(job, job.AssignedDwarfId, _ctx!);

        job.Status = JobStatus.Complete;
        CleanupJob(job.Id);
        _eventBus?.Emit(new JobCompletedEvent(job.Id, job.AssignedDwarfId, job.JobDefId, job.EntityId, job.TargetPos, job.ReservedItemIds.ToArray()));
    }

    /// <summary>
    /// Centralized job cleanup to prevent memory leaks.
    /// Removes all per-job data from all tracking dictionaries.
    /// </summary>
    private void CleanupJob(int jobId)
    {
        _stepQueues.Remove(jobId);
        _pathQueues.Remove(jobId);
        _moveProgress.Remove(jobId);
        _activeWorkAnimations.Remove(jobId);
        _jobs.Remove(jobId);
    }

    /// <summary>
    /// Resolve the required labor type for a job from its JobDef.
    /// Falls back to Misc if the job definition is not found.
    /// </summary>
    private string GetRequiredLabor(string jobDefId)
    {
        var data = _ctx?.TryGet<DataManager>();
        var jobDef = data?.Jobs.GetOrNull(jobDefId);
        return jobDef?.RequiredLaborId ?? LaborIds.Misc;
    }

    // ── Command handlers ───────────────────────────────────────────────────

    private void OnDesignateMine(DesignateMineCommand cmd)
    {
        var from = cmd.From;
        var to   = cmd.To;
        var data = _ctx!.TryGet<DataManager>();
        var map  = _ctx.Get<World.WorldMap>();
        var existingMineTargets = _jobs.Values
            .Where(j => j.JobDefId == JobDefIds.MineTile && j.Status is JobStatus.Pending or JobStatus.InProgress)
            .Select(j => j.TargetPos)
            .ToHashSet();
        var candidates = new List<Vec3i>();

        for (int x = Math.Min(from.X, to.X); x <= Math.Max(from.X, to.X); x++)
        for (int y = Math.Min(from.Y, to.Y); y <= Math.Max(from.Y, to.Y); y++)
        for (int z = Math.Min(from.Z, to.Z); z <= Math.Max(from.Z, to.Z); z++)
        {
            var pos  = new Vec3i(x, y, z);
            var tile = map.GetTile(pos);
            var tileDef = data?.Tiles.GetOrNull(tile.TileDefId);

            // Only designate solid (non-passable) tiles that aren't already queued
            if (tile.IsPassable || tile.TileDefId == World.TileDefIds.Empty || tile.TileDefId == World.TileDefIds.Tree) continue;
            if (tileDef is not null && !tileDef.IsMineable) continue;
            if (existingMineTargets.Contains(pos)) continue;
            candidates.Add(pos);
        }

        // Propagate designation from exposed walls so selection direction does not matter.
        // A wall can become mine-designatable if it touches open terrain or a newly designated wall.
        var progress = true;
        while (progress && candidates.Count > 0)
        {
            progress = false;

            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                var pos = candidates[i];
                if (!World.MiningLineOfSight.CanDesignateMine(map, pos))
                    continue;

                var tile = map.GetTile(pos);
                tile.IsDesignated = true;
                map.SetTile(pos, tile);
                CreateJob(JobDefIds.MineTile, pos, priority: 5);
                candidates.RemoveAt(i);
                progress = true;
            }
        }
    }

    private void OnDesignateCutTrees(DesignateCutTreesCommand cmd)
    {
        var from = cmd.From;
        var to   = cmd.To;

        for (int x = Math.Min(from.X, to.X); x <= Math.Max(from.X, to.X); x++)
        for (int y = Math.Min(from.Y, to.Y); y <= Math.Max(from.Y, to.Y); y++)
        for (int z = Math.Min(from.Z, to.Z); z <= Math.Max(from.Z, to.Z); z++)
        {
            var pos  = new Vec3i(x, y, z);
            var map  = _ctx!.Get<World.WorldMap>();
            var tile = map.GetTile(pos);
            if (tile.TileDefId != World.TileDefIds.Tree) continue;

            // Don't duplicate-queue if a job already exists for this tile
            if (_jobs.Values.Any(j => j.TargetPos == pos && j.JobDefId == JobDefIds.CutTree
                                   && j.Status is JobStatus.Pending or JobStatus.InProgress)) continue;

            tile.IsDesignated = true;
            map.SetTile(pos, tile);
            CreateJob(JobDefIds.CutTree, pos, priority: 4);
        }
    }

    private void OnDesignateHarvest(DesignateHarvestCommand cmd)
    {
        var data = _ctx!.TryGet<DataManager>();
        var map  = _ctx.Get<World.WorldMap>();
        var existingHarvestTargets = _jobs.Values
            .Where(j => j.JobDefId == JobDefIds.HarvestPlant && j.Status is JobStatus.Pending or JobStatus.InProgress)
            .Select(j => j.TargetPos)
            .ToHashSet();

        for (int x = Math.Min(cmd.From.X, cmd.To.X); x <= Math.Max(cmd.From.X, cmd.To.X); x++)
        for (int y = Math.Min(cmd.From.Y, cmd.To.Y); y <= Math.Max(cmd.From.Y, cmd.To.Y); y++)
        for (int z = Math.Min(cmd.From.Z, cmd.To.Z); z <= Math.Max(cmd.From.Z, cmd.To.Z); z++)
        {
            var pos = new Vec3i(x, y, z);
            if (existingHarvestTargets.Contains(pos)) continue;
            if (data is null || !PlantHarvesting.TryGetHarvestablePlant(map, data, pos, out _)) continue;
            CreateJob(JobDefIds.HarvestPlant, pos, priority: 4);
        }
    }

    private void OnCancelDesignation(CancelDesignationCommand cmd)
    {
        var from = cmd.From;
        var to   = cmd.To;

        for (int x = Math.Min(from.X, to.X); x <= Math.Max(from.X, to.X); x++)
        for (int y = Math.Min(from.Y, to.Y); y <= Math.Max(from.Y, to.Y); y++)
        for (int z = Math.Min(from.Z, to.Z); z <= Math.Max(from.Z, to.Z); z++)
        {
            var pos = new Vec3i(x, y, z);
            foreach (var job in _jobs.Values
                         .Where(j => j.TargetPos == pos && j.Status is JobStatus.Pending)
                         .ToList())
                CancelJob(job.Id);
        }
    }
}

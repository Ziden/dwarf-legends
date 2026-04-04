using System;
using System.Collections.Generic;
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
public record struct MiningDesignationSafetyCancelledEvent(Vec3i Position, string HazardKind);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages all jobs: creation, assignment, step execution, and completion.
/// Order 10 — after world and entity systems.
/// 
/// Optimizations applied:
/// - Zero-LINQ TickActiveJobs: tracks active job IDs in _activeJobIds list
/// - O(1) IsDwarfWorking: uses _dwarfActiveJobs dictionary
/// - Reusable buffers to avoid per-tick allocations
/// </summary>
public sealed class JobSystem : IGameSystem
{
    private static readonly Vec3i[] InteractionDirections =
        [Vec3i.North, Vec3i.South, Vec3i.East, Vec3i.West];

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
    private readonly Dictionary<int, float>             _moveElapsedSeconds = new();
    private readonly Dictionary<int, string>            _activeWorkAnimations = new();
    private readonly Dictionary<int, float>             _repathCooldowns = new();

    // Zero-LINQ hot path tracking — maintained on status change
    private readonly List<int>              _activeJobIds    = new(); // Job IDs with InProgress status
    private readonly Dictionary<int, int>   _dwarfActiveJobs = new(); // dwarfId → jobId (only InProgress jobs)

    // Reusable buffers to avoid per-tick allocations
    private readonly List<Job>   _pendingJobBuffer   = new();
    private readonly List<Dwarf> _idleDwarfBuffer    = new();
    private readonly List<int>   _activeJobIdBuffer  = new();
    private readonly HashSet<Vec3i> _pendingMineTargets = new();

    private int       _nextJobId = 1;
    private EventBus? _eventBus;
    private GameContext? _ctx;
    private SimulationProfiler? _profiler;
    private readonly JobActionExecutor _actionExecutor = new();

    // ── IGameSystem ────────────────────────────────────────────────────────

    public void Initialize(GameContext ctx)
    {
        _eventBus = ctx.EventBus;
        _ctx      = ctx;
        _profiler = ctx.Profiler;

        ctx.Commands.Register<DesignateMineCommand>(OnDesignateMine);
        ctx.Commands.Register<DesignateCutTreesCommand>(OnDesignateCutTrees);
        ctx.Commands.Register<DesignateHarvestCommand>(OnDesignateHarvest);
        ctx.Commands.Register<CancelDesignationCommand>(OnCancelDesignation);
        ctx.EventBus.On<TileChangedEvent>(OnTileChanged);
    }

    public void Tick(float delta)
    {
        var entityRegistry = _ctx!.Get<EntityRegistry>();

        // Assign pending jobs to idle dwarves
        using (_profiler?.Measure("assign_jobs") ?? default)
            AssignJobs(entityRegistry);

        // Progress in-progress jobs
        using (_profiler?.Measure("tick_active_jobs") ?? default)
            TickActiveJobs(delta, entityRegistry);
    }

    public void OnSave(SaveWriter w)
    {
        w.Write("nextJobId", _nextJobId);
        
        // Write jobs without LINQ — iterate and filter manually
        var jobList = new List<JobDto>();
        foreach (var job in _jobs.Values)
        {
            if (job.Status is JobStatus.Pending or JobStatus.InProgress)
            {
                jobList.Add(new JobDto
                {
                    Id       = job.Id,
                    JobDefId = job.JobDefId,
                    X        = job.TargetPos.X,
                    Y        = job.TargetPos.Y,
                    Z        = job.TargetPos.Z,
                    Priority = job.Priority,
                    EntityId = job.EntityId,
                });
            }
        }
        w.Write("jobs", jobList);
    }

    public void OnLoad(SaveReader r)
    {
        _nextJobId = r.TryRead<int>("nextJobId");
        if (_nextJobId <= 0) _nextJobId = 1;

        _jobs.Clear();
        _stepQueues.Clear();
        _pathQueues.Clear();
        _moveProgress.Clear();
        _moveElapsedSeconds.Clear();
        _activeWorkAnimations.Clear();
        _activeJobIds.Clear();
        _dwarfActiveJobs.Clear();
        _repathCooldowns.Clear();

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

    /// <summary>Sets job status and updates tracking collections to keep hot paths allocation-free.</summary>
    private void SetJobStatus(Job job, JobStatus status)
    {
        var oldStatus = job.Status;
        job.Status = status;

        var wasActive = oldStatus == JobStatus.InProgress;
        var isActive  = status == JobStatus.InProgress;

        if (wasActive && !isActive)
        {
            _activeJobIds.Remove(job.Id);
            if (job.AssignedDwarfId >= 0)
                _dwarfActiveJobs.Remove(job.AssignedDwarfId);
        }
        else if (!wasActive && isActive)
        {
            _activeJobIds.Add(job.Id);
            if (job.AssignedDwarfId >= 0)
                _dwarfActiveJobs[job.AssignedDwarfId] = job.Id;
        }
    }

    public Job? GetJob(int jobId) =>
        _jobs.TryGetValue(jobId, out var j) ? j : null;

    public Job? GetAssignedJob(int dwarfId)
    {
        // Zero-LINQ: check tracked active job for this dwarf
        if (!_dwarfActiveJobs.TryGetValue(dwarfId, out var jobId) || 
            !_jobs.TryGetValue(jobId, out var job))
            return null;

        return job.Status == JobStatus.InProgress ? job : null;
    }

    public string DescribeCurrentStep(int jobId)
    {
        if (!_stepQueues.TryGetValue(jobId, out var steps) || steps.Count == 0)
            return "finishing";

        return DescribeStep(steps.Peek());
    }

    public ActionStep? GetCurrentStep(int jobId)
    {
        if (!_stepQueues.TryGetValue(jobId, out var steps) || steps.Count == 0)
            return null;

        return steps.Peek();
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
        SetJobStatus(job, JobStatus.Cancelled);
        CleanupJob(jobId);
        _eventBus?.Emit(new JobCancelledEvent(jobId));
    }

    public IEnumerable<Job> GetPendingJobs()
    {
        foreach (var job in _jobs.Values)
            if (job.Status == JobStatus.Pending)
                yield return job;
    }

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

            var candidate = job.AssignedDwarfId >= 0
                ? FindPreassignedCandidate(job, strat, _idleDwarfBuffer)
                : FindCandidateDwarf(job, strat, _idleDwarfBuffer);

            if (candidate is null) continue;

            // Check low courage attribute (courage <= 2) near water - refuse job if target is near water
            if (AttributeEffectSystem.FearsWater(candidate, _ctx?.TryGet<DataManager>()) && IsJobNearWater(job))
            {
                _eventBus?.Emit(new Systems.JobRefusedEvent(candidate.Id, job.Id, AttributeIds.Courage,
                    $"{candidate.FirstName} is too afraid to work near water."));
                continue;
            }

            var steps = strat.GetSteps(job, candidate.Id, _ctx!);
            _stepQueues[job.Id] = new Queue<ActionStep>(steps);

            SetJobStatus(job, JobStatus.InProgress);
            job.AssignedDwarfId = candidate.Id;
            _dwarfActiveJobs[candidate.Id] = job.Id;
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
                SetJobStatus(idleJob, JobStatus.InProgress);
                idleJob.AssignedDwarfId = dwarf.Id;
                _dwarfActiveJobs[dwarf.Id] = idleJob.Id;
                _eventBus?.Emit(new JobAssignedEvent(idleJob.Id, dwarf.Id));
            }
        }
    }

    /// <summary>Check if a dwarf currently has an in-progress job. O(1) via tracked dictionary.</summary>
    private bool IsDwarfWorking(int dwarfId) => _dwarfActiveJobs.ContainsKey(dwarfId);

    /// <summary>Check if a dwarf has a job with the given definition in pending or in-progress state.</summary>
    private bool HasActiveJob(int dwarfId, string jobDefId)
    {
        // Fast path: check in-progress jobs via O(1) lookup
        if (_dwarfActiveJobs.TryGetValue(dwarfId, out var jobId) && 
            _jobs.TryGetValue(jobId, out var activeJob) && 
            activeJob.JobDefId == jobDefId)
            return true;

        // Slow path: scan pending jobs only
        foreach (var job in _jobs.Values)
        {
            if (job.Status != JobStatus.Pending) continue;
            if (job.AssignedDwarfId == dwarfId && job.JobDefId == jobDefId)
                return true;
        }
        return false;
    }

    private Dwarf? FindCandidateDwarf(Job job, IJobStrategy strat, IReadOnlyList<Dwarf> idleDwarves)
    {
        // Survival jobs are basic self-preservation and should never be blocked by labor toggles.
        if (IsSurvivalJob(job.JobDefId))
            return idleDwarves.FirstOrDefault(d => strat.CanExecute(job, d.Id, _ctx!));

        var requiredLabor = GetRequiredLabor(job);
        var skilledCandidate = idleDwarves.FirstOrDefault(d =>
            d.Labors.IsEnabled(requiredLabor) &&
            strat.CanExecute(job, d.Id, _ctx!));

        if (skilledCandidate is not null)
            return skilledCandidate;

        if (!string.Equals(job.JobDefId, JobDefIds.CutTree, StringComparison.OrdinalIgnoreCase))
            return null;

        return idleDwarves.FirstOrDefault(d => strat.CanExecute(job, d.Id, _ctx!));
    }

    private Dwarf? FindPreassignedCandidate(Job job, IJobStrategy strat, IReadOnlyList<Dwarf> idleDwarves)
    {
        for (var i = 0; i < idleDwarves.Count; i++)
        {
            var dwarf = idleDwarves[i];
            if (dwarf.Id != job.AssignedDwarfId)
                continue;

            return strat.CanExecute(job, dwarf.Id, _ctx!) ? dwarf : null;
        }

        return null;
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

    /// <summary>
    /// Zero-LINQ check: does a pending/in-progress job with the given defId target this position?
    /// </summary>
    private bool HasPendingJobAt(string jobDefId, Vec3i pos)
    {
        foreach (var job in _jobs.Values)
        {
            if (job.JobDefId == jobDefId
                && job.TargetPos == pos
                && (job.Status is JobStatus.Pending or JobStatus.InProgress))
                return true;
        }
        return false;
    }

    // ── Tick active jobs (zero-LINQ) ───────────────────────────────────────

    private void TickActiveJobs(float delta, EntityRegistry registry)
    {
        // Zero-allocation iteration of active job IDs (pre-tracked on status change)
        _activeJobIdBuffer.Clear();
        _activeJobIdBuffer.AddRange(_activeJobIds);

        // Tick repath cooldowns so dwarves can retry pathfinding after being blocked
        for (int i = _activeJobIdBuffer.Count - 1; i >= 0; i--)
        {
            var jobId = _activeJobIdBuffer[i];
            if (_repathCooldowns.TryGetValue(jobId, out var cooldown))
            {
                cooldown -= delta;
                if (cooldown <= 0f)
                    _repathCooldowns.Remove(jobId);
                else
                    _repathCooldowns[jobId] = cooldown;
            }
        }

        foreach (var jobId in _activeJobIdBuffer)
        {
            if (!_jobs.TryGetValue(jobId, out var job) || job.Status != JobStatus.InProgress)
                continue;

            if (!_stepQueues.TryGetValue(jobId, out var steps) || steps.Count == 0)
            {
                CompleteJob(job);
                continue;
            }

            var step = steps.Peek();

            var executionContext = new JobActionExecutionContext(this, _ctx!, job, steps, registry, delta);
            _actionExecutor.Execute(executionContext, step);
        }
    }

    internal void StartWorkAnimation(Job job, WorkAtStep work)
    {
        if (job.AssignedDwarfId < 0 || string.IsNullOrWhiteSpace(work.AnimationHint))
            return;

        if (_activeWorkAnimations.TryGetValue(job.Id, out var currentHint) && string.Equals(currentHint, work.AnimationHint, StringComparison.OrdinalIgnoreCase))
            return;

        StopWorkAnimation(job);
        _activeWorkAnimations[job.Id] = work.AnimationHint;
        _eventBus?.Emit(new JobWorkStartedEvent(job.Id, job.AssignedDwarfId, job.JobDefId, work.AnimationHint, job.EntityId, job.TargetPos));
    }

    internal void StopWorkAnimation(Job job)
    {
        if (!_activeWorkAnimations.Remove(job.Id, out var currentHint) || job.AssignedDwarfId < 0)
            return;

        _eventBus?.Emit(new JobWorkStoppedEvent(job.Id, job.AssignedDwarfId, job.JobDefId, currentHint, job.EntityId, job.TargetPos));
    }

    internal void TickMoveStep(Job job, MoveToStep move, float delta,
                               Queue<ActionStep> steps, EntityRegistry registry)
    {
        var entity = registry.TryGetById(job.AssignedDwarfId);
        if (entity is null) { FailJob(job, "entity_missing"); return; }

        var map = _ctx!.Get<WorldMap>();
        var spatial = _ctx.TryGet<SpatialIndexSystem>();
        var posComp = entity.Components.Get<PositionComponent>();
        if (HasReachedMoveTarget(map, posComp.Position, move))
        {
            steps.Dequeue();
            _pathQueues.Remove(job.Id);
            _moveProgress.Remove(job.Id);
            _moveElapsedSeconds.Remove(job.Id);
            return;
        }

        // Compute or reuse existing path
        if (!_pathQueues.TryGetValue(job.Id, out var pathQ))
        {
            var path = FindPathToMoveTarget(map, posComp.Position, move, entity.Id, spatial, avoidOccupiedTiles: false);
            if (path.Count == 0) { FailJob(job, "no_path"); return; }

            // Skip index 0 (current position) — manual iteration to avoid LINQ Skip()
            pathQ = new Queue<Vec3i>();
            for (int i = 1; i < path.Count; i++)
                pathQ.Enqueue(path[i]);
            _pathQueues[job.Id] = pathQ;
        }

        if (pathQ.Count == 0)
        {
            steps.Dequeue();
            _pathQueues.Remove(job.Id);
            _moveProgress.Remove(job.Id);
            _moveElapsedSeconds.Remove(job.Id);
            return;
        }

        // Accumulate movement progress (speed = tiles per second)
        float speed = entity.Components.Has<StatComponent>()
            ? entity.Components.Get<StatComponent>().Speed.Value
            : 1f;

        _moveProgress.TryGetValue(job.Id, out var prog);
        _moveElapsedSeconds.TryGetValue(job.Id, out var elapsedSeconds);
        prog += delta * speed;
        elapsedSeconds += delta;

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
                _moveElapsedSeconds[job.Id] = ResolveElapsedSecondsForProgress(_moveProgress[job.Id], speed);
                return;
            }

            if (pathQ.Count == 0)
            {
                steps.Dequeue();
                _pathQueues.Remove(job.Id);
                _moveProgress.Remove(job.Id);
                _moveElapsedSeconds.Remove(job.Id);
                return;
            }

            var newPos = pathQ.Dequeue();
            var carryProgress = MathF.Max(0f, prog - 1.0f);
            var carryElapsedSeconds = ResolveElapsedSecondsForProgress(carryProgress, speed);
            var segmentDurationSeconds = MathF.Max(0f, elapsedSeconds - carryElapsedSeconds);
            EntityMovement.TryMove(_ctx!, entity, newPos, segmentDurationSeconds);
            prog = carryProgress;
            elapsedSeconds = carryElapsedSeconds;
        }

        _moveProgress[job.Id] = prog;
        _moveElapsedSeconds[job.Id] = elapsedSeconds;
    }

    internal bool EnsureWorkPosition(Job job, WorkAtStep work, Queue<ActionStep> steps, EntityRegistry registry)
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
        _moveElapsedSeconds.Remove(jobId);
    }

    private static float ResolveElapsedSecondsForProgress(float progress, float speed)
    {
        if (progress <= 0f)
            return 0f;

        return progress / MathF.Max(speed, 0.0001f);
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

        // Cooldown: don't repath too frequently to avoid cascading A* calls
        var canRepath = !_repathCooldowns.ContainsKey(job.Id);
        if (!canRepath)
            return MoveStepState.Wait;

        var reroute = FindPathToMoveTarget(map, origin, move, entityId, spatial, avoidOccupiedTiles: true);
        if (reroute.Count > 1)
        {
            pathQ = new Queue<Vec3i>();
            for (int i = 1; i < reroute.Count; i++)
                pathQ.Enqueue(reroute[i]);
            _pathQueues[job.Id] = pathQ;
            _repathCooldowns[job.Id] = 0.5f;  // Half-second cooldown before next repath
            return pathQ.Count == 0 || IsStepAvailable(map, origin, pathQ.Peek(), entityId, spatial)
                ? MoveStepState.Ready
                : MoveStepState.Wait;
        }

        var terrainOnlyPath = FindPathToMoveTarget(map, origin, move, entityId, spatial: null, avoidOccupiedTiles: false);
        return terrainOnlyPath.Count > 1
            ? MoveStepState.Wait
            : MoveStepState.Fail;
    }

    private IReadOnlyList<Vec3i> FindPathToMoveTarget(
        WorldMap map,
        Vec3i origin,
        MoveToStep move,
        int entityId,
        SpatialIndexSystem? spatial,
        bool avoidOccupiedTiles)
    {
        if (move.AcceptableDistance <= 0)
        {
            if (!avoidOccupiedTiles || spatial is null)
                return Pathfinder.FindPath(map, origin, move.Target);

            return Pathfinder.FindPath(map, origin, move.Target, pos => IsOccupiedByOtherEntity(spatial, pos, entityId));
        }

        IReadOnlyList<Vec3i> bestPath = Array.Empty<Vec3i>();
        var bestPathLength = int.MaxValue;
        var bestPenalty = int.MaxValue;

        foreach (var candidate in EnumerateMoveTargetCandidates(map, move))
        {
            IReadOnlyList<Vec3i> path;
            if (!avoidOccupiedTiles || spatial is null)
            {
                path = Pathfinder.FindPath(map, origin, candidate);
            }
            else
            {
                path = Pathfinder.FindPath(map, origin, candidate, pos => IsOccupiedByOtherEntity(spatial, pos, entityId));
            }

            if (path.Count == 0)
                continue;

            var penalty = move.PreferAdjacent && candidate == move.Target ? 1 : 0;
            if (path.Count < bestPathLength || (path.Count == bestPathLength && penalty < bestPenalty))
            {
                bestPath = path;
                bestPathLength = path.Count;
                bestPenalty = penalty;
            }
        }

        return bestPath;
    }

    private static bool HasReachedMoveTarget(WorldMap map, Vec3i position, MoveToStep move)
    {
        if (position == move.Target)
            return true;

        if (move.AcceptableDistance <= 0)
            return false;

        return IsValidMoveTargetCandidate(map, position, move);
    }

    private static IEnumerable<Vec3i> EnumerateMoveTargetCandidates(WorldMap map, MoveToStep move)
    {
        if (move.AcceptableDistance <= 0)
        {
            if (IsValidMoveTargetCandidate(map, move.Target, move))
                yield return move.Target;
            yield break;
        }

        if (move.PreferAdjacent)
        {
            foreach (var candidate in EnumerateAdjacentCandidates(map, move))
                yield return candidate;

            if (IsValidMoveTargetCandidate(map, move.Target, move))
                yield return move.Target;
            yield break;
        }

        if (IsValidMoveTargetCandidate(map, move.Target, move))
            yield return move.Target;

        foreach (var candidate in EnumerateAdjacentCandidates(map, move))
            yield return candidate;
    }

    private static IEnumerable<Vec3i> EnumerateAdjacentCandidates(WorldMap map, MoveToStep move)
    {
        foreach (var direction in InteractionDirections)
        {
            var candidate = move.Target + direction;
            if (IsValidMoveTargetCandidate(map, candidate, move))
                yield return candidate;
        }
    }

    private static bool IsValidMoveTargetCandidate(WorldMap map, Vec3i candidate, MoveToStep move)
    {
        if (!map.IsInBounds(candidate))
            return false;

        if (candidate.Z != move.Target.Z)
            return false;

        if (candidate.ManhattanDistanceTo(move.Target) > move.AcceptableDistance)
            return false;

        return map.IsWalkable(candidate);
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

    internal void FailJob(Job job, string reason)
    {
        if (job.IsAssigned && _ctx is not null)
        {
            if (_strategies.TryGetValue(job.JobDefId, out var strat))
                strat.OnInterrupt(job, job.AssignedDwarfId, _ctx);
        }

        StopWorkAnimation(job);
        SetJobStatus(job, JobStatus.Failed);
        CleanupJob(job.Id);
        _eventBus?.Emit(new JobFailedEvent(job.Id, reason));
    }

    private void CompleteJob(Job job)
    {
        StopWorkAnimation(job);
        if (_strategies.TryGetValue(job.JobDefId, out var strat))
            strat.OnComplete(job, job.AssignedDwarfId, _ctx!);

        SetJobStatus(job, JobStatus.Complete);
        CleanupJob(job.Id);
        _eventBus?.Emit(new JobCompletedEvent(job.Id, job.AssignedDwarfId, job.JobDefId, job.EntityId, job.TargetPos, job.ReservedItemIds.ToArray()));
    }

    /// <summary>
    /// Centralized job cleanup to prevent memory leaks.
    /// Removes all per-job data from all tracking dictionaries.
    /// </summary>
    private void CleanupJob(int jobId)
    {
        _activeJobIds.Remove(jobId);
        _stepQueues.Remove(jobId);
        _pathQueues.Remove(jobId);
        _moveProgress.Remove(jobId);
        _moveElapsedSeconds.Remove(jobId);
        _activeWorkAnimations.Remove(jobId);
        _repathCooldowns.Remove(jobId);
        _jobs.Remove(jobId);
    }

    /// <summary>
    /// Resolve the required labor type for a job from its JobDef.
    /// Falls back to Misc if the job definition is not found.
    /// </summary>
    private string GetRequiredLabor(Job job)
    {
        var data = _ctx?.TryGet<DataManager>();
        if (string.Equals(job.JobDefId, JobDefIds.Craft, StringComparison.OrdinalIgnoreCase) && job.EntityId >= 0)
        {
            var recipeSystem = _ctx?.TryGet<RecipeSystem>();
            var order = recipeSystem?.GetOrCreateQueue(job.EntityId).Peek();
            var recipeLabor = order is null ? null : data?.Recipes.GetOrNull(order.RecipeId)?.RequiredLaborId;
            if (!string.IsNullOrWhiteSpace(recipeLabor))
                return recipeLabor!;
        }

        var jobDef = data?.Jobs.GetOrNull(job.JobDefId);
        return jobDef?.RequiredLaborId ?? LaborIds.Misc;
    }

    // ── Command handlers ───────────────────────────────────────────────────

    private void OnDesignateMine(DesignateMineCommand cmd)
    {
        var from = cmd.From;
        var to   = cmd.To;
        var data = _ctx!.TryGet<DataManager>();
        var map  = _ctx.Get<World.WorldMap>();
        
        // Zero-LINQ: collect existing mine targets manually
        _pendingMineTargets.Clear();
        foreach (var job in _jobs.Values)
        {
            if (job.JobDefId == JobDefIds.MineTile 
                && (job.Status is JobStatus.Pending or JobStatus.InProgress))
                _pendingMineTargets.Add(job.TargetPos);
        }
        
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
            if (MiningHazardAnalysis.GetVisibleWallHazardKind(map, pos) is not null) continue;
            if (_pendingMineTargets.Contains(pos)) continue;
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
                _pendingMineTargets.Add(pos);
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
            if (HasPendingJobAt(JobDefIds.CutTree, pos)) continue;

            tile.IsDesignated = true;
            map.SetTile(pos, tile);
            CreateJob(JobDefIds.CutTree, pos, priority: 4);
        }
    }

    private void OnDesignateHarvest(DesignateHarvestCommand cmd)
    {
        var data = _ctx!.TryGet<DataManager>();
        var map  = _ctx.Get<World.WorldMap>();

        for (int x = Math.Min(cmd.From.X, cmd.To.X); x <= Math.Max(cmd.From.X, cmd.To.X); x++)
        for (int y = Math.Min(cmd.From.Y, cmd.To.Y); y <= Math.Max(cmd.From.Y, cmd.To.Y); y++)
        for (int z = Math.Min(cmd.From.Z, cmd.To.Z); z <= Math.Max(cmd.From.Z, cmd.To.Z); z++)
        {
            var pos = new Vec3i(x, y, z);
            if (HasPendingJobAt(JobDefIds.HarvestPlant, pos)) continue;
            if (data is null || !PlantHarvesting.TryGetHarvestablePlant(map, data, pos, out _)) continue;

            var tile = map.GetTile(pos);
            tile.IsDesignated = true;
            map.SetTile(pos, tile);
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
            // Collect pending jobs at this position without LINQ
            var jobsToCancel = new List<int>();
            foreach (var job in _jobs.Values)
            {
                if (job.TargetPos == pos && job.Status is JobStatus.Pending)
                    jobsToCancel.Add(job.Id);
            }
            foreach (var jobId in jobsToCancel)
                CancelJob(jobId);

            if (jobsToCancel.Count == 0)
                continue;

            var map = _ctx!.Get<World.WorldMap>();
            var tile = map.GetTile(pos);
            if (!tile.IsDesignated)
                continue;

            tile.IsDesignated = false;
            map.SetTile(pos, tile);
        }
    }

    private void OnTileChanged(TileChangedEvent e)
    {
        if (_ctx is null)
            return;

        if (!e.NewTile.IsPassable || e.OldTile.IsPassable == e.NewTile.IsPassable)
            return;

        var map = _ctx.Get<WorldMap>();
        CancelExposedUnsafeMining(map, e.Pos + Vec3i.North);
        CancelExposedUnsafeMining(map, e.Pos + Vec3i.South);
        CancelExposedUnsafeMining(map, e.Pos + Vec3i.East);
        CancelExposedUnsafeMining(map, e.Pos + Vec3i.West);
        CancelExposedUnsafeMining(map, e.Pos + Vec3i.Up);
        CancelExposedUnsafeMining(map, e.Pos + Vec3i.Down);
    }

    private void CancelExposedUnsafeMining(WorldMap map, Vec3i pos)
    {
        if (!map.IsInBounds(pos))
            return;

        var hazardKind = MiningHazardAnalysis.GetVisibleWallHazardKind(map, pos);
        if (hazardKind is null)
            return;

        var tile = map.GetTile(pos);
        if (!tile.IsDesignated)
            return;

        var jobsToCancel = new List<int>();
        foreach (var job in _jobs.Values)
        {
            if (job.TargetPos != pos || !string.Equals(job.JobDefId, JobDefIds.MineTile, StringComparison.OrdinalIgnoreCase))
                continue;
            if (job.Status is not (JobStatus.Pending or JobStatus.InProgress))
                continue;
            jobsToCancel.Add(job.Id);
        }

        if (jobsToCancel.Count == 0)
            return;

        foreach (var jobId in jobsToCancel)
            CancelJob(jobId);

        tile.IsDesignated = false;
        map.SetTile(pos, tile);
        _eventBus?.Emit(new MiningDesignationSafetyCancelledEvent(pos, hazardKind));
    }
}

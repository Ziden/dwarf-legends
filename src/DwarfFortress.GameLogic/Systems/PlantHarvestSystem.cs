using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

public sealed class PlantHarvestSystem : IGameSystem
{
    public string SystemId => SystemIds.PlantHarvestSystem;
    public int UpdateOrder => 7;
    public bool IsEnabled { get; set; } = true;

    private const float ScanIntervalSeconds = 3f;
    private const int MaxQueuedHarvestJobs = 12;

    private GameContext? _ctx;
    private float _scanTimer;
    private readonly Queue<Vec3i> _harvestCandidates = new();
    private readonly HashSet<Vec3i> _queuedHarvestCandidates = new();

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        ctx.EventBus.On<TileChangedEvent>(OnTileChanged);
        RebuildHarvestCandidates();
    }

    public void Tick(float delta)
    {
        _scanTimer += delta;
        if (_scanTimer < ScanIntervalSeconds)
            return;

        _scanTimer = 0f;

        var ctx = _ctx!;
        var map = ctx.Get<WorldMap>();
        var data = ctx.Get<Data.DataManager>();
        var jobSystem = ctx.TryGet<JobSystem>();
        if (jobSystem is null || map.Depth <= 0 || data.Plants.Count == 0)
            return;

        var queuedTargets = new HashSet<Vec3i>(jobSystem.GetAllJobs()
            .Where(job => job.Status is JobStatus.Pending or JobStatus.InProgress)
            .Where(job => job.JobDefId == JobDefIds.HarvestPlant || job.JobDefId == JobDefIds.Eat)
            .Select(job => job.TargetPos));

        if (queuedTargets.Count >= MaxQueuedHarvestJobs)
            return;

        while (_harvestCandidates.Count > 0)
        {
            if (queuedTargets.Count >= MaxQueuedHarvestJobs)
                return;

            var pos = _harvestCandidates.Dequeue();
            _queuedHarvestCandidates.Remove(pos);

            if (queuedTargets.Contains(pos))
                continue;
            if (!PlantHarvesting.TryGetHarvestablePlant(map, data, pos, out _))
                continue;
            if (!PlantHarvesting.ResolveHarvestStandPosition(map, pos).HasValue)
                continue;

            jobSystem.CreateJob(JobDefIds.HarvestPlant, pos, priority: 8);
            queuedTargets.Add(pos);
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r)
    {
        _scanTimer = 0f;
        RebuildHarvestCandidates();
    }

    private void OnTileChanged(TileChangedEvent e)
    {
        var ctx = _ctx;
        if (ctx is null)
            return;

        var data = ctx.TryGet<Data.DataManager>();
        if (data is null)
            return;

        var wasHarvestable = PlantHarvesting.TryGetHarvestablePlant(e.OldTile, data, out _);
        var isHarvestable = PlantHarvesting.TryGetHarvestablePlant(e.NewTile, data, out _);
        if (!wasHarvestable && isHarvestable)
            EnqueueHarvestCandidate(e.Pos);
    }

    private void RebuildHarvestCandidates()
    {
        _harvestCandidates.Clear();
        _queuedHarvestCandidates.Clear();

        var ctx = _ctx;
        if (ctx is null)
            return;

        var map = ctx.TryGet<WorldMap>();
        var data = ctx.TryGet<Data.DataManager>();
        if (map is null || data is null || map.Depth <= 0)
            return;

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var pos = new Vec3i(x, y, 0);
            if (PlantHarvesting.TryGetHarvestablePlant(map, data, pos, out _))
                EnqueueHarvestCandidate(pos);
        }
    }

    private void EnqueueHarvestCandidate(Vec3i pos)
    {
        if (_queuedHarvestCandidates.Add(pos))
            _harvestCandidates.Enqueue(pos);
    }
}
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

    public void Initialize(GameContext ctx) => _ctx = ctx;

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

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (queuedTargets.Count >= MaxQueuedHarvestJobs)
                return;

            var pos = new Vec3i(x, y, 0);
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
    public void OnLoad(SaveReader r) => _scanTimer = 0f;
}
using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Jobs.Strategies;

namespace DwarfFortress.GameLogic.Systems;

public sealed class CombatResponseSystem : IGameSystem
{
    private const int ThreatSearchRadius = 12;

    private readonly Dictionary<int, int> _engageJobsByDwarf = new();
    private readonly List<int> _nearbyCreatureIds = new();

    private GameContext? _ctx;

    public string SystemId => SystemIds.CombatResponseSystem;
    public int UpdateOrder => 9;
    public bool IsEnabled { get; set; } = true;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
    }

    public void Tick(float delta)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        var jobSystem = _ctx.TryGet<JobSystem>();
        var spatial = _ctx.TryGet<SpatialIndexSystem>();
        if (jobSystem is null)
            return;

        CleanupTrackedJobs(jobSystem);

        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            if (dwarf.Health.IsDead || !dwarf.Health.IsConscious)
            {
                CancelTrackedEngagement(jobSystem, dwarf.Id);
                continue;
            }

            var target = FindNearestHostile(dwarf, registry, spatial);
            var currentJob = jobSystem.GetAssignedJob(dwarf.Id);

            if (target is null)
            {
                if (currentJob is not null && string.Equals(currentJob.JobDefId, JobDefIds.EngageHostile, StringComparison.OrdinalIgnoreCase))
                    jobSystem.CancelJob(currentJob.Id);

                CancelTrackedEngagement(jobSystem, dwarf.Id);
                continue;
            }

            if (!EngageHostileStrategy.TryBuildPlan(_ctx, dwarf.Id, target.Id, out _))
                continue;

            if (currentJob is not null &&
                string.Equals(currentJob.JobDefId, JobDefIds.EngageHostile, StringComparison.OrdinalIgnoreCase) &&
                currentJob.EntityId == target.Id)
            {
                _engageJobsByDwarf[dwarf.Id] = currentJob.Id;
                continue;
            }

            if (_engageJobsByDwarf.TryGetValue(dwarf.Id, out var trackedJobId) &&
                jobSystem.GetJob(trackedJobId) is { } trackedJob &&
                string.Equals(trackedJob.JobDefId, JobDefIds.EngageHostile, StringComparison.OrdinalIgnoreCase) &&
                trackedJob.EntityId == target.Id &&
                trackedJob.Status is JobStatus.Pending or JobStatus.InProgress)
            {
                continue;
            }

            if (currentJob is not null)
                jobSystem.CancelJob(currentJob.Id);

            CancelTrackedEngagement(jobSystem, dwarf.Id);

            var engageJob = jobSystem.CreateJob(JobDefIds.EngageHostile, target.Position.Position, priority: 100, entityId: target.Id);
            engageJob.AssignedDwarfId = dwarf.Id;
            _engageJobsByDwarf[dwarf.Id] = engageJob.Id;
        }
    }

    public void OnSave(SaveWriter w) { }

    public void OnLoad(SaveReader r)
    {
        _engageJobsByDwarf.Clear();
        _nearbyCreatureIds.Clear();
    }

    private Creature? FindNearestHostile(Dwarf dwarf, EntityRegistry registry, SpatialIndexSystem? spatial)
    {
        var origin = dwarf.Position.Position;
        var nearestDistance = int.MaxValue;
        Creature? nearest = null;

        if (spatial is not null)
        {
            spatial.CollectCreaturesInBounds(
                origin.Z,
                origin.X - ThreatSearchRadius,
                origin.Y - ThreatSearchRadius,
                origin.X + ThreatSearchRadius,
                origin.Y + ThreatSearchRadius,
                _nearbyCreatureIds);

            for (var i = 0; i < _nearbyCreatureIds.Count; i++)
            {
                if (!registry.TryGetById<Creature>(_nearbyCreatureIds[i], out var creature) || creature is null || !creature.IsHostile || creature.Health.IsDead)
                    continue;

                var distance = origin.ManhattanDistanceTo(creature.Position.Position);
                if (distance > ThreatSearchRadius || distance >= nearestDistance)
                    continue;
                if (!EngageHostileStrategy.TryBuildPlan(_ctx!, dwarf.Id, creature.Id, out _))
                    continue;

                nearest = creature;
                nearestDistance = distance;
            }

            return nearest;
        }

        foreach (var creature in registry.GetAlive<Creature>())
        {
            if (!creature.IsHostile || creature.Health.IsDead)
                continue;

            var distance = origin.ManhattanDistanceTo(creature.Position.Position);
            if (distance > ThreatSearchRadius || distance >= nearestDistance)
                continue;
            if (!EngageHostileStrategy.TryBuildPlan(_ctx!, dwarf.Id, creature.Id, out _))
                continue;

            nearest = creature;
            nearestDistance = distance;
        }

        return nearest;
    }

    private void CleanupTrackedJobs(JobSystem jobSystem)
    {
        if (_engageJobsByDwarf.Count == 0)
            return;

        var staleDwarfIds = new List<int>();
        foreach (var pair in _engageJobsByDwarf)
        {
            var job = jobSystem.GetJob(pair.Value);
            if (job is not null && job.Status is JobStatus.Pending or JobStatus.InProgress)
                continue;

            staleDwarfIds.Add(pair.Key);
        }

        for (var i = 0; i < staleDwarfIds.Count; i++)
            _engageJobsByDwarf.Remove(staleDwarfIds[i]);
    }

    private void CancelTrackedEngagement(JobSystem jobSystem, int dwarfId)
    {
        if (!_engageJobsByDwarf.Remove(dwarfId, out var jobId))
            return;

        if (jobSystem.GetJob(jobId) is { Status: JobStatus.Pending or JobStatus.InProgress } job)
            jobSystem.CancelJob(job.Id);
    }
}
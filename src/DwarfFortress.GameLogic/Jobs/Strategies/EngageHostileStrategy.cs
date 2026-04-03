using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

internal readonly record struct EngageHostilePlan(int HostileId, Vec3i HostilePos, Vec3i StandPos, bool AlreadyInRange);

public sealed class EngageHostileStrategy : IJobStrategy
{
    public string JobDefId => JobDefIds.EngageHostile;

    public bool CanExecute(Job job, int dwarfId, GameContext ctx)
        => TryBuildPlan(ctx, dwarfId, job.EntityId, out _);

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        if (!TryBuildPlan(ctx, dwarfId, job.EntityId, out var plan))
            return Array.Empty<ActionStep>();

        ctx.Get<EntityRegistry>().TryGetById<Dwarf>(dwarfId, out var dwarf);
        var steps = new List<ActionStep>(2);
        if (!plan.AlreadyInRange)
            steps.Add(new MoveToStep(plan.StandPos));

        steps.Add(new WorkAtStep(
            Duration: dwarf is null ? 0.6f : CombatSystem.CalculateAttackCooldownSeconds(dwarf),
            AnimationHint: "combat",
            RequiredPosition: plan.StandPos));
        return steps;
    }

    public void OnInterrupt(Job job, int dwarfId, GameContext ctx) { }

    public void OnComplete(Job job, int dwarfId, GameContext ctx)
    {
        if (!TryBuildPlan(ctx, dwarfId, job.EntityId, out var plan) || !plan.AlreadyInRange)
            return;

        ctx.TryGet<CombatSystem>()?.TryAttackAdjacentEntity(dwarfId, job.EntityId);
    }

    internal static bool TryBuildPlan(GameContext ctx, int dwarfId, int hostileId, out EngageHostilePlan plan)
    {
        var registry = ctx.Get<EntityRegistry>();
        if (!registry.TryGetById<Dwarf>(dwarfId, out var dwarf) || dwarf is null || dwarf.Health.IsDead || !dwarf.Health.IsConscious)
        {
            plan = default;
            return false;
        }

        if (!registry.TryGetById<Creature>(hostileId, out var hostile) || hostile is null || !hostile.IsHostile || hostile.Health.IsDead)
        {
            plan = default;
            return false;
        }

        var dwarfPos = dwarf.Position.Position;
        var hostilePos = hostile.Position.Position;
        if (dwarfPos.Z != hostilePos.Z)
        {
            plan = default;
            return false;
        }

        if (dwarfPos.ManhattanDistanceTo(hostilePos) <= 1)
        {
            plan = new EngageHostilePlan(hostile.Id, hostilePos, dwarfPos, AlreadyInRange: true);
            return true;
        }

        var standPos = TryFindAttackStandPosition(ctx, dwarf.Id, dwarfPos, hostilePos);
        if (!standPos.HasValue)
        {
            plan = default;
            return false;
        }

        plan = new EngageHostilePlan(hostile.Id, hostilePos, standPos.Value, AlreadyInRange: false);
        return true;
    }

    private static Vec3i? TryFindAttackStandPosition(GameContext ctx, int dwarfId, Vec3i dwarfPos, Vec3i hostilePos)
    {
        var map = ctx.Get<WorldMap>();
        var spatial = ctx.TryGet<SpatialIndexSystem>();
        Vec3i? best = null;
        var bestPathLength = int.MaxValue;
        var bestDistance = int.MaxValue;

        foreach (var candidate in hostilePos.Neighbours6())
        {
            if (candidate.Z != hostilePos.Z)
                continue;
            if (!map.IsWalkable(candidate))
                continue;
            if (IsOccupiedByOtherEntity(spatial, candidate, dwarfId))
                continue;

            var path = spatial is null
                ? Pathfinder.FindPath(map, dwarfPos, candidate)
                : Pathfinder.FindPath(map, dwarfPos, candidate, pos => IsOccupiedByOtherEntity(spatial, pos, dwarfId));
            if (path.Count == 0)
                continue;

            var pathLength = path.Count;
            var distance = dwarfPos.ManhattanDistanceTo(candidate);
            if (pathLength > bestPathLength)
                continue;
            if (pathLength == bestPathLength && distance >= bestDistance)
                continue;

            best = candidate;
            bestPathLength = pathLength;
            bestDistance = distance;
        }

        return best;
    }

    private static bool IsOccupiedByOtherEntity(SpatialIndexSystem? spatial, Vec3i position, int entityId)
    {
        if (spatial is null)
            return false;

        foreach (var dwarfId in spatial.GetDwarvesAt(position))
            if (dwarfId != entityId)
                return true;

        foreach (var creatureId in spatial.GetCreaturesAt(position))
            if (creatureId != entityId)
                return true;

        return false;
    }
}
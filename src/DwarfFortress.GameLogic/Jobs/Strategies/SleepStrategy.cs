using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

/// <summary>
/// Dwarf walks to an owned bed (or floor) and sleeps until rest need is satisfied.
/// Enhanced with sleep location scoring: beds > near trees/plants > quiet workshops.
/// </summary>
public sealed class SleepStrategy : IJobStrategy
{
    public string JobDefId => JobDefIds.Sleep;

    // Sleep always fully restores the sleep need — waking before the timer
    // ends (job interrupt) leaves it partially satisfied via NeedsSystem decay.
    private const float SleepSatisfaction = 1.0f;
    private const float GroundSleepDuration = 8f;
    private const float BedSleepDuration = 5f;
    private const int SleepSearchRadius = 16;

    public bool CanExecute(Job job, int dwarfId, GameContext ctx) => true;

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        var sleepTarget = FindBestSleepTarget(dwarfId, ctx) ?? job.TargetPos;
        var isNearBed = IsNearBed(sleepTarget, ctx);
        var duration = isNearBed ? BedSleepDuration : GroundSleepDuration;

        return new ActionStep[]
        {
            new MoveToStep(sleepTarget),
            new WorkAtStep(Duration: duration, RequiredPosition: sleepTarget),
        };
    }

    public void OnInterrupt(Job job, int dwarfId, GameContext ctx) { }

    public void OnComplete(Job job, int dwarfId, GameContext ctx)
    {
        var entityRegistry = ctx.Get<EntityRegistry>();
        if (!entityRegistry.TryGetById<Dwarf>(dwarfId, out var dwarf) || dwarf is null) return;

        // Sleep recovery is driven by stamina and focus attributes.
        var recoveryMultiplier = SleepSystem.GetSleepRecoveryMultiplier(dwarf, ctx.TryGet<DataManager>());
        dwarf.Needs.Get(NeedIds.Sleep).Satisfy(SleepSatisfaction * recoveryMultiplier);

        // Emit satisfaction event to trigger cooldown in NeedsSystem
        ctx.EventBus.Emit(new Systems.NeedSatisfiedEvent(dwarfId, NeedIds.Sleep));
    }

    /// <summary>
    /// Finds the best sleep location for a dwarf using scoring.
    /// Priority: beds > near trees/plants > quiet workshops > avoid animals.
    /// </summary>
    private Vec3i? FindBestSleepTarget(int dwarfId, GameContext ctx)
    {
        var entityRegistry = ctx.Get<EntityRegistry>();
        if (!entityRegistry.TryGetById<Dwarf>(dwarfId, out var dwarf) || dwarf is null) return null;

        var map = ctx.TryGet<WorldMap>();
        var buildingSystem = ctx.TryGet<BuildingSystem>();
        if (map is null) return null;

        var dwarfPos = dwarf.Position.Position;
        var bestPos = dwarfPos;
        var bestScore = ScoreDwarfSleepSpot(dwarfPos, dwarf, ctx, map, buildingSystem);

        // First check if there's an available bed
        if (buildingSystem is not null)
        {
            var beds = buildingSystem.GetAll()
                .Where(b => b.BuildingDefId == BuildingDefIds.Bed)
                .OrderBy(b => b.Origin.ManhattanDistanceTo(dwarfPos))
                .ToList();

            foreach (var bed in beds)
            {
                var score = ScoreDwarfSleepSpot(bed.Origin, dwarf, ctx, map, buildingSystem);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPos = bed.Origin;
                }
            }
        }

        // Search nearby tiles for better spots (only if no good bed found)
        if (bestScore < 50) // bed score threshold
        {
            for (int dx = -SleepSearchRadius; dx <= SleepSearchRadius; dx++)
            {
                for (int dy = -SleepSearchRadius; dy <= SleepSearchRadius; dy++)
                {
                    var pos = new Vec3i(dwarfPos.X + dx, dwarfPos.Y + dy, dwarfPos.Z);
                    if (!map.IsInBounds(pos) || !map.IsWalkable(pos))
                        continue;

                    var score = ScoreDwarfSleepSpot(pos, dwarf, ctx, map, buildingSystem);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPos = pos;
                    }
                }
            }
        }

        // Only move if the new spot is significantly better
        return bestScore > ScoreDwarfSleepSpot(dwarfPos, dwarf, ctx, map, buildingSystem) + 5
            ? bestPos
            : null;
    }

    private static int ScoreDwarfSleepSpot(Vec3i pos, Dwarf dwarf, GameContext ctx, WorldMap map, BuildingSystem? buildingSystem)
    {
        var score = 0;

        // Beds are highest priority
        if (buildingSystem is not null)
        {
            foreach (var building in buildingSystem.GetAll())
            {
                if (building.BuildingDefId == BuildingDefIds.Bed &&
                    building.Origin.ManhattanDistanceTo(pos) <= 1)
                {
                    score += 100; // Very high priority for beds
                }
            }
        }

        // Prefer near trees/plants
        foreach (var neighbor in pos.Neighbours4())
        {
            if (map.IsInBounds(neighbor))
            {
                var tile = map.GetTile(neighbor);
                if (tile.TileDefId == TileDefIds.Tree)
                    score += 10;
                if (!string.IsNullOrEmpty(tile.PlantDefId))
                    score += 5;
            }
        }

        // Prefer near quiet workshops
        if (buildingSystem is not null)
        {
            foreach (var building in buildingSystem.GetAll())
            {
                if (building.Origin.Z == pos.Z &&
                    building.Origin.ManhattanDistanceTo(pos) <= 3)
                {
                    score += 3;
                }
            }
        }

        // Avoid animals nearby
        var registry = ctx.Get<EntityRegistry>();
        foreach (var creature in registry.GetAlive<Creature>())
        {
            var creaturePos = creature.Position.Position;
            if (creaturePos.Z != pos.Z) continue;
            if (creaturePos.ManhattanDistanceTo(pos) <= 2)
                score -= 15;
        }

        // Avoid water
        var spotTile = map.GetTile(pos);
        if (spotTile.FluidType == FluidType.Water || spotTile.TileDefId == World.TileDefIds.Water)
            score -= 20;

        return score;
    }

    private static bool IsNearBed(Vec3i pos, GameContext ctx)
    {
        var buildingSystem = ctx.TryGet<BuildingSystem>();
        if (buildingSystem is null) return false;

        return buildingSystem.GetAll()
            .Any(b => b.BuildingDefId == BuildingDefIds.Bed &&
                      b.Origin.ManhattanDistanceTo(pos) <= 2);
    }
}

using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

/// <summary>
/// Constructs a building at the target position.
/// Requires construction materials to already be hauled adjacently.
/// </summary>
public sealed class ConstructBuildingStrategy : IJobStrategy
{
    public string JobDefId => JobDefIds.Construct;

    public bool CanExecute(Job job, int dwarfId, GameContext ctx)
    {
        var map  = ctx.Get<WorldMap>();
        var tile = map.GetTile(job.TargetPos);
        return tile.IsUnderConstruction && FindAdjacentPassable(map, job.TargetPos).HasValue;
    }

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        var map          = ctx.Get<WorldMap>();
        var buildFromPos = FindAdjacentPassable(map, job.TargetPos)
                           ?? job.TargetPos + Vec3i.South; // fallback (guarded by CanExecute)
        return new ActionStep[]
        {
            new MoveToStep(buildFromPos),
            new WorkAtStep(Duration: 10f, AnimationHint: "construction", RequiredPosition: buildFromPos),
        };
    }

    public void OnInterrupt(Job job, int dwarfId, GameContext ctx) { /* building remains under construction */ }

    public void OnComplete(Job job, int dwarfId, GameContext ctx)
    {
        var map  = ctx.Get<WorldMap>();
        var tile = map.GetTile(job.TargetPos);
        tile.IsUnderConstruction = false;
        map.SetTile(job.TargetPos, tile);
    }

    private static readonly Vec3i[] Neighbours =
        [Vec3i.South, Vec3i.North, Vec3i.East, Vec3i.West];

    private static Vec3i? FindAdjacentPassable(WorldMap map, Vec3i pos)
        => Neighbours.Select(d => pos + d)
                     .Cast<Vec3i?>()
                     .FirstOrDefault(n => map.IsWalkable(n!.Value));
}

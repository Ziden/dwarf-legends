using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Systems;
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
        return TryResolveConstructionSite(job, ctx, out var building, out var definition) &&
               !building.IsComplete &&
               FindBuildFromPosition(ctx.Get<WorldMap>(), definition, building).HasValue;
    }

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        if (!TryResolveConstructionSite(job, ctx, out var building, out var definition))
            return System.Array.Empty<ActionStep>();

        var map = ctx.Get<WorldMap>();
        var buildFromPos = FindBuildFromPosition(map, definition, building);
        if (!buildFromPos.HasValue)
            return System.Array.Empty<ActionStep>();

        return new ActionStep[]
        {
            new MoveToStep(buildFromPos.Value),
            new WorkAtStep(Duration: System.MathF.Max(0.1f, definition.ConstructionTime), AnimationHint: "construction", RequiredPosition: buildFromPos.Value),
        };
    }

    public void OnInterrupt(Job job, int dwarfId, GameContext ctx) { /* building remains under construction */ }

    public void OnComplete(Job job, int dwarfId, GameContext ctx)
    {
        if (job.EntityId >= 0)
            ctx.Get<BuildingSystem>().CompleteConstruction(job.EntityId, job.Id);
    }

    private static readonly Vec3i[] Neighbours =
        [Vec3i.South, Vec3i.North, Vec3i.East, Vec3i.West];

    private static bool TryResolveConstructionSite(
        Job job,
        GameContext ctx,
        out PlacedBuildingData building,
        out BuildingDef definition)
    {
        building = null!;
        definition = null!;

        if (job.EntityId < 0)
            return false;

        var buildingSystem = ctx.TryGet<BuildingSystem>();
        var dataManager = ctx.TryGet<DataManager>();
        var candidate = buildingSystem?.GetById(job.EntityId);
        var def = candidate is null ? null : dataManager?.Buildings.GetOrNull(candidate.BuildingDefId);
        if (candidate is null || def is null)
            return false;

        building = candidate;
        definition = def;
        return true;
    }

    private static Vec3i? FindBuildFromPosition(WorldMap map, BuildingDef definition, PlacedBuildingData building)
    {
        var footprint = BuildingPlacementGeometry
            .EnumerateWorldFootprint(definition, building.Origin, building.Rotation)
            .OrderBy(pos => pos.Y)
            .ThenBy(pos => pos.X)
            .ToArray();
        var footprintSet = footprint.ToHashSet();

        foreach (var cell in footprint)
        {
            foreach (var direction in Neighbours)
            {
                var candidate = cell + direction;
                if (!footprintSet.Contains(candidate) && map.IsWalkable(candidate))
                    return candidate;
            }
        }

        foreach (var cell in footprint)
        {
            if (map.IsWalkable(cell))
                return cell;
        }

        return null;
    }
}

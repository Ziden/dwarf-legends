using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

/// <summary>
/// Dwarf finds a drink source and satisfies thirst.
/// Prefers drink items; falls back to nearby natural water.
/// </summary>
public sealed class DrinkStrategy : IJobStrategy
{
    public string JobDefId => JobDefIds.Drink;

    // How much of the thirst need is satisfied by drinking once.
    private const float ThirstSatisfaction = 0.9f;
    private const int WaterSearchRadius = 14;

    private static readonly Vec3i[] CardinalDirections =
        [Vec3i.North, Vec3i.South, Vec3i.East, Vec3i.West];

    // A dwarf drinking when thirst is above this is over-drinking → nausea
    private const float OverdrinkThreshold = 0.5f;
    private const float NauseaDuration = 60f;
 worki
    public bool CanExecute(Job job, int dwarfId, GameContext ctx)
    {
        // Refuse if nauseous
        var registry = ctx.Get<Entities.EntityRegistry>();
        if (registry.TryGetById<Entities.Dwarf>(dwarfId, out var d) && d is not null)
            if (d.Components.TryGet<Entities.Components.StatusEffectComponent>()?.Has(Entities.Components.StatusEffectIds.Nausea) == true)
                return false;

        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        if (itemSystem?.FindDrinkItem() is not null)
            return true;

        if (!TryGetDwarfAndMap(dwarfId, ctx, out var dwarf, out var map))
            return false;

        return CanDrinkAt(map, dwarf.Position.Position)
            || TryFindNearestDrinkablePosition(map, dwarf.Position.Position, out _);
    }

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        if (!TryGetDwarfAndMap(dwarfId, ctx, out var dwarf, out var map))
            return System.Array.Empty<ActionStep>();

        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        var drink = itemSystem?.FindDrinkItem();
        if (drink is not null)
        {
            drink.IsClaimed = true;
            job.ReservedItemIds.Add(drink.Id);

            return new ActionStep[]
            {
                new MoveToStep(drink.Components.Get<PositionComponent>().Position),
                new PickUpItemStep(drink.Id),
                new WorkAtStep(Duration: 1f, RequiredPosition: drink.Components.Get<PositionComponent>().Position),
            };
        }

        var origin = dwarf.Position.Position;
        if (CanDrinkAt(map, origin))
        {
            return new ActionStep[]
            {
                new WorkAtStep(Duration: 1f, RequiredPosition: origin),
            };
        }

        if (!TryFindNearestDrinkablePosition(map, origin, out var drinkTarget))
            return System.Array.Empty<ActionStep>();

        return new ActionStep[]
        {
            new MoveToStep(drinkTarget),
            new WorkAtStep(Duration: 1f, RequiredPosition: drinkTarget),
        };
    }

    public void OnInterrupt(Job job, int dwarfId, GameContext ctx)
    {
        ReleaseReserved(job, ctx);
    }

    public void OnComplete(Job job, int dwarfId, GameContext ctx)
    {
        var entityRegistry = ctx.Get<Entities.EntityRegistry>();
        if (!entityRegistry.TryGetById<Entities.Dwarf>(dwarfId, out var dwarf) || dwarf is null)
            return;

        dwarf.Needs.Get(NeedIds.Thirst).Satisfy(ThirstSatisfaction);

        // Emit satisfaction event to trigger cooldown in NeedsSystem
        ctx.EventBus.Emit(new Systems.NeedSatisfiedEvent(dwarfId, NeedIds.Thirst));

        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        var consumedItemName = ResolveConsumedItemName(job, itemSystem, ctx);
        foreach (var id in job.ReservedItemIds)
            itemSystem?.DestroyItem(id);
        job.ReservedItemIds.Clear();

        ctx.EventBus.Emit(new EntityActivityEvent(dwarf.Id, $"Drank {consumedItemName}", dwarf.Position.Position));
    }

    private static bool TryGetDwarfAndMap(
        int dwarfId,
        GameContext ctx,
        out Dwarf dwarf,
        out WorldMap map)
    {
        dwarf = null!;
        map = null!;

        var entityRegistry = ctx.Get<Entities.EntityRegistry>();
        if (!entityRegistry.TryGetById<Entities.Dwarf>(dwarfId, out var found) || found is null)
            return false;

        dwarf = found;
        map = ctx.Get<WorldMap>();
        return true;
    }

    /// <summary>
    /// Uses BFS flood-fill to find the nearest reachable drinkable water tile.
    /// This is O(N) where N = reachable tiles, and avoids running A* on every candidate.
    /// Early termination as soon as a drinkable tile is found.
    /// </summary>
    private static bool TryFindNearestDrinkablePosition(
        WorldMap map,
        Vec3i origin,
        out Vec3i nearest)
    {
        nearest = origin;

        // Quick check: can we drink right here?
        if (CanDrinkAt(map, origin))
            return true;

        // BFS flood-fill to find nearest drinkable tile
        // Only traverse walkable tiles, which guarantees the path is actually reachable
        var visited = new HashSet<Vec3i>();
        var queue = new Queue<(Vec3i pos, int distance)>();
        queue.Enqueue((origin, 0));
        visited.Add(origin);

        while (queue.Count > 0)
        {
            var (current, distance) = queue.Dequeue();

            // Don't search beyond configured radius
            var dx = Math.Abs(current.X - origin.X);
            var dy = Math.Abs(current.Y - origin.Y);
            if (dx > WaterSearchRadius || dy > WaterSearchRadius)
                continue;

            // Check all 4 cardinal neighbours
            foreach (var dir in CardinalDirections)
            {
                var next = current + dir;
                if (!map.IsInBounds(next) || !visited.Add(next))
                    continue;

                // Non-walkable tiles cannot be traversed or used as drink sources
                if (!map.IsWalkable(next))
                    continue;

                // Check if this tile has drinkable water — BFS guarantees nearest!
                if (CanDrinkAt(map, next))
                {
                    nearest = next;
                    return true;
                }

                // Enqueue for further BFS exploration
                queue.Enqueue((next, distance + 1));
            }
        }

        return false;
    }

    private static bool CanDrinkAt(WorldMap map, Vec3i position)
    {
        if (IsDrinkableWaterTile(map, position))
            return true;

        foreach (var direction in CardinalDirections)
            if (IsDrinkableWaterTile(map, position + direction))
                return true;

        return false;
    }

    private static bool IsDrinkableWaterTile(WorldMap map, Vec3i position)
    {
        if (!map.IsInBounds(position))
            return false;

        var tile = map.GetTile(position);
        if (tile.FluidType == FluidType.Magma || tile.TileDefId == TileDefIds.Magma)
            return false;

        return (tile.FluidType == FluidType.Water || tile.TileDefId == TileDefIds.Water)
               && tile.FluidLevel > 0;
    }

    private static void ReleaseReserved(Job job, GameContext ctx)
    {
        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        if (itemSystem is null) return;
        var registry = ctx.Get<Entities.EntityRegistry>();
        var dropPos = registry.TryGetById<Entities.Dwarf>(job.AssignedDwarfId, out var dwarf) && dwarf is not null
            ? dwarf.Position.Position
            : job.TargetPos;
        foreach (var id in job.ReservedItemIds)
            if (itemSystem.TryGetItem(id, out var item) && item is not null)
            {
                if (item.CarriedByEntityId == job.AssignedDwarfId)
                    itemSystem.ReleaseCarriedItem(id, dropPos);
                item.IsClaimed = false;
            }
        job.ReservedItemIds.Clear();
    }

    private static string ResolveConsumedItemName(Job job, Systems.ItemSystem? itemSystem, GameContext ctx)
    {
        if (itemSystem is not null)
        {
            foreach (var itemId in job.ReservedItemIds)
                if (itemSystem.TryGetItem(itemId, out var item) && item is not null)
                    return ctx.TryGet<DataManager>()?.Items.GetOrNull(item.DefId)?.DisplayName
                           ?? item.DefId.Replace('_', ' ');
        }

        return CanJobUseNaturalWater(job) ? "water" : "drink";
    }

    private static bool CanJobUseNaturalWater(Job job)
        => job.ReservedItemIds.Count == 0;
}

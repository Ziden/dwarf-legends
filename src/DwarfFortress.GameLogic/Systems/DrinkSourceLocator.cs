using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

internal static class DrinkSourceLocator
{
    private static readonly Vec3i[] CardinalDirections =
        [Vec3i.North, Vec3i.South, Vec3i.East, Vec3i.West];

    public static bool TryFindNearestDrinkablePosition(
        WorldMap map,
        Vec3i origin,
        int searchRadius,
        out Vec3i nearest)
    {
        nearest = origin;
        if (CanDrinkAt(map, origin))
            return true;

        var visited = new HashSet<Vec3i> { origin };
        var queue = new Queue<Vec3i>();
        queue.Enqueue(origin);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var dx = Math.Abs(current.X - origin.X);
            var dy = Math.Abs(current.Y - origin.Y);
            if (dx > searchRadius || dy > searchRadius)
                continue;

            foreach (var direction in CardinalDirections)
            {
                var next = current + direction;
                if (!map.IsInBounds(next) || !visited.Add(next))
                    continue;

                if (!map.IsWalkable(next))
                    continue;

                if (CanDrinkAt(map, next))
                {
                    nearest = next;
                    return true;
                }

                queue.Enqueue(next);
            }
        }

        return false;
    }

    public static bool TryFindNearestDrinkableTile(
        WorldMap map,
        Vec3i origin,
        int searchRadius,
        out Vec3i drinkTile)
    {
        drinkTile = origin;
        if (TryResolveDrinkTile(map, origin, out drinkTile))
            return true;

        var visited = new HashSet<Vec3i> { origin };
        var queue = new Queue<Vec3i>();
        queue.Enqueue(origin);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var dx = Math.Abs(current.X - origin.X);
            var dy = Math.Abs(current.Y - origin.Y);
            if (dx > searchRadius || dy > searchRadius)
                continue;

            foreach (var direction in CardinalDirections)
            {
                var next = current + direction;
                if (!map.IsInBounds(next) || !visited.Add(next))
                    continue;

                if (!map.IsWalkable(next))
                    continue;

                if (TryResolveDrinkTile(map, next, out drinkTile))
                    return true;

                queue.Enqueue(next);
            }
        }

        return false;
    }

    public static bool TryFindReachableDrinkStandPosition(
        WorldMap map,
        Vec3i origin,
        Vec3i drinkTile,
        out Vec3i standPosition)
    {
        standPosition = origin;
        if (origin == drinkTile || origin.ManhattanDistanceTo(drinkTile) == 1)
        {
            if (CanDrinkAt(map, origin))
                return true;
        }

        var found = false;
        var bestPathLength = int.MaxValue;

        foreach (var direction in CardinalDirections)
        {
            var candidate = drinkTile + direction;
            if (!map.IsInBounds(candidate) || !map.IsWalkable(candidate))
                continue;

            if (!CanDrinkAt(map, candidate))
                continue;

            var path = Pathfinder.FindPath(map, origin, candidate);
            if (path.Count == 0)
                continue;

            if (path.Count < bestPathLength)
            {
                bestPathLength = path.Count;
                standPosition = candidate;
                found = true;
            }
        }

        return found;
    }

    public static bool CanDrinkAt(WorldMap map, Vec3i position)
    {
        if (IsDrinkableWaterTile(map, position))
            return true;

        foreach (var direction in CardinalDirections)
            if (IsDrinkableWaterTile(map, position + direction))
                return true;

        return false;
    }

    public static bool IsDrinkableWaterTile(WorldMap map, Vec3i position)
    {
        if (!map.IsInBounds(position))
            return false;

        var tile = map.GetTile(position);
        if (tile.FluidType == FluidType.Magma || tile.TileDefId == TileDefIds.Magma)
            return false;

        return (tile.FluidType == FluidType.Water || tile.TileDefId == TileDefIds.Water)
               && tile.FluidLevel > 0;
    }

    public static bool TryResolveDrinkTile(WorldMap map, Vec3i position, out Vec3i drinkTile)
    {
        if (IsDrinkableWaterTile(map, position))
        {
            drinkTile = position;
            return true;
        }

        foreach (var direction in CardinalDirections)
        {
            var candidate = position + direction;
            if (!IsDrinkableWaterTile(map, candidate))
                continue;

            drinkTile = candidate;
            return true;
        }

        drinkTile = position;
        return false;
    }

}
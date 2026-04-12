using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

internal static class ItemPickupHelper
{
    private static readonly Vec3i[] InteractionDirections =
        [Vec3i.North, Vec3i.South, Vec3i.East, Vec3i.West];

    public static MoveToStep CreatePickupMoveStep(Item item)
        => RequiresStandOff(item)
            ? new MoveToStep(item.Position.Position, AcceptableDistance: 1, PreferAdjacent: true)
            : new MoveToStep(item.Position.Position);

    public static Vec3i? ResolveConsumeWorkPosition(Item item)
        => RequiresStandOff(item) ? null : item.Position.Position;

    public static bool CanReachForPickup(WorldMap map, Vec3i origin, Item item)
    {
        var move = CreatePickupMoveStep(item);
        if (HasReachedPickupTarget(map, origin, move))
            return true;

        foreach (var candidate in EnumeratePickupTargetCandidates(map, move))
            if (Pathfinder.FindPath(map, origin, candidate).Count > 0)
                return true;

        return false;
    }

    private static bool RequiresStandOff(Item item)
        => item.ContainerItemId >= 0 || item.StockpileId >= 0;

    private static bool HasReachedPickupTarget(WorldMap map, Vec3i position, MoveToStep move)
    {
        if (position == move.Target)
            return true;

        if (move.AcceptableDistance <= 0)
            return false;

        return IsValidPickupTargetCandidate(map, position, move);
    }

    private static IEnumerable<Vec3i> EnumeratePickupTargetCandidates(WorldMap map, MoveToStep move)
    {
        if (move.AcceptableDistance <= 0)
        {
            if (IsValidPickupTargetCandidate(map, move.Target, move))
                yield return move.Target;
            yield break;
        }

        if (move.PreferAdjacent)
        {
            foreach (var candidate in EnumerateAdjacentPickupCandidates(map, move))
                yield return candidate;

            if (IsValidPickupTargetCandidate(map, move.Target, move))
                yield return move.Target;
            yield break;
        }

        if (IsValidPickupTargetCandidate(map, move.Target, move))
            yield return move.Target;

        foreach (var candidate in EnumerateAdjacentPickupCandidates(map, move))
            yield return candidate;
    }

    private static IEnumerable<Vec3i> EnumerateAdjacentPickupCandidates(WorldMap map, MoveToStep move)
    {
        foreach (var direction in InteractionDirections)
        {
            var candidate = move.Target + direction;
            if (IsValidPickupTargetCandidate(map, candidate, move))
                yield return candidate;
        }
    }

    private static bool IsValidPickupTargetCandidate(WorldMap map, Vec3i candidate, MoveToStep move)
    {
        if (!map.IsInBounds(candidate))
            return false;

        if (candidate.Z != move.Target.Z)
            return false;

        if (candidate.ManhattanDistanceTo(move.Target) > move.AcceptableDistance)
            return false;

        return map.IsWalkable(candidate);
    }
}
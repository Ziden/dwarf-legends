using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.World;

/// <summary>
/// Centralized hazard checks for mining UX and safety behavior.
/// </summary>
public static class MiningHazardAnalysis
{
    private static readonly Vec3i[] HazardNeighbours =
        [Vec3i.North, Vec3i.South, Vec3i.East, Vec3i.West, Vec3i.Up, Vec3i.Down];

    public static bool IsDampWall(WorldMap map, Vec3i pos)
    {
        if (!map.IsInBounds(pos))
            return false;

        var tile = map.GetTile(pos);
        if (tile.IsPassable || !tile.IsAquifer)
            return false;

        return MiningLineOfSight.IsTileVisible(map, pos);
    }

    public static bool IsWarmWall(WorldMap map, Vec3i pos)
    {
        if (!map.IsInBounds(pos))
            return false;

        var tile = map.GetTile(pos);
        if (tile.IsPassable || !MiningLineOfSight.IsTileVisible(map, pos))
            return false;

        foreach (var offset in HazardNeighbours)
        {
            var neighborPos = pos + offset;
            if (!map.IsInBounds(neighborPos))
                continue;

            var neighbor = map.GetTile(neighborPos);
            if (neighbor.FluidType == FluidType.Magma || neighbor.TileDefId == TileDefIds.Magma)
                return true;
        }

        return false;
    }

    public static string? GetVisibleWallHazardKind(WorldMap map, Vec3i pos)
    {
        if (IsDampWall(map, pos))
            return "damp";
        if (IsWarmWall(map, pos))
            return "warm";

        return null;
    }
}
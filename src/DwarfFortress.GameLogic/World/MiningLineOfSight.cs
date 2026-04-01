using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.World;

/// <summary>
/// Visibility rules used by mining UX so players discover ore progressively
/// from exposed tunnels instead of seeing every vein immediately.
/// </summary>
public static class MiningLineOfSight
{
    private static readonly Vec3i[] HorizontalNeighbours =
        [Vec3i.North, Vec3i.South, Vec3i.East, Vec3i.West];

    /// <summary>
    /// Returns true when the tile has line-of-sight exposure to open space
    /// (directly passable itself or adjacent to passable terrain).
    /// </summary>
    public static bool HasExposure(WorldMap map, Vec3i pos, bool includeDesignatedWalls = false)
    {
        if (!map.IsInBounds(pos))
            return false;

        var tile = map.GetTile(pos);
        if (tile.IsPassable)
            return true;

        foreach (var offset in HorizontalNeighbours)
        {
            var neighborPos = pos + offset;
            if (!map.IsInBounds(neighborPos))
                continue;

            var neighbor = map.GetTile(neighborPos);
            if (neighbor.IsPassable)
                return true;

            if (includeDesignatedWalls && neighbor.IsDesignated && !neighbor.IsPassable)
                return true;
        }

        return false;
    }

    public static bool IsOreVisible(WorldMap map, Vec3i pos)
        => HasExposure(map, pos, includeDesignatedWalls: false);

    public static bool CanDesignateMine(WorldMap map, Vec3i pos)
        => HasExposure(map, pos, includeDesignatedWalls: true);

    public static bool IsTileVisible(WorldMap map, Vec3i pos)
    {
        if (!map.IsInBounds(pos))
            return false;

        if (pos.Z <= 0)
            return true;

        var tile = map.GetTile(pos);
        if (tile.IsPassable)
            return true;

        // Visibility must follow real excavation/open space, not planned designations.
        return HasExposure(map, pos, includeDesignatedWalls: false);
    }
}

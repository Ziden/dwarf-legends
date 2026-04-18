using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.Data.Defs;

public enum BuildingRotation
{
    None = 0,
    Clockwise90 = 1,
    Clockwise180 = 2,
    Clockwise270 = 3,
}

public static class BuildingVisualArchetypes
{
    public const string Hut = "hut";
    public const string Workshop = "workshop";
}

public readonly record struct RotatedBuildingTile(Vec2i Offset, BuildingTile Tile);

public readonly record struct BuildingBounds(int MinX, int MinY, int MaxX, int MaxY)
{
    public int Width => MaxX - MinX + 1;
    public int Height => MaxY - MinY + 1;
}

public readonly record struct BuildingEntryPoint(Vec3i TilePosition, Vec3i OutwardDirection);

public static class BuildingPlacementGeometry
{
    public static IReadOnlyList<RotatedBuildingTile> EnumerateRotatedTiles(BuildingDef definition, BuildingRotation rotation)
    {
        if (definition.Footprint.Count == 0)
            return [];

        var originalBounds = GetOriginalBounds(definition);
        return definition.Footprint
            .Select(tile => new RotatedBuildingTile(RotateOffset(tile.Offset, originalBounds, rotation), tile))
            .OrderBy(tile => tile.Offset.Y)
            .ThenBy(tile => tile.Offset.X)
            .ToArray();
    }

    public static IReadOnlyList<Vec3i> EnumerateWorldFootprint(BuildingDef definition, Vec3i origin, BuildingRotation rotation)
    {
        return EnumerateRotatedTiles(definition, rotation)
            .Select(tile => new Vec3i(origin.X + tile.Offset.X, origin.Y + tile.Offset.Y, origin.Z))
            .ToArray();
    }

    public static BuildingBounds GetRotatedBounds(BuildingDef definition, BuildingRotation rotation)
    {
        var rotatedTiles = EnumerateRotatedTiles(definition, rotation);
        if (rotatedTiles.Count == 0)
            return new BuildingBounds(0, 0, 0, 0);

        var minX = rotatedTiles.Min(tile => tile.Offset.X);
        var minY = rotatedTiles.Min(tile => tile.Offset.Y);
        var maxX = rotatedTiles.Max(tile => tile.Offset.X);
        var maxY = rotatedTiles.Max(tile => tile.Offset.Y);
        return new BuildingBounds(minX, minY, maxX, maxY);
    }

    public static Vec2i RotateDirection(Vec2i direction, BuildingRotation rotation)
    {
        return rotation switch
        {
            BuildingRotation.Clockwise90 => new Vec2i(-direction.Y, direction.X),
            BuildingRotation.Clockwise180 => new Vec2i(-direction.X, -direction.Y),
            BuildingRotation.Clockwise270 => new Vec2i(direction.Y, -direction.X),
            _ => direction,
        };
    }

    public static IReadOnlyList<BuildingEntryPoint> GetEntryPoints(BuildingDef definition, Vec3i origin, BuildingRotation rotation)
    {
        if (definition.Entries.Count == 0)
            return [];

        var originalBounds = GetOriginalBounds(definition);
        return definition.Entries
            .Select(entry =>
            {
                var rotatedOffset = RotateOffset(entry.Offset, originalBounds, rotation);
                var outwardDirection2D = RotateDirection(entry.OutwardDirection, rotation);
                return new BuildingEntryPoint(
                    new Vec3i(origin.X + rotatedOffset.X, origin.Y + rotatedOffset.Y, origin.Z),
                    outwardDirection2D.ToVec3i());
            })
            .ToArray();
    }

    public static bool CanTraverseBoundary(BuildingDef definition, Vec3i origin, BuildingRotation rotation, Vec3i from, Vec3i to)
    {
        var footprint = EnumerateWorldFootprint(definition, origin, rotation).ToHashSet();
        var fromInside = footprint.Contains(from);
        var toInside = footprint.Contains(to);

        if (fromInside == toInside)
            return true;

        foreach (var entry in GetEntryPoints(definition, origin, rotation))
        {
            var outside = entry.TilePosition + entry.OutwardDirection;
            if ((from == entry.TilePosition && to == outside) ||
                (to == entry.TilePosition && from == outside))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<Vec3i> GetAutoStockpileCells(BuildingDef definition, Vec3i origin, BuildingRotation rotation)
    {
        if (definition.AutoStockpileAcceptedTags.Count == 0)
            return [];

        var footprint = EnumerateWorldFootprint(definition, origin, rotation);
        if (footprint.Count == 0)
            return [];

        var bounds = GetRotatedBounds(definition, rotation);
        var entry = GetEntryPoints(definition, origin, rotation).FirstOrDefault();

        IEnumerable<Vec3i> selected = entry.OutwardDirection switch
        {
            var dir when dir == Vec3i.South => footprint.Where(cell => cell.Y == origin.Y + bounds.MinY),
            var dir when dir == Vec3i.North => footprint.Where(cell => cell.Y == origin.Y + bounds.MaxY),
            var dir when dir == Vec3i.East => footprint.Where(cell => cell.X == origin.X + bounds.MinX),
            var dir when dir == Vec3i.West => footprint.Where(cell => cell.X == origin.X + bounds.MaxX),
            _ => footprint.Where(cell => cell.Y == origin.Y + bounds.MinY),
        };

        var cells = selected
            .OrderBy(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ToArray();

        return cells.Length > 0 ? cells : footprint;
    }

    public static IReadOnlyList<Vec3i> GetPreferredSleepCells(BuildingDef definition, Vec3i origin, BuildingRotation rotation)
    {
        var footprint = EnumerateWorldFootprint(definition, origin, rotation);
        if (footprint.Count == 0)
            return [];

        var bounds = GetRotatedBounds(definition, rotation);
        var entry = GetEntryPoints(definition, origin, rotation).FirstOrDefault();
        var entryTile = entry.TilePosition;

        IEnumerable<Vec3i> preferred = entry.OutwardDirection switch
        {
            var dir when dir == Vec3i.South || dir == Vec3i.North
                => footprint.Where(cell => cell.Y == origin.Y + bounds.MinY + ((bounds.Height - 1) / 2)),
            var dir when dir == Vec3i.East || dir == Vec3i.West
                => footprint.Where(cell => cell.X == origin.X + bounds.MinX + ((bounds.Width - 1) / 2)),
            _ => footprint.Where(cell => cell.Y == origin.Y + bounds.MinY + ((bounds.Height - 1) / 2)),
        };

        var cells = preferred
            .Where(cell => cell != entryTile)
            .OrderBy(cell => cell.X)
            .ThenBy(cell => cell.Y)
            .ToArray();

        if (cells.Length > 0)
            return cells;

        return footprint
            .Where(cell => cell != entryTile)
            .OrderBy(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ToArray();
    }

    private static BuildingBounds GetOriginalBounds(BuildingDef definition)
    {
        if (definition.Footprint.Count == 0)
            return new BuildingBounds(0, 0, 0, 0);

        var minX = definition.Footprint.Min(tile => tile.Offset.X);
        var minY = definition.Footprint.Min(tile => tile.Offset.Y);
        var maxX = definition.Footprint.Max(tile => tile.Offset.X);
        var maxY = definition.Footprint.Max(tile => tile.Offset.Y);
        return new BuildingBounds(minX, minY, maxX, maxY);
    }

    private static Vec2i RotateOffset(Vec2i offset, BuildingBounds bounds, BuildingRotation rotation)
    {
        var normalizedX = offset.X - bounds.MinX;
        var normalizedY = offset.Y - bounds.MinY;
        var width = bounds.Width;
        var height = bounds.Height;

        return rotation switch
        {
            BuildingRotation.Clockwise90 => new Vec2i(height - 1 - normalizedY, normalizedX),
            BuildingRotation.Clockwise180 => new Vec2i(width - 1 - normalizedX, height - 1 - normalizedY),
            BuildingRotation.Clockwise270 => new Vec2i(normalizedY, width - 1 - normalizedX),
            _ => new Vec2i(normalizedX, normalizedY),
        };
    }

}

using System;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

internal static class TerrainClearanceHelper
{
    private static readonly Vec3i[] Neighbours =
        [Vec3i.South, Vec3i.North, Vec3i.East, Vec3i.West];

    public static Vec3i? FindAdjacentPassable(WorldMap map, Vec3i pos)
    {
        foreach (var offset in Neighbours)
        {
            var candidate = pos + offset;
            if (map.IsWalkable(candidate))
                return candidate;
        }

        return null;
    }

    public static Vec3i? FindReachableAdjacentPassable(WorldMap map, Vec3i targetPos, Vec3i origin)
    {
        Vec3i? bestCandidate = null;
        var bestPathLength = int.MaxValue;

        foreach (var offset in Neighbours)
        {
            var candidate = targetPos + offset;
            if (!map.IsWalkable(candidate))
                continue;

            var path = Pathfinder.FindPath(map, origin, candidate);
            if (path.Count == 0)
                continue;

            if (path.Count >= bestPathLength)
                continue;

            bestCandidate = candidate;
            bestPathLength = path.Count;
        }

        return bestCandidate;
    }

    public static TerrainClearedGroundResult ResolveClearedTerrain(GameContext ctx, WorldMap map, Vec3i pos, string? currentMaterialId)
    {
        var data = ctx.TryGet<DataManager>();
        return TerrainClearedGroundResolver.Resolve(
            pos,
            currentMaterialId,
            isWoodMaterial: materialId => string.Equals(materialId, MaterialIds.Wood, StringComparison.OrdinalIgnoreCase),
            isDirtMaterial: materialId => MaterialHasTag(data, materialId, TagIds.Dirt),
            tryGetTerrainMaterial: candidatePos => TryGetTerrainMaterialSample(map, candidatePos, data));
    }

    private static bool MaterialHasTag(DataManager? data, string? materialId, string tagId)
    {
        if (string.IsNullOrWhiteSpace(materialId))
            return false;

        return data?.Materials.GetOrNull(materialId)?.Tags.Contains(tagId) == true;
    }

    private static TerrainGroundMaterialSample? TryGetTerrainMaterialSample(WorldMap map, Vec3i pos, DataManager? data)
    {
        if (pos.X < 0 || pos.Y < 0 || pos.Z < 0 || pos.X >= map.Width || pos.Y >= map.Height || pos.Z >= map.Depth)
            return null;

        var tile = map.GetTile(pos);
        if (tile.TileDefId == TileDefIds.Empty || tile.TileDefId == TileDefIds.Tree || string.IsNullOrWhiteSpace(tile.MaterialId))
            return null;

        if (string.Equals(tile.MaterialId, MaterialIds.Wood, StringComparison.OrdinalIgnoreCase))
            return null;

        var material = data?.Materials.GetOrNull(tile.MaterialId);
        if (material is null)
            return null;

        return material.Tags.Contains(TagIds.Dirt) || material.Tags.Contains(TagIds.Stone)
            ? new TerrainGroundMaterialSample(
                tile.MaterialId,
                material.Tags.Contains(TagIds.Dirt) ? TerrainGroundMaterialKind.Dirt : TerrainGroundMaterialKind.Stone)
            : null;
    }
}

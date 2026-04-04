using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

internal readonly record struct ClearedTerrainResult(string TileDefId, string MaterialId);

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

    public static ClearedTerrainResult ResolveClearedTerrain(GameContext ctx, WorldMap map, Vec3i pos, string? currentMaterialId)
    {
        var materialId = ResolveClearedGroundMaterialId(ctx, map, pos, currentMaterialId);
        return new ClearedTerrainResult(ResolveClearedGroundTileDefId(ctx, materialId), materialId);
    }

    private static string ResolveClearedGroundMaterialId(GameContext ctx, WorldMap map, Vec3i pos, string? currentMaterialId)
    {
        if (!string.IsNullOrWhiteSpace(currentMaterialId) &&
            !string.Equals(currentMaterialId, MaterialIds.Wood, StringComparison.OrdinalIgnoreCase))
            return currentMaterialId!;

        var data = ctx.TryGet<DataManager>();
        var belowMaterialId = TryGetTerrainMaterial(map, pos + new Vec3i(0, 0, 1), data);
        if (belowMaterialId is not null)
            return belowMaterialId;

        string? bestMaterialId = null;
        var bestScore = int.MinValue;
        for (int radius = 1; radius <= 4; radius++)
        {
            foreach (var candidatePos in EnumerateRing(pos, radius))
            {
                var candidateMaterialId = TryGetTerrainMaterial(map, candidatePos, data);
                if (candidateMaterialId is null)
                    continue;

                var score = (10 - radius) * 10;
                var material = data?.Materials.GetOrNull(candidateMaterialId);
                if (material?.Tags.Contains(TagIds.Dirt) == true)
                    score += 4;
                else if (material?.Tags.Contains(TagIds.Stone) == true)
                    score += 2;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestMaterialId = candidateMaterialId;
            }
        }

        if (bestMaterialId is not null)
            return bestMaterialId;

        return MaterialIds.Granite;
    }

    private static string ResolveClearedGroundTileDefId(GameContext ctx, string materialId)
    {
        var data = ctx.TryGet<DataManager>();
        var material = data?.Materials.GetOrNull(materialId);
        if (material?.Tags.Contains(TagIds.Dirt) == true)
            return TileDefIds.Soil;

        return TileDefIds.StoneFloor;
    }

    private static string? TryGetTerrainMaterial(WorldMap map, Vec3i pos, DataManager? data)
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
            return tile.MaterialId;

        return material.Tags.Contains(TagIds.Dirt) || material.Tags.Contains(TagIds.Stone)
            ? tile.MaterialId
            : null;
    }

    private static IEnumerable<Vec3i> EnumerateRing(Vec3i origin, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
        {
            if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                continue;

            yield return new Vec3i(origin.X + dx, origin.Y + dy, origin.Z);
        }
    }
}
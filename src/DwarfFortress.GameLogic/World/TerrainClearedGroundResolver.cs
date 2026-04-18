using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;

namespace DwarfFortress.GameLogic.World;

public enum TerrainGroundMaterialKind : byte
{
    None = 0,
    Dirt = 1,
    Stone = 2,
}

public readonly record struct TerrainGroundMaterialSample(string MaterialId, TerrainGroundMaterialKind Kind);

public readonly record struct TerrainClearedGroundResult(string TileDefId, string MaterialId);

public static class TerrainClearedGroundResolver
{
    public static TerrainClearedGroundResult Resolve(
        Vec3i pos,
        string? currentMaterialId,
        Func<string?, bool> isWoodMaterial,
        Func<string?, bool> isDirtMaterial,
        Func<Vec3i, TerrainGroundMaterialSample?> tryGetTerrainMaterial)
    {
        if (!string.IsNullOrWhiteSpace(currentMaterialId) && !isWoodMaterial(currentMaterialId))
            return BuildResult(currentMaterialId!, isDirtMaterial(currentMaterialId));

        var belowMaterial = tryGetTerrainMaterial(pos + Vec3i.Up);
        if (belowMaterial is TerrainGroundMaterialSample resolvedBelowMaterial)
            return BuildResult(resolvedBelowMaterial.MaterialId, resolvedBelowMaterial.Kind == TerrainGroundMaterialKind.Dirt);

        TerrainGroundMaterialSample? bestMaterial = null;
        var bestScore = int.MinValue;
        for (var radius = 1; radius <= 4; radius++)
        {
            foreach (var candidatePos in EnumerateRing(pos, radius))
            {
                var candidateMaterial = tryGetTerrainMaterial(candidatePos);
                if (candidateMaterial is not TerrainGroundMaterialSample resolvedCandidateMaterial)
                    continue;

                var score = (10 - radius) * 10;
                score += resolvedCandidateMaterial.Kind switch
                {
                    TerrainGroundMaterialKind.Dirt => 4,
                    TerrainGroundMaterialKind.Stone => 2,
                    _ => 0,
                };

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestMaterial = resolvedCandidateMaterial;
            }
        }

        if (bestMaterial is TerrainGroundMaterialSample resolvedBestMaterial)
            return BuildResult(resolvedBestMaterial.MaterialId, resolvedBestMaterial.Kind == TerrainGroundMaterialKind.Dirt);

        return new TerrainClearedGroundResult(TileDefIds.StoneFloor, MaterialIds.Granite);
    }

    private static TerrainClearedGroundResult BuildResult(string materialId, bool isDirtMaterial)
        => new(isDirtMaterial ? TileDefIds.Soil : TileDefIds.StoneFloor, materialId);

    private static IEnumerable<Vec3i> EnumerateRing(Vec3i origin, int radius)
    {
        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                continue;

            yield return new Vec3i(origin.X + dx, origin.Y + dy, origin.Z);
        }
    }
}

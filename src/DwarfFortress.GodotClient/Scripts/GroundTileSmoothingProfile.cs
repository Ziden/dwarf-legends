using System;
using DwarfFortress.GameLogic.World;

public static class GroundTileSmoothingProfile
{
    public static TileTransitionRuleProfile<TileRenderData, GroundVisualData> Create(Func<string?, string?>? resolveGroundFromMaterial)
        => new(
            ResolveNeighborValue: tile => TerrainGroundResolver.ResolveGroundVisual(tile, resolveGroundFromMaterial),
            CanSmoothBase: static ground => ground.IsNaturalBlendTile,
            ShouldBlendNeighbor: ShouldBlendNeighbor,
            ResolveCorner: ResolveCorner);

    private static bool ShouldBlendNeighbor(GroundVisualData baseGround, GroundVisualData neighborGround)
    {
        if (!neighborGround.IsNaturalBlendTile)
            return false;
        if (string.Equals(baseGround.TileDefId, neighborGround.TileDefId, StringComparison.Ordinal))
            return false;

        var basePriority = GetGroundBlendPriority(baseGround.TileDefId);
        var neighborPriority = GetGroundBlendPriority(neighborGround.TileDefId);
        if (neighborPriority != basePriority)
            return neighborPriority > basePriority;

        return string.CompareOrdinal(neighborGround.TileDefId, baseGround.TileDefId) > 0;
    }

    private static GroundVisualData ResolveCorner(
        GroundVisualData first,
        GroundVisualData second,
        int x,
        int y,
        int z,
        TerrainCornerQuadrant corner)
    {
        var sameTile = string.Equals(first.TileDefId, second.TileDefId, StringComparison.Ordinal);
        var sameMaterial = string.Equals(first.MaterialId, second.MaterialId, StringComparison.OrdinalIgnoreCase);
        if (sameTile && sameMaterial)
            return first;

        var firstPriority = GetGroundBlendPriority(first.TileDefId);
        var secondPriority = GetGroundBlendPriority(second.TileDefId);
        if (firstPriority != secondPriority)
            return firstPriority > secondPriority ? first : second;

        var chooser = StableNoise01(x, y, z, (int)corner, 401, 73);
        return chooser < 0.5f ? first : second;
    }

    private static int GetGroundBlendPriority(string tileDefId)
        => tileDefId switch
        {
            TileDefIds.Grass => 50,
            TileDefIds.Mud => 45,
            TileDefIds.Soil => 40,
            TileDefIds.Sand => 35,
            TileDefIds.Snow => 30,
            _ => 10,
        };

    private static float StableNoise01(int a, int b, int c, int d, int e, int f)
    {
        unchecked
        {
            uint hash = 2166136261;
            hash = (hash ^ (uint)a) * 16777619;
            hash = (hash ^ (uint)b) * 16777619;
            hash = (hash ^ (uint)c) * 16777619;
            hash = (hash ^ (uint)d) * 16777619;
            hash = (hash ^ (uint)e) * 16777619;
            hash = (hash ^ (uint)f) * 16777619;
            return (hash & 0x00FFFFFF) / 16777215f;
        }
    }
}
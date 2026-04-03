using System;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GodotClient.Rendering;

public static class TerrainGroundResolver
{
    private static readonly (int X, int Y)[] NeighborOffsets =
    [
        (0, -1),
        (0, 1),
        (-1, 0),
        (1, 0),
    ];

    public static bool IsNaturalBlendTileDefId(string tileDefId)
        => tileDefId is TileDefIds.Grass or TileDefIds.Soil or TileDefIds.Sand or TileDefIds.Mud or TileDefIds.Snow;

    public static GroundVisualData ResolveTreeGroundVisual(
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial)
    {
        var belowTile = tryGetTile(x, y, z + 1);
        // Keep tree-underlay deterministic so visuals stay stable when trees are cut.
        return ResolveGroundVisual(belowTile, resolveGroundFromMaterial) ?? new GroundVisualData(TileDefIds.Grass, null);
    }

    public static GroundVisualData ResolveLiquidGroundVisual(
        int x,
        int y,
        int z,
        TileRenderData tile,
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial)
    {
        var sameTileGround = ResolveGroundVisual(tile, resolveGroundFromMaterial);
        if (sameTileGround is { } resolvedSameTileGround)
            return resolvedSameTileGround;

        var belowTile = tryGetTile(x, y, z + 1);
        var belowGround = ResolveGroundVisual(belowTile, resolveGroundFromMaterial);
        if (belowGround is { } resolvedBelowGround)
            return resolvedBelowGround;

        foreach (var (dx, dy) in NeighborOffsets)
        {
            var neighborTile = tryGetTile(x + dx, y + dy, z);
            var neighborGround = ResolveGroundVisual(neighborTile, resolveGroundFromMaterial);
            if (neighborGround is { } resolvedNeighborGround)
                return resolvedNeighborGround;
        }

        return new GroundVisualData(TileDefIds.Soil, null);
    }

    public static GroundVisualData? ResolveGroundVisual(
        TileRenderData? tile,
        Func<string?, string?>? resolveGroundFromMaterial)
    {
        if (tile is null)
            return null;

        if (tile.Value.TileDefId == TileDefIds.Grass)
            return new GroundVisualData(TileDefIds.Grass, tile.Value.MaterialId);

        if (tile.Value.TileDefId == TileDefIds.Sand)
            return new GroundVisualData(TileDefIds.Sand, tile.Value.MaterialId);

        if (tile.Value.TileDefId == TileDefIds.Mud)
            return new GroundVisualData(TileDefIds.Mud, tile.Value.MaterialId);

        if (tile.Value.TileDefId == TileDefIds.Snow)
            return new GroundVisualData(TileDefIds.Snow, tile.Value.MaterialId);

        if (tile.Value.TileDefId == TileDefIds.Soil || tile.Value.TileDefId == TileDefIds.SoilWall)
            return new GroundVisualData(TileDefIds.Soil, tile.Value.MaterialId);

        if (tile.Value.TileDefId == TileDefIds.Tree || tile.Value.TileDefId == TileDefIds.Empty)
            return null;

        if (tile.Value.TileDefId is TileDefIds.Water or TileDefIds.Magma)
            return null;

        var groundTileDefId = GroundMaterialResolver.ResolveGroundTileDefIdFromTileDef(tile.Value.TileDefId);
        if (groundTileDefId is not null)
            return new GroundVisualData(groundTileDefId, tile.Value.MaterialId);

        var materialGroundTileDefId = resolveGroundFromMaterial?.Invoke(tile.Value.MaterialId) ?? GroundMaterialResolver.ResolveGroundTileDefId(tile.Value.MaterialId);
        return materialGroundTileDefId is null ? null : new GroundVisualData(materialGroundTileDefId, tile.Value.MaterialId);
    }
}

using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.World;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

internal static class TerrainSurfaceRecipeBuilder
{
    private static readonly Dictionary<Texture2D, Image> SourceImageCache = new();

    public static TerrainSurfaceRecipe Build(
        TileRenderData tile,
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial,
        TerrainTransitionSet? cachedGroundTransitions,
        GroundVisualData? cachedDisplayGround = null,
        Func<int, int, int, GroundVisualData?>? tryGetDisplayGround = null)
    {
        var displayGround = cachedDisplayGround ?? TerrainGroundResolver.ResolveDisplayGround(tile, x, y, z, tryGetTile, resolveGroundFromMaterial);
        var displayGroundAccessor = tryGetDisplayGround ?? CreateDisplayGroundAccessor(tryGetTile, resolveGroundFromMaterial);
        var liquidTileDefId = TerrainGroundResolver.ResolveDisplayedLiquidTileDefId(tile);
        if (liquidTileDefId is not null)
        {
            var ground = displayGround ?? TerrainGroundResolver.ResolveLiquidGroundVisual(x, y, z, tile, tryGetTile, resolveGroundFromMaterial);
            var transitions = cachedGroundTransitions
                ?? ResolveGroundTransitions(tile, x, y, z, tryGetTile, resolveGroundFromMaterial, displayGroundAccessor, ground)
                ?? TerrainTransitionResolver.Resolve(ground, x, y, z, displayGroundAccessor);
            var neighbors = ResolveLiquidNeighborState(liquidTileDefId, x, y, z, tryGetTile);
            return BuildLiquidRecipe(ground, transitions, liquidTileDefId, tile.FluidLevel, neighbors, x, y, z);
        }

        if (tile.TileDefId == TileDefIds.Tree || TerrainGroundResolver.IsNaturalBlendTileDefId(tile.TileDefId))
        {
            var ground = displayGround ?? TerrainGroundResolver.ResolveDisplayGround(tile, x, y, z, tryGetTile, resolveGroundFromMaterial);
            var transitions = cachedGroundTransitions
                ?? ResolveGroundTransitions(tile, x, y, z, tryGetTile, resolveGroundFromMaterial, displayGroundAccessor, ground);
            return BuildGroundRecipe(ground?.TileDefId ?? tile.TileDefId, ground?.MaterialId ?? tile.MaterialId, transitions, x, y, z);
        }

        return BuildGroundRecipe(tile.TileDefId, tile.MaterialId, transitions: null, x, y, z);
    }

    public static Image ComposeImage(TerrainSurfaceRecipe recipe)
    {
        var image = Image.CreateEmpty(PixelArtFactory.Size, PixelArtFactory.Size, false, Image.Format.Rgba8);
        image.Fill(new Color(0f, 0f, 0f, 0f));

        BlendTexture(image, PixelArtFactory.GetTile(recipe.BaseTileDefId, NullIfEmpty(recipe.BaseMaterialId)));

        BlendGroundPatch(image, recipe.NorthEdge);
        BlendGroundPatch(image, recipe.SouthEdge);
        BlendGroundPatch(image, recipe.WestEdge);
        BlendGroundPatch(image, recipe.EastEdge);
        BlendGroundPatch(image, recipe.NorthWestCorner);
        BlendGroundPatch(image, recipe.NorthEastCorner);
        BlendGroundPatch(image, recipe.SouthWestCorner);
        BlendGroundPatch(image, recipe.SouthEastCorner);

        if (!string.IsNullOrEmpty(recipe.LiquidTileDefId) && recipe.LiquidDepth is LiquidDepthPatchRecipe liquidDepth)
        {
            FillRectAlpha(image, ResolveLiquidOverlayColor(recipe.LiquidTileDefId, recipe.FluidLevel));
            BlendTexture(image, LiquidTransitionPatchRenderer.GetLiquidDepthTexture(recipe.LiquidTileDefId, recipe.FluidLevel, liquidDepth.SameCount, liquidDepth.Variant));
            BlendLiquidPatch(image, recipe.LiquidNorthEdge);
            BlendLiquidPatch(image, recipe.LiquidSouthEdge);
            BlendLiquidPatch(image, recipe.LiquidWestEdge);
            BlendLiquidPatch(image, recipe.LiquidEastEdge);
            BlendLiquidPatch(image, recipe.LiquidNorthWestCorner);
            BlendLiquidPatch(image, recipe.LiquidNorthEastCorner);
            BlendLiquidPatch(image, recipe.LiquidSouthWestCorner);
            BlendLiquidPatch(image, recipe.LiquidSouthEastCorner);
        }

        return image;
    }

    public static string NormalizeKey(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    public static TerrainTransitionSet? ResolveGroundTransitions(
        TileRenderData tile,
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial,
        Func<int, int, int, GroundVisualData?>? tryGetDisplayGround = null,
        GroundVisualData? cachedDisplayGround = null)
    {
        if (tile.TileDefId != TileDefIds.Tree
            && TerrainGroundResolver.ResolveDisplayedLiquidTileDefId(tile) is null
            && !TerrainGroundResolver.IsNaturalBlendTileDefId(tile.TileDefId))
            return null;

        var baseGround = cachedDisplayGround ?? TerrainGroundResolver.ResolveDisplayGround(tile, x, y, z, tryGetTile, resolveGroundFromMaterial);
        if (baseGround is not GroundVisualData resolvedBaseGround)
            return null;

        return TerrainTransitionResolver.Resolve(
            resolvedBaseGround,
            x,
            y,
            z,
            tryGetDisplayGround ?? CreateDisplayGroundAccessor(tryGetTile, resolveGroundFromMaterial));
    }

    private static TerrainSurfaceRecipe BuildGroundRecipe(
        string baseTileDefId,
        string? baseMaterialId,
        TerrainTransitionSet? transitions,
        int x,
        int y,
        int z)
    {
        return new TerrainSurfaceRecipe(
            BaseTileDefId: baseTileDefId,
            BaseMaterialId: NormalizeKey(baseMaterialId),
            NorthEdge: BuildGroundEdgePatch(transitions?.North, TerrainEdgeDirection.North, x, y, z, trimStart: transitions?.HasWest ?? false, trimEnd: transitions?.HasEast ?? false),
            SouthEdge: BuildGroundEdgePatch(transitions?.South, TerrainEdgeDirection.South, x, y, z, trimStart: transitions?.HasWest ?? false, trimEnd: transitions?.HasEast ?? false),
            WestEdge: BuildGroundEdgePatch(transitions?.West, TerrainEdgeDirection.West, x, y, z, trimStart: transitions?.HasNorth ?? false, trimEnd: transitions?.HasSouth ?? false),
            EastEdge: BuildGroundEdgePatch(transitions?.East, TerrainEdgeDirection.East, x, y, z, trimStart: transitions?.HasNorth ?? false, trimEnd: transitions?.HasSouth ?? false),
            NorthWestCorner: BuildGroundCornerPatch(transitions?.NorthWest, TerrainCornerQuadrant.NorthWest, x, y, z),
            NorthEastCorner: BuildGroundCornerPatch(transitions?.NorthEast, TerrainCornerQuadrant.NorthEast, x, y, z),
            SouthWestCorner: BuildGroundCornerPatch(transitions?.SouthWest, TerrainCornerQuadrant.SouthWest, x, y, z),
            SouthEastCorner: BuildGroundCornerPatch(transitions?.SouthEast, TerrainCornerQuadrant.SouthEast, x, y, z),
            LiquidTileDefId: string.Empty,
            FluidLevel: 0,
            LiquidDepth: null,
            LiquidNorthEdge: null,
            LiquidSouthEdge: null,
            LiquidWestEdge: null,
            LiquidEastEdge: null,
            LiquidNorthWestCorner: null,
            LiquidNorthEastCorner: null,
            LiquidSouthWestCorner: null,
            LiquidSouthEastCorner: null);
    }

    private static TerrainSurfaceRecipe BuildLiquidRecipe(
        GroundVisualData ground,
        TerrainTransitionSet transitions,
        string liquidTileDefId,
        byte fluidLevel,
        LiquidNeighborState neighbors,
        int x,
        int y,
        int z)
    {
        return new TerrainSurfaceRecipe(
            BaseTileDefId: ground.TileDefId,
            BaseMaterialId: NormalizeKey(ground.MaterialId),
            NorthEdge: BuildGroundEdgePatch(transitions.North, TerrainEdgeDirection.North, x, y, z, trimStart: transitions.HasWest, trimEnd: transitions.HasEast),
            SouthEdge: BuildGroundEdgePatch(transitions.South, TerrainEdgeDirection.South, x, y, z, trimStart: transitions.HasWest, trimEnd: transitions.HasEast),
            WestEdge: BuildGroundEdgePatch(transitions.West, TerrainEdgeDirection.West, x, y, z, trimStart: transitions.HasNorth, trimEnd: transitions.HasSouth),
            EastEdge: BuildGroundEdgePatch(transitions.East, TerrainEdgeDirection.East, x, y, z, trimStart: transitions.HasNorth, trimEnd: transitions.HasSouth),
            NorthWestCorner: BuildGroundCornerPatch(transitions.NorthWest, TerrainCornerQuadrant.NorthWest, x, y, z),
            NorthEastCorner: BuildGroundCornerPatch(transitions.NorthEast, TerrainCornerQuadrant.NorthEast, x, y, z),
            SouthWestCorner: BuildGroundCornerPatch(transitions.SouthWest, TerrainCornerQuadrant.SouthWest, x, y, z),
            SouthEastCorner: BuildGroundCornerPatch(transitions.SouthEast, TerrainCornerQuadrant.SouthEast, x, y, z),
            LiquidTileDefId: liquidTileDefId,
            FluidLevel: fluidLevel,
            LiquidDepth: new LiquidDepthPatchRecipe(liquidTileDefId, fluidLevel, (byte)Math.Clamp(neighbors.SameCount, 0, 4), LiquidTransitionPatchRenderer.ResolveLiquidDepthVariant(x, y, z)),
            LiquidNorthEdge: neighbors.HasNorthShore ? BuildLiquidEdgePatch(liquidTileDefId, TerrainEdgeDirection.North, x, y, z, trimStart: neighbors.HasWestShore, trimEnd: neighbors.HasEastShore) : null,
            LiquidSouthEdge: neighbors.HasSouthShore ? BuildLiquidEdgePatch(liquidTileDefId, TerrainEdgeDirection.South, x, y, z, trimStart: neighbors.HasWestShore, trimEnd: neighbors.HasEastShore) : null,
            LiquidWestEdge: neighbors.HasWestShore ? BuildLiquidEdgePatch(liquidTileDefId, TerrainEdgeDirection.West, x, y, z, trimStart: neighbors.HasNorthShore, trimEnd: neighbors.HasSouthShore) : null,
            LiquidEastEdge: neighbors.HasEastShore ? BuildLiquidEdgePatch(liquidTileDefId, TerrainEdgeDirection.East, x, y, z, trimStart: neighbors.HasNorthShore, trimEnd: neighbors.HasSouthShore) : null,
            LiquidNorthWestCorner: neighbors.HasNorthShore && neighbors.HasWestShore ? BuildLiquidCornerPatch(liquidTileDefId, TerrainCornerQuadrant.NorthWest, x, y, z) : null,
            LiquidNorthEastCorner: neighbors.HasNorthShore && neighbors.HasEastShore ? BuildLiquidCornerPatch(liquidTileDefId, TerrainCornerQuadrant.NorthEast, x, y, z) : null,
            LiquidSouthWestCorner: neighbors.HasSouthShore && neighbors.HasWestShore ? BuildLiquidCornerPatch(liquidTileDefId, TerrainCornerQuadrant.SouthWest, x, y, z) : null,
            LiquidSouthEastCorner: neighbors.HasSouthShore && neighbors.HasEastShore ? BuildLiquidCornerPatch(liquidTileDefId, TerrainCornerQuadrant.SouthEast, x, y, z) : null);
    }

    private static GroundEdgePatchRecipe? BuildGroundEdgePatch(
        GroundVisualData? neighborGround,
        TerrainEdgeDirection direction,
        int x,
        int y,
        int z,
        bool trimStart,
        bool trimEnd)
    {
        if (neighborGround is not GroundVisualData resolved)
            return null;

        return new GroundEdgePatchRecipe(
            resolved.TileDefId,
            NormalizeKey(resolved.MaterialId),
            direction,
            TerrainTransitionPatchRenderer.ResolveGroundEdgeVariant(direction, x, y, z),
            trimStart,
            trimEnd);
    }

    private static GroundCornerPatchRecipe? BuildGroundCornerPatch(
        GroundVisualData? cornerGround,
        TerrainCornerQuadrant corner,
        int x,
        int y,
        int z)
    {
        if (cornerGround is not GroundVisualData resolved)
            return null;

        return new GroundCornerPatchRecipe(
            resolved.TileDefId,
            NormalizeKey(resolved.MaterialId),
            corner,
            TerrainTransitionPatchRenderer.ResolveGroundCornerVariant(corner, x, y, z));
    }

    private static LiquidEdgePatchRecipe BuildLiquidEdgePatch(
        string liquidTileDefId,
        TerrainEdgeDirection direction,
        int x,
        int y,
        int z,
        bool trimStart,
        bool trimEnd)
    {
        return new LiquidEdgePatchRecipe(
            liquidTileDefId,
            direction,
            LiquidTransitionPatchRenderer.ResolveLiquidEdgeVariant(direction, x, y, z),
            trimStart,
            trimEnd);
    }

    private static LiquidCornerPatchRecipe BuildLiquidCornerPatch(
        string liquidTileDefId,
        TerrainCornerQuadrant corner,
        int x,
        int y,
        int z)
    {
        return new LiquidCornerPatchRecipe(
            liquidTileDefId,
            corner,
            LiquidTransitionPatchRenderer.ResolveLiquidCornerVariant(corner, x, y, z));
    }

    private static LiquidNeighborState ResolveLiquidNeighborState(
        string liquidTileDefId,
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile)
    {
        var matches = TileSmoothingResolver.ResolveMatches(
            x,
            y,
            z,
            tryGetTile,
            new TileMatchRuleProfile<TileRenderData>(tile => IsSameLiquid(liquidTileDefId, tile)));

        return new LiquidNeighborState(
            NorthSame: matches.NorthMatch,
            SouthSame: matches.SouthMatch,
            WestSame: matches.WestMatch,
            EastSame: matches.EastMatch);
    }

    private static bool IsSameLiquid(string liquidTileDefId, TileRenderData? tile)
    {
        if (tile is null)
            return false;

        return string.Equals(TerrainGroundResolver.ResolveDisplayedLiquidTileDefId(tile.Value), liquidTileDefId, StringComparison.Ordinal);
    }

    private static Func<int, int, int, GroundVisualData?> CreateDisplayGroundAccessor(
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial)
    {
        return (neighborX, neighborY, neighborZ) =>
        {
            var neighborTile = tryGetTile(neighborX, neighborY, neighborZ);
            return neighborTile is TileRenderData resolvedNeighborTile
                ? TerrainGroundResolver.ResolveDisplayGround(resolvedNeighborTile, neighborX, neighborY, neighborZ, tryGetTile, resolveGroundFromMaterial)
                : null;
        };
    }

    private static void BlendGroundPatch(Image target, GroundEdgePatchRecipe? patch)
    {
        if (patch is not GroundEdgePatchRecipe resolved)
            return;

        BlendTexture(
            target,
            TerrainTransitionPatchRenderer.GetGroundEdgePatchTexture(
                new GroundVisualData(resolved.TileDefId, NullIfEmpty(resolved.MaterialId)),
                resolved.Direction,
                resolved.Variant,
                resolved.TrimStart,
                resolved.TrimEnd));
    }

    private static void BlendGroundPatch(Image target, GroundCornerPatchRecipe? patch)
    {
        if (patch is not GroundCornerPatchRecipe resolved)
            return;

        BlendTexture(
            target,
            TerrainTransitionPatchRenderer.GetGroundCornerPatchTexture(
                new GroundVisualData(resolved.TileDefId, NullIfEmpty(resolved.MaterialId)),
                resolved.Corner,
                resolved.Variant));
    }

    private static void BlendLiquidPatch(Image target, LiquidEdgePatchRecipe? patch)
    {
        if (patch is not LiquidEdgePatchRecipe resolved)
            return;

        BlendTexture(
            target,
            LiquidTransitionPatchRenderer.GetLiquidEdgePatchTexture(
                resolved.LiquidTileDefId,
                resolved.Direction,
                resolved.Variant,
                resolved.TrimStart,
                resolved.TrimEnd));
    }

    private static void BlendLiquidPatch(Image target, LiquidCornerPatchRecipe? patch)
    {
        if (patch is not LiquidCornerPatchRecipe resolved)
            return;

        BlendTexture(
            target,
            LiquidTransitionPatchRenderer.GetLiquidCornerPatchTexture(
                resolved.LiquidTileDefId,
                resolved.Corner,
                resolved.Variant));
    }

    private static void BlendTexture(Image target, Texture2D texture)
    {
        var source = GetSourceImage(texture);
        if (source is null)
            return;

        var width = Math.Min(target.GetWidth(), source.GetWidth());
        var height = Math.Min(target.GetHeight(), source.GetHeight());
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sourceColor = source.GetPixel(x, y);
            if (sourceColor.A <= 0.001f)
                continue;

            target.SetPixel(x, y, target.GetPixel(x, y).Blend(sourceColor));
        }
    }

    private static void FillRectAlpha(Image target, Color color)
    {
        if (color.A <= 0.001f)
            return;

        var width = target.GetWidth();
        var height = target.GetHeight();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            target.SetPixel(x, y, target.GetPixel(x, y).Blend(color));
    }

    private static Image? GetSourceImage(Texture2D texture)
    {
        if (SourceImageCache.TryGetValue(texture, out var cached))
            return cached;

        try
        {
            var image = texture.GetImage();
            image.Convert(Image.Format.Rgba8);
            SourceImageCache[texture] = image;
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static Color ResolveLiquidOverlayColor(string liquidTileDefId, byte fluidLevel)
    {
        var depthAlpha = Mathf.Clamp(0.48f + fluidLevel * 0.04f, 0.48f, 0.72f);
        return liquidTileDefId == TileDefIds.Magma
            ? new Color(0.95f, 0.38f, 0.05f, depthAlpha)
            : new Color(0.18f, 0.44f, 0.90f, depthAlpha);
    }

    private static string? NullIfEmpty(string value)
        => string.IsNullOrEmpty(value) ? null : value;
}

internal readonly record struct GroundEdgePatchRecipe(
    string TileDefId,
    string MaterialId,
    TerrainEdgeDirection Direction,
    byte Variant,
    bool TrimStart,
    bool TrimEnd);

internal readonly record struct GroundCornerPatchRecipe(
    string TileDefId,
    string MaterialId,
    TerrainCornerQuadrant Corner,
    byte Variant);

internal readonly record struct LiquidDepthPatchRecipe(
    string LiquidTileDefId,
    byte FluidLevel,
    byte SameCount,
    byte Variant);

internal readonly record struct LiquidEdgePatchRecipe(
    string LiquidTileDefId,
    TerrainEdgeDirection Direction,
    byte Variant,
    bool TrimStart,
    bool TrimEnd);

internal readonly record struct LiquidCornerPatchRecipe(
    string LiquidTileDefId,
    TerrainCornerQuadrant Corner,
    byte Variant);

internal readonly record struct TerrainSurfaceRecipe(
    string BaseTileDefId,
    string BaseMaterialId,
    GroundEdgePatchRecipe? NorthEdge,
    GroundEdgePatchRecipe? SouthEdge,
    GroundEdgePatchRecipe? WestEdge,
    GroundEdgePatchRecipe? EastEdge,
    GroundCornerPatchRecipe? NorthWestCorner,
    GroundCornerPatchRecipe? NorthEastCorner,
    GroundCornerPatchRecipe? SouthWestCorner,
    GroundCornerPatchRecipe? SouthEastCorner,
    string LiquidTileDefId,
    byte FluidLevel,
    LiquidDepthPatchRecipe? LiquidDepth,
    LiquidEdgePatchRecipe? LiquidNorthEdge,
    LiquidEdgePatchRecipe? LiquidSouthEdge,
    LiquidEdgePatchRecipe? LiquidWestEdge,
    LiquidEdgePatchRecipe? LiquidEastEdge,
    LiquidCornerPatchRecipe? LiquidNorthWestCorner,
    LiquidCornerPatchRecipe? LiquidNorthEastCorner,
    LiquidCornerPatchRecipe? LiquidSouthWestCorner,
    LiquidCornerPatchRecipe? LiquidSouthEastCorner);

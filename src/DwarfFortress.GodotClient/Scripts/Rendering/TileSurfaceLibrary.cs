using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.World;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public static class TileSurfaceLibrary
{
    private const int InitialTextureArrayCapacity = 64;

    private static readonly object Sync = new();
    private static readonly Dictionary<TileSurfaceRecipe, TileSurfaceEntry> Entries = new();
    private static readonly Dictionary<Texture2D, Image> SourceImageCache = new();
    private static readonly List<Image> ArrayLayerImages = new();

    private static Texture2DArray? _textureArray;
    private static int _textureArrayCapacity;

    public static Texture2D GetOrCreateTexture(
        TileRenderData tile,
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial,
        TerrainTransitionSet? cachedGroundTransitions = null)
    {
        lock (Sync)
        {
            var recipe = BuildRecipe(tile, x, y, z, tryGetTile, resolveGroundFromMaterial, cachedGroundTransitions);
            return GetOrCreateEntry(recipe).Texture;
        }
    }

    public static int GetOrCreateArrayLayer(
        TileRenderData tile,
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial,
        TerrainTransitionSet? cachedGroundTransitions = null)
    {
        lock (Sync)
        {
            var recipe = BuildRecipe(tile, x, y, z, tryGetTile, resolveGroundFromMaterial, cachedGroundTransitions);
            var entry = GetOrCreateEntry(recipe);
            return EnsureArrayLayer(entry);
        }
    }

    public static Texture2DArray? GetTextureArray()
    {
        lock (Sync)
            return _textureArray;
    }

    private static TileSurfaceEntry GetOrCreateEntry(TileSurfaceRecipe recipe)
    {
        if (Entries.TryGetValue(recipe, out var existing))
            return existing;

        var image = ComposeSurfaceImage(recipe);
        var texture = ImageTexture.CreateFromImage(image);
        var created = new TileSurfaceEntry(image, texture);
        Entries[recipe] = created;
        return created;
    }

    private static int EnsureArrayLayer(TileSurfaceEntry entry)
    {
        if (entry.ArrayLayer >= 0)
            return entry.ArrayLayer;

        entry.ArrayLayer = ArrayLayerImages.Count;
        ArrayLayerImages.Add(entry.Image);
        EnsureTextureArrayCapacity(entry.ArrayLayer + 1);
        _textureArray!.UpdateLayer(entry.Image, entry.ArrayLayer);
        return entry.ArrayLayer;
    }

    private static void EnsureTextureArrayCapacity(int requiredLayerCount)
    {
        if (_textureArray is not null && requiredLayerCount <= _textureArrayCapacity)
            return;

        _textureArrayCapacity = _textureArrayCapacity == 0
            ? Math.Max(InitialTextureArrayCapacity, requiredLayerCount)
            : Math.Max(_textureArrayCapacity * 2, requiredLayerCount);

        var images = new Godot.Collections.Array<Image>();
        for (var index = 0; index < _textureArrayCapacity; index++)
        {
            images.Add(index < ArrayLayerImages.Count
                ? ArrayLayerImages[index]
                : CreateEmptyLayerImage());
        }

        var nextTextureArray = new Texture2DArray();
        nextTextureArray.CreateFromImages(images);

        var previousTextureArray = _textureArray;
        _textureArray = nextTextureArray;
        previousTextureArray?.Dispose();
    }

    private static Image ComposeSurfaceImage(TileSurfaceRecipe recipe)
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

    private static Image CreateEmptyLayerImage()
    {
        var image = Image.CreateEmpty(PixelArtFactory.Size, PixelArtFactory.Size, false, Image.Format.Rgba8);
        image.Fill(new Color(0f, 0f, 0f, 0f));
        return image;
    }

    private static TileSurfaceRecipe BuildRecipe(
        TileRenderData tile,
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial,
        TerrainTransitionSet? cachedGroundTransitions)
    {
        if (tile.TileDefId == TileDefIds.Tree)
        {
            var ground = TerrainGroundResolver.ResolveTreeGroundVisual(x, y, z, tryGetTile, resolveGroundFromMaterial);
            var transitions = cachedGroundTransitions
                ?? TerrainTransitionResolver.Resolve(ground, x, y, z, tryGetTile, resolveGroundFromMaterial);
            return BuildGroundRecipe(ground.TileDefId, ground.MaterialId, transitions, x, y, z);
        }

        var liquidTileDefId = ResolveDisplayedLiquidTileDefId(tile);
        if (liquidTileDefId is not null)
        {
            var ground = TerrainGroundResolver.ResolveLiquidGroundVisual(x, y, z, tile, tryGetTile, resolveGroundFromMaterial);
            var transitions = cachedGroundTransitions
                ?? TerrainTransitionResolver.Resolve(ground, x, y, z, tryGetTile, resolveGroundFromMaterial);
            var neighbors = ResolveLiquidNeighborState(liquidTileDefId, x, y, z, tryGetTile);
            return BuildLiquidRecipe(ground, transitions, liquidTileDefId, tile.FluidLevel, neighbors, x, y, z);
        }

        if (TerrainGroundResolver.IsNaturalBlendTileDefId(tile.TileDefId))
        {
            var transitions = cachedGroundTransitions;
            if (!transitions.HasValue && TerrainGroundResolver.ResolveGroundVisual(tile, resolveGroundFromMaterial) is GroundVisualData baseGround)
                transitions = TerrainTransitionResolver.Resolve(baseGround, x, y, z, tryGetTile, resolveGroundFromMaterial);

            return BuildGroundRecipe(tile.TileDefId, tile.MaterialId, transitions, x, y, z);
        }

        return BuildGroundRecipe(tile.TileDefId, tile.MaterialId, transitions: null, x, y, z);
    }

    private static TileSurfaceRecipe BuildGroundRecipe(
        string baseTileDefId,
        string? baseMaterialId,
        TerrainTransitionSet? transitions,
        int x,
        int y,
        int z)
    {
        return new TileSurfaceRecipe(
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

    private static TileSurfaceRecipe BuildLiquidRecipe(
        GroundVisualData ground,
        TerrainTransitionSet transitions,
        string liquidTileDefId,
        byte fluidLevel,
        LiquidNeighborState neighbors,
        int x,
        int y,
        int z)
    {
        return new TileSurfaceRecipe(
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

    private static string? ResolveDisplayedLiquidTileDefId(TileRenderData tile)
        => tile.FluidType switch
        {
            FluidType.Water when tile.FluidLevel > 0 => TileDefIds.Water,
            FluidType.Magma when tile.FluidLevel > 0 => TileDefIds.Magma,
            _ when tile.TileDefId is TileDefIds.Water or TileDefIds.Magma => tile.TileDefId,
            _ => null,
        };

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

        return string.Equals(ResolveDisplayedLiquidTileDefId(tile.Value), liquidTileDefId, StringComparison.Ordinal);
    }

    private static Color ResolveLiquidOverlayColor(string liquidTileDefId, byte fluidLevel)
    {
        var depthAlpha = Mathf.Clamp(0.48f + fluidLevel * 0.04f, 0.48f, 0.72f);
        return liquidTileDefId == TileDefIds.Magma
            ? new Color(0.95f, 0.38f, 0.05f, depthAlpha)
            : new Color(0.18f, 0.44f, 0.90f, depthAlpha);
    }

    private static string NormalizeKey(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string? NullIfEmpty(string value)
        => string.IsNullOrEmpty(value) ? null : value;

    private sealed class TileSurfaceEntry
    {
        public TileSurfaceEntry(Image image, Texture2D texture)
        {
            Image = image;
            Texture = texture;
        }

        public Image Image { get; }

        public Texture2D Texture { get; }

        public int ArrayLayer { get; set; } = -1;
    }

    private readonly record struct GroundEdgePatchRecipe(
        string TileDefId,
        string MaterialId,
        TerrainEdgeDirection Direction,
        byte Variant,
        bool TrimStart,
        bool TrimEnd);

    private readonly record struct GroundCornerPatchRecipe(
        string TileDefId,
        string MaterialId,
        TerrainCornerQuadrant Corner,
        byte Variant);

    private readonly record struct LiquidDepthPatchRecipe(
        string LiquidTileDefId,
        byte FluidLevel,
        byte SameCount,
        byte Variant);

    private readonly record struct LiquidEdgePatchRecipe(
        string LiquidTileDefId,
        TerrainEdgeDirection Direction,
        byte Variant,
        bool TrimStart,
        bool TrimEnd);

    private readonly record struct LiquidCornerPatchRecipe(
        string LiquidTileDefId,
        TerrainCornerQuadrant Corner,
        byte Variant);

    private readonly record struct TileSurfaceRecipe(
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
}

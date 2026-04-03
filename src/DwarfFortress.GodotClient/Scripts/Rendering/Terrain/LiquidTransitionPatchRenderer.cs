using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.World;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public readonly record struct LiquidNeighborState(bool NorthSame, bool SouthSame, bool WestSame, bool EastSame)
{
    public int SameCount => (NorthSame ? 1 : 0) + (SouthSame ? 1 : 0) + (WestSame ? 1 : 0) + (EastSame ? 1 : 0);
    public bool HasNorthShore => !NorthSame;
    public bool HasSouthShore => !SouthSame;
    public bool HasWestShore => !WestSame;
    public bool HasEastShore => !EastSame;
}

public static class LiquidTransitionPatchRenderer
{
    private readonly record struct LiquidEdgePatchKey(string LiquidTileDefId, TerrainEdgeDirection Direction, byte Variant, byte TrimMask);
    private readonly record struct LiquidCornerPatchKey(string LiquidTileDefId, TerrainCornerQuadrant Corner, byte Variant);
    private readonly record struct LiquidDepthPatchKey(string LiquidTileDefId, byte FluidLevel, byte SameCount, byte Variant);

    private static readonly Dictionary<LiquidEdgePatchKey, Texture2D> LiquidEdgePatchCache = new();
    private static readonly Dictionary<LiquidCornerPatchKey, Texture2D> LiquidCornerPatchCache = new();
    private static readonly Dictionary<LiquidDepthPatchKey, Texture2D> LiquidDepthPatchCache = new();

    private const byte EdgePatchVariantCount = 5;
    private const byte CornerPatchVariantCount = 4;
    private const byte DepthPatchVariantCount = 6;

    public static void DrawLiquidTransitions(
        CanvasItem canvas,
        Rect2 rect,
        string liquidTileDefId,
        byte fluidLevel,
        int x,
        int y,
        int z,
        LiquidNeighborState neighbors)
    {
        DrawLiquidDepth(canvas, rect, liquidTileDefId, fluidLevel, x, y, z, neighbors.SameCount);
        DrawLiquidShoreTransitions(canvas, rect, liquidTileDefId, x, y, z, neighbors);
    }

    public static Texture2D ResolveLiquidDepthTexture(
        string liquidTileDefId,
        byte fluidLevel,
        int x,
        int y,
        int z,
        int sameCount)
    {
        var variant = ResolveLiquidDepthVariant(x, y, z);
        var key = new LiquidDepthPatchKey(liquidTileDefId, fluidLevel, (byte)Math.Clamp(sameCount, 0, 4), variant);
        if (!LiquidDepthPatchCache.TryGetValue(key, out var texture))
        {
            texture = CreateLiquidDepthPatchTexture(liquidTileDefId, fluidLevel, sameCount, variant);
            LiquidDepthPatchCache[key] = texture;
        }

        return texture;
    }

    public static Texture2D ResolveLiquidEdgePatchTexture(
        string liquidTileDefId,
        TerrainEdgeDirection direction,
        int x,
        int y,
        int z,
        bool trimStart,
        bool trimEnd)
    {
        var variant = ResolveLiquidEdgeVariant(direction, x, y, z);
        var trimMask = (byte)((trimStart ? 1 : 0) | (trimEnd ? 2 : 0));
        var key = new LiquidEdgePatchKey(liquidTileDefId, direction, variant, trimMask);
        if (!LiquidEdgePatchCache.TryGetValue(key, out var texture))
        {
            texture = CreateLiquidEdgePatchTexture(liquidTileDefId, direction, variant, trimStart, trimEnd);
            LiquidEdgePatchCache[key] = texture;
        }

        return texture;
    }

    public static Texture2D ResolveLiquidCornerPatchTexture(
        string liquidTileDefId,
        TerrainCornerQuadrant corner,
        int x,
        int y,
        int z)
    {
        var variant = ResolveLiquidCornerVariant(corner, x, y, z);
        var key = new LiquidCornerPatchKey(liquidTileDefId, corner, variant);
        if (!LiquidCornerPatchCache.TryGetValue(key, out var texture))
        {
            texture = CreateLiquidCornerPatchTexture(liquidTileDefId, corner, variant);
            LiquidCornerPatchCache[key] = texture;
        }

        return texture;
    }

    public static byte ResolveLiquidDepthVariant(int x, int y, int z)
    {
        var variantNoise = StableNoise01(x, y, z, 701, 19, 47);
        return (byte)Math.Clamp((int)(variantNoise * DepthPatchVariantCount), 0, DepthPatchVariantCount - 1);
    }

    public static byte ResolveLiquidEdgeVariant(TerrainEdgeDirection direction, int x, int y, int z)
    {
        var variantNoise = StableNoise01(x, y, z, (int)direction, 211, 31);
        return (byte)Math.Clamp((int)(variantNoise * EdgePatchVariantCount), 0, EdgePatchVariantCount - 1);
    }

    public static byte ResolveLiquidCornerVariant(TerrainCornerQuadrant corner, int x, int y, int z)
    {
        var variantNoise = StableNoise01(x, y, z, (int)corner, 251, 73);
        return (byte)Math.Clamp((int)(variantNoise * CornerPatchVariantCount), 0, CornerPatchVariantCount - 1);
    }

    public static Texture2D GetLiquidDepthTexture(string liquidTileDefId, byte fluidLevel, int sameCount, byte variant)
    {
        var key = new LiquidDepthPatchKey(liquidTileDefId, fluidLevel, (byte)Math.Clamp(sameCount, 0, 4), variant);
        if (!LiquidDepthPatchCache.TryGetValue(key, out var texture))
        {
            texture = CreateLiquidDepthPatchTexture(liquidTileDefId, fluidLevel, sameCount, variant);
            LiquidDepthPatchCache[key] = texture;
        }

        return texture;
    }

    public static Texture2D GetLiquidEdgePatchTexture(
        string liquidTileDefId,
        TerrainEdgeDirection direction,
        byte variant,
        bool trimStart,
        bool trimEnd)
    {
        var trimMask = (byte)((trimStart ? 1 : 0) | (trimEnd ? 2 : 0));
        var key = new LiquidEdgePatchKey(liquidTileDefId, direction, variant, trimMask);
        if (!LiquidEdgePatchCache.TryGetValue(key, out var texture))
        {
            texture = CreateLiquidEdgePatchTexture(liquidTileDefId, direction, variant, trimStart, trimEnd);
            LiquidEdgePatchCache[key] = texture;
        }

        return texture;
    }

    public static Texture2D GetLiquidCornerPatchTexture(
        string liquidTileDefId,
        TerrainCornerQuadrant corner,
        byte variant)
    {
        var key = new LiquidCornerPatchKey(liquidTileDefId, corner, variant);
        if (!LiquidCornerPatchCache.TryGetValue(key, out var texture))
        {
            texture = CreateLiquidCornerPatchTexture(liquidTileDefId, corner, variant);
            LiquidCornerPatchCache[key] = texture;
        }

        return texture;
    }

    private static void DrawLiquidDepth(
        CanvasItem canvas,
        Rect2 rect,
        string liquidTileDefId,
        byte fluidLevel,
        int x,
        int y,
        int z,
        int sameCount)
    {
        var texture = ResolveLiquidDepthTexture(liquidTileDefId, fluidLevel, x, y, z, sameCount);
        canvas.DrawTextureRect(texture, rect, false);
    }

    private static void DrawLiquidShoreTransitions(
        CanvasItem canvas,
        Rect2 rect,
        string liquidTileDefId,
        int x,
        int y,
        int z,
        LiquidNeighborState neighbors)
    {
        if (neighbors.HasNorthShore)
            DrawLiquidEdgeTransition(canvas, rect, liquidTileDefId, x, y, z, TerrainEdgeDirection.North, trimStart: neighbors.HasWestShore, trimEnd: neighbors.HasEastShore);
        if (neighbors.HasSouthShore)
            DrawLiquidEdgeTransition(canvas, rect, liquidTileDefId, x, y, z, TerrainEdgeDirection.South, trimStart: neighbors.HasWestShore, trimEnd: neighbors.HasEastShore);
        if (neighbors.HasWestShore)
            DrawLiquidEdgeTransition(canvas, rect, liquidTileDefId, x, y, z, TerrainEdgeDirection.West, trimStart: neighbors.HasNorthShore, trimEnd: neighbors.HasSouthShore);
        if (neighbors.HasEastShore)
            DrawLiquidEdgeTransition(canvas, rect, liquidTileDefId, x, y, z, TerrainEdgeDirection.East, trimStart: neighbors.HasNorthShore, trimEnd: neighbors.HasSouthShore);

        if (neighbors.HasNorthShore && neighbors.HasWestShore)
            DrawLiquidCornerTransition(canvas, rect, liquidTileDefId, x, y, z, TerrainCornerQuadrant.NorthWest);
        if (neighbors.HasNorthShore && neighbors.HasEastShore)
            DrawLiquidCornerTransition(canvas, rect, liquidTileDefId, x, y, z, TerrainCornerQuadrant.NorthEast);
        if (neighbors.HasSouthShore && neighbors.HasWestShore)
            DrawLiquidCornerTransition(canvas, rect, liquidTileDefId, x, y, z, TerrainCornerQuadrant.SouthWest);
        if (neighbors.HasSouthShore && neighbors.HasEastShore)
            DrawLiquidCornerTransition(canvas, rect, liquidTileDefId, x, y, z, TerrainCornerQuadrant.SouthEast);
    }

    private static void DrawLiquidEdgeTransition(
        CanvasItem canvas,
        Rect2 rect,
        string liquidTileDefId,
        int x,
        int y,
        int z,
        TerrainEdgeDirection direction,
        bool trimStart,
        bool trimEnd)
    {
        var texture = ResolveLiquidEdgePatchTexture(liquidTileDefId, direction, x, y, z, trimStart, trimEnd);
        canvas.DrawTextureRect(texture, rect, false);
    }

    private static void DrawLiquidCornerTransition(
        CanvasItem canvas,
        Rect2 rect,
        string liquidTileDefId,
        int x,
        int y,
        int z,
        TerrainCornerQuadrant corner)
    {
        var texture = ResolveLiquidCornerPatchTexture(liquidTileDefId, corner, x, y, z);
        canvas.DrawTextureRect(texture, rect, false);
    }

    private static Texture2D CreateLiquidEdgePatchTexture(
        string liquidTileDefId,
        TerrainEdgeDirection direction,
        byte variant,
        bool trimStart,
        bool trimEnd)
    {
        var image = Image.CreateEmpty(PixelArtFactory.Size, PixelArtFactory.Size, false, Image.Format.Rgba8);
        image.Fill(new Color(0f, 0f, 0f, 0f));

        var (foamColor, edgeColor, _, _) = ResolveLiquidPalette(liquidTileDefId);
        var width = image.GetWidth();
        var height = image.GetHeight();

        for (var py = 0; py < height; py++)
        for (var px = 0; px < width; px++)
        {
            if (!TryResolveEdgePixel(direction, variant, px, py, width, height, trimStart, trimEnd, out var inwardNorm, out var depth))
                continue;

            var t = Math.Clamp(inwardNorm / Math.Max(0.001f, depth), 0f, 1f);
            var mix = MathF.Min(1f, MathF.Max(0f, (t * 1.35f) - 0.2f));
            var color = LerpColor(foamColor, edgeColor, mix);
            image.SetPixel(px, py, color);
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D CreateLiquidCornerPatchTexture(
        string liquidTileDefId,
        TerrainCornerQuadrant corner,
        byte variant)
    {
        var image = Image.CreateEmpty(PixelArtFactory.Size, PixelArtFactory.Size, false, Image.Format.Rgba8);
        image.Fill(new Color(0f, 0f, 0f, 0f));

        var (foamColor, edgeColor, _, _) = ResolveLiquidPalette(liquidTileDefId);
        var width = image.GetWidth();
        var height = image.GetHeight();

        for (var py = 0; py < height; py++)
        for (var px = 0; px < width; px++)
        {
            if (!TryResolveCornerPixel(corner, variant, px, py, width, height, out var distNorm, out var radius))
                continue;

            var t = Math.Clamp(distNorm / Math.Max(0.001f, radius), 0f, 1f);
            var mix = MathF.Min(1f, MathF.Max(0f, (t * 1.25f) - 0.16f));
            var color = LerpColor(foamColor, edgeColor, mix);
            image.SetPixel(px, py, color);
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D CreateLiquidDepthPatchTexture(
        string liquidTileDefId,
        byte fluidLevel,
        int sameCount,
        byte variant)
    {
        var image = Image.CreateEmpty(PixelArtFactory.Size, PixelArtFactory.Size, false, Image.Format.Rgba8);
        image.Fill(new Color(0f, 0f, 0f, 0f));

        var (_, _, depthColor, highlightColor) = ResolveLiquidPalette(liquidTileDefId);
        var width = image.GetWidth();
        var height = image.GetHeight();
        var levelNorm = Math.Clamp(fluidLevel / 7f, 0.14f, 1f);
        var basinNorm = Math.Clamp(0.22f + (sameCount * 0.18f), 0.22f, 0.94f);
        var depthNorm = Math.Clamp((levelNorm * 0.68f) + (basinNorm * 0.32f), 0.18f, 0.96f);
        var radius = Math.Clamp(0.24f + (depthNorm * 0.22f) + (variant * 0.01f), 0.20f, 0.56f);
        var centerX = (width - 1) * 0.5f;
        var centerY = (height - 1) * 0.5f;

        for (var py = 0; py < height; py++)
        for (var px = 0; px < width; px++)
        {
            var nx = (px - centerX) / Math.Max(1f, centerX);
            var ny = (py - centerY) / Math.Max(1f, centerY);
            var dist = MathF.Sqrt((nx * nx) + (ny * ny));

            var noise = (StableNoise01(px, py, fluidLevel, sameCount, variant, 97) - 0.5f) * 0.08f;
            var wave = MathF.Sin(((nx + ny) * (9.5f + (variant * 0.7f))) + (variant * 1.19f)) * 0.03f;
            var threshold = Math.Clamp(radius + noise + wave, 0.16f, 0.62f);
            if (dist > threshold)
                continue;

            var depthBlend = Math.Clamp(dist / Math.Max(0.001f, threshold), 0f, 1f);
            var shaded = LerpColor(depthColor, depthColor.Darkened(0.24f), depthBlend * 0.7f);
            var highlightMask = py < centerY && depthBlend < 0.82f;
            if (highlightMask)
            {
                var highlightStrength = (1f - depthBlend) * 0.45f;
                shaded = LerpColor(shaded, highlightColor, highlightStrength);
            }

            image.SetPixel(px, py, shaded);
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static (Color Foam, Color Edge, Color Depth, Color Highlight) ResolveLiquidPalette(string liquidTileDefId)
    {
        if (liquidTileDefId == TileDefIds.Magma)
        {
            return (
                new Color(1.00f, 0.85f, 0.34f, 0.34f),
                new Color(0.72f, 0.22f, 0.10f, 0.28f),
                new Color(0.46f, 0.10f, 0.04f, 0.24f),
                new Color(1.00f, 0.72f, 0.22f, 0.12f));
        }

        return (
            new Color(0.90f, 0.97f, 1.00f, 0.40f),
            new Color(0.12f, 0.38f, 0.72f, 0.30f),
            new Color(0.05f, 0.17f, 0.38f, 0.26f),
            new Color(0.72f, 0.95f, 1.00f, 0.14f));
    }

    private static bool TryResolveEdgePixel(
        TerrainEdgeDirection direction,
        byte variant,
        int px,
        int py,
        int width,
        int height,
        bool trimStart,
        bool trimEnd,
        out float inwardNorm,
        out float depth)
    {
        var denomX = Math.Max(1, width - 1);
        var denomY = Math.Max(1, height - 1);
        float alongNorm;

        switch (direction)
        {
            case TerrainEdgeDirection.North:
                alongNorm = px / (float)denomX;
                inwardNorm = py / (float)denomY;
                break;
            case TerrainEdgeDirection.South:
                alongNorm = px / (float)denomX;
                inwardNorm = 1f - (py / (float)denomY);
                break;
            case TerrainEdgeDirection.West:
                alongNorm = py / (float)denomY;
                inwardNorm = px / (float)denomX;
                break;
            default:
                alongNorm = py / (float)denomY;
                inwardNorm = 1f - (px / (float)denomX);
                break;
        }

        const float cornerTrimBand = 0.14f;
        if (trimStart && alongNorm < cornerTrimBand)
        {
            depth = 0f;
            return false;
        }

        if (trimEnd && alongNorm > 1f - cornerTrimBand)
        {
            depth = 0f;
            return false;
        }

        var macroWave = MathF.Sin((alongNorm * (8.7f + (variant * 0.8f))) + (variant * 1.37f)) * 0.040f;
        var microWave = MathF.Sin((alongNorm * (18.5f + variant)) + (variant * 2.21f)) * 0.018f;
        var jitter = (StableNoise01(px, py, (int)direction, variant, 37, 83) - 0.5f) * 0.07f;
        depth = Math.Clamp(0.12f + (variant * 0.012f) + macroWave + microWave + jitter, 0.06f, 0.24f);

        if (inwardNorm <= depth)
            return true;
        if (inwardNorm >= depth + 0.045f)
            return false;

        var fringeNoise = StableNoise01(px + 29, py + 11, (int)direction, variant, 79, 13);
        var edgeDistance = (inwardNorm - depth) / 0.045f;
        var fringeThreshold = 0.34f + (edgeDistance * 0.58f);
        return fringeNoise > fringeThreshold;
    }

    private static bool TryResolveCornerPixel(
        TerrainCornerQuadrant corner,
        byte variant,
        int px,
        int py,
        int width,
        int height,
        out float distNorm,
        out float radius)
    {
        var denomX = Math.Max(1, width - 1);
        var denomY = Math.Max(1, height - 1);
        float dx;
        float dy;

        switch (corner)
        {
            case TerrainCornerQuadrant.NorthWest:
                dx = px / (float)denomX;
                dy = py / (float)denomY;
                break;
            case TerrainCornerQuadrant.NorthEast:
                dx = (denomX - px) / (float)denomX;
                dy = py / (float)denomY;
                break;
            case TerrainCornerQuadrant.SouthWest:
                dx = px / (float)denomX;
                dy = (denomY - py) / (float)denomY;
                break;
            default:
                dx = (denomX - px) / (float)denomX;
                dy = (denomY - py) / (float)denomY;
                break;
        }

        distNorm = MathF.Sqrt((dx * dx) + (dy * dy));
        var wave = MathF.Sin(((dx + dy) * (10.4f + (variant * 1.3f))) + (variant * 1.43f)) * 0.020f;
        var jitter = (StableNoise01(px, py, (int)corner, variant, 211, 19) - 0.5f) * 0.060f;
        radius = Math.Clamp(0.16f + (variant * 0.012f) + wave + jitter, 0.10f, 0.30f);

        if (distNorm <= radius)
            return true;
        if (distNorm >= radius + 0.055f)
            return false;

        var fringeNoise = StableNoise01(px + 17, py + 29, (int)corner, variant, 59, 7);
        var edgeDistance = (distNorm - radius) / 0.055f;
        var fringeThreshold = 0.34f + (edgeDistance * 0.56f);
        return fringeNoise > fringeThreshold;
    }

    private static Color LerpColor(Color from, Color to, float t)
    {
        var clamped = Math.Clamp(t, 0f, 1f);
        return new Color(
            Mathf.Lerp(from.R, to.R, clamped),
            Mathf.Lerp(from.G, to.G, clamped),
            Mathf.Lerp(from.B, to.B, clamped),
            Mathf.Lerp(from.A, to.A, clamped));
    }

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

using System;
using System.Collections.Generic;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public static class TerrainTransitionPatchRenderer
{
    private readonly record struct GroundEdgePatchKey(string TileDefId, string MaterialIdKey, TerrainEdgeDirection Direction, byte Variant, byte TrimMask);
    private readonly record struct GroundCornerPatchKey(string TileDefId, string MaterialIdKey, TerrainCornerQuadrant Corner, byte Variant);

    private static readonly Dictionary<GroundEdgePatchKey, Texture2D> GroundEdgePatchCache = new();
    private static readonly Dictionary<GroundCornerPatchKey, Texture2D> GroundCornerPatchCache = new();

    private const byte EdgePatchVariantCount = 5;
    private const byte CornerPatchVariantCount = 4;

    public static void DrawTransitions(
        CanvasItem canvas,
        Rect2 rect,
        int x,
        int y,
        int z,
        in TerrainTransitionSet transitions)
    {
        if (transitions.North is { } n)
            DrawGroundEdgeTextureBlend(canvas, rect, x, y, z, n, TerrainEdgeDirection.North, trimStart: transitions.HasWest, trimEnd: transitions.HasEast);
        if (transitions.South is { } s)
            DrawGroundEdgeTextureBlend(canvas, rect, x, y, z, s, TerrainEdgeDirection.South, trimStart: transitions.HasWest, trimEnd: transitions.HasEast);
        if (transitions.West is { } w)
            DrawGroundEdgeTextureBlend(canvas, rect, x, y, z, w, TerrainEdgeDirection.West, trimStart: transitions.HasNorth, trimEnd: transitions.HasSouth);
        if (transitions.East is { } e)
            DrawGroundEdgeTextureBlend(canvas, rect, x, y, z, e, TerrainEdgeDirection.East, trimStart: transitions.HasNorth, trimEnd: transitions.HasSouth);

        if (transitions.NorthWest is { } nw)
            DrawGroundCornerTextureBlend(canvas, rect, x, y, z, nw, TerrainCornerQuadrant.NorthWest);
        if (transitions.NorthEast is { } ne)
            DrawGroundCornerTextureBlend(canvas, rect, x, y, z, ne, TerrainCornerQuadrant.NorthEast);
        if (transitions.SouthWest is { } sw)
            DrawGroundCornerTextureBlend(canvas, rect, x, y, z, sw, TerrainCornerQuadrant.SouthWest);
        if (transitions.SouthEast is { } se)
            DrawGroundCornerTextureBlend(canvas, rect, x, y, z, se, TerrainCornerQuadrant.SouthEast);
    }

    public static Texture2D ResolveGroundEdgePatchTexture(
        GroundVisualData neighborGround,
        TerrainEdgeDirection direction,
        int x,
        int y,
        int z,
        bool trimStart,
        bool trimEnd)
    {
        var variant = ResolveGroundEdgeVariant(direction, x, y, z);
        return GetGroundEdgePatchTexture(neighborGround, direction, variant, trimStart, trimEnd);
    }

    public static Texture2D ResolveGroundCornerPatchTexture(
        GroundVisualData cornerGround,
        TerrainCornerQuadrant corner,
        int x,
        int y,
        int z)
    {
        var variant = ResolveGroundCornerVariant(corner, x, y, z);
        return GetGroundCornerPatchTexture(cornerGround, corner, variant);
    }

    public static byte ResolveGroundEdgeVariant(TerrainEdgeDirection direction, int x, int y, int z)
    {
        var variantNoise = StableNoise01(x, y, z, (int)direction, 97, 43);
        return (byte)Math.Clamp((int)(variantNoise * EdgePatchVariantCount), 0, EdgePatchVariantCount - 1);
    }

    public static byte ResolveGroundCornerVariant(TerrainCornerQuadrant corner, int x, int y, int z)
    {
        var variantNoise = StableNoise01(x, y, z, (int)corner, 131, 17);
        return (byte)Math.Clamp((int)(variantNoise * CornerPatchVariantCount), 0, CornerPatchVariantCount - 1);
    }

    public static Texture2D GetGroundEdgePatchTexture(
        GroundVisualData neighborGround,
        TerrainEdgeDirection direction,
        byte variant,
        bool trimStart,
        bool trimEnd)
        => ResolveGroundEdgePatchTextureForVariant(neighborGround, direction, variant, trimStart, trimEnd);

    public static Texture2D GetGroundCornerPatchTexture(
        GroundVisualData cornerGround,
        TerrainCornerQuadrant corner,
        byte variant)
        => ResolveGroundCornerPatchTextureForVariant(cornerGround, corner, variant);

    private static void DrawGroundEdgeTextureBlend(
        CanvasItem canvas,
        Rect2 rect,
        int x,
        int y,
        int z,
        GroundVisualData neighborGround,
        TerrainEdgeDirection direction,
        bool trimStart,
        bool trimEnd)
    {
        var patchTexture = ResolveGroundEdgePatchTexture(neighborGround, direction, x, y, z, trimStart, trimEnd);
        canvas.DrawTextureRect(patchTexture, rect, false);
    }

    private static void DrawGroundCornerTextureBlend(
        CanvasItem canvas,
        Rect2 rect,
        int x,
        int y,
        int z,
        GroundVisualData cornerGround,
        TerrainCornerQuadrant corner)
    {
        var patchTexture = ResolveGroundCornerPatchTexture(cornerGround, corner, x, y, z);
        canvas.DrawTextureRect(patchTexture, rect, false);
    }

    private static Texture2D ResolveGroundEdgePatchTextureForVariant(
        GroundVisualData neighborGround,
        TerrainEdgeDirection direction,
        byte variant,
        bool trimStart,
        bool trimEnd)
    {
        var materialKey = string.IsNullOrWhiteSpace(neighborGround.MaterialId)
            ? string.Empty
            : neighborGround.MaterialId.Trim().ToLowerInvariant();
        var trimMask = (byte)((trimStart ? 1 : 0) | (trimEnd ? 2 : 0));
        var cacheKey = new GroundEdgePatchKey(neighborGround.TileDefId, materialKey, direction, variant, trimMask);
        if (GroundEdgePatchCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var sourceTexture = PixelArtFactory.GetTile(neighborGround.TileDefId, neighborGround.MaterialId);
        Image? sourceImage;
        try
        {
            sourceImage = sourceTexture.GetImage();
        }
        catch
        {
            sourceImage = null;
        }

        if (sourceImage is null)
        {
            GroundEdgePatchCache[cacheKey] = sourceTexture;
            return sourceTexture;
        }

        sourceImage.Convert(Image.Format.Rgba8);
        var width = sourceImage.GetWidth();
        var height = sourceImage.GetHeight();
        if (width <= 0 || height <= 0)
        {
            GroundEdgePatchCache[cacheKey] = sourceTexture;
            return sourceTexture;
        }

        var patch = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        patch.Fill(new Color(0f, 0f, 0f, 0f));

        for (var py = 0; py < height; py++)
        for (var px = 0; px < width; px++)
        {
            if (!IsGroundEdgeMaskPixel(direction, variant, px, py, width, height, trimStart, trimEnd))
                continue;

            var color = sourceImage.GetPixel(px, py);
            if (color.A <= 0.01f)
                continue;

            patch.SetPixel(px, py, color);
        }

        var patchTexture = ImageTexture.CreateFromImage(patch);
        GroundEdgePatchCache[cacheKey] = patchTexture;
        return patchTexture;
    }

    private static Texture2D ResolveGroundCornerPatchTextureForVariant(
        GroundVisualData cornerGround,
        TerrainCornerQuadrant corner,
        byte variant)
    {
        var materialKey = string.IsNullOrWhiteSpace(cornerGround.MaterialId)
            ? string.Empty
            : cornerGround.MaterialId.Trim().ToLowerInvariant();
        var cacheKey = new GroundCornerPatchKey(cornerGround.TileDefId, materialKey, corner, variant);
        if (GroundCornerPatchCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var sourceTexture = PixelArtFactory.GetTile(cornerGround.TileDefId, cornerGround.MaterialId);
        Image? sourceImage;
        try
        {
            sourceImage = sourceTexture.GetImage();
        }
        catch
        {
            sourceImage = null;
        }

        if (sourceImage is null)
        {
            GroundCornerPatchCache[cacheKey] = sourceTexture;
            return sourceTexture;
        }

        sourceImage.Convert(Image.Format.Rgba8);
        var width = sourceImage.GetWidth();
        var height = sourceImage.GetHeight();
        if (width <= 0 || height <= 0)
        {
            GroundCornerPatchCache[cacheKey] = sourceTexture;
            return sourceTexture;
        }

        var patch = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        patch.Fill(new Color(0f, 0f, 0f, 0f));

        for (var py = 0; py < height; py++)
        for (var px = 0; px < width; px++)
        {
            if (!IsGroundCornerMaskPixel(corner, variant, px, py, width, height))
                continue;

            var color = sourceImage.GetPixel(px, py);
            if (color.A <= 0.01f)
                continue;

            patch.SetPixel(px, py, color);
        }

        var patchTexture = ImageTexture.CreateFromImage(patch);
        GroundCornerPatchCache[cacheKey] = patchTexture;
        return patchTexture;
    }

    private static bool IsGroundEdgeMaskPixel(
        TerrainEdgeDirection direction,
        byte variant,
        int px,
        int py,
        int width,
        int height,
        bool trimStart,
        bool trimEnd)
    {
        var denomX = Math.Max(1, width - 1);
        var denomY = Math.Max(1, height - 1);
        float alongNorm;
        float inwardNorm;
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
            return false;
        if (trimEnd && alongNorm > 1f - cornerTrimBand)
            return false;

        var macroWave = MathF.Sin((alongNorm * (8.4f + (variant * 0.7f))) + (variant * 1.37f)) * 0.042f;
        var microWave = MathF.Sin((alongNorm * (19f + variant)) + (variant * 2.11f)) * 0.016f;
        var jitter = (StableNoise01(px, py, (int)direction, variant, 37, 83) - 0.5f) * 0.072f;
        var depth = Math.Clamp(0.15f + (variant * 0.012f) + macroWave + microWave + jitter, 0.08f, 0.30f);

        if (inwardNorm <= depth)
            return true;
        if (inwardNorm >= depth + 0.05f)
            return false;

        var fringeNoise = StableNoise01(px + 19, py + 53, (int)direction, variant, 79, 13);
        var edgeDistance = (inwardNorm - depth) / 0.05f;
        var fringeThreshold = 0.38f + (edgeDistance * 0.52f);
        return fringeNoise > fringeThreshold;
    }

    private static bool IsGroundCornerMaskPixel(
        TerrainCornerQuadrant corner,
        byte variant,
        int px,
        int py,
        int width,
        int height)
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

        var dist = MathF.Sqrt((dx * dx) + (dy * dy));
        var baseRadius = 0.17f + (variant * 0.013f);
        var wave = MathF.Sin(((dx + dy) * (10f + (variant * 1.7f))) + (variant * 1.41f)) * 0.020f;
        var jitter = (StableNoise01(px, py, (int)corner, variant, 211, 19) - 0.5f) * 0.060f;
        var radius = Math.Clamp(baseRadius + wave + jitter, 0.11f, 0.31f);

        if (dist <= radius)
            return true;
        if (dist >= radius + 0.06f)
            return false;

        var fringeNoise = StableNoise01(px + 17, py + 29, (int)corner, variant, 59, 7);
        var edgeDistance = (dist - radius) / 0.06f;
        var fringeThreshold = 0.34f + (edgeDistance * 0.56f);
        return fringeNoise > fringeThreshold;
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

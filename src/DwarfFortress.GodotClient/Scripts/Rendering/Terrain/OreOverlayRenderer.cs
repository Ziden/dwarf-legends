using System;
using DwarfFortress.WorldGen.Content;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public static class OreOverlayRenderer
{
    private const int VariantCount = 8;

    public static void Draw(CanvasItem canvas, Rect2 rect, string? oreItemDefId, int x, int y, int z)
    {
        if (string.IsNullOrWhiteSpace(oreItemDefId))
            return;

        Draw(canvas, rect, oreItemDefId, ResolveVariant(oreItemDefId, x, y, z));
    }

    public static void Draw(CanvasItem canvas, Rect2 rect, string? oreItemDefId, byte variant)
    {
        if (string.IsNullOrWhiteSpace(oreItemDefId))
            return;

        var (fill, highlight, shadow) = ResolveOrePalette(oreItemDefId);
        var state = BuildOreSeed(oreItemDefId, variant);
        var clusterCount = 2 + (int)(Next01(ref state) * 3f);
        var clusterRadius = Mathf.Clamp(rect.Size.X * 0.055f, 2f, 5f);
        var xPadding = rect.Size.X * 0.18f;
        var yPadding = rect.Size.Y * 0.18f;

        for (var clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
        {
            var center = new Vector2(
                rect.Position.X + xPadding + (rect.Size.X - 2f * xPadding) * Next01(ref state),
                rect.Position.Y + yPadding + (rect.Size.Y - 2f * yPadding) * Next01(ref state));
            var nodeCount = 2 + (int)(Next01(ref state) * 3f);

            for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                var jitter = new Vector2(
                    (Next01(ref state) - 0.5f) * clusterRadius * 1.9f,
                    (Next01(ref state) - 0.5f) * clusterRadius * 1.9f);
                var nodeCenter = center + jitter;
                var rx = clusterRadius * Mathf.Lerp(0.55f, 1.05f, Next01(ref state));
                var ry = clusterRadius * Mathf.Lerp(0.45f, 0.95f, Next01(ref state));
                var blobRect = new Rect2(nodeCenter - new Vector2(rx, ry), new Vector2(rx * 2f, ry * 2f));
                var highlightRect = new Rect2(
                    blobRect.Position + new Vector2(blobRect.Size.X * 0.18f, blobRect.Size.Y * 0.18f),
                    new Vector2(blobRect.Size.X * 0.46f, blobRect.Size.Y * 0.40f));

                canvas.DrawRect(blobRect.Grow(0.8f), shadow);
                canvas.DrawRect(blobRect, fill);
                canvas.DrawRect(highlightRect, highlight);
            }
        }
    }

    public static void Draw(Image image, Rect2I rect, string? oreItemDefId, int x, int y, int z)
    {
        if (string.IsNullOrWhiteSpace(oreItemDefId))
            return;

        Draw(image, rect, oreItemDefId, ResolveVariant(oreItemDefId, x, y, z));
    }

    public static void Draw(Image image, Rect2I rect, string? oreItemDefId, byte variant)
    {
        if (string.IsNullOrWhiteSpace(oreItemDefId))
            return;

        var drawRect = new Rect2(rect.Position, rect.Size);
        var (fill, highlight, shadow) = ResolveOrePalette(oreItemDefId);
        var state = BuildOreSeed(oreItemDefId, variant);
        var clusterCount = 2 + (int)(Next01(ref state) * 3f);
        var clusterRadius = Mathf.Clamp(drawRect.Size.X * 0.055f, 2f, 5f);
        var xPadding = drawRect.Size.X * 0.18f;
        var yPadding = drawRect.Size.Y * 0.18f;

        for (var clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
        {
            var center = new Vector2(
                drawRect.Position.X + xPadding + (drawRect.Size.X - 2f * xPadding) * Next01(ref state),
                drawRect.Position.Y + yPadding + (drawRect.Size.Y - 2f * yPadding) * Next01(ref state));
            var nodeCount = 2 + (int)(Next01(ref state) * 3f);

            for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                var jitter = new Vector2(
                    (Next01(ref state) - 0.5f) * clusterRadius * 1.9f,
                    (Next01(ref state) - 0.5f) * clusterRadius * 1.9f);
                var nodeCenter = center + jitter;
                var rx = clusterRadius * Mathf.Lerp(0.55f, 1.05f, Next01(ref state));
                var ry = clusterRadius * Mathf.Lerp(0.45f, 0.95f, Next01(ref state));
                var blobRect = new Rect2(nodeCenter - new Vector2(rx, ry), new Vector2(rx * 2f, ry * 2f));
                var highlightRect = new Rect2(
                    blobRect.Position + new Vector2(blobRect.Size.X * 0.18f, blobRect.Size.Y * 0.18f),
                    new Vector2(blobRect.Size.X * 0.46f, blobRect.Size.Y * 0.40f));

                BlendFilledRect(image, ToPixelRect(blobRect.Grow(0.8f)), shadow);
                BlendFilledRect(image, ToPixelRect(blobRect), fill);
                BlendFilledRect(image, ToPixelRect(highlightRect), highlight);
            }
        }
    }

    private static (Color Fill, Color Highlight, Color Shadow) ResolveOrePalette(string oreItemDefId)
    {
        var materialId = ClientContentQueries.ResolveMaterialIdForFormItemDefId(oreItemDefId, ContentFormRoles.Ore);
        var fill = materialId switch
        {
            "iron" => new Color(0.70f, 0.72f, 0.78f, 0.92f),
            "copper" => new Color(0.79f, 0.46f, 0.25f, 0.92f),
            "coal" => new Color(0.36f, 0.36f, 0.40f, 0.92f),
            "tin" => new Color(0.75f, 0.78f, 0.82f, 0.92f),
            "silver" => new Color(0.86f, 0.88f, 0.92f, 0.92f),
            "gold" => new Color(0.95f, 0.82f, 0.32f, 0.92f),
            _ => ResolveHashedOreColor(materialId ?? oreItemDefId),
        };

        return (fill, fill.Lightened(0.22f), fill.Darkened(0.66f));
    }

    private static Color ResolveHashedOreColor(string key)
    {
        unchecked
        {
            uint hash = 2166136261u;
            for (var i = 0; i < key.Length; i++)
                hash = (hash ^ key[i]) * 16777619u;

            var red = 0.58f + (((hash >> 0) & 0x3F) / 255f);
            var green = 0.56f + (((hash >> 6) & 0x3F) / 255f);
            var blue = 0.52f + (((hash >> 12) & 0x3F) / 255f);
            return new Color(Mathf.Clamp(red, 0f, 1f), Mathf.Clamp(green, 0f, 1f), Mathf.Clamp(blue, 0f, 1f), 0.90f);
        }
    }

    public static byte ResolveVariant(string oreItemDefId, int x, int y, int z)
    {
        unchecked
        {
            var state = BuildOreSeed(oreItemDefId, x, y, z);
            return (byte)(state % VariantCount);
        }
    }

    private static uint BuildOreSeed(string oreItemDefId, int x, int y, int z)
    {
        unchecked
        {
            uint seed = 2166136261u;
            seed = (seed ^ (uint)x) * 16777619u;
            seed = (seed ^ (uint)y) * 16777619u;
            seed = (seed ^ (uint)z) * 16777619u;

            for (var i = 0; i < oreItemDefId.Length; i++)
                seed = (seed ^ oreItemDefId[i]) * 16777619u;

            return seed;
        }
    }

    private static uint BuildOreSeed(string oreItemDefId, byte variant)
    {
        unchecked
        {
            uint seed = 2166136261u;
            seed = (seed ^ variant) * 16777619u;

            for (var i = 0; i < oreItemDefId.Length; i++)
                seed = (seed ^ oreItemDefId[i]) * 16777619u;

            return seed;
        }
    }

    private static float Next01(ref uint state)
    {
        unchecked
        {
            state += 0x9E3779B9u;
            var value = state;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777215f;
        }
    }

    private static Rect2I ToPixelRect(Rect2 rect)
    {
        var left = (int)MathF.Floor(rect.Position.X);
        var top = (int)MathF.Floor(rect.Position.Y);
        var right = (int)MathF.Ceiling(rect.End.X);
        var bottom = (int)MathF.Ceiling(rect.End.Y);
        return new Rect2I(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static void BlendFilledRect(Image image, Rect2I rect, Color color)
    {
        if (rect.Size.X <= 0 || rect.Size.Y <= 0)
            return;

        var maxX = Math.Min(image.GetWidth(), rect.End.X);
        var maxY = Math.Min(image.GetHeight(), rect.End.Y);
        for (var py = Math.Max(0, rect.Position.Y); py < maxY; py++)
        for (var px = Math.Max(0, rect.Position.X); px < maxX; px++)
            image.SetPixel(px, py, image.GetPixel(px, py).Blend(color));
    }
}

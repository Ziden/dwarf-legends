using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public readonly record struct StockpileVisualStyle(
    string Label,
    Color FillColor,
    Color GridColor,
    Color BorderColor,
    Color InnerBorderColor,
    Color ChipFillColor,
    Color ChipBorderColor,
    Color LabelColor,
    Color OverlayColor);

public static class StockpileVisualResolver
{
    public static StockpileVisualStyle Resolve(IReadOnlyList<string> acceptedTags)
    {
        var normalizedTags = acceptedTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();

        var label = ResolveLabel(normalizedTags);
        var accent = ResolveAccentColor(normalizedTags);
        return new StockpileVisualStyle(
            label,
            SetAlpha(accent.Darkened(0.66f), 0.12f),
            SetAlpha(accent.Lightened(0.08f), 0.16f),
            SetAlpha(accent.Lightened(0.18f), 0.86f),
            SetAlpha(accent.Lightened(0.34f), 0.48f),
            SetAlpha(accent.Darkened(0.76f), 0.76f),
            SetAlpha(accent.Lightened(0.12f), 0.74f),
            SetAlpha(accent.Lightened(0.46f), 1f),
            SetAlpha(accent, 0.18f));
    }

    private static string ResolveLabel(IReadOnlyList<string> tags)
    {
        if (tags.Count == 0)
            return "ALL";

        var first = HumanizeTag(tags[0]);
        if (tags.Count == 1)
            return first;

        return $"{first}+{tags.Count - 1}";
    }

    private static string HumanizeTag(string tag)
    {
        var normalized = tag.Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? "SP"
            : normalized.ToUpperInvariant();
    }

    private static Color ResolveAccentColor(IEnumerable<string> normalizedTags)
    {
        unchecked
        {
            var hash = 17;
            foreach (var tag in normalizedTags)
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(tag);

            if (hash == 17)
                hash = 0x2E8F9A;

            var hue = Math.Abs(hash % 360) / 360f;
            return Color.FromHsv(hue, 0.58f, 0.94f);
        }
    }

    private static Color SetAlpha(Color color, float alpha)
        => new(color.R, color.G, color.B, alpha);
}

using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Systems;

namespace DwarfFortress.GodotClient.UI;


public static class ItemTextFormatter
{
    public static string BuildInspectorSummary(ItemView item)
    {
        var parts = new List<string>();
        if (item.Corpse is not null)
        {
            parts.Add($"Formerly {item.Corpse.DisplayName}");
            parts.Add(FormatToken(item.Corpse.RotStage));
        }
        else if (!string.IsNullOrWhiteSpace(item.MaterialId))
        {
            parts.Add(FormatToken(item.MaterialId!));
        }

        if (item.StackSize > 1)
            parts.Add($"x{item.StackSize}");

        return string.Join(" ", parts);
    }

    public static string BuildHoverSummary(ItemView item)
    {
        if (item.Corpse is not null)
            return $"{item.DisplayName} ({Humanize(item.Corpse.RotStage)})";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.MaterialId))
            parts.Add(Humanize(item.MaterialId!));

        parts.Add(item.DisplayName);
        return string.Join(" ", parts);
    }

    public static string BuildDetailedInspection(ItemView item)
    {
        if (item.Corpse is not null)
            return $"{item.DisplayName} [{Humanize(item.Corpse.RotStage)}, rot {item.Corpse.RotProgress:P0}, died of {Humanize(item.Corpse.DeathCause)}]";

        var materialText = item.MaterialId is not null ? $" ({Humanize(item.MaterialId)})" : string.Empty;
        var stackText = item.StackSize > 1 ? $" x{item.StackSize}" : string.Empty;
        var weightText = item.Weight > 0f ? $" [{item.Weight:F1} kg]" : string.Empty;
        return $"{item.DisplayName}{materialText}{stackText}{weightText}";
    }

    public static string BuildContainedCardTitle(ItemView item)
    {
        if (item.Corpse is not null)
            return item.DisplayName;

        var stackText = item.StackSize > 1 ? $" x{item.StackSize}" : string.Empty;
        var weightText = item.Weight > 0f ? $" [{item.Weight:F1} kg]" : string.Empty;
        return $"{GetDisplayName(item, includeMaterialPrefix: true)}{stackText}{weightText}";
    }

    public static string BuildContainedCardDetails(ItemView item)
    {
        if (item.Corpse is not null)
            return $"Died of {FormatToken(item.Corpse.DeathCause)} â€¢ {FormatToken(item.Corpse.RotStage)} â€¢ Item #{item.Id}";

        return $"Item #{item.Id} â€¢ Position: ({item.Position.X}, {item.Position.Y}, {item.Position.Z})";
    }

    public static string BuildStorageRowLabel(ItemView item)
    {
        var materialText = !string.IsNullOrWhiteSpace(item.MaterialId)
            ? $" ({FormatToken(item.MaterialId!)})"
            : string.Empty;
        var weightText = item.Weight > 0f ? $" [{item.Weight:F1} kg]" : string.Empty;
        return $"{item.DisplayName}{materialText}{weightText}";
    }

    public static string GetDisplayName(ItemView item, bool includeMaterialPrefix)
    {
        if (!includeMaterialPrefix || string.IsNullOrWhiteSpace(item.MaterialId) || item.Corpse is not null)
            return item.DisplayName;

        return $"{FormatToken(item.MaterialId!)} {item.DisplayName}";
    }

    public static string FormatToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return "Unknown";

        var words = Humanize(token)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant());
        return string.Join(" ", words);
    }

    public static string Humanize(string token)
        => string.IsNullOrWhiteSpace(token) ? "Unknown" : token.Replace('_', ' ');
}
using System;
using System.Linq;
using DwarfFortress.WorldGen.Content;
using Godot;

namespace DwarfFortress.GodotClient.Bootstrap;

public static class ClientContentQueries
{
    private static readonly Lazy<SharedContentCatalog?> Catalog = new(LoadCatalog);
    private static readonly Lazy<ContentQueryService?> Queries = new(() =>
    {
        var catalog = Catalog.Value;
        return catalog is null ? null : new ContentQueryService(catalog);
    });

    public static SharedContentCatalog? SharedCatalog => Catalog.Value;

    public static string? ResolveMaterialIdForFormItemDefId(string? itemDefId, string? role = null)
        => Queries.Value?.ResolveMaterialIdForFormItemDefId(itemDefId, role);

    public static bool HasItemTag(string? itemDefId, string tag)
    {
        if (string.IsNullOrWhiteSpace(itemDefId) || string.IsNullOrWhiteSpace(tag))
            return false;

        var catalog = Catalog.Value;
        return catalog?.Items.TryGetValue(itemDefId.Trim(), out var item) == true &&
               item.Tags.Any(existingTag => string.Equals(existingTag, tag, StringComparison.OrdinalIgnoreCase));
    }

    public static string? ResolveCreatureProceduralProfileId(string? creatureDefId)
        => Queries.Value?.ResolveCreatureVisuals(creatureDefId)?.ProceduralProfileId;

    public static string? ResolveCreatureMovementModeId(string? creatureDefId)
    {
        if (string.IsNullOrWhiteSpace(creatureDefId))
            return null;

        return Catalog.Value?.Creatures.TryGetValue(creatureDefId.Trim(), out var creature) == true
            ? creature.MovementModeId
            : null;
    }

    public static string? ResolveCreatureWaterEffectStyleId(string? creatureDefId)
        => Queries.Value?.ResolveCreatureVisuals(creatureDefId)?.WaterEffectStyleId;

    public static Color? ResolveCreatureViewerColor(string? creatureDefId)
    {
        var raw = Queries.Value?.ResolveCreatureVisuals(creatureDefId)?.ViewerColor;
        return TryParseHtmlColor(raw, out var color) ? color : null;
    }

    private static bool TryParseHtmlColor(string? value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];

        if (normalized.Length != 6 ||
            !byte.TryParse(normalized[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(normalized.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(normalized.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = new Color(r / 255f, g / 255f, b / 255f);
        return true;
    }

    private static SharedContentCatalog? LoadCatalog()
    {
        try
        {
            return SharedContentCatalogLoader.Load(new DirectoryContentFileSource(ClientSimulationFactory.ResolveDataPath()));
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Failed to load shared client content catalog: {exception.Message}");
            return null;
        }
    }
}

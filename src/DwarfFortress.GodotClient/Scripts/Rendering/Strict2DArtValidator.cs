using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Data;

namespace DwarfFortress.GodotClient.Rendering;

public static class Strict2DArtValidator
{
    public static void ValidateRequiredContent(DataManager data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var missing = new List<string>();
        CollectMissing(missing, "tile", data.Tiles.AllIds(), id => PixelArtFactory.CanResolveTile(id));
        CollectMissing(missing, "item", data.Items.AllIds(), id => PixelArtFactory.CanResolveItem(id));
        CollectMissing(missing, "plant overlay", data.Plants.AllIds(), PixelArtFactory.CanResolvePlantOverlay);
        CollectMissing(missing, "creature", data.Creatures.AllIds(), PixelArtFactory.CanResolveEntity);
        CollectMissing(missing, "building", data.Buildings.AllIds(), PixelArtFactory.CanResolveBuilding);

        if (missing.Count == 0)
            return;

        throw new InvalidOperationException(
            "Strict 2D art validation failed. Add sprite mappings or explicit PixelArtFactory coverage for:\n" +
            string.Join("\n", missing));
    }

    private static void CollectMissing(List<string> missing, string category, IReadOnlyCollection<string> ids, Func<string, bool> canResolve)
    {
        foreach (var id in ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            if (!canResolve(id))
                missing.Add($"- {category}: {id}");
        }
    }
}
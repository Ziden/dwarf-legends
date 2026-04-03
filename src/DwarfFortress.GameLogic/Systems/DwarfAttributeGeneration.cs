using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Systems;

internal static class DwarfAttributeGeneration
{
    public static void Randomize(Dwarf dwarf, DataManager? data)
    {
        ArgumentNullException.ThrowIfNull(dwarf);

        var seed = DwarfAppearanceComponent.CreateSeed(dwarf.Id, dwarf.FirstName, dwarf.Position.Position);
        var rng = new Random(seed);
        dwarf.Attributes.Randomize(ResolveAttributeIds(data), rng);
    }

    public static void ApplyOverrides(Dwarf dwarf, IReadOnlyDictionary<string, int>? attributeLevels)
    {
        ArgumentNullException.ThrowIfNull(dwarf);

        if (attributeLevels is null || attributeLevels.Count == 0)
            return;

        foreach (var (attributeId, level) in attributeLevels)
        {
            if (string.IsNullOrWhiteSpace(attributeId))
                continue;

            dwarf.Attributes.SetLevel(attributeId, level);
        }
    }

    private static IEnumerable<string> ResolveAttributeIds(DataManager? data)
    {
        var ids = data?.Attributes.All()
            .Select(attribute => attribute.Id)
            .Where(attributeId => !string.IsNullOrWhiteSpace(attributeId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ids is { Length: > 0 } ? ids : AttributeIds.All;
    }
}
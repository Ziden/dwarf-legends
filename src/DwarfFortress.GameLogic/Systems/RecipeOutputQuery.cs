using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;

namespace DwarfFortress.GameLogic.Systems;

/// <summary>
/// Public read-only query helpers for presenting recipe outputs in UI and tooling.
/// This stays separate from the runtime crafting path so callers do not need item instances.
/// </summary>
public static class RecipeOutputQuery
{
    public static IReadOnlyList<string> ResolveItemDefIds(DataManager data, RecipeDef recipe)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(recipe);

        return RecipeResolver.ResolveCraftableOutputItemIds(data, recipe)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> ResolveItemDefIds(DataManager data, RecipeOutput output)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(output);

        return RecipeResolver.ResolvePotentialOutputItemDefIds(data, output);
    }
}

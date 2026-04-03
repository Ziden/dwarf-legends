using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;

namespace DwarfFortress.GameLogic.Systems;

internal sealed record ResolvedRecipeOutput(string ItemDefId, string MaterialId, int Quantity);

internal static class RecipeResolver
{
    public static bool TryMatchInputs(DataManager data, IReadOnlyList<RecipeInput> inputs, IReadOnlyList<Item> availableItems, out List<Item> matchedItems)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(availableItems);

        var requiredInputs = inputs
            .SelectMany(input => Enumerable.Repeat(input, input.Quantity))
            .OrderByDescending(GetSpecificity)
            .ToList();

        matchedItems = new List<Item>();
        return TryMatchInputs(data, requiredInputs, 0, availableItems, new HashSet<int>(), matchedItems);
    }

    public static bool TryMatchRecipe(
        DataManager data,
        RecipeDef recipe,
        IReadOnlyList<Item> availableItems,
        out List<Item> matchedItems,
        out List<ResolvedRecipeOutput> outputs)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(availableItems);

        var requiredInputs = recipe.Inputs
            .SelectMany(input => Enumerable.Repeat(input, input.Quantity))
            .OrderByDescending(GetSpecificity)
            .ToList();

        matchedItems = new List<Item>();
        return TryMatchRecipe(data, recipe, requiredInputs, 0, availableItems, new HashSet<int>(), matchedItems, out outputs);
    }

    public static bool TryResolveOutputs(DataManager data, RecipeDef recipe, IReadOnlyList<Item> matchedInputs, out List<ResolvedRecipeOutput> outputs)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(matchedInputs);

        outputs = new List<ResolvedRecipeOutput>(recipe.Outputs.Count);
        foreach (var output in recipe.Outputs)
        {
            if (output.Quantity <= 0)
                continue;

            var materialId = ResolveOutputMaterialId(data, output, matchedInputs);
            var itemDefId = ResolveOutputItemDefId(data, output, materialId);
            if (string.IsNullOrWhiteSpace(itemDefId))
                return false;

            outputs.Add(new ResolvedRecipeOutput(itemDefId, materialId, output.Quantity));
        }

        return true;
    }

    public static IEnumerable<string> ResolveCraftableOutputItemIds(DataManager data, RecipeDef recipe)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(recipe);

        var yieldedItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var output in recipe.Outputs)
        {
            foreach (var outputItemDefId in ResolvePotentialOutputItemDefIds(data, output))
                if (yieldedItemIds.Add(outputItemDefId))
                    yield return outputItemDefId;
        }
    }

    internal static IReadOnlyList<string> ResolvePotentialOutputItemDefIds(DataManager data, RecipeOutput output)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(output);

        var itemDefIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(output.ItemDefId))
            itemDefIds.Add(output.ItemDefId!);

        if (!string.IsNullOrWhiteSpace(output.FormRole))
        {
            foreach (var materialId in ResolveRecipeMaterialHints(data, output.MaterialInheritFrom))
            {
                var itemDefId = data.ContentQueries?.ResolveMaterialFormItemDefId(materialId, output.FormRole);
                if (!string.IsNullOrWhiteSpace(itemDefId))
                    itemDefIds.Add(itemDefId!);
            }
        }

        return itemDefIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool TryMatchInputs(
        DataManager data,
        IReadOnlyList<RecipeInput> requiredInputs,
        int index,
        IReadOnlyList<Item> availableItems,
        HashSet<int> matchedIds,
        List<Item> matchedItems)
    {
        if (index >= requiredInputs.Count)
            return true;

        var requirement = requiredInputs[index];
        var candidates = availableItems
            .Where(item => !matchedIds.Contains(item.Id) && MatchesInput(data, item, requirement))
            .OrderBy(item => CountFutureMatches(data, item, requiredInputs, index + 1))
            .ToList();

        foreach (var candidate in candidates)
        {
            matchedIds.Add(candidate.Id);
            matchedItems.Add(candidate);

            if (TryMatchInputs(data, requiredInputs, index + 1, availableItems, matchedIds, matchedItems))
                return true;

            matchedItems.RemoveAt(matchedItems.Count - 1);
            matchedIds.Remove(candidate.Id);
        }

        return false;
    }

    private static bool TryMatchRecipe(
        DataManager data,
        RecipeDef recipe,
        IReadOnlyList<RecipeInput> requiredInputs,
        int index,
        IReadOnlyList<Item> availableItems,
        HashSet<int> matchedIds,
        List<Item> matchedItems,
        out List<ResolvedRecipeOutput> outputs)
    {
        if (index >= requiredInputs.Count)
        {
            try
            {
                return TryResolveOutputs(data, recipe, matchedItems, out outputs);
            }
            catch (InvalidOperationException)
            {
                outputs = new List<ResolvedRecipeOutput>();
                return false;
            }
        }

        var requirement = requiredInputs[index];
        var candidates = availableItems
            .Where(item => !matchedIds.Contains(item.Id) && MatchesInput(data, item, requirement))
            .OrderBy(item => CountFutureMatches(data, item, requiredInputs, index + 1))
            .ToList();

        foreach (var candidate in candidates)
        {
            matchedIds.Add(candidate.Id);
            matchedItems.Add(candidate);

            if (TryMatchRecipe(data, recipe, requiredInputs, index + 1, availableItems, matchedIds, matchedItems, out outputs))
                return true;

            matchedItems.RemoveAt(matchedItems.Count - 1);
            matchedIds.Remove(candidate.Id);
        }

        outputs = new List<ResolvedRecipeOutput>();
        return false;
    }

    private static bool MatchesInput(DataManager data, Item item, RecipeInput requirement)
    {
        var def = data.Items.GetOrNull(item.DefId);
        if (def is null || !def.Tags.HasAll(requirement.RequiredTags.All.ToArray()))
            return false;

        if (!string.IsNullOrWhiteSpace(requirement.ItemDefId) &&
            !string.Equals(item.DefId, requirement.ItemDefId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requirement.MaterialId) &&
            !string.Equals(item.MaterialId, requirement.MaterialId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static int CountFutureMatches(DataManager data, Item item, IReadOnlyList<RecipeInput> requiredInputs, int startIndex)
    {
        var count = 0;
        for (var index = startIndex; index < requiredInputs.Count; index++)
            if (MatchesInput(data, item, requiredInputs[index]))
                count++;

        return count;
    }

    private static int GetSpecificity(RecipeInput input)
    {
        var specificity = input.RequiredTags.Count;
        if (!string.IsNullOrWhiteSpace(input.ItemDefId))
            specificity += 100;
        if (!string.IsNullOrWhiteSpace(input.MaterialId))
            specificity += 50;

        return specificity;
    }

    private static string ResolveOutputMaterialId(DataManager data, RecipeOutput output, IReadOnlyList<Item> matchedInputs)
    {
        if (string.IsNullOrWhiteSpace(output.MaterialInheritFrom))
            return "unknown";

        var matches = ResolveMaterialSourceMatches(data, matchedInputs, output.MaterialInheritFrom).ToList();
        if (matches.Count == 0)
            throw new InvalidOperationException($"Recipe output selector '{output.MaterialInheritFrom}' did not match any consumed inputs.");

        var materialId = matches[0].MaterialId ?? "unknown";
        for (var i = 1; i < matches.Count; i++)
        {
            var candidateMaterialId = matches[i].MaterialId ?? "unknown";
            if (!string.Equals(materialId, candidateMaterialId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Recipe output selector '{output.MaterialInheritFrom}' matched inputs with mixed materials ('{materialId}' and '{candidateMaterialId}').");
            }
        }

        return materialId;
    }

    private static string ResolveOutputItemDefId(DataManager data, RecipeOutput output, string materialId)
    {
        if (!string.IsNullOrWhiteSpace(output.FormRole))
        {
            var resolvedItemDefId = data.ContentQueries?.ResolveMaterialFormItemDefId(materialId, output.FormRole);
            if (string.IsNullOrWhiteSpace(resolvedItemDefId))
                throw new InvalidOperationException($"Material '{materialId}' does not define output form '{output.FormRole}'.");

            if (!string.IsNullOrWhiteSpace(output.ItemDefId) &&
                !string.Equals(output.ItemDefId, resolvedItemDefId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Recipe output item '{output.ItemDefId}' does not match derived form '{output.FormRole}' item '{resolvedItemDefId}'.");
            }

            return resolvedItemDefId!;
        }

        if (!string.IsNullOrWhiteSpace(output.ItemDefId))
            return output.ItemDefId!;

        throw new InvalidOperationException("Recipe output must define either 'itemId' or 'formRole'.");
    }

    private static IEnumerable<Item> ResolveMaterialSourceMatches(DataManager data, IReadOnlyList<Item> matchedInputs, string selector)
    {
        var normalizedSelector = selector.Trim();
        if (normalizedSelector.Length == 0)
            return Array.Empty<Item>();

        var separatorIndex = normalizedSelector.IndexOf(':');
        if (separatorIndex > 0)
        {
            var selectorType = normalizedSelector[..separatorIndex].Trim().ToLowerInvariant();
            var selectorValue = normalizedSelector[(separatorIndex + 1)..].Trim();
            return ResolveMaterialSourceMatchesByType(data, matchedInputs, selectorType, selectorValue);
        }

        return ResolveMaterialSourceMatchesByType(data, matchedInputs, "tag", normalizedSelector);
    }

    private static IEnumerable<Item> ResolveMaterialSourceMatchesByType(DataManager data, IReadOnlyList<Item> matchedInputs, string selectorType, string selectorValue)
    {
        if (string.IsNullOrWhiteSpace(selectorValue))
            return Array.Empty<Item>();

        return selectorType switch
        {
            "item" or "itemid" => matchedInputs.Where(item => string.Equals(item.DefId, selectorValue, StringComparison.OrdinalIgnoreCase)),
            "material" or "materialid" => matchedInputs.Where(item => string.Equals(item.MaterialId, selectorValue, StringComparison.OrdinalIgnoreCase)),
            _ => matchedInputs.Where(item =>
            {
                var def = data.Items.GetOrNull(item.DefId);
                return def?.Tags.Contains(selectorValue) == true;
            }),
        };
    }

    private static string? ResolveRecipeMaterialHint(DataManager data, string? selector)
    {
        return ResolveRecipeMaterialHints(data, selector).FirstOrDefault();
    }

    private static IReadOnlyList<string> ResolveRecipeMaterialHints(DataManager data, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return Array.Empty<string>();

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedSelector = selector.Trim();
        var separatorIndex = normalizedSelector.IndexOf(':');
        if (separatorIndex < 0)
        {
            foreach (var materialId in ResolveSelectorTagMaterials(data, normalizedSelector))
                results.Add(materialId);

            return results.Count > 0
                ? results.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();
        }

        var selectorType = normalizedSelector[..separatorIndex].Trim().ToLowerInvariant();
        var selectorValue = normalizedSelector[(separatorIndex + 1)..].Trim();
        if (selectorValue.Length == 0)
            return Array.Empty<string>();

        switch (selectorType)
        {
            case "item":
            case "itemid":
                foreach (var materialId in data.ContentQueries?.ResolveMaterialIdsForFormItemDefId(selectorValue) ?? Array.Empty<string>())
                    results.Add(materialId);
                break;

            case "material":
            case "materialid":
                results.Add(selectorValue);
                break;

            default:
                foreach (var materialId in ResolveSelectorTagMaterials(data, selectorValue))
                    results.Add(materialId);
                break;
        }

        return results.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> ResolveSelectorTagMaterials(DataManager data, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return Array.Empty<string>();

        return data.Items.All()
            .Where(item => item.Tags.Contains(tag))
            .SelectMany(item => data.ContentQueries?.ResolveMaterialIdsForFormItemDefId(item.Id) ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

using DwarfFortress.ContentEditor.Models;
using DwarfFortress.WorldGen.Content;
using DwarfFortress.WorldGen.Config;

namespace DwarfFortress.ContentEditor.Services;

public record ValidationError(string Category, string ItemId, string Message);

/// <summary>
/// Cross-reference validation: checks IDs are unique, recipe workshops exist, etc.
/// </summary>
public class ValidationService
{
    private readonly DataService _data;

    public ValidationService(DataService data)
    {
        _data = data;
    }

    public List<ValidationError> Validate()
    {
        var errors = new List<ValidationError>();

        var items = _data.LoadItems();
        var tiles = _data.LoadTiles();
        var materials = _data.LoadMaterials();
        var recipes = _data.LoadRecipes();
        var buildings = _data.LoadBuildings();

        CheckDuplicateIds(errors, "items", items.Select(x => x.Id));
        CheckDuplicateIds(errors, "tiles", tiles.Select(x => x.Id));
        CheckDuplicateIds(errors, "materials", materials.Select(x => x.Id));
        CheckDuplicateIds(errors, "recipes", recipes.Select(x => x.Id));
        CheckDuplicateIds(errors, "buildings", buildings.Select(x => x.Id));

        SharedContentCatalog? sharedContent = null;
        try
        {
            sharedContent = SharedContentCatalogLoader.Load(new DirectoryContentFileSource(_data.DataRootPath));
        }
        catch (Exception ex)
        {
            errors.Add(new("content", "shared_content", ex.Message));
        }

        var itemIds = new HashSet<string>(items.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
        var materialIds = new HashSet<string>(materials.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
        if (sharedContent is not null)
        {
            foreach (var itemId in sharedContent.Items.Keys)
                itemIds.Add(itemId);

            foreach (var materialId in sharedContent.Materials.Keys)
                materialIds.Add(materialId);
        }

        var buildingIds = new HashSet<string>(buildings.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var recipe in recipes)
            ValidateRecipe(errors, recipe, itemIds, materialIds, buildingIds);

        ValidateWorldgenContent(errors, sharedContent);

        return errors;
    }

    private static void ValidateRecipe(
        List<ValidationError> errors,
        RecipeModel recipe,
        IReadOnlySet<string> itemIds,
        IReadOnlySet<string> materialIds,
        IReadOnlySet<string> buildingIds)
    {
        if (!string.IsNullOrEmpty(recipe.Workshop) && !buildingIds.Contains(recipe.Workshop))
            errors.Add(new("recipes", recipe.Id, $"Workshop '{recipe.Workshop}' not found in buildings"));

        foreach (var input in recipe.Inputs)
        {
            if (!string.IsNullOrWhiteSpace(input.ItemId) && !itemIds.Contains(input.ItemId))
                errors.Add(new("recipes", recipe.Id, $"Input item '{input.ItemId}' not found in items"));

            if (!string.IsNullOrWhiteSpace(input.MaterialId) && !materialIds.Contains(input.MaterialId))
                errors.Add(new("recipes", recipe.Id, $"Input material '{input.MaterialId}' not found in materials"));
        }

        foreach (var output in recipe.Outputs)
        {
            var hasItemId = !string.IsNullOrWhiteSpace(output.ItemId);
            var hasFormRole = !string.IsNullOrWhiteSpace(output.FormRole);
            if (!hasItemId && !hasFormRole)
            {
                errors.Add(new("recipes", recipe.Id, "Each output must define either 'itemId' or 'formRole'"));
                continue;
            }

            if (hasItemId && !itemIds.Contains(output.ItemId))
                errors.Add(new("recipes", recipe.Id, $"Output item '{output.ItemId}' not found in items"));

            if (hasFormRole && string.IsNullOrWhiteSpace(output.MaterialFrom))
                errors.Add(new("recipes", recipe.Id, $"Derived output form '{output.FormRole}' requires 'materialFrom'"));

            if (!TryValidateSelectorTarget(output.MaterialFrom, itemIds, materialIds, out var selectorError))
                errors.Add(new("recipes", recipe.Id, selectorError));
        }
    }

    private static bool TryValidateSelectorTarget(
        string? selector,
        IReadOnlySet<string> itemIds,
        IReadOnlySet<string> materialIds,
        out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(selector))
            return true;

        var normalizedSelector = selector.Trim();
        var separatorIndex = normalizedSelector.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= normalizedSelector.Length - 1)
            return true;

        var selectorType = normalizedSelector[..separatorIndex].Trim().ToLowerInvariant();
        var selectorValue = normalizedSelector[(separatorIndex + 1)..].Trim();
        if (selectorValue.Length == 0)
            return true;

        if ((selectorType == "item" || selectorType == "itemid") && !itemIds.Contains(selectorValue))
        {
            error = $"materialFrom selector item '{selectorValue}' not found in items";
            return false;
        }

        if ((selectorType == "material" || selectorType == "materialid") && !materialIds.Contains(selectorValue))
        {
            error = $"materialFrom selector material '{selectorValue}' not found in materials";
            return false;
        }

        return true;
    }

    private void ValidateWorldgenContent(List<ValidationError> errors, SharedContentCatalog? sharedContent)
    {
        var worldgenPath = _data.ResolveFullPath("ConfigBundle/worldgen/worldgen_content.json");
        if (!File.Exists(worldgenPath))
            return;

        try
        {
            var config = WorldGenContentConfigLoader.LoadFromFile(worldgenPath);
            _ = sharedContent is not null
                ? WorldGenContentCatalog.FromConfig(config, sharedContent)
                : WorldGenContentCatalog.FromConfig(config);
        }
        catch (Exception ex)
        {
            errors.Add(new("worldgen", "worldgen_content", ex.Message));
        }
    }

    private static void CheckDuplicateIds(List<ValidationError> errors, string category, IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (!seen.Add(id))
                errors.Add(new(category, id, "Duplicate ID"));
        }
    }
}

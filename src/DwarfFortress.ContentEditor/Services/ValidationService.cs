using DwarfFortress.ContentEditor.Models;

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

        var items     = _data.LoadItems();
        var tiles     = _data.LoadTiles();
        var materials = _data.LoadMaterials();
        var recipes   = _data.LoadRecipes();
        var buildings = _data.LoadBuildings();

        CheckDuplicateIds(errors, "items",     items.Select(x => x.Id));
        CheckDuplicateIds(errors, "tiles",     tiles.Select(x => x.Id));
        CheckDuplicateIds(errors, "materials", materials.Select(x => x.Id));
        CheckDuplicateIds(errors, "recipes",   recipes.Select(x => x.Id));
        CheckDuplicateIds(errors, "buildings", buildings.Select(x => x.Id));

        var itemIds     = items.Select(x => x.Id).ToHashSet();
        var buildingIds = buildings.Select(x => x.Id).ToHashSet();

        foreach (var r in recipes)
        {
            if (!string.IsNullOrEmpty(r.Workshop) && !buildingIds.Contains(r.Workshop))
                errors.Add(new("recipes", r.Id, $"Workshop '{r.Workshop}' not found in buildings"));

            foreach (var o in r.Outputs)
                if (!itemIds.Contains(o.ItemId))
                    errors.Add(new("recipes", r.Id, $"Output item '{o.ItemId}' not found in items"));
        }

        return errors;
    }

    private static void CheckDuplicateIds(List<ValidationError> errors, string category, IEnumerable<string> ids)
    {
        var seen = new HashSet<string>();
        foreach (var id in ids)
        {
            if (!seen.Add(id))
                errors.Add(new(category, id, "Duplicate ID"));
        }
    }
}

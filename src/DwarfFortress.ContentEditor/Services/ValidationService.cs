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
        var tileIds     = tiles.Select(x => x.Id).ToHashSet();
        var materialIds = materials.Select(x => x.Id).ToHashSet();
        var buildingIds = buildings.Select(x => x.Id).ToHashSet();

        foreach (var b in buildings)
        {
            if (string.IsNullOrWhiteSpace(b.Id))
                errors.Add(new("buildings", b.Id, "Building ID is required"));

            if (b.ConstructionTime < 0)
                errors.Add(new("buildings", b.Id, "Construction time cannot be negative"));

            if (b.ResidenceCapacity < 0)
                errors.Add(new("buildings", b.Id, "Residence capacity cannot be negative"));

            var footprintCells = new HashSet<(int X, int Y)>();
            foreach (var tile in b.Footprint)
            {
                if (!footprintCells.Add((tile.X, tile.Y)))
                    errors.Add(new("buildings", b.Id, $"Duplicate footprint tile at ({tile.X},{tile.Y})"));

                if (string.IsNullOrWhiteSpace(tile.Tile) || !tileIds.Contains(tile.Tile))
                    errors.Add(new("buildings", b.Id, $"Footprint tile '{tile.Tile}' not found in tiles"));
            }

            ValidateBuildingInputs(errors, b.Id, "construction", b.ConstructionInputs, itemIds, materialIds);
            ValidateBuildingInputs(errors, b.Id, "discovery", b.DiscoveryInputs, itemIds, materialIds);

            foreach (var entry in b.Entries)
            {
                if (!footprintCells.Contains((entry.X, entry.Y)))
                    errors.Add(new("buildings", b.Id, $"Entry ({entry.X},{entry.Y}) is outside the footprint"));

                if (!IsCardinalDirection(entry.OutwardDirection))
                    errors.Add(new("buildings", b.Id, $"Entry direction '{entry.OutwardDirection}' must be north, south, east, or west"));
            }

            if (b.VisualProfile is not null && string.IsNullOrWhiteSpace(b.VisualProfile.Archetype))
                errors.Add(new("buildings", b.Id, "Visual profile archetype is required when a visual profile is present"));
        }

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

    private static void ValidateBuildingInputs(
        List<ValidationError> errors,
        string buildingId,
        string inputKind,
        IEnumerable<BuildingInput> inputs,
        IReadOnlySet<string> itemIds,
        IReadOnlySet<string> materialIds)
    {
        foreach (var input in inputs)
        {
            if (input.Qty <= 0)
                errors.Add(new("buildings", buildingId, $"{inputKind} input quantity must be positive"));

            if (input.Tags.Count == 0 &&
                string.IsNullOrWhiteSpace(input.ItemDefId) &&
                string.IsNullOrWhiteSpace(input.MaterialId))
            {
                errors.Add(new("buildings", buildingId, $"{inputKind} input needs a tag, item ID, or material ID"));
            }

            if (!string.IsNullOrWhiteSpace(input.ItemDefId) && !itemIds.Contains(input.ItemDefId))
                errors.Add(new("buildings", buildingId, $"{inputKind} input item '{input.ItemDefId}' not found in items"));

            if (!string.IsNullOrWhiteSpace(input.MaterialId) && !materialIds.Contains(input.MaterialId))
                errors.Add(new("buildings", buildingId, $"{inputKind} input material '{input.MaterialId}' not found in materials"));
        }
    }

    private static bool IsCardinalDirection(string? direction)
        => string.Equals(direction, "north", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(direction, "south", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(direction, "east", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(direction, "west", StringComparison.OrdinalIgnoreCase);
}

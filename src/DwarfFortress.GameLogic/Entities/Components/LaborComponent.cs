using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// Tracks which labors a dwarf has enabled.
/// Labors are referenced by string constant IDs (LaborIds).
/// </summary>
public sealed class LaborComponent
{
    private readonly HashSet<string> _enabled = new(System.StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled(string laborId) => _enabled.Contains(laborId);
    public void Enable(string laborId)    => _enabled.Add(laborId);
    public void Disable(string laborId)   => _enabled.Remove(laborId);

    public void EnableAll(IEnumerable<string> laborIds)
    {
        foreach (var id in laborIds) _enabled.Add(id);
    }

    public void DisableAll() => _enabled.Clear();

    public IReadOnlyCollection<string> EnabledLabors => _enabled;
}

/// <summary>All labor ID string constants. No magic strings in simulation code.</summary>
public static class LaborIds
{
    public const string Mining       = "mining";
    public const string WoodCutting  = "wood_cutting";
    public const string Hauling      = "hauling";
    public const string Construction = "construction";
    public const string Masonry      = "masonry";
    public const string Carpentry    = "carpentry";
    public const string Smithing     = "smithing";
    public const string Farming      = "farming";
    public const string Cooking      = "cooking";
    public const string Brewing      = "brewing";
    public const string Fishing      = "fishing";
    public const string Hunting      = "hunting";
    public const string Healthcare   = "healthcare";
    public const string Military     = "military";
    public const string Crafting     = "crafting";
    public const string Misc         = "misc";

    /// <summary>All labor IDs, used to enable every labor at once on a fresh dwarf.</summary>
    public static readonly string[] All =
    [
        Mining, WoodCutting, Hauling, Construction, Masonry, Carpentry,
        Smithing, Farming, Cooking, Brewing, Fishing, Hunting,
        Healthcare, Military, Crafting, Misc,
    ];
}

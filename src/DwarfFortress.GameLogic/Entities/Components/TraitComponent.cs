using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// Tracks which traits a dwarf has. Traits are permanent personality/biological modifiers.
/// </summary>
public sealed class TraitComponent
{
    private readonly HashSet<string> _traitIds = new();

    /// <summary>All trait IDs this dwarf currently has.</summary>
    public IReadOnlyCollection<string> TraitIds => _traitIds;

    /// <summary>Check if the dwarf has a specific trait.</summary>
    public bool HasTrait(string traitId) => _traitIds.Contains(traitId);

    /// <summary>Add a trait to this dwarf. Idempotent — does nothing if already present.</summary>
    public void AddTrait(string traitId) => _traitIds.Add(traitId);

    /// <summary>Remove a trait from this dwarf.</summary>
    public void RemoveTrait(string traitId) => _traitIds.Remove(traitId);

    /// <summary>Clear all traits (useful for save/load or testing).</summary>
    public void Clear() => _traitIds.Clear();

    /// <summary>Number of traits this dwarf has.</summary>
    public int Count => _traitIds.Count;
}
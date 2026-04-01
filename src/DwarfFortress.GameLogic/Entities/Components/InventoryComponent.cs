using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// Tracks items currently carried by an entity.
/// Equipment slots can layer on top of this later without changing haul semantics.
/// </summary>
public sealed class InventoryComponent
{
    private readonly HashSet<int> _carriedItemIds = new();

    public IReadOnlyCollection<int> CarriedItemIds => _carriedItemIds;

    public bool Contains(int itemId) => _carriedItemIds.Contains(itemId);

    public void AddCarriedItem(int itemId) => _carriedItemIds.Add(itemId);

    public void RemoveCarriedItem(int itemId) => _carriedItemIds.Remove(itemId);

    public void Clear() => _carriedItemIds.Clear();
}
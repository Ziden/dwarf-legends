using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// Tracks small items currently carried by an entity.
/// Separate hauling state handles large items carried in the hands.
/// </summary>
public sealed class InventoryComponent
{
    /// <summary>Maximum number of items an entity can carry at once.</summary>
    public const int MaxCapacity = 4;

    private readonly HashSet<int> _carriedItemIds = new();

    public IReadOnlyCollection<int> CarriedItemIds => _carriedItemIds;
    public int Count => _carriedItemIds.Count;
    public bool IsFull => _carriedItemIds.Count >= MaxCapacity;

    public bool Contains(int itemId) => _carriedItemIds.Contains(itemId);

    public void AddCarriedItem(int itemId) => _carriedItemIds.Add(itemId);

    /// <summary>Attempts to add an item to inventory. Returns false if inventory is full.</summary>
    public bool TryAddCarriedItem(int itemId)
    {
        if (IsFull) return false;
        _carriedItemIds.Add(itemId);
        return true;
    }

    public void RemoveCarriedItem(int itemId) => _carriedItemIds.Remove(itemId);

    public void Clear() => _carriedItemIds.Clear();
}

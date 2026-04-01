using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// Stores items inside a container entity (box, barrel, etc.).
/// Distinct from <see cref="InventoryComponent"/> which tracks items being carried while walking.
/// </summary>
public sealed class ContainerComponent
{
    private readonly List<int> _itemIds = new();

    public int Capacity { get; }

    public IReadOnlyList<int> StoredItemIds => _itemIds;

    public int  Count   => _itemIds.Count;
    public bool IsFull  => _itemIds.Count >= Capacity;
    public bool IsEmpty => _itemIds.Count == 0;

    public ContainerComponent(int capacity) => Capacity = capacity;

    /// <summary>Returns true and adds the item if capacity allows.</summary>
    public bool TryAdd(int itemId)
    {
        if (IsFull) return false;
        _itemIds.Add(itemId);
        return true;
    }

    public bool Remove(int itemId) => _itemIds.Remove(itemId);

    public bool Contains(int itemId) => _itemIds.Contains(itemId);

    public void Clear() => _itemIds.Clear();
}

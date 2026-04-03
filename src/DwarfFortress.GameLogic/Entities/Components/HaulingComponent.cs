namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// Tracks a single item currently being hauled in an entity's hands.
/// This is distinct from inventory, which represents small carried items.
/// </summary>
public sealed class HaulingComponent
{
    public int HauledItemId { get; private set; } = -1;

    public bool IsHauling => HauledItemId >= 0;

    public bool Contains(int itemId) => HauledItemId == itemId;

    public bool TryStartHauling(int itemId)
    {
        if (HauledItemId >= 0 && HauledItemId != itemId)
            return false;

        HauledItemId = itemId;
        return true;
    }

    public void StopHauling(int itemId)
    {
        if (HauledItemId == itemId)
            HauledItemId = -1;
    }

    public void Clear() => HauledItemId = -1;
}

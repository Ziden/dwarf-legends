namespace DwarfFortress.GameLogic.Entities;

/// <summary>
/// Identifies how an item is currently being carried.
/// </summary>
public enum ItemCarryMode : byte
{
    None = 0,
    Inventory = 1,
    Hauling = 2,
}

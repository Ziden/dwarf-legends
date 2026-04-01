using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Entities;

/// <summary>Quality tier of a crafted item.</summary>
public enum ItemQuality { Terrible = 0, Poor, Ordinary, Fine, Superior, Exceptional, Legendary }

/// <summary>A physical item in the world (log, ore, bar, food, weapon, furniture, etc.).</summary>
public sealed class Item : Entity
{
    public PositionComponent Position => Components.Get<PositionComponent>();

    /// <summary>References MaterialDef.Id; null for items with no specific material (e.g. food chunks).</summary>
    public string?      MaterialId { get; set; }

    /// <summary>Stack size (for stackable items like arrows, bolts, seeds).</summary>
    public int          StackSize  { get; set; } = 1;

    public ItemQuality  Quality    { get; set; } = ItemQuality.Ordinary;

    /// <summary>-1 = not stored in a stockpile. &gt;= 0 = stockpile entity ID.</summary>
    public int          StockpileId{ get; set; } = -1;

    /// <summary>-1 = not stored in a building container. &gt;= 0 = building ID holding this item.</summary>
    public int          ContainerBuildingId { get; set; } = -1;

    /// <summary>-1 = not stored inside another item. &gt;= 0 = parent item ID holding this item.</summary>
    public int          ContainerItemId { get; set; } = -1;

    /// <summary>-1 = not currently carried. &gt;= 0 = entity ID carrying this item.</summary>
    public int          CarriedByEntityId { get; set; } = -1;

    /// <summary>True when this item is claimed by a job and should not be hauled elsewhere.</summary>
    public bool         IsClaimed  { get; set; }

    public Item(int id, string defId, Vec3i pos, string? materialId = null)
        : base(id, defId)
    {
        MaterialId = materialId;
        Components.Add(new PositionComponent(pos));
    }
}

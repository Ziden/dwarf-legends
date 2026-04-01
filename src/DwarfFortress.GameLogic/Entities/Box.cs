using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Entities;

/// <summary>
/// A wooden box that sits on a stockpile tile and stores up to <see cref="DefaultCapacity"/> items.
/// Boxes are not placed by the player; dwarves place them automatically on stockpile slots.
/// A box item (DefId "box") is crafted at the Carpenter Workshop; when a dwarf executes a
/// PlaceBox job the item is consumed and a Box entity is created in its place.
/// </summary>
public sealed class Box : Entity
{
    public new const string DefId     = "box";
    public const int  DefaultCapacity = 10;

    public PositionComponent  Position  => Components.Get<PositionComponent>();
    public ContainerComponent Container => Components.Get<ContainerComponent>();

    public Box(int id, Vec3i pos, int capacity = DefaultCapacity)
        : base(id, DefId)
    {
        Components.Add(new PositionComponent(pos));
        Components.Add(new ContainerComponent(capacity));
    }
}

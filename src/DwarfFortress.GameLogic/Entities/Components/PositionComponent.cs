using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>The entity's location in the 3D world.</summary>
public sealed class PositionComponent
{
    public Vec3i Position { get; set; }

    /// <summary>Simple 4-direction facing: 0=North, 1=East, 2=South, 3=West.</summary>
    public int Facing { get; set; }

    public PositionComponent(Vec3i position) => Position = position;
}

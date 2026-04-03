using System.Collections.Generic;
using DwarfFortress.WorldGen.Generation;

namespace DwarfFortress.WorldGen.History;

public sealed class CivilizationRecord
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsHostile { get; init; }
    public string PrimaryUnitDefId { get; init; } = "";
    public float Influence { get; init; }
    public float Militarism { get; init; }
    public float TradeFocus { get; init; }
    public WorldCoord Capital { get; init; }
    public IReadOnlyList<WorldCoord> Territory { get; init; } = [];
}

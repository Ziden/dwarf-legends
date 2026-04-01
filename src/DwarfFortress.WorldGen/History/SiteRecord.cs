using DwarfFortress.WorldGen.Generation;

namespace DwarfFortress.WorldGen.History;

public sealed class SiteRecord
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string OwnerCivilizationId { get; init; } = "";
    public WorldCoord Location { get; init; }
    public float Development { get; init; }
    public float Security { get; init; }
}


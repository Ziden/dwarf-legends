using System.Collections.Generic;
using DwarfFortress.WorldGen.Generation;

namespace DwarfFortress.WorldGen.History;

public sealed class RoadRecord
{
    public string Id { get; init; } = "";
    public string OwnerCivilizationId { get; init; } = "";
    public string FromSiteId { get; init; } = "";
    public string ToSiteId { get; init; } = "";
    public IReadOnlyList<WorldCoord> Path { get; init; } = [];
}


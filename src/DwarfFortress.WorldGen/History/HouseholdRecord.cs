using System.Collections.Generic;

namespace DwarfFortress.WorldGen.History;

public sealed class HouseholdRecord
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string CivilizationId { get; init; } = "";
    public string HomeSiteId { get; init; } = "";
    public IReadOnlyList<string> MemberFigureIds { get; init; } = [];
}
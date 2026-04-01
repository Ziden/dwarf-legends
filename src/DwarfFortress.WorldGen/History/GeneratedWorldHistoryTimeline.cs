using System.Collections.Generic;

namespace DwarfFortress.WorldGen.History;

public sealed class GeneratedWorldHistoryTimeline
{
    public GeneratedWorldHistory FinalHistory { get; init; } = new();
    public IReadOnlyList<HistoryYearSnapshot> Years { get; init; } = [];
}


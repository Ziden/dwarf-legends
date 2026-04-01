using System.Collections.Generic;
using DwarfFortress.WorldGen.Generation;

namespace DwarfFortress.WorldGen.History;

public sealed class GeneratedWorldHistory
{
    public int Seed { get; init; }
    public int SimulatedYears { get; init; }
    public IReadOnlyList<CivilizationRecord> Civilizations { get; init; } = [];
    public IReadOnlyList<SiteRecord> Sites { get; init; } = [];
    public IReadOnlyList<RoadRecord> Roads { get; init; } = [];
    public IReadOnlyList<HistoricalEventRecord> Events { get; init; } = [];
    public IReadOnlyDictionary<WorldCoord, string> TerritoryByTile { get; init; }
        = new Dictionary<WorldCoord, string>();

    public bool TryGetOwner(WorldCoord coord, out string civilizationId)
        => TerritoryByTile.TryGetValue(coord, out civilizationId!);
}


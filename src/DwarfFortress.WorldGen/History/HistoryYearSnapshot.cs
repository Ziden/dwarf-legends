using System.Collections.Generic;
using DwarfFortress.WorldGen.Generation;

namespace DwarfFortress.WorldGen.History;

public sealed class HistoryYearSnapshot
{
    public int Year { get; init; }
    public float AverageProsperity { get; init; }
    public float AverageThreat { get; init; }
    public IReadOnlyList<HistoricalEventRecord> Events { get; init; } = [];
    public IReadOnlyList<CivilizationYearRecord> Civilizations { get; init; } = [];
    public IReadOnlyList<SiteYearRecord> Sites { get; init; } = [];
    public IReadOnlyList<SitePopulationRecord> SitePopulations { get; init; } = [];
    public IReadOnlyList<HouseholdYearRecord> Households { get; init; } = [];
    public IReadOnlyList<HistoricalFigureYearRecord> Figures { get; init; } = [];
    public IReadOnlyList<RoadRecord> Roads { get; init; } = [];
    public IReadOnlyDictionary<WorldCoord, string> TerritoryByTile { get; init; }
        = new Dictionary<WorldCoord, string>();

    public bool TryGetOwner(WorldCoord coord, out string civilizationId)
        => TerritoryByTile.TryGetValue(coord, out civilizationId!);
}

public sealed class CivilizationYearRecord
{
    public string CivilizationId { get; init; } = "";
    public string Name { get; init; } = "";
    public int TerritoryTiles { get; init; }
    public float Prosperity { get; init; }
    public float Threat { get; init; }
}

public sealed class SiteYearRecord
{
    public string SiteId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string OwnerCivilizationId { get; init; } = "";
    public WorldCoord Location { get; init; }
    public float Development { get; init; }
    public float Security { get; init; }
}

public sealed class HouseholdYearRecord
{
    public string HouseholdId { get; init; } = "";
    public string Name { get; init; } = "";
    public string CivilizationId { get; init; } = "";
    public string HomeSiteId { get; init; } = "";
    public int MemberCount { get; init; }
}

public sealed class HistoricalFigureYearRecord
{
    public string FigureId { get; init; } = "";
    public string Name { get; init; } = "";
    public string SpeciesDefId { get; init; } = "";
    public string CivilizationId { get; init; } = "";
    public string CurrentSiteId { get; init; } = "";
    public string HouseholdId { get; init; } = "";
    public int BirthYear { get; init; }
    public bool IsAlive { get; init; } = true;
    public bool IsFounder { get; init; }
    public string ProfessionId { get; init; } = "peasant";
}

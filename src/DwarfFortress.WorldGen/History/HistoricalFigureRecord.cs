using System.Collections.Generic;

namespace DwarfFortress.WorldGen.History;

public sealed class HistoricalFigureRecord
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string SpeciesDefId { get; init; } = "";
    public string CivilizationId { get; init; } = "";
    public string BirthSiteId { get; init; } = "";
    public string CurrentSiteId { get; init; } = "";
    public string HouseholdId { get; init; } = "";
    public int BirthYear { get; init; }
    public int? DeathYear { get; init; }
    public bool IsAlive { get; init; } = true;
    public bool IsFounder { get; init; }
    public string ProfessionId { get; init; } = "peasant";
    public IReadOnlyList<string> LaborIds { get; init; } = [];
    public IReadOnlyDictionary<string, int> SkillLevels { get; init; }
        = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> AttributeLevels { get; init; }
        = new Dictionary<string, int>();
    public string? LikedFoodId { get; init; }
    public string? DislikedFoodId { get; init; }
}

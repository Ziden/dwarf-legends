using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.WorldGen.Generation;

namespace DwarfFortress.GameLogic.World;

public sealed class WorldHistoryRuntimeSnapshot
{
    public GeneratedEmbarkContext EmbarkContext { get; set; }
    public WorldHistoryEmbarkSummary EmbarkSummary { get; set; } = new();
    public List<RuntimeHistoryCivilizationSnapshot> Civilizations { get; set; } = [];
    public List<RuntimeHistorySiteSnapshot> Sites { get; set; } = [];
    public List<RuntimeHistoryHouseholdSnapshot> Households { get; set; } = [];
    public List<RuntimeHistoryFigureSnapshot> Figures { get; set; } = [];
}

public sealed class WorldHistoryEmbarkSummary
{
    public string RegionName { get; set; } = "";
    public string BiomeId { get; set; } = "";
    public int SimulatedYears { get; set; }
    public string? OwnerCivilizationId { get; set; }
    public string? OwnerCivilizationName { get; set; }
    public string? PrimarySiteId { get; set; }
    public string? PrimarySiteName { get; set; }
    public string? PrimarySiteKind { get; set; }
    public int PrimarySitePopulation { get; set; }
    public int PrimarySiteHouseholdCount { get; set; }
    public int PrimarySiteMilitaryCount { get; set; }
    public string[] RecentEvents { get; set; } = Array.Empty<string>();
}

public sealed class RuntimeHistoryCivilizationSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsHostile { get; set; }
    public string PrimaryUnitDefId { get; set; } = "";
    public float Influence { get; set; }
    public float Militarism { get; set; }
    public float TradeFocus { get; set; }
}

public sealed class RuntimeHistorySiteSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string OwnerCivilizationId { get; set; } = "";
    public int WorldX { get; set; }
    public int WorldY { get; set; }
    public float Development { get; set; }
    public float Security { get; set; }
    public int Population { get; set; }
    public int HouseholdCount { get; set; }
    public int MilitaryCount { get; set; }
    public int CraftCount { get; set; }
    public int AgrarianCount { get; set; }
    public int MiningCount { get; set; }
}

public sealed class RuntimeHistoryHouseholdSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CivilizationId { get; set; } = "";
    public string HomeSiteId { get; set; } = "";
    public List<string> MemberFigureIds { get; set; } = [];
}

public sealed class RuntimeHistoryFigureSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SpeciesDefId { get; set; } = "";
    public string CivilizationId { get; set; } = "";
    public string BirthSiteId { get; set; } = "";
    public string CurrentSiteId { get; set; } = "";
    public string HouseholdId { get; set; } = "";
    public int BirthYear { get; set; }
    public bool IsAlive { get; set; } = true;
    public bool IsFounder { get; set; }
    public string ProfessionId { get; set; } = "peasant";
    public List<string> LaborIds { get; set; } = [];
    public Dictionary<string, int> SkillLevels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> AttributeLevels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? LikedFoodId { get; set; }
    public string? DislikedFoodId { get; set; }
}

public sealed class RuntimeStartingDwarfProfile
{
    public string Name { get; set; } = "Urist";
    public string ProfessionId { get; set; } = "peasant";
    public string[] LaborIds { get; set; } = [];
    public Dictionary<string, int> SkillLevels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> AttributeLevels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? LikedFoodId { get; set; }
    public string? DislikedFoodId { get; set; }
    public DwarfProvenanceComponent Provenance { get; set; } = new();
}

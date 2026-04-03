using System.Collections.Generic;
using DwarfFortress.WorldGen.Ids;

namespace DwarfFortress.WorldGen.Story;

public sealed class WorldLoreState
{
    public int Seed { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Depth { get; set; }

    public string RegionName { get; set; } = "";
    public string BiomeId { get; set; } = MacroBiomeIds.TemperatePlains;

    public float Threat { get; set; }
    public float Prosperity { get; set; }
    public int SimulatedYears { get; set; }

    public List<FactionLoreState> Factions { get; set; } = [];
    public List<FactionRelationLoreState> FactionRelations { get; set; } = [];
    public List<SiteLoreState> Sites { get; set; } = [];
    public List<HistoricalEventLoreState> History { get; set; } = [];
}

public sealed class FactionLoreState
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsHostile { get; set; }
    public string PrimaryUnitDefId { get; set; } = "";
    public float Influence { get; set; }
    public float Militarism { get; set; }
    public float TradeFocus { get; set; }
    public string Motto { get; set; } = "";
}

public sealed class SiteLoreState
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string OwnerFactionId { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string Summary { get; set; } = "";
    public string Status { get; set; } = SiteStatusIds.Stable;
    public float Development { get; set; }
    public float Security { get; set; }
}

public sealed class FactionRelationLoreState
{
    public string FactionAId { get; set; } = "";
    public string FactionBId { get; set; } = "";
    public float Score { get; set; }
    public string Stance { get; set; } = RelationStanceIds.Neutral;
}

public sealed class HistoricalEventLoreState
{
    public int Year { get; set; }
    public string Type { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? FactionAId { get; set; }
    public string? FactionBId { get; set; }
    public string? SiteId { get; set; }
}

public static class RelationStanceIds
{
    public const string Ally = "ally";
    public const string Neutral = "neutral";
    public const string Hostile = "hostile";
}

public static class SiteStatusIds
{
    public const string Growing = "growing";
    public const string Stable = "stable";
    public const string Declining = "declining";
    public const string Ruined = "ruined";
    public const string Fortified = "fortified";
}

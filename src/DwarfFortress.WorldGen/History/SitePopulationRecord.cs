namespace DwarfFortress.WorldGen.History;

public sealed class SitePopulationRecord
{
    public string SiteId { get; init; } = "";
    public int Year { get; init; }
    public int Population { get; init; }
    public int HouseholdCount { get; init; }
    public int MilitaryCount { get; init; }
    public int CraftCount { get; init; }
    public int AgrarianCount { get; init; }
    public int MiningCount { get; init; }
    public float Prosperity { get; init; }
    public float Security { get; init; }
}
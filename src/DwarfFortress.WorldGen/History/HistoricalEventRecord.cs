namespace DwarfFortress.WorldGen.History;

public sealed class HistoricalEventRecord
{
    public int Year { get; init; }
    public string Type { get; init; } = "";
    public string Summary { get; init; } = "";
    public string? PrimaryCivilizationId { get; init; }
    public string? SecondaryCivilizationId { get; init; }
    public string? SiteId { get; init; }
}


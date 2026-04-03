namespace DwarfFortress.GameLogic.Entities.Components;

public sealed class DwarfProvenanceComponent
{
    public int WorldSeed { get; set; }
    public string? FigureId { get; set; }
    public string? HouseholdId { get; set; }
    public string? CivilizationId { get; set; }
    public string? OriginSiteId { get; set; }
    public string? BirthSiteId { get; set; }
    public string? MigrationWaveId { get; set; }
    public int? WorldX { get; set; }
    public int? WorldY { get; set; }
    public int? RegionX { get; set; }
    public int? RegionY { get; set; }

    public bool HasKnownOrigin =>
        !string.IsNullOrWhiteSpace(FigureId) ||
        !string.IsNullOrWhiteSpace(HouseholdId) ||
        !string.IsNullOrWhiteSpace(CivilizationId) ||
        !string.IsNullOrWhiteSpace(OriginSiteId) ||
        !string.IsNullOrWhiteSpace(BirthSiteId);
}
namespace DwarfFortress.ContentEditor.Models;

public class FootprintTile
{
    public int X { get; set; }
    public int Y { get; set; }
    public string Tile { get; set; } = "";
}

public class BuildingInput
{
    public List<string> Tags { get; set; } = [];
    public int Qty { get; set; } = 1;
    public string? ItemDefId { get; set; }
    public string? MaterialId { get; set; }
}

public class BuildingEntryModel
{
    public int X { get; set; }
    public int Y { get; set; }
    public string OutwardDirection { get; set; } = "south";
}

public class BuildingVisualProfileModel
{
    public string Archetype { get; set; } = "";
    public string? Palette { get; set; }
    public bool HideRoofOnHover { get; set; }
}

public class BuildingModel
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public List<FootprintTile> Footprint { get; set; } = [];
    public List<BuildingInput> ConstructionInputs { get; set; } = [];
    public List<BuildingInput> DiscoveryInputs { get; set; } = [];
    public float ConstructionTime { get; set; }
    public bool IsWorkshop { get; set; }
    public string? ProducedSmokeId { get; set; }
    public int ResidenceCapacity { get; set; }
    public List<BuildingEntryModel> Entries { get; set; } = [];
    public List<string> AutoStockpileAcceptedTags { get; set; } = [];
    public BuildingVisualProfileModel? VisualProfile { get; set; }
}

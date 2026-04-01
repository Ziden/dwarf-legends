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
}

public class BuildingModel
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public List<FootprintTile> Footprint { get; set; } = [];
    public List<BuildingInput> ConstructionInputs { get; set; } = [];
    public float ConstructionTime { get; set; }
    public bool IsWorkshop { get; set; }
}

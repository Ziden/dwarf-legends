namespace DwarfFortress.ContentEditor.Models;

public class TileModel
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public bool IsPassable { get; set; }
    public bool IsOpaque { get; set; }
    public bool IsMineable { get; set; }
}

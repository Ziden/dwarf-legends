namespace DwarfFortress.ContentEditor.Models;

public class MaterialModel
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public float Hardness { get; set; }
}

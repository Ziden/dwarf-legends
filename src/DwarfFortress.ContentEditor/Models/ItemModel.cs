namespace DwarfFortress.ContentEditor.Models;

public class ItemModel
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public float Weight { get; set; }
    public bool Stackable { get; set; }
    public int MaxStack { get; set; }
    public float BaseValue { get; set; }
}

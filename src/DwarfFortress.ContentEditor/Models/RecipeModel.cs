namespace DwarfFortress.ContentEditor.Models;

public class RecipeInput
{
    public List<string> Tags { get; set; } = [];
    public int Qty { get; set; } = 1;
    public string ItemId { get; set; } = "";
    public string MaterialId { get; set; } = "";
}

public class RecipeOutput
{
    public string ItemId { get; set; } = "";
    public int Qty { get; set; } = 1;
    public string MaterialFrom { get; set; } = "";
    public string FormRole { get; set; } = "";
}

public class RecipeModel
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Workshop { get; set; } = "";
    public string Labor { get; set; } = "";
    public List<RecipeInput> Inputs { get; set; } = [];
    public List<RecipeOutput> Outputs { get; set; } = [];
    public float WorkTime { get; set; }
    public int SkillXp { get; set; }
}

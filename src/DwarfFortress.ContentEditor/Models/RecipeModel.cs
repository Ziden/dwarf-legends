namespace DwarfFortress.ContentEditor.Models;

public class RecipeInput
{
    public List<string> Tags { get; set; } = [];
    public int Qty { get; set; } = 1;
}

public class RecipeOutput
{
    public string ItemId { get; set; } = "";
    public int Qty { get; set; } = 1;
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

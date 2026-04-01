using DwarfFortress.GameLogic.Data;

namespace DwarfFortress.GameLogic.Data.Defs;

public static class MaterialIds
{
    public const string Granite = "granite";
    public const string Wood = "wood";
    public const string Food = "food";
    public const string Drink = "drink";
}

/// <summary>
/// Immutable definition of a material (stone, metal, wood, etc.).
/// Loaded from materials.json. Never mutated at runtime.
/// </summary>
public sealed record MaterialDef(
    string  Id,
    string  DisplayName,
    TagSet  Tags,
    float   Hardness      = 1.0f,
    float   MeltingPoint  = float.MaxValue,  // degrees Celsius; MaxValue = does not melt
    float   Density       = 1.0f,
    int     Value         = 1,               // base trade value per unit
    string? Color         = null);           // hex color for rendering ("7a7a8a")

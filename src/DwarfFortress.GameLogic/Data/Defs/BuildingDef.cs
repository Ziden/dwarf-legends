using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;

namespace DwarfFortress.GameLogic.Data.Defs;

/// <summary>A tile in the building's footprint, relative to the origin.</summary>
public sealed record BuildingTile(Vec2i Offset, string TileDefId);

/// <summary>
/// Immutable definition of a building or workshop type.
/// Loaded from buildings.json.
/// </summary>
public sealed record BuildingDef(
    string                      Id,
    string                      DisplayName,
    TagSet                      Tags,
    IReadOnlyList<BuildingTile> Footprint,
    IReadOnlyList<RecipeInput>  ConstructionInputs,
    float                       ConstructionTime = 50f,
    bool                        IsWorkshop       = false,
    string?                     ProducedSmokeId  = null);

public static class BuildingDefIds
{
    public const string Bed = "bed";
    public const string Chair = "chair";
    public const string Table = "table";
    public const string CarpenterWorkshop = "carpenter_workshop";
    public const string Kitchen = "kitchen";
    public const string Still = "still";
}

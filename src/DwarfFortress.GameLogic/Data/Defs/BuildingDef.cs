using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;

namespace DwarfFortress.GameLogic.Data.Defs;

/// <summary>A tile in the building's footprint, relative to the origin.</summary>
public sealed record BuildingTile(Vec2i Offset, string TileDefId);

/// <summary>A building entry tile and the direction creatures cross to leave the building.</summary>
public sealed record BuildingEntryDef(Vec2i Offset, Vec2i OutwardDirection);

/// <summary>Client-facing visual profile data for procedural building rendering.</summary>
public sealed record BuildingVisualProfile(
    string Archetype,
    string? Palette = null,
    bool HideRoofOnHover = false);

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
    IReadOnlyList<RecipeInput>? DiscoveryInputs = null,
    float                       ConstructionTime = 50f,
    bool                        IsWorkshop       = false,
    string?                     ProducedSmokeId  = null,
    int                         ResidenceCapacity = 0,
    IReadOnlyList<BuildingEntryDef> Entries = null!,
    IReadOnlyList<string>       AutoStockpileAcceptedTags = null!,
    BuildingVisualProfile?      VisualProfile = null);

public static class BuildingDefIds
{
    public const string Bed = "bed";
    public const string Chair = "chair";
    public const string Table = "table";
    public const string House = "house";
    public const string CarpenterWorkshop = "carpenter_workshop";
    public const string Smelter = "smelter";
    public const string Kitchen = "kitchen";
    public const string Still = "still";
}

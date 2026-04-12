using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Data.Defs;

public enum PlantHostKind : byte
{
    Ground = 0,
    Tree = 1,
}

/// <summary>
/// Immutable definition of a surface plant or tree-borne fruit canopy.
/// Loaded from plants.json.
/// </summary>
public sealed record PlantDef(
    string Id,
    string DisplayName,
    PlantHostKind HostKind,
    IReadOnlyList<string> AllowedBiomeIds,
    IReadOnlyList<string> AllowedGroundTileDefIds,
    IReadOnlyList<string> SupportedTreeSpeciesIds,
    float MinMoisture,
    float MaxMoisture,
    float MinTerrain,
    float MaxTerrain,
    bool PrefersNearWater,
    bool PrefersFarFromWater,
    byte MaxGrowthStage,
    float SecondsPerStage,
    float FruitingCycleSeconds,
    float SeedSpreadChance,
    int SeedSpreadRadiusMin,
    int SeedSpreadRadiusMax,
    float Energy,
    float Protein,
    float Vitamins,
    float Minerals,
    string? HarvestItemDefId = null,
    string? SeedItemDefId = null,
    string? FruitItemDefId = null,
    bool DropYieldOnHostRemoval = false);
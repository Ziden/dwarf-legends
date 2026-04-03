using System.Collections.Generic;

namespace DwarfFortress.WorldGen.Config;

public sealed class WorldGenContentConfig
{
    public List<GeologyProfileContentConfig> GeologyProfiles { get; init; } = [];
    public List<TreeBiomeContentConfig> TreeProfiles { get; init; } = [];
    public List<BiomeGenerationContentConfig> BiomeProfiles { get; init; } = [];
    public List<CaveWildlifeLayerContentConfig> CaveWildlifeLayers { get; init; } = [];
    public HistoryFigureGenerationContentConfig HistoryFigures { get; init; } = new();
}

public sealed class GeologyProfileContentConfig
{
    public string Id { get; init; } = string.Empty;
    public int SeedSalt { get; init; }
    public float AquiferDepthFraction { get; init; }
    public List<StrataLayerContentConfig> Layers { get; init; } = [];
    public List<MineralVeinContentConfig> MineralVeins { get; init; } = [];
}

public sealed class StrataLayerContentConfig
{
    public string RockTypeId { get; init; } = string.Empty;
    public int ThicknessMin { get; init; }
    public int ThicknessMax { get; init; }
}

public sealed class MineralVeinContentConfig
{
    public string MaterialId { get; init; } = string.Empty;
    public string ResourceFormRole { get; init; } = string.Empty;
    public string OreId { get; init; } = string.Empty;
    public string Shape { get; init; } = string.Empty;
    public float Frequency { get; init; }
    public string RequiredRockTypeId { get; init; } = string.Empty;
    public int SizeMin { get; init; }
    public int SizeMax { get; init; }
}

public sealed class TreeBiomeContentConfig
{
    public string BiomeId { get; init; } = string.Empty;
    public string SubsurfaceMaterialId { get; init; } = string.Empty;
    public List<TreeSpeciesRuleContentConfig> Rules { get; init; } = [];
    public List<WeightedTreeSpeciesContentConfig> DefaultSpecies { get; init; } = [];
}

public sealed class TreeSpeciesRuleContentConfig
{
    public float? MinMoisture { get; init; }
    public float? MaxMoisture { get; init; }
    public float? MinTerrain { get; init; }
    public float? MaxTerrain { get; init; }
    public float? MinRiparianBoost { get; init; }
    public float? MaxRiparianBoost { get; init; }
    public float? Chance { get; init; }
    public List<WeightedTreeSpeciesContentConfig> Species { get; init; } = [];
}

public sealed class WeightedTreeSpeciesContentConfig
{
    public string SpeciesId { get; init; } = string.Empty;
    public float Weight { get; init; }
}

public sealed class BiomeGenerationContentConfig
{
    public string Id { get; init; } = string.Empty;
    public float? GroundPlantDensity { get; init; }
    public float? TerrainRuggedness { get; init; }
    public float? BaseMoisture { get; init; }
    public float? TreeCoverageBoost { get; init; }
    public float? TreeSuitabilityFloor { get; init; }
    public bool? DenseForest { get; init; }
    public int? SurfaceCreatureGroupBias { get; init; }
    public float TreeCoverMin { get; init; }
    public float TreeCoverMax { get; init; }
    public int OutcropMin { get; init; }
    public int OutcropMax { get; init; }
    public int StreamBands { get; init; }
    public int MarshPoolCount { get; init; }
    public bool StoneSurface { get; init; }
    public List<CreatureSpawnContentConfig> SurfaceWildlife { get; init; } = [];
}

public sealed class CaveWildlifeLayerContentConfig
{
    public int Layer { get; init; }
    public List<CreatureSpawnContentConfig> Spawns { get; init; } = [];
}

public sealed class CreatureSpawnContentConfig
{
    public string CreatureDefId { get; init; } = string.Empty;
    public float Weight { get; init; }
    public int MinGroup { get; init; }
    public int MaxGroup { get; init; }
    public bool RequiresWater { get; init; }
    public bool AvoidEmbarkCenter { get; init; } = true;
}

public readonly record struct BiomeGenerationProfile(
    string Id,
    float GroundPlantDensity,
    float TerrainRuggedness,
    float BaseMoisture,
    float TreeCoverageBoost,
    float TreeSuitabilityFloor,
    bool DenseForest,
    int SurfaceCreatureGroupBias,
    float TreeCoverMin,
    float TreeCoverMax,
    int OutcropMin,
    int OutcropMax,
    int StreamBands,
    int MarshPoolCount,
    bool StoneSurface);

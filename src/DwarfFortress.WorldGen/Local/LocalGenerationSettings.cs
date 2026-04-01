namespace DwarfFortress.WorldGen.Local;

/// <summary>
/// Settings used by local-map generation. Defaults preserve existing embark behavior.
/// </summary>
public readonly record struct LocalGenerationSettings(
    int Width,
    int Height,
    int Depth,
    string? BiomeOverrideId = null,
    float TreeDensityBias = 0f,
    float OutcropBias = 0f,
    int StreamBandBias = 0,
    int MarshPoolBias = 0,
    float ParentWetnessBias = 0f,
    float ParentSoilDepthBias = 0f,
    string? GeologyProfileId = null,
    bool? StoneSurfaceOverride = null,
    LocalRiverPortal[]? RiverPortals = null,
    float ForestPatchBias = 0f,
    float SettlementInfluence = 0f,
    float RoadInfluence = 0f,
    LocalSettlementAnchor[]? SettlementAnchors = null,
    LocalRoadPortal[]? RoadPortals = null,
    string? SurfaceTileOverrideId = null,
    float? ForestCoverageTarget = null,
    int NoiseOriginX = 0,
    int NoiseOriginY = 0);

using DwarfFortress.WorldGen.Generation;

namespace DwarfFortress.WorldGen.Local;

/// <summary>
/// Explicit parent-to-child continuity inputs for local embark generation.
/// Fields are nullable so callers can incrementally migrate from legacy settings.
/// </summary>
public readonly record struct LocalContinuityContract(
    string? BiomeOverrideId = null,
    float? TreeDensityBias = null,
    float? OutcropBias = null,
    int? StreamBandBias = null,
    int? MarshPoolBias = null,
    float? ParentWetnessBias = null,
    float? ParentSoilDepthBias = null,
    string? GeologyProfileId = null,
    bool? StoneSurfaceOverride = null,
    LocalRiverPortal[]? RiverPortals = null,
    float? ForestPatchBias = null,
    float? SettlementInfluence = null,
    float? RoadInfluence = null,
    LocalSettlementAnchor[]? SettlementAnchors = null,
    LocalRoadPortal[]? RoadPortals = null,
    string? SurfaceTileOverrideId = null,
    float? ForestCoverageTarget = null,
    EcologyEdgeDescriptors? EcologyEdges = null,
    int? NoiseOriginX = null,
    int? NoiseOriginY = null,
    int? ContinuitySeed = null,
    LocalSurfaceIntentGrid? SurfaceIntentGrid = null)
{
    public int GetFingerprint()
        => LocalGenerationFingerprint.Compute(this);
}

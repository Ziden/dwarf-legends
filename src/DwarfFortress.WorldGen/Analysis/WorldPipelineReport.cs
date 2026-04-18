using System.Collections.Generic;
using System.Linq;

namespace DwarfFortress.WorldGen.Analysis;

public sealed record WorldPipelineReport(
    int SeedCount,
    int EvaluatedSeedCount,
    bool BiomeCoverageRequested,
    int SampledRegionCount,
    float WorldRiverEdgeMismatchRatio,
    float WorldRoadEdgeMismatchRatio,
    float RegionRiverEdgeMismatchRatio,
    float RegionRoadEdgeMismatchRatio,
    int LocalBoundarySampleCount,
    float LocalSurfaceBoundaryMismatchRatio,
    float LocalWaterBoundaryMismatchRatio,
    float LocalEcologyBoundaryMismatchRatio,
    float LocalTreeBoundaryMismatchRatio,
    float RegionParentMacroAlignmentRatio,
    float RegionVegetationGroundwaterCorrelation,
    float RegionVegetationSuitabilityCorrelation,
    float WorldForestRegionVegetationCorrelation,
    float WorldForestLocalTreeDensityCorrelation,
    float WorldMountainRegionSlopeCorrelation,
    float TropicalLandShare,
    float AridLandShare,
    float ColdLandShare,
    float DesertLandShare,
    int DenseForestSampleCount,
    int TropicalSampleCount,
    int AridSampleCount,
    float DenseForestMedianTreeDensity,
    float TropicalMedianTreeDensity,
    float AridMedianTreeDensity,
    float DenseForestMedianLargestPatchRatio,
    float DenseForestMedianOpeningRatio,
    float DenseForestMedianReachableOpeningRatio,
    bool DenseForestCoverageAchieved,
    bool TropicalCoverageAchieved,
    bool AridCoverageAchieved,
    float LocalTreeSuitabilityCorrelation,
    float AvgLocalTreeDensity,
    IReadOnlyList<DepthBudgetResult> Budgets,
    int LocalBoundaryBandSampleCount = 0,
    float LocalSurfaceBoundaryBandMismatchRatio = 0f,
    float LocalWaterBoundaryBandMismatchRatio = 0f,
    float LocalEcologyBoundaryBandMismatchRatio = 0f,
    float LocalTreeBoundaryBandMismatchRatio = 0f)
{
    public int AdditionalSeedsUsed => EvaluatedSeedCount > SeedCount ? EvaluatedSeedCount - SeedCount : 0;
    public bool Passed => Budgets.Count > 0 && Budgets.All(b => b.Passed);
}

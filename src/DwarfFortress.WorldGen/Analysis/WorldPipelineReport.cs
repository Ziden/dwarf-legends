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
    bool DenseForestCoverageAchieved,
    bool TropicalCoverageAchieved,
    bool AridCoverageAchieved,
    float LocalTreeSuitabilityCorrelation,
    float AvgLocalTreeDensity,
    IReadOnlyList<DepthBudgetResult> Budgets)
{
    public int AdditionalSeedsUsed => EvaluatedSeedCount > SeedCount ? EvaluatedSeedCount - SeedCount : 0;
    public bool Passed => Budgets.Count > 0 && Budgets.All(b => b.Passed);
}

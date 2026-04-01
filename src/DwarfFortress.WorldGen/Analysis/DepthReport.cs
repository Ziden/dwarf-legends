using System.Collections.Generic;
using System.Linq;

namespace DwarfFortress.WorldGen.Analysis;

public sealed record DepthBudgetResult(
    string Name,
    bool Passed,
    string Detail);

public sealed record DepthReport(
    int SeedCount,
    int DistinctBiomeCount,
    float AvgPassableSurfaceRatio,
    float AvgFactionCount,
    float AvgHostileFactionCount,
    float AvgRelationCount,
    float AvgHostileRelationCount,
    float AvgSiteCount,
    float AvgGrowingSiteCount,
    float AvgDecliningSiteCount,
    float AvgRuinedSiteCount,
    float AvgFortifiedSiteCount,
    float AvgSiteDevelopment,
    float AvgSiteSecurity,
    float AvgEventCount,
    float AvgHostileEventRatio,
    float AvgThreat,
    float AvgProsperity,
    IReadOnlyList<DepthBudgetResult> Budgets)
{
    public bool Passed => Budgets.Count > 0 && Budgets.All(b => b.Passed);
}

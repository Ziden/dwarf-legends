using System.Collections.Generic;
using System.Linq;
using DwarfFortress.WorldGen.Maps;

namespace DwarfFortress.WorldGen.Analysis;

public sealed record EmbarkStageReport(
    int Seed,
    int SurfaceTileCount,
    int UndergroundTileCount,
    IReadOnlyList<EmbarkGenerationStageSnapshot> StageSnapshots,
    IReadOnlyList<DepthBudgetResult> Budgets)
{
    public bool Passed => Budgets.Count > 0 && Budgets.All(b => b.Passed);
}
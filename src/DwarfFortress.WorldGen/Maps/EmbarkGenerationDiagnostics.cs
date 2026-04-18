using System;
using System.Collections.Generic;

namespace DwarfFortress.WorldGen.Maps;

public enum EmbarkGenerationStageId
{
    Inputs = 0,
    SurfaceShape = 1,
    UndergroundStructure = 2,
    Hydrology = 3,
    Ecology = 4,
    HydrologyPolish = 5,
    CivilizationOverlay = 6,
    Vegetation = 7,
    SurfaceAccessPrep = 8,
    BoundaryContinuity = 9,
    Playability = 10,
    Population = 11,
}

public readonly record struct EmbarkGenerationStageSnapshot(
    EmbarkGenerationStageId StageId,
    int SurfacePassableTiles,
    int SurfaceWaterTiles,
    int SurfaceTreeTiles,
    int SurfaceWallTiles,
    int UndergroundPassableTiles,
    int AquiferTiles,
    int OreTiles,
    int MagmaTiles,
    int CreatureSpawnCount);

public readonly record struct ExactBoundaryContinuityMetrics(
    int BoundaryCellsProcessed,
    int SurfaceCellsAdjusted,
    int TreesPlaced,
    int TreesRemoved,
    int TreeCanopyAdjustedCells,
    int GroundPlantAdjustedCells)
{
    public int VegetationAdjustedCells
        => TreesPlaced + TreesRemoved + TreeCanopyAdjustedCells + GroundPlantAdjustedCells;
}

public sealed class EmbarkGenerationDiagnostics
{
    public EmbarkGenerationDiagnostics(
        int seed,
        IReadOnlyList<EmbarkGenerationStageSnapshot> stageSnapshots,
        ExactBoundaryContinuityMetrics? exactBoundaryContinuity = null)
    {
        Seed = seed;
        StageSnapshots = stageSnapshots ?? throw new ArgumentNullException(nameof(stageSnapshots));
        ExactBoundaryContinuity = exactBoundaryContinuity;
    }

    public int Seed { get; }
    public IReadOnlyList<EmbarkGenerationStageSnapshot> StageSnapshots { get; }
    public ExactBoundaryContinuityMetrics? ExactBoundaryContinuity { get; }
}
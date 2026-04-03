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
    Playability = 7,
    Population = 8,
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

public sealed class EmbarkGenerationDiagnostics
{
    public EmbarkGenerationDiagnostics(int seed, IReadOnlyList<EmbarkGenerationStageSnapshot> stageSnapshots)
    {
        Seed = seed;
        StageSnapshots = stageSnapshots ?? throw new ArgumentNullException(nameof(stageSnapshots));
    }

    public int Seed { get; }
    public IReadOnlyList<EmbarkGenerationStageSnapshot> StageSnapshots { get; }
}
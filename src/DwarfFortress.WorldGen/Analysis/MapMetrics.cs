namespace DwarfFortress.WorldGen.Analysis;

public sealed record MapMetrics(
    int Width,
    int Height,
    int Depth,
    int SurfaceTiles,
    int PassableSurfaceTiles,
    float PassableSurfaceRatio,
    int TreeTiles,
    int WaterTiles,
    int WallTiles,
    bool BordersPassable,
    bool CornerPathExists);

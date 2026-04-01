namespace DwarfFortress.WorldGen.Generation;

public readonly record struct WorldCoord(int X, int Y);

public readonly record struct RegionCoord(int WorldX, int WorldY, int RegionX, int RegionY);

public readonly record struct LocalCoord(
    int WorldX,
    int WorldY,
    int RegionX,
    int RegionY,
    int LocalX,
    int LocalY,
    int Z);

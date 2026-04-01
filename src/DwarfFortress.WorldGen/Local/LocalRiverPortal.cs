namespace DwarfFortress.WorldGen.Local;

public enum LocalMapEdge : byte
{
    North = 0,
    East = 1,
    South = 2,
    West = 3,
}

public readonly record struct LocalRiverPortal(
    LocalMapEdge Edge,
    float NormalizedOffset,
    byte Strength = 1);

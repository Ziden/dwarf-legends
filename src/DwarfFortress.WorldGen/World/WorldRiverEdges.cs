using System;

namespace DwarfFortress.WorldGen.World;

[Flags]
public enum WorldRiverEdges : byte
{
    None = 0,
    North = 1 << 0,
    East = 1 << 1,
    South = 1 << 2,
    West = 1 << 3,
}

public static class WorldRiverEdgeMask
{
    public static bool Has(WorldRiverEdges value, WorldRiverEdges edge)
        => (value & edge) != 0;
}

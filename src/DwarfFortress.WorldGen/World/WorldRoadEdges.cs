using System;

namespace DwarfFortress.WorldGen.World;

[Flags]
public enum WorldRoadEdges : byte
{
    None = 0,
    North = 1 << 0,
    East = 1 << 1,
    South = 1 << 2,
    West = 1 << 3,
}

public static class WorldRoadEdgeMask
{
    public static bool Has(WorldRoadEdges value, WorldRoadEdges edge)
        => (value & edge) != 0;
}

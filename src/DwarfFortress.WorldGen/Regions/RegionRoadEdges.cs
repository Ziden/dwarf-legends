using System;

namespace DwarfFortress.WorldGen.Regions;

[Flags]
public enum RegionRoadEdges : byte
{
    None = 0,
    North = 1 << 0,
    East = 1 << 1,
    South = 1 << 2,
    West = 1 << 3,
}

public static class RegionRoadEdgeMask
{
    public static bool Has(RegionRoadEdges value, RegionRoadEdges edge)
        => (value & edge) != 0;
}

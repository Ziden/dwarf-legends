using System;

namespace DwarfFortress.WorldGen.Regions;

[Flags]
public enum RegionRiverEdges : byte
{
    None = 0,
    North = 1 << 0,
    East = 1 << 1,
    South = 1 << 2,
    West = 1 << 3,
}

public static class RegionRiverEdgeMask
{
    public static bool Has(RegionRiverEdges value, RegionRiverEdges edge)
        => (value & edge) != 0;
}

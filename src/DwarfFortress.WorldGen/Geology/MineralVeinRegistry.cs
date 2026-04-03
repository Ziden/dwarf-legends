using System;
using System.Collections.Generic;
using DwarfFortress.WorldGen.Config;

namespace DwarfFortress.WorldGen.Geology;

public static class MineralVeinRegistry
{
    public static IReadOnlyList<MineralVeinDef> Resolve(string? geologyProfileId)
        => WorldGenContentRegistry.Current.ResolveMineralVeins(geologyProfileId);

    public static bool IsOreCompatible(string oreId, string? rockTypeId)
        => WorldGenContentRegistry.Current.IsOreCompatible(oreId, rockTypeId);
}

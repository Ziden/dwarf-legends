using System;
using DwarfFortress.WorldGen.Config;

namespace DwarfFortress.WorldGen.Geology;

public static class StrataProfileRegistry
{
    public static StrataProfile Resolve(string? geologyProfileId)
        => WorldGenContentRegistry.Current.ResolveStrataProfile(geologyProfileId);
}

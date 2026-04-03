using DwarfFortress.WorldGen.Config;
using DwarfFortress.WorldGen.Ids;

namespace DwarfFortress.WorldGen.Creatures;

/// <summary>
/// Static biome -> species table for local surface wildlife placement.
/// </summary>
public static class BiomeCreatureTable
{
    public static IReadOnlyList<SpawnEntry> GetSurface(string? macroBiomeId)
        => WorldGenContentRegistry.Current.ResolveSurfaceWildlife(macroBiomeId, seed: 0);

    public static IReadOnlyList<SpawnEntry> GetCave(int caveLayer)
        => WorldGenContentRegistry.Current.ResolveCaveWildlife(caveLayer);
}

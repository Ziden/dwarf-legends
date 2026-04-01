using DwarfFortress.WorldGen.Ids;

namespace DwarfFortress.WorldGen.Creatures;

/// <summary>
/// Static biome -> species table for local surface wildlife placement.
/// </summary>
public static class BiomeCreatureTable
{
    private static readonly SpawnEntry[] TemperateSurface =
    [
        new(CreatureDefIds.Elk, Weight: 0.62f, MinGroup: 2, MaxGroup: 4),
        new(CreatureDefIds.Dog, Weight: 0.20f, MinGroup: 1, MaxGroup: 2),
        new(CreatureDefIds.Cat, Weight: 0.18f, MinGroup: 1, MaxGroup: 2),
    ];

    private static readonly SpawnEntry[] ConiferSurface =
    [
        new(CreatureDefIds.Elk, Weight: 0.72f, MinGroup: 2, MaxGroup: 5),
        new(CreatureDefIds.Dog, Weight: 0.16f, MinGroup: 1, MaxGroup: 2),
        new(CreatureDefIds.Cat, Weight: 0.12f, MinGroup: 1, MaxGroup: 2),
    ];

    private static readonly SpawnEntry[] HighlandSurface =
    [
        new(CreatureDefIds.Elk, Weight: 0.60f, MinGroup: 1, MaxGroup: 3),
        new(CreatureDefIds.Dog, Weight: 0.30f, MinGroup: 1, MaxGroup: 2),
        new(CreatureDefIds.Cat, Weight: 0.10f, MinGroup: 1, MaxGroup: 1),
    ];

    private static readonly SpawnEntry[] MarshSurface =
    [
        new(CreatureDefIds.GiantCarp, Weight: 0.42f, MinGroup: 2, MaxGroup: 5, RequiresWater: true),
        new(CreatureDefIds.Elk, Weight: 0.34f, MinGroup: 1, MaxGroup: 3),
        new(CreatureDefIds.Dog, Weight: 0.14f, MinGroup: 1, MaxGroup: 2),
        new(CreatureDefIds.Cat, Weight: 0.10f, MinGroup: 1, MaxGroup: 2),
    ];

    private static readonly SpawnEntry[] SteppeSurface =
    [
        new(CreatureDefIds.Elk, Weight: 0.48f, MinGroup: 1, MaxGroup: 3),
        new(CreatureDefIds.Dog, Weight: 0.34f, MinGroup: 1, MaxGroup: 2),
        new(CreatureDefIds.Cat, Weight: 0.18f, MinGroup: 1, MaxGroup: 2),
    ];

    private static readonly SpawnEntry[] TropicalSurface =
    [
        new(CreatureDefIds.Elk, Weight: 0.58f, MinGroup: 2, MaxGroup: 5),
        new(CreatureDefIds.GiantCarp, Weight: 0.20f, MinGroup: 2, MaxGroup: 4, RequiresWater: true),
        new(CreatureDefIds.Dog, Weight: 0.12f, MinGroup: 1, MaxGroup: 2),
        new(CreatureDefIds.Cat, Weight: 0.10f, MinGroup: 1, MaxGroup: 2),
    ];

    private static readonly SpawnEntry[] SavannaSurface =
    [
        new(CreatureDefIds.Elk, Weight: 0.50f, MinGroup: 1, MaxGroup: 3),
        new(CreatureDefIds.Dog, Weight: 0.32f, MinGroup: 1, MaxGroup: 2),
        new(CreatureDefIds.Cat, Weight: 0.18f, MinGroup: 1, MaxGroup: 2),
    ];

    private static readonly SpawnEntry[] DesertSurface =
    [
        new(CreatureDefIds.Dog, Weight: 0.60f, MinGroup: 1, MaxGroup: 2),
        new(CreatureDefIds.Cat, Weight: 0.40f, MinGroup: 1, MaxGroup: 1),
    ];

    private static readonly SpawnEntry[] TundraSurface =
    [
        new(CreatureDefIds.Elk, Weight: 0.62f, MinGroup: 1, MaxGroup: 2),
        new(CreatureDefIds.Dog, Weight: 0.28f, MinGroup: 1, MaxGroup: 2),
        new(CreatureDefIds.Cat, Weight: 0.10f, MinGroup: 1, MaxGroup: 1),
    ];

    private static readonly SpawnEntry[] BorealSurface =
    [
        new(CreatureDefIds.Elk, Weight: 0.74f, MinGroup: 2, MaxGroup: 4),
        new(CreatureDefIds.Dog, Weight: 0.18f, MinGroup: 1, MaxGroup: 2),
        new(CreatureDefIds.Cat, Weight: 0.08f, MinGroup: 1, MaxGroup: 1),
    ];

    private static readonly SpawnEntry[] IceSurface =
    [
        new(CreatureDefIds.Dog, Weight: 0.70f, MinGroup: 1, MaxGroup: 2),
        new(CreatureDefIds.Cat, Weight: 0.30f, MinGroup: 1, MaxGroup: 1),
    ];

    private static readonly SpawnEntry[] OceanSurface =
    [
        new(CreatureDefIds.GiantCarp, Weight: 1f, MinGroup: 3, MaxGroup: 6, RequiresWater: true, AvoidEmbarkCenter: false),
    ];

    private static readonly SpawnEntry[] CaveLayer1 =
    [
        new(CreatureDefIds.Goblin, Weight: 0.56f, MinGroup: 1, MaxGroup: 3, AvoidEmbarkCenter: false),
        new(CreatureDefIds.GiantCarp, Weight: 0.30f, MinGroup: 2, MaxGroup: 4, RequiresWater: true, AvoidEmbarkCenter: false),
        new(CreatureDefIds.Troll, Weight: 0.14f, MinGroup: 1, MaxGroup: 1, AvoidEmbarkCenter: false),
    ];

    private static readonly SpawnEntry[] CaveLayer2 =
    [
        new(CreatureDefIds.Goblin, Weight: 0.42f, MinGroup: 1, MaxGroup: 3, AvoidEmbarkCenter: false),
        new(CreatureDefIds.Troll, Weight: 0.38f, MinGroup: 1, MaxGroup: 2, AvoidEmbarkCenter: false),
        new(CreatureDefIds.GiantCarp, Weight: 0.20f, MinGroup: 2, MaxGroup: 5, RequiresWater: true, AvoidEmbarkCenter: false),
    ];

    private static readonly SpawnEntry[] CaveLayer3 =
    [
        new(CreatureDefIds.Troll, Weight: 0.55f, MinGroup: 1, MaxGroup: 2, AvoidEmbarkCenter: false),
        new(CreatureDefIds.Goblin, Weight: 0.30f, MinGroup: 1, MaxGroup: 2, AvoidEmbarkCenter: false),
        new(CreatureDefIds.GiantCarp, Weight: 0.15f, MinGroup: 2, MaxGroup: 4, RequiresWater: true, AvoidEmbarkCenter: false),
    ];

    public static IReadOnlyList<SpawnEntry> GetSurface(string? macroBiomeId)
    {
        if (string.IsNullOrWhiteSpace(macroBiomeId))
            return TemperateSurface;

        return macroBiomeId switch
        {
            MacroBiomeIds.TemperatePlains => TemperateSurface,
            MacroBiomeIds.ConiferForest => ConiferSurface,
            MacroBiomeIds.Highland => HighlandSurface,
            MacroBiomeIds.MistyMarsh => MarshSurface,
            MacroBiomeIds.WindsweptSteppe => SteppeSurface,
            MacroBiomeIds.TropicalRainforest => TropicalSurface,
            MacroBiomeIds.Savanna => SavannaSurface,
            MacroBiomeIds.Desert => DesertSurface,
            MacroBiomeIds.Tundra => TundraSurface,
            MacroBiomeIds.BorealForest => BorealSurface,
            MacroBiomeIds.IcePlains => IceSurface,
            MacroBiomeIds.OceanShallow => OceanSurface,
            MacroBiomeIds.OceanDeep => OceanSurface,
            _ => TemperateSurface,
        };
    }

    public static IReadOnlyList<SpawnEntry> GetCave(int caveLayer)
    {
        return caveLayer switch
        {
            1 => CaveLayer1,
            2 => CaveLayer2,
            3 => CaveLayer3,
            _ => CaveLayer1,
        };
    }
}

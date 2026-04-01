namespace DwarfFortress.WorldGen.Creatures;

/// <summary>
/// A worldgen-authored creature placement on the local embark map.
/// </summary>
public readonly record struct CreatureSpawn(
    string CreatureDefId,
    int X,
    int Y,
    int Z);

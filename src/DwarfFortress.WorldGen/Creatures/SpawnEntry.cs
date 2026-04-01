namespace DwarfFortress.WorldGen.Creatures;

/// <summary>
/// Weighted species entry used by biome-driven creature placement.
/// </summary>
public readonly record struct SpawnEntry(
    string CreatureDefId,
    float Weight,
    int MinGroup,
    int MaxGroup,
    bool RequiresWater = false,
    bool AvoidEmbarkCenter = true);

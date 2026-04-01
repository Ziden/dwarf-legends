namespace DwarfFortress.WorldGen.Local;

/// <summary>
/// Edge portal for local road imprinting.
/// </summary>
public readonly record struct LocalRoadPortal(
    LocalMapEdge Edge,
    float NormalizedOffset,
    byte Width = 1);


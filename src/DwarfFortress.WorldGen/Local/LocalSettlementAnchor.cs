namespace DwarfFortress.WorldGen.Local;

/// <summary>
/// Normalized (0..1) point for local settlement imprinting.
/// </summary>
public readonly record struct LocalSettlementAnchor(
    float NormalizedX,
    float NormalizedY,
    byte Strength = 3);


namespace DwarfFortress.GameLogic.Data;

/// <summary>How a modifier's value is combined with the base stat value.</summary>
public enum ModType
{
    /// <summary>Added to the base value before percentage calculations.</summary>
    Flat,

    /// <summary>All PercentAdd modifiers are summed, then multiplied: base * (1 + sum).</summary>
    PercentAdd,

    /// <summary>Each PercentMult modifier multiplies the result independently.</summary>
    PercentMult,
}

/// <summary>
/// A single modifier applied to a stat via a ModifierStack.
/// Represents a buff, debuff, equipment bonus, wound penalty, skill bonus, etc.
/// </summary>
public sealed record Modifier(
    string   SourceId,   // unique ID for removal (e.g. "hunger_penalty", "iron_sword")
    ModType  Type,
    float    Value,
    float    Duration = -1f);  // seconds remaining; -1 = permanent

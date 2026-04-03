using System;
using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Data.Defs;

/// <summary>
/// Defines a dwarf attribute with 1-5 scale and effect curves.
/// Loaded from dwarf_attributes.json.
/// Level 3 is the neutral default and is never displayed in UI.
/// </summary>
public sealed record DwarfAttributeDef(
    string Id,
    string DisplayName,
    string Description,
    string Category,          // "physiological", "mental", "social"
    TagSet Tags,
    /// <summary>Effect value for each attribute level 1-5. Null = no effect at that level.</summary>
    IReadOnlyDictionary<string, AttributeEffectCurve> EffectCurves
);

/// <summary>
/// Numeric effect curve for a single attribute level.
/// Systems read these to determine multipliers/modifiers.
/// </summary>
public sealed record AttributeEffectCurve(
    /// <summary>Effects keyed by op name (e.g. "hunger_satisfaction_multiplier").</summary>
    IReadOnlyDictionary<string, float> Effects
);

/// <summary>
/// A single attribute instance attached to a dwarf with its current level (1-5).
/// </summary>
public sealed record DwarfAttribute(
    string DefId,
    int Level,                   // 1-5, where 3 is default
    DwarfAttributeDef Def
)
{
    /// <summary>Returns true if this attribute should be displayed (level != 3).</summary>
    public bool IsVisible => Level != 3;

    /// <summary>Get the user-facing label (e.g. "low stamina", "high focus"). Empty string if default.</summary>
    public string Label => Level switch
    {
        1 => "very low",
        2 => "low",
        3 => "",
        4 => "high",
        5 => "very high",
        _ => ""
    };
}

/// <summary>
/// Canonical attribute ID constants — only for bootstrap defaults and migration.
/// New code should use data-driven lookups via DwarfAttributeDef.Id.
/// </summary>
public static class AttributeIds
{
    public const string Appetite    = "appetite";
    public const string Thirst      = "thirst";
    public const string Stamina     = "stamina";
    public const string Strength    = "strength";
    public const string Focus       = "focus";
    public const string Courage     = "courage";
    public const string Sociability = "sociability";

    public static readonly string[] All =
    [
        Appetite,
        Thirst,
        Stamina,
        Strength,
        Focus,
        Courage,
        Sociability,
    ];
}

/// <summary>Canonical attribute category constants.</summary>
public static class AttributeCategories
{
    public const string Physiological = "physiological";
    public const string Mental        = "mental";
    public const string Social        = "social";
}
using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Data.Defs;

/// <summary>
/// Immutable definition of a dwarf trait (e.g. Gluttony, Runner, Fit).
/// Loaded from traits.json.
/// </summary>
public sealed record TraitDef(
    string              Id,
    string              DisplayName,
    string              Description,
    string              Category,       // "physical", "mental", "phobia"
    TagSet              Tags);

/// <summary>String constants for trait IDs. No magic strings in simulation code.</summary>
public static class TraitIds
{
    public const string Gluttony    = "gluttony";
    public const string Runner      = "runner";
    public const string Fit         = "fit";
    public const string Lazy        = "lazy";
    public const string Motivated   = "motivated";
    public const string Fearful     = "fearful";
    public const string FearsWater  = "fears_water";
    public const string Sleepy      = "sleepy";
    public const string Energetic   = "energetic";
    public const string Strong      = "strong";
}

/// <summary>Canonical trait category constants.</summary>
public static class TraitCategories
{
    public const string Physical = "physical";
    public const string Mental   = "mental";
    public const string Phobia   = "phobia";
}
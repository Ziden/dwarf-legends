using System.Collections.Generic;
using DwarfFortress.GameLogic.Data;

namespace DwarfFortress.GameLogic.Data.Defs;

/// <summary>Time-based trigger condition for a world event.</summary>
public sealed record EventTrigger(
    string Type,   // e.g. "year_elapsed", "population_above", "season_is"
    IReadOnlyDictionary<string, string> Params);

/// <summary>
/// Immutable definition of a world event (migrant wave, siege, caravan, megabeast attack).
/// WorldEventManager evaluates triggers and fires effects when conditions are met.
/// </summary>
public sealed record WorldEventDef(
    string                          Id,
    string                          DisplayName,
    IReadOnlyList<EventTrigger>     Triggers,
    IReadOnlyList<EffectBlock>      Effects,
    float                           Probability  = 1.0f,
    float                           Cooldown     = 0f,    // minimum seconds between firings
    bool                            Repeatable   = true);

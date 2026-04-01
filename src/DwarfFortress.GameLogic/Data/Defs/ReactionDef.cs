using System.Collections.Generic;
using DwarfFortress.GameLogic.Data;

namespace DwarfFortress.GameLogic.Data.Defs;

/// <summary>Trigger condition for a world reaction (e.g. tile_has_fluid:magma).</summary>
public sealed record ReactionTrigger(string Type, IReadOnlyDictionary<string, string> Params);

/// <summary>
/// Immutable definition of an automated world reaction.
/// Reactions are evaluated by ReactionPipeline on each TileChangedEvent.
/// Example: magma adjacent to water → obsidian + steam.
/// </summary>
public sealed record ReactionDef(
    string                          Id,
    IReadOnlyList<ReactionTrigger>  Triggers,
    IReadOnlyList<EffectBlock>      Effects,
    float                           Probability = 1.0f);  // 0–1 chance per check

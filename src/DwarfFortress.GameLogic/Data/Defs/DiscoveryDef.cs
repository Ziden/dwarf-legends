using System;

namespace DwarfFortress.GameLogic.Data.Defs;

/// <summary>
/// Optional flavor text for discovered items. The actual discovery graph is
/// computed automatically from building constructionInputs and recipe inputs/outputs.
/// This definition only provides display names and descriptions.
/// </summary>
public sealed record DiscoveryDef(
    string Id,
    string DisplayName,
    string? Description = null,
    string? Category = null);
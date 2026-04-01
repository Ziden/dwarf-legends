using System.Collections.Generic;
using DwarfFortress.GameLogic.Data;

namespace DwarfFortress.GameLogic.Data.Defs;

/// <summary>
/// Immutable definition of a tile type (floor, wall, ramp, etc.).
/// Tile traits drive behaviour; the definition just carries static data.
/// </summary>
public sealed record TileDef(
    string   Id,
    string   DisplayName,
    TagSet   Tags,
    bool     IsPassable    = true,
    bool     IsOpaque      = false,
    bool     IsMineable    = false,
    bool     SupportsTrees = false,
    int      TilesetIndex  = 0,    // sprite index in the tileset atlas
    string?  Color         = null,
    /// <summary>Item def ID spawned when this tile is mined. Null = no drop.</summary>
    string?  DropItemDefId = null);

using System;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using DwarfFortress.GodotClient.Input;
using Godot;

namespace DwarfFortress.GodotClient.Presentation;

public enum HoverWorldTargetKind
{
    None = 0,
    RawTile = 1,
    Dwarf = 2,
    Creature = 3,
    Building = 4,
    Item = 5,
    Tree = 6,
    Plant = 7,
}

public readonly record struct HoverWorldTarget(HoverWorldTargetKind Kind, Vec3i TilePosition, int? TargetId = null)
{
    public static HoverWorldTarget None => new(HoverWorldTargetKind.None, Vec3i.Zero);

    public bool IsSemanticTarget => Kind is not HoverWorldTargetKind.None and not HoverWorldTargetKind.RawTile;

    public string DebugKey => Kind switch
    {
        HoverWorldTargetKind.None => "none",
        HoverWorldTargetKind.RawTile => $"tile:{TilePosition.X}:{TilePosition.Y}:{TilePosition.Z}",
        HoverWorldTargetKind.Dwarf => $"dwarf:{TargetId ?? -1}",
        HoverWorldTargetKind.Creature => $"creature:{TargetId ?? -1}",
        HoverWorldTargetKind.Building => $"building:{TargetId ?? -1}",
        HoverWorldTargetKind.Item => $"item:{TargetId ?? -1}",
        HoverWorldTargetKind.Tree => $"tree:{TilePosition.X}:{TilePosition.Y}:{TilePosition.Z}",
        HoverWorldTargetKind.Plant => $"plant:{TilePosition.X}:{TilePosition.Y}:{TilePosition.Z}",
        _ => "none",
    };
}

public static class HoverSelectionResolver
{
    public static HoverWorldTarget ResolvePrimaryTarget(
        WorldQuerySystem? query,
        Vector2I hoveredTile,
        int currentZ,
        HoverSelectionMode selectionMode)
    {
        var tilePosition = new Vec3i(hoveredTile.X, hoveredTile.Y, currentZ);
        if (query is null)
            return new HoverWorldTarget(HoverWorldTargetKind.RawTile, tilePosition);

        var tileResult = query.QueryTile(tilePosition);
        return selectionMode switch
        {
            HoverSelectionMode.RawTile => ResolveRawTileTarget(tileResult),
            _ => ResolveQueryTileTarget(tileResult),
        };
    }

    private static HoverWorldTarget ResolveQueryTileTarget(TileQueryResult tileResult)
    {
        var dwarf = tileResult.Dwarves
            .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault();
        if (dwarf is not null)
            return new HoverWorldTarget(HoverWorldTargetKind.Dwarf, tileResult.Position, dwarf.Id);

        var creature = tileResult.Creatures
            .OrderBy(candidate => candidate.DefId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault();
        if (creature is not null)
            return new HoverWorldTarget(HoverWorldTargetKind.Creature, tileResult.Position, creature.Id);

        if (tileResult.Building is not null)
            return new HoverWorldTarget(HoverWorldTargetKind.Building, tileResult.Position, tileResult.Building.Id);

        var item = tileResult.Items
            .OrderByDescending(candidate => candidate.Corpse is not null)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault();
        if (item is not null)
            return new HoverWorldTarget(HoverWorldTargetKind.Item, tileResult.Position, item.Id);

        return ResolveVegetationTarget(tileResult)
            ?? new HoverWorldTarget(HoverWorldTargetKind.RawTile, tileResult.Position);
    }

    private static HoverWorldTarget ResolveRawTileTarget(TileQueryResult tileResult)
        => ResolveVegetationTarget(tileResult)
            ?? new HoverWorldTarget(HoverWorldTargetKind.RawTile, tileResult.Position);

    private static HoverWorldTarget? ResolveVegetationTarget(TileQueryResult tileResult)
    {
        if (tileResult.Tile is null)
            return null;

        if (string.Equals(tileResult.Tile.TileDefId, TileDefIds.Tree, StringComparison.Ordinal))
            return new HoverWorldTarget(HoverWorldTargetKind.Tree, tileResult.Position);

        if (!string.IsNullOrWhiteSpace(tileResult.Tile.PlantDefId))
            return new HoverWorldTarget(HoverWorldTargetKind.Plant, tileResult.Position);

        return null;
    }
}

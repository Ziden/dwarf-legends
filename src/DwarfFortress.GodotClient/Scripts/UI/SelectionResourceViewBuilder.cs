using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Godot;

namespace DwarfFortress.GodotClient.UI;

internal enum SelectionResourceActionKind
{
    None,
    Mine,
    CutTree,
    HarvestPlant,
}

internal sealed record SelectionResourceGroup(
    string Id,
    string CategoryLabel,
    string Title,
    string Details,
    int TileCount,
    Texture2D? Icon,
    SelectionResourceActionKind ActionKind,
    Vec3i[] Positions);

internal sealed record SelectionResourceView(int TotalTileCount, SelectionResourceGroup[] Groups)
{
    public static SelectionResourceView Empty { get; } = new(0, Array.Empty<SelectionResourceGroup>());
}

internal static class SelectionResourceViewBuilder
{
    private static readonly Dictionary<string, int> CategoryOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Plants"] = 0,
        ["Trees"] = 1,
        ["Ore"] = 2,
        ["Mineables"] = 3,
        ["Ground"] = 4,
        ["Fluids"] = 5,
        ["Terrain"] = 6,
        ["Unknown"] = 7,
    };

    public static SelectionResourceView BuildSingleTileActionView(WorldQuerySystem query, WorldMap map, DataManager data, TileQueryResult tileResult)
    {
        if (tileResult.Tile is null)
            return SelectionResourceView.Empty;

        var groups = DescribeTileResources(map, data, tileResult.Position, tileResult.Tile)
            .Where(descriptor => descriptor.ActionKind != SelectionResourceActionKind.None)
            .Select(descriptor => new SelectionResourceGroup(
                descriptor.Id,
                descriptor.CategoryLabel,
                descriptor.Title,
                descriptor.Details,
                TileCount: 1,
                descriptor.Icon,
                descriptor.ActionKind,
                [tileResult.Position]))
            .ToArray();

        if (groups.Length == 0)
            return SelectionResourceView.Empty;

        return new SelectionResourceView(TotalTileCount: 1, Groups: groups);
    }

    public static SelectionResourceView BuildAreaView(WorldQuerySystem query, WorldMap map, DataManager data, Vector2I from, Vector2I to, int z)
    {
        var groups = new Dictionary<string, MutableSelectionResourceGroup>(StringComparer.Ordinal);
        var totalTileCount = 0;

        for (var x = Math.Min(from.X, to.X); x <= Math.Max(from.X, to.X); x++)
        for (var y = Math.Min(from.Y, to.Y); y <= Math.Max(from.Y, to.Y); y++)
        {
            var pos = new Vec3i(x, y, z);
            var tile = query.GetTileView(pos);
            if (tile is null)
                continue;

            totalTileCount++;
            foreach (var descriptor in DescribeTileResources(map, data, pos, tile))
            {
                if (!groups.TryGetValue(descriptor.Id, out var group))
                {
                    group = new MutableSelectionResourceGroup(
                        descriptor.Id,
                        descriptor.CategoryLabel,
                        descriptor.Title,
                        descriptor.Details,
                        descriptor.Icon,
                        descriptor.ActionKind);
                    groups.Add(descriptor.Id, group);
                }

                group.Positions.Add(pos);
            }
        }

        if (totalTileCount == 0)
            return SelectionResourceView.Empty;

        var resolvedGroups = groups.Values
            .Select(group => new SelectionResourceGroup(
                group.Id,
                group.CategoryLabel,
                group.Title,
                group.Details,
                group.Positions.Count,
                group.Icon,
                group.ActionKind,
                group.Positions
                    .OrderBy(pos => pos.Z)
                    .ThenBy(pos => pos.Y)
                    .ThenBy(pos => pos.X)
                    .ToArray()))
            .OrderBy(group => ResolveCategoryOrder(group.CategoryLabel))
            .ThenBy(group => group.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SelectionResourceView(totalTileCount, resolvedGroups);
    }

    public static void DispatchAction(GameSimulation simulation, SelectionResourceGroup group)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(group);

        foreach (var position in group.Positions)
        {
            switch (group.ActionKind)
            {
                case SelectionResourceActionKind.Mine:
                    simulation.Context.Commands.Dispatch(new DesignateMineCommand(position, position));
                    break;

                case SelectionResourceActionKind.CutTree:
                    simulation.Context.Commands.Dispatch(new DesignateCutTreesCommand(position, position));
                    break;

                case SelectionResourceActionKind.HarvestPlant:
                    simulation.Context.Commands.Dispatch(new DesignateHarvestCommand(position, position));
                    break;
            }
        }
    }

    public static string ResolveActionLabel(SelectionResourceActionKind actionKind) => actionKind switch
    {
        SelectionResourceActionKind.Mine => "Mine",
        SelectionResourceActionKind.CutTree => "Chop",
        SelectionResourceActionKind.HarvestPlant => "Harvest",
        _ => string.Empty,
    };

    private static SelectionResourceDescriptor[] DescribeTileResources(WorldMap map, DataManager data, Vec3i position, TileView tile)
    {
        if (!tile.IsVisible)
        {
            return
            [
                new SelectionResourceDescriptor(
                Id: "unknown",
                CategoryLabel: "Unknown",
                Title: "Unknown Tile",
                Details: "Unrevealed terrain.",
                Icon: null,
                ActionKind: SelectionResourceActionKind.None),
            ];
        }

        var descriptors = new List<SelectionResourceDescriptor>(capacity: 2);

        if (PlantHarvesting.TryGetHarvestablePlant(map, data, position, out var harvestablePlant))
        {
            descriptors.Add(new SelectionResourceDescriptor(
                Id: $"plant:{harvestablePlant.Id}:ready",
                CategoryLabel: "Plants",
                Title: harvestablePlant.DisplayName,
                Details: "Mature plant ready for harvesting.",
                Icon: PixelArtFactory.GetPlantOverlay(harvestablePlant.Id, tile.PlantGrowthStage, tile.PlantYieldLevel, tile.PlantSeedLevel),
                ActionKind: SelectionResourceActionKind.HarvestPlant));
        }

        else if (!string.IsNullOrWhiteSpace(tile.PlantDefId))
        {
            var plantDef = data.Plants.GetOrNull(tile.PlantDefId);
            descriptors.Add(new SelectionResourceDescriptor(
                Id: $"plant:{tile.PlantDefId}:growing",
                CategoryLabel: "Plants",
                Title: plantDef?.DisplayName ?? ItemTextFormatter.FormatToken(tile.PlantDefId!),
                Details: tile.PlantYieldLevel > 0 ? "Plant growth is progressing." : "Plant is not ready to harvest.",
                Icon: PixelArtFactory.GetPlantOverlay(tile.PlantDefId!, tile.PlantGrowthStage, tile.PlantYieldLevel, tile.PlantSeedLevel),
                ActionKind: SelectionResourceActionKind.None));
        }

        if (string.Equals(tile.TileDefId, DwarfFortress.GameLogic.World.TileDefIds.Tree, StringComparison.OrdinalIgnoreCase))
        {
            var speciesName = !string.IsNullOrWhiteSpace(tile.TreeSpeciesId)
                ? $"{ItemTextFormatter.FormatToken(tile.TreeSpeciesId!)} Tree"
                : "Tree";
            descriptors.Add(new SelectionResourceDescriptor(
                Id: $"tree:{tile.TreeSpeciesId ?? tile.TileDefId}",
                CategoryLabel: "Trees",
                Title: speciesName,
                Details: "Tree resource can be designated for clearing.",
                Icon: PixelArtFactory.GetTile(tile.TileDefId, tile.TreeSpeciesId ?? tile.MaterialId),
                ActionKind: SelectionResourceActionKind.CutTree));
        }

        if (!string.IsNullOrWhiteSpace(tile.OreItemDefId))
        {
            var oreDef = data.Items.GetOrNull(tile.OreItemDefId);
            var oreTitle = oreDef?.DisplayName ?? ItemTextFormatter.FormatToken(tile.OreItemDefId!);
            descriptors.Add(new SelectionResourceDescriptor(
                Id: $"ore:{tile.OreItemDefId}",
                CategoryLabel: "Ore",
                Title: oreTitle,
                Details: "Ore-bearing wall can be designated for mining.",
                Icon: PixelArtFactory.GetItem(tile.OreItemDefId!, tile.MaterialId),
                ActionKind: SelectionResourceActionKind.Mine));
        }

        if (descriptors.Count > 0)
            return descriptors.ToArray();

        var tileDef = data.Tiles.GetOrNull(tile.TileDefId);
        var title = BuildTileTitle(tile, tileDef);
        var category = ResolveTileCategory(tile, tileDef);
        var details = ResolveTileDetails(tile, tileDef);
        var actionKind = tileDef?.IsMineable == true
            ? SelectionResourceActionKind.Mine
            : SelectionResourceActionKind.None;

        return
        [
            new SelectionResourceDescriptor(
                Id: $"tile:{tile.TileDefId}:{tile.MaterialId ?? string.Empty}",
                CategoryLabel: category,
                Title: title,
                Details: details,
                Icon: PixelArtFactory.GetTile(tile.TileDefId, tile.MaterialId),
                ActionKind: actionKind),
        ];
    }

    private static string BuildTileTitle(TileView tile, TileDef? tileDef)
    {
        var tileName = tileDef?.DisplayName ?? ItemTextFormatter.FormatToken(tile.TileDefId);
        if (string.IsNullOrWhiteSpace(tile.MaterialId))
            return tileName;

        var materialName = ItemTextFormatter.FormatToken(tile.MaterialId!);
        return tileName.Contains(materialName, StringComparison.OrdinalIgnoreCase)
            ? tileName
            : $"{materialName} {tileName}";
    }

    private static string ResolveTileCategory(TileView tile, TileDef? tileDef)
    {
        if (tile.FluidLevel > 0)
            return "Fluids";
        if (tileDef?.IsMineable == true)
            return "Mineables";
        if (tile.IsPassable)
            return "Ground";
        return "Terrain";
    }

    private static string ResolveTileDetails(TileView tile, TileDef? tileDef)
    {
        if (tile.FluidLevel > 0)
            return $"Fluid depth {tile.FluidLevel}/7.";
        if (tileDef?.IsMineable == true)
            return "Mineable terrain can be designated for clearing.";
        if (tile.IsPassable)
            return "Walkable terrain.";
        return "Solid terrain.";
    }

    private static int ResolveCategoryOrder(string categoryLabel)
        => CategoryOrder.TryGetValue(categoryLabel, out var order) ? order : int.MaxValue;

    private sealed record SelectionResourceDescriptor(
        string Id,
        string CategoryLabel,
        string Title,
        string Details,
        Texture2D? Icon,
        SelectionResourceActionKind ActionKind);

    private sealed class MutableSelectionResourceGroup
    {
        public MutableSelectionResourceGroup(
            string id,
            string categoryLabel,
            string title,
            string details,
            Texture2D? icon,
            SelectionResourceActionKind actionKind)
        {
            Id = id;
            CategoryLabel = categoryLabel;
            Title = title;
            Details = details;
            Icon = icon;
            ActionKind = actionKind;
        }

        public string Id { get; }
        public string CategoryLabel { get; }
        public string Title { get; }
        public string Details { get; }
        public Texture2D? Icon { get; }
        public SelectionResourceActionKind ActionKind { get; }
        public List<Vec3i> Positions { get; } = new();
    }
}

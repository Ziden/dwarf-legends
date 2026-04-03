using System.Collections.Generic;
using System.Linq;
using System.Text;
using DwarfFortress.GameLogic.Systems;
using Godot;

namespace DwarfFortress.GodotClient.UI;


public static class TileInspectionFormatter
{
    public static string BuildHoverSummary(WorldQuerySystem query, Vector2I hovered, int z)
    {
        var labels = new List<string>();
        var result = query.QueryTile(new DwarfFortress.GameLogic.Core.Vec3i(hovered.X, hovered.Y, z));
        var tile = result.Tile;

        if (tile is not null)
        {
            AddDistinct(labels, tile.IsVisible ? FormatTileSummary(tile) : "Unknown");
            if (tile.IsDamp)
                AddDistinct(labels, "Damp wall");
            if (tile.IsWarm)
                AddDistinct(labels, "Warm wall");
            if (!string.IsNullOrWhiteSpace(tile.OreItemDefId))
                AddDistinct(labels, $"{Humanize(tile.OreItemDefId!)} vein");
            if (!string.IsNullOrWhiteSpace(tile.PlantDefId))
                AddDistinct(labels, BuildPlantSummary(tile));
        }

        foreach (var container in result.Containers.Take(2))
            AddDistinct(labels, container.Storage.StoredItemCount > 0 ? $"{container.DefId} ({container.Storage.StoredItemCount})" : container.DefId);

        foreach (var item in result.Items.Take(3))
            AddDistinct(labels, ItemTextFormatter.BuildHoverSummary(item));

        return labels.Count > 0 ? string.Join(", ", labels.Take(3)) : "-";
    }

    /// <summary>
    /// Returns a compact summary of any dwarves or creatures on the hovered tile.
    /// Returns an empty string when the tile has no occupants.
    /// </summary>
    public static string BuildHoverUnitsSummary(WorldQuerySystem query, Vector2I hovered, int z)
    {
        var parts = new List<string>();
        var result = query.QueryTile(new DwarfFortress.GameLogic.Core.Vec3i(hovered.X, hovered.Y, z));

        foreach (var dwarf in result.Dwarves)
        {
            parts.Add(dwarf.Name);
        }

        foreach (var creature in result.Creatures)
        {
            var tag = creature.IsHostile ? " (!)" : "";
            parts.Add(Humanize(creature.DefId) + tag);
        }

        return string.Join(", ", parts);
    }

    public static string BuildDetailedText(WorldQuerySystem query, Vector2I tilePos, int z)
    {
        var result = query.QueryTile(new DwarfFortress.GameLogic.Core.Vec3i(tilePos.X, tilePos.Y, z));
        return BuildDetailedText(result);
    }

    public static string BuildDetailedText(TileQueryResult result)
        => BuildTileDetailedText(result);

    public static string BuildItemDetailedText(ItemView item, TileQueryResult tileResult)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Selected Item]");
        sb.AppendLine(ItemTextFormatter.BuildContainedCardTitle(item));
        sb.AppendLine($"Item #{item.Id}");
        sb.AppendLine($"Position: ({item.Position.X}, {item.Position.Y}, z:{item.Position.Z})");

        if (item.Corpse is not null)
        {
            sb.AppendLine($"Formerly: {item.Corpse.DisplayName}");
            sb.AppendLine($"Former type: {Humanize(item.Corpse.FormerDefId)}");
            sb.AppendLine($"Cause of death: {Humanize(item.Corpse.DeathCause)}");
            sb.AppendLine($"Rot stage: {Humanize(item.Corpse.RotStage)}");
        }
        else if (!string.IsNullOrWhiteSpace(item.MaterialId))
        {
            sb.AppendLine($"Material: {Humanize(item.MaterialId!)}");
        }

        if (item.StackSize > 1)
            sb.AppendLine($"Stack size: {item.StackSize}");

        if (item.Weight > 0f)
            sb.AppendLine($"Weight: {item.Weight:F1} kg");

        sb.AppendLine($"Location: {ResolveItemLocation(item)}");

        if (item.Storage is not null)
            sb.AppendLine($"Stores: {item.Storage.StoredItemCount} item(s)");

        sb.AppendLine();
        sb.AppendLine("Tile:");
        sb.Append(BuildTileDetailedText(tileResult, item.Id));
        return sb.ToString().TrimEnd();
    }

    private static string BuildTileDetailedText(TileQueryResult result, int? excludedItemId = null)
    {
        var tile = result.Tile;
        if (tile is null)
            return $"({result.Position.X}, {result.Position.Y}, z:{result.Position.Z})\n-";

        var sb = new StringBuilder();
        if (!tile.IsVisible)
        {
            sb.AppendLine("[Unknown]");
            sb.AppendLine($"({tile.X}, {tile.Y}, z:{tile.Z})");
            sb.AppendLine("  Unrevealed stone");
            return sb.ToString();
        }

        sb.AppendLine($"[{Humanize(tile.TileDefId)}]");
        sb.AppendLine($"({tile.X}, {tile.Y}, z:{tile.Z})");
        if (tile.MaterialId is not null)
            sb.AppendLine($"  {Humanize(tile.MaterialId)}");
        if (tile.IsDamp)
            sb.AppendLine("  Damp wall");
        if (tile.IsWarm)
            sb.AppendLine("  Warm wall");
        if (tile.OreItemDefId is not null)
            sb.AppendLine($"  Ore vein: {Humanize(tile.OreItemDefId)}");
        sb.AppendLine(tile.IsPassable ? "  walkable" : "  solid");
        if (tile.IsDesignated) sb.AppendLine("  * Designated");
        if (tile.IsUnderConstruction) sb.AppendLine("  * Under construction");
        if (tile.FluidLevel > 0) sb.AppendLine($"  Water: {tile.FluidLevel}/7");
        if (tile.CoatingAmount > 0f) sb.AppendLine($"  Coating: {tile.CoatingAmount:P0}");
        if (tile.PlantDefId is not null)
            sb.AppendLine($"  Plant: {BuildPlantSummary(tile)}");

        var dwarves = result.Dwarves;
        if (dwarves.Count > 0)
        {
            sb.AppendLine("Dwarves:");
            foreach (var dwarf in dwarves)
                sb.AppendLine($"  {dwarf.Name} ({dwarf.Mood}) HP {dwarf.CurrentHealth:F0}/{dwarf.MaxHealth:F0}");
        }

        var creatures = result.Creatures;
        if (creatures.Count > 0)
        {
            sb.AppendLine("Creatures:");
            foreach (var creature in creatures)
            {
                var attitude = creature.IsHostile ? "hostile" : "neutral";
                sb.AppendLine($"  {Humanize(creature.DefId)} ({attitude}) HP {creature.CurrentHealth:F0}/{creature.MaxHealth:F0}");
            }
        }

        var items = excludedItemId is int itemId
            ? result.Items.Where(item => item.Id != itemId).ToArray()
            : result.Items.ToArray();
        if (items.Length > 0)
        {
            sb.AppendLine("Items:");
            foreach (var item in items.Take(5))
            {
                sb.AppendLine($"  {ItemTextFormatter.BuildDetailedInspection(item)}");

                if (item.Storage?.StoredItemCount > 0)
                {
                    foreach (var contained in item.Storage.Contents.Take(4))
                        sb.AppendLine($"    contains: {ItemTextFormatter.BuildDetailedInspection(contained)}");

                    if (item.Storage.StoredItemCount > 4)
                        sb.AppendLine($"    +{item.Storage.StoredItemCount - 4} more inside...");
                }
            }

            if (items.Length > 5)
                sb.AppendLine($"  +{items.Length - 5} more...");
        }

        var containers = result.Containers;
        if (containers.Count > 0)
        {
            sb.AppendLine("Containers:");
            foreach (var container in containers)
                sb.AppendLine($"  {Humanize(container.DefId)} [{container.Storage.StoredItemCount}/{container.Storage.Capacity}]");
        }

        var stockpile = result.Stockpile;
        if (stockpile is not null)
            sb.AppendLine($"Stockpile #{stockpile.Id} [{string.Join(", ", stockpile.AcceptedTags)}]");

        return sb.ToString();
    }

    private static string ResolveItemLocation(ItemView item)
    {
        if (item.CarriedByEntityId >= 0)
            return $"Carried by entity #{item.CarriedByEntityId}";

        if (item.ContainerBuildingId >= 0)
            return $"Stored in building #{item.ContainerBuildingId}";

        if (item.ContainerItemId >= 0)
            return $"Inside container #{item.ContainerItemId}";

        if (item.StockpileId >= 0)
            return $"On ground in stockpile #{item.StockpileId}";

        return "On ground";
    }

    private static void AddDistinct(ICollection<string> labels, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return;

        var value = Humanize(rawValue);
        if (labels.Contains(value))
            return;

        labels.Add(value);
    }

    private static string FormatTileSummary(TileView tile)
    {
        if (!string.IsNullOrWhiteSpace(tile.PlantDefId) && tile.PlantGrowthStage >= 2)
            return $"{Humanize(tile.PlantDefId!)} on {Humanize(tile.TileDefId)}";

        if (!string.IsNullOrWhiteSpace(tile.MaterialId))
            return Humanize(tile.MaterialId!);

        return Humanize(tile.TileDefId);
    }

    private static string BuildPlantSummary(TileView tile)
    {
        if (string.IsNullOrWhiteSpace(tile.PlantDefId))
            return string.Empty;

        var stage = tile.PlantGrowthStage switch
        {
            0 => "seed",
            1 => "sprout",
            2 => "young",
            _ => "mature",
        };

        var suffix = tile.PlantYieldLevel > 0 ? "yielding" : tile.PlantSeedLevel > 0 ? "seeded" : stage;
        return $"{Humanize(tile.PlantDefId!)} ({suffix})";
    }

    private static string Humanize(string rawValue) => ItemTextFormatter.Humanize(rawValue);
}

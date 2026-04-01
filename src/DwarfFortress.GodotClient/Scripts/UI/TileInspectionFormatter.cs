using System.Collections.Generic;
using System.Linq;
using System.Text;
using DwarfFortress.GameLogic.Systems;
using Godot;

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
            if (!string.IsNullOrWhiteSpace(tile.OreItemDefId))
                AddDistinct(labels, $"{Humanize(tile.OreItemDefId!)} vein");
            if (!string.IsNullOrWhiteSpace(tile.PlantDefId))
                AddDistinct(labels, BuildPlantSummary(tile));
        }

        foreach (var item in result.Items.Take(3))
            AddDistinct(labels, FormatItemSummary(item));

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
        var tile = result.Tile;
        if (tile is null)
            return $"({tilePos.X}, {tilePos.Y}, z:{z})\n-";

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

        var items = result.Items;
        if (items.Count > 0)
        {
            sb.AppendLine("Items:");
            foreach (var item in items.Take(5))
            {
                sb.AppendLine($"  {FormatDetailedItem(item)}");

                if (item.Corpse?.Contents.Length > 0)
                {
                    foreach (var contained in item.Corpse.Contents.Take(4))
                        sb.AppendLine($"    contains: {FormatDetailedItem(contained)}");

                    if (item.Corpse.Contents.Length > 4)
                        sb.AppendLine($"    +{item.Corpse.Contents.Length - 4} more inside...");
                }
            }

            if (items.Count > 5)
                sb.AppendLine($"  +{items.Count - 5} more...");
        }

        var stockpile = result.Stockpile;
        if (stockpile is not null)
            sb.AppendLine($"Stockpile #{stockpile.Id} [{string.Join(", ", stockpile.AcceptedTags)}]");

        return sb.ToString();
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

    private static string FormatItemSummary(ItemView item)
    {
        if (item.Corpse is not null)
            return $"{item.Corpse.DisplayName} corpse ({item.Corpse.RotStage})";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.MaterialId))
            parts.Add(Humanize(item.MaterialId!));

        parts.Add(Humanize(item.DefId));
        return string.Join(" ", parts);
    }

    private static string FormatDetailedItem(ItemView item)
    {
        if (item.Corpse is not null)
            return $"{item.Corpse.DisplayName} corpse [{item.Corpse.RotStage}, rot {item.Corpse.RotProgress:P0}, died of {Humanize(item.Corpse.DeathCause)}]";

        var mat = item.MaterialId is not null ? $" ({Humanize(item.MaterialId)})" : "";
        var qty = item.StackSize > 1 ? $" x{item.StackSize}" : "";
        return $"{Humanize(item.DefId)}{mat}{qty}";
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

    private static string Humanize(string rawValue) => rawValue.Replace('_', ' ');
}

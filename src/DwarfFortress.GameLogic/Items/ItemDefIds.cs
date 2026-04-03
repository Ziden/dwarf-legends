using System;

namespace DwarfFortress.GameLogic.Items;

/// <summary>
/// Constants for item definition IDs used across systems and strategies.
/// </summary>
public static class ItemDefIds
{
    public const string GraniteBoulder  = "granite_boulder";
    public const string LimestoneBoulder = "limestone_boulder";
    public const string SandstoneBoulder = "sandstone_boulder";
    public const string BasaltBoulder   = "basalt_boulder";
    public const string ShaleBoulder    = "shale_boulder";
    public const string SlateBoulder    = "slate_boulder";
    public const string MarbleBoulder   = "marble_boulder";
    public const string Log             = "log";
    public const string Plank           = "plank";
    public const string IronOre         = "iron_ore";
    public const string IronBar         = "iron_bar";
    public const string CopperOre       = "copper_ore";
    public const string CopperBar       = "copper_bar";
    public const string CoalOre         = "coal_ore";
    public const string TinOre          = "tin_ore";
    public const string TinBar          = "tin_bar";
    public const string SilverOre       = "silver_ore";
    public const string SilverBar       = "silver_bar";
    public const string GoldOre         = "gold_ore";
    public const string GoldBar         = "gold_bar";
    public const string Bed             = "bed";
    public const string Table           = "table";
    public const string Chair           = "chair";
    public const string Barrel          = "barrel";
    public const string Bucket          = "bucket";
    public const string Meal            = "meal";
    public const string Drink           = "drink";
    public const string Seed            = "seed";
    public const string PlantMatter     = "plant_matter";
    public const string RawMeat         = "raw_meat";
    public const string BerryCluster    = "berry_cluster";
    public const string SunrootBulb     = "sunroot_bulb";
    public const string StoneTuber      = "stone_tuber";
    public const string MarshReedShoot  = "marsh_reed_shoot";
    public const string Apple           = "apple";
    public const string Fig             = "fig";
    public const string SunrootSeed     = "sunroot_seed";
    public const string StoneTuberSeed  = "stone_tuber_seed";
    public const string MarshReedSeed   = "marsh_reed_seed";
    public const string AppleSeed       = "apple_seed";
    public const string FigSeed         = "fig_seed";
    public const string Leather         = "leather";
    public const string Cloth           = "cloth";
    public const string Bone            = "bone";
    public const string Corpse          = "corpse";
    public const string Box             = "box";

    public static string? ResolveStoneBoulder(string? materialId)
    {
        if (string.IsNullOrWhiteSpace(materialId))
            return null;

        return materialId.Trim().ToLowerInvariant() switch
        {
            "granite" => GraniteBoulder,
            "limestone" => LimestoneBoulder,
            "sandstone" => SandstoneBoulder,
            "basalt" => BasaltBoulder,
            "shale" => ShaleBoulder,
            "slate" => SlateBoulder,
            "marble" => MarbleBoulder,
            _ => null,
        };
    }
}

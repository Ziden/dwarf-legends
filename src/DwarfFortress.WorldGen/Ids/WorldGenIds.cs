using System;
using System.Collections.Generic;

namespace DwarfFortress.WorldGen.Ids;

public static class MacroBiomeIds
{
    public const string TemperatePlains = "temperate_plains";
    public const string ConiferForest = "conifer_forest";
    public const string Highland = "highland";
    public const string MistyMarsh = "misty_marsh";
    public const string WindsweptSteppe = "windswept_steppe";
    public const string OceanShallow = "ocean_shallow";
    public const string OceanDeep = "ocean_deep";
    public const string TropicalRainforest = "tropical_rainforest";
    public const string Savanna = "savanna";
    public const string Desert = "desert";
    public const string Tundra = "tundra";
    public const string BorealForest = "boreal_forest";
    public const string IcePlains = "ice_plains";

    public static IReadOnlyList<string> All { get; } =
    [
        TemperatePlains,
        ConiferForest,
        Highland,
        MistyMarsh,
        WindsweptSteppe,
        OceanShallow,
        OceanDeep,
        TropicalRainforest,
        Savanna,
        Desert,
        Tundra,
        BorealForest,
        IcePlains,
    ];

    private static readonly HashSet<string> Known = new(All, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string? biomeId)
        => !string.IsNullOrWhiteSpace(biomeId) && Known.Contains(biomeId);

    public static bool IsOcean(string? biomeId)
        => string.Equals(biomeId, OceanShallow, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(biomeId, OceanDeep, StringComparison.OrdinalIgnoreCase);
}

public static class GeologyProfileIds
{
    public const string MixedBedrock = "mixed_bedrock";
    public const string IgneousUplift = "igneous_uplift";
    public const string SedimentaryWetlands = "sedimentary_wetlands";
    public const string AlluvialBasin = "alluvial_basin";
    public const string MetamorphicSpine = "metamorphic_spine";
}

public static class RockTypeIds
{
    public const string Granite = "granite";
    public const string Limestone = "limestone";
    public const string Sandstone = "sandstone";
    public const string Basalt = "basalt";
    public const string Shale = "shale";
    public const string Slate = "slate";
    public const string Marble = "marble";
}

public static class OreIds
{
    public const string IronOre = "iron_ore";
    public const string CopperOre = "copper_ore";
    public const string CoalOre = "coal_ore";
    public const string TinOre = "tin_ore";
    public const string SilverOre = "silver_ore";
    public const string GoldOre = "gold_ore";
}

public static class TreeSpeciesIds
{
    public const string Oak = "oak";
    public const string Pine = "pine";
    public const string Spruce = "spruce";
    public const string Birch = "birch";
    public const string Willow = "willow";
    public const string Palm = "palm";
    public const string Baobab = "baobab";
    public const string Apple = "apple";
    public const string Fig = "fig";
    public const string Deadwood = "deadwood";
}

public static class PlantSpeciesIds
{
    public const string BerryBush = "berry_bush";
    public const string Sunroot = "sunroot";
    public const string StoneTuber = "stone_tuber";
    public const string MarshReed = "marsh_reed";
    public const string AppleCanopy = "apple_canopy";
    public const string FigCanopy = "fig_canopy";
}

public static class GeneratedPlantGrowthStages
{
    public const byte Seed = 0;
    public const byte Sprout = 1;
    public const byte Young = 2;
    public const byte Mature = 3;
}

public static class CreatureDefIds
{
    public const string Cat = "cat";
    public const string Dog = "dog";
    public const string Elk = "elk";
    public const string GiantCarp = "giant_carp";
    public const string Goblin = "goblin";
    public const string Troll = "troll";
}

public static class RegionBiomeVariantIds
{
    public const string DenseConifer = "dense_conifer";
    public const string ConiferWoodland = "conifer_woodland";
    public const string RockyConiferEdge = "rocky_conifer_edge";

    public const string AlpineRidge = "alpine_ridge";
    public const string HighlandFoothills = "highland_foothills";

    public const string FloodplainMarsh = "floodplain_marsh";
    public const string ReedMarsh = "reed_marsh";

    public const string TropicalCanopy = "tropical_canopy";
    public const string TropicalLowland = "tropical_lowland";
    public const string SavannaGrassland = "savanna_grassland";
    public const string AridBadlands = "arid_badlands";
    public const string PolarTundra = "polar_tundra";
    public const string GlacialField = "glacial_field";

    public const string DrySteppe = "dry_steppe";
    public const string SteppeScrub = "steppe_scrub";

    public const string TemperateWoodland = "temperate_woodland";
    public const string TemperatePlainsOpen = "temperate_plains_open";
    public const string MeadowPlain = "meadow_plain";
    public const string CoastalShallows = "coastal_shallows";
    public const string OpenOcean = "open_ocean";
    public const string RockyHighland = "rocky_highland";
    public const string BoggyFen = "boggy_fen";
    public const string SparseSteppe = "sparse_steppe";
    public const string ForestedFoothills = "forested_foothills";
    public const string RiverValley = "river_valley";

    public static bool IsConiferVariant(string? variantId)
        => EqualsVariant(variantId, DenseConifer) ||
           EqualsVariant(variantId, ConiferWoodland) ||
           EqualsVariant(variantId, RockyConiferEdge);

    public static bool IsHighlandVariant(string? variantId)
        => EqualsVariant(variantId, AlpineRidge) ||
           EqualsVariant(variantId, HighlandFoothills);

    public static bool IsMarshVariant(string? variantId)
        => EqualsVariant(variantId, FloodplainMarsh) ||
           EqualsVariant(variantId, ReedMarsh);

    public static bool IsSteppeVariant(string? variantId)
        => EqualsVariant(variantId, DrySteppe) ||
           EqualsVariant(variantId, SteppeScrub) ||
           EqualsVariant(variantId, SavannaGrassland);

    public static bool IsRockyVariant(string? variantId)
        => EqualsVariant(variantId, AlpineRidge) ||
           EqualsVariant(variantId, RockyConiferEdge) ||
           EqualsVariant(variantId, AridBadlands);

    public static bool IsOceanVariant(string? variantId)
        => EqualsVariant(variantId, CoastalShallows) ||
           EqualsVariant(variantId, OpenOcean);

    public static string ResolveMacroBiomeId(string? variantId)
    {
        if (IsConiferVariant(variantId))
            return MacroBiomeIds.ConiferForest;
        if (EqualsVariant(variantId, TropicalCanopy) || EqualsVariant(variantId, TropicalLowland))
            return MacroBiomeIds.TropicalRainforest;
        if (EqualsVariant(variantId, SavannaGrassland))
            return MacroBiomeIds.Savanna;
        if (EqualsVariant(variantId, AridBadlands))
            return MacroBiomeIds.Desert;
        if (EqualsVariant(variantId, PolarTundra))
            return MacroBiomeIds.Tundra;
        if (EqualsVariant(variantId, GlacialField))
            return MacroBiomeIds.IcePlains;
        if (IsHighlandVariant(variantId))
            return MacroBiomeIds.Highland;
        if (EqualsVariant(variantId, ForestedFoothills))
            return MacroBiomeIds.Highland;
        if (EqualsVariant(variantId, RockyHighland))
            return MacroBiomeIds.Highland;
        if (IsMarshVariant(variantId))
            return MacroBiomeIds.MistyMarsh;
        if (EqualsVariant(variantId, BoggyFen))
            return MacroBiomeIds.MistyMarsh;
        if (IsSteppeVariant(variantId))
            return MacroBiomeIds.WindsweptSteppe;
        if (EqualsVariant(variantId, SparseSteppe))
            return MacroBiomeIds.WindsweptSteppe;
        if (EqualsVariant(variantId, MeadowPlain))
            return MacroBiomeIds.TemperatePlains;
        if (EqualsVariant(variantId, RiverValley))
            return MacroBiomeIds.TemperatePlains;
        if (EqualsVariant(variantId, TemperateWoodland))
            return MacroBiomeIds.TemperatePlains;
        if (EqualsVariant(variantId, TemperatePlainsOpen))
            return MacroBiomeIds.TemperatePlains;
        if (EqualsVariant(variantId, CoastalShallows))
            return MacroBiomeIds.OceanShallow;
        if (EqualsVariant(variantId, OpenOcean))
            return MacroBiomeIds.OceanDeep;
        return MacroBiomeIds.TemperatePlains;
    }

    private static bool EqualsVariant(string? left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

public static class RegionSurfaceClassIds
{
    public const string Grass = "grass";
    public const string Soil = "soil";
    public const string Sand = "sand";
    public const string Mud = "mud";
    public const string Snow = "snow";
    public const string Stone = "stone";

    public static IReadOnlyList<string> All { get; } =
    [
        Grass,
        Soil,
        Sand,
        Mud,
        Snow,
        Stone,
    ];

    private static readonly HashSet<string> Known = new(All, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string? surfaceClassId)
        => !string.IsNullOrWhiteSpace(surfaceClassId) && Known.Contains(surfaceClassId);
}

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

public static class FactionUnitRoleIds
{
    public const string CivilizedPrimary = "civilized_primary";
    public const string HostilePrimary = "hostile_primary";
    public const string HostileAlternate = "hostile_alternate";
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

    private static readonly HashSet<string> ConiferVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        DenseConifer,
        ConiferWoodland,
        RockyConiferEdge,
    };

    private static readonly HashSet<string> HighlandVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        AlpineRidge,
        HighlandFoothills,
    };

    private static readonly HashSet<string> MarshVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        FloodplainMarsh,
        ReedMarsh,
    };

    private static readonly HashSet<string> SteppeVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        DrySteppe,
        SteppeScrub,
        SavannaGrassland,
    };

    private static readonly HashSet<string> RockyVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        AlpineRidge,
        RockyConiferEdge,
        AridBadlands,
    };

    private static readonly HashSet<string> OceanVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        CoastalShallows,
        OpenOcean,
    };

    private static readonly Dictionary<string, string> MacroBiomeByVariant = new(StringComparer.OrdinalIgnoreCase)
    {
        [DenseConifer] = MacroBiomeIds.ConiferForest,
        [ConiferWoodland] = MacroBiomeIds.ConiferForest,
        [RockyConiferEdge] = MacroBiomeIds.ConiferForest,
        [TropicalCanopy] = MacroBiomeIds.TropicalRainforest,
        [TropicalLowland] = MacroBiomeIds.TropicalRainforest,
        [SavannaGrassland] = MacroBiomeIds.Savanna,
        [AridBadlands] = MacroBiomeIds.Desert,
        [PolarTundra] = MacroBiomeIds.Tundra,
        [GlacialField] = MacroBiomeIds.IcePlains,
        [AlpineRidge] = MacroBiomeIds.Highland,
        [HighlandFoothills] = MacroBiomeIds.Highland,
        [ForestedFoothills] = MacroBiomeIds.Highland,
        [RockyHighland] = MacroBiomeIds.Highland,
        [FloodplainMarsh] = MacroBiomeIds.MistyMarsh,
        [ReedMarsh] = MacroBiomeIds.MistyMarsh,
        [BoggyFen] = MacroBiomeIds.MistyMarsh,
        [DrySteppe] = MacroBiomeIds.WindsweptSteppe,
        [SteppeScrub] = MacroBiomeIds.WindsweptSteppe,
        [SparseSteppe] = MacroBiomeIds.WindsweptSteppe,
        [MeadowPlain] = MacroBiomeIds.TemperatePlains,
        [RiverValley] = MacroBiomeIds.TemperatePlains,
        [TemperateWoodland] = MacroBiomeIds.TemperatePlains,
        [TemperatePlainsOpen] = MacroBiomeIds.TemperatePlains,
        [CoastalShallows] = MacroBiomeIds.OceanShallow,
        [OpenOcean] = MacroBiomeIds.OceanDeep,
    };

    private static readonly Dictionary<string, float> TreeDensityBiasByVariant = new(StringComparer.OrdinalIgnoreCase)
    {
        [DenseConifer] = 0.15f,
        [ForestedFoothills] = 0.15f,
        [ConiferWoodland] = 0.10f,
        [TemperateWoodland] = 0.10f,
        [RiverValley] = 0.08f,
        [FloodplainMarsh] = 0.08f,
        [TropicalCanopy] = 0.18f,
        [TropicalLowland] = 0.12f,
        [SavannaGrassland] = -0.05f,
        [DrySteppe] = -0.12f,
        [SparseSteppe] = -0.12f,
        [AridBadlands] = -0.20f,
        [AlpineRidge] = -0.08f,
        [RockyHighland] = -0.08f,
        [PolarTundra] = -0.16f,
        [GlacialField] = -0.24f,
    };

    public static bool IsConiferVariant(string? variantId)
        => ContainsVariant(variantId, ConiferVariants);

    public static bool IsHighlandVariant(string? variantId)
        => ContainsVariant(variantId, HighlandVariants);

    public static bool IsMarshVariant(string? variantId)
        => ContainsVariant(variantId, MarshVariants);

    public static bool IsSteppeVariant(string? variantId)
        => ContainsVariant(variantId, SteppeVariants);

    public static bool IsRockyVariant(string? variantId)
        => ContainsVariant(variantId, RockyVariants);

    public static bool IsOceanVariant(string? variantId)
        => ContainsVariant(variantId, OceanVariants);

    public static string ResolveMacroBiomeId(string? variantId)
        => !string.IsNullOrWhiteSpace(variantId) && MacroBiomeByVariant.TryGetValue(variantId, out var macroBiomeId)
            ? macroBiomeId
            : MacroBiomeIds.TemperatePlains;

    public static float ResolveTreeDensityBias(string? variantId)
        => !string.IsNullOrWhiteSpace(variantId) && TreeDensityBiasByVariant.TryGetValue(variantId, out var bias)
            ? bias
            : 0f;

    private static bool ContainsVariant(string? variantId, HashSet<string> variants)
        => !string.IsNullOrWhiteSpace(variantId) && variants.Contains(variantId);
}

public static class HistoricalEventTypeIds
{
    public const string Treaty = "treaty";
    public const string Raid = "raid";
    public const string Founding = "founding";
    public const string Skirmish = "skirmish";
    public const string Crisis = "crisis";
}

public static class SiteKindIds
{
    public const string Fortress = "fortress";
    public const string Hamlet = "hamlet";
    public const string Ruin = "ruin";
    public const string Shrine = "shrine";
    public const string Cave = "cave";
    public const string Watchtower = "watchtower";
    public const string Capital = "capital";
    public const string City = "city";
    public const string Town = "town";
    public const string Village = "village";
    public const string Camp = "camp";
    public const string Settlement = "settlement";
    public const string WatchFragment = "watch";
    public const string MineFragment = "mine";

    public static bool IsMajorSettlementKind(string? siteKind)
        => ContainsAny(siteKind, Fortress, City, Capital, Town);

    public static bool IsMinorSettlementKind(string? siteKind)
        => ContainsAny(siteKind, Hamlet, Village, Camp);

    public static bool HasRiparianSettlementSemantics(string? siteKind)
        => ContainsAny(siteKind, Fortress, Hamlet, Shrine);

    public static bool HasGarrisonSemantics(string? siteKind)
        => ContainsAny(siteKind, WatchFragment, Fortress, Cave);

    public static bool HasAgrarianSemantics(string? siteKind)
        => ContainsAny(siteKind, Hamlet, Village, Shrine);

    public static bool HasMiningSemantics(string? siteKind)
        => ContainsAny(siteKind, Cave, MineFragment, Ruin);

    private static bool ContainsAny(string? value, params string[] fragments)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var fragment in fragments)
        {
            if (value.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
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

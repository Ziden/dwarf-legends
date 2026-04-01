using System;
using System.Collections.Generic;
using DwarfFortress.WorldGen.Ids;

namespace DwarfFortress.WorldGen.Geology;

public static class MineralVeinRegistry
{
    private static readonly MineralVeinDef[] MixedBedrock =
    [
        new(OreIds.IronOre, VeinShape.Vein, Frequency: 0.32f, RequiredRockType: RockTypeIds.Granite, SizeMin: 10, SizeMax: 24),
        new(OreIds.CopperOre, VeinShape.Cluster, Frequency: 0.24f, RequiredRockType: RockTypeIds.Limestone, SizeMin: 8, SizeMax: 20),
        new(OreIds.CoalOre, VeinShape.Layer, Frequency: 0.28f, RequiredRockType: RockTypeIds.Sandstone, SizeMin: 18, SizeMax: 36),
        new(OreIds.TinOre, VeinShape.Scattered, Frequency: 0.18f, RequiredRockType: RockTypeIds.Slate, SizeMin: 8, SizeMax: 18),
        new(OreIds.SilverOre, VeinShape.Vein, Frequency: 0.12f, RequiredRockType: RockTypeIds.Granite, SizeMin: 8, SizeMax: 18),
    ];

    private static readonly MineralVeinDef[] IgneousUplift =
    [
        new(OreIds.IronOre, VeinShape.Vein, Frequency: 0.44f, RequiredRockType: RockTypeIds.Granite, SizeMin: 14, SizeMax: 28),
        new(OreIds.IronOre, VeinShape.Cluster, Frequency: 0.20f, RequiredRockType: RockTypeIds.Basalt, SizeMin: 10, SizeMax: 24),
        new(OreIds.CopperOre, VeinShape.Scattered, Frequency: 0.16f, RequiredRockType: RockTypeIds.Basalt, SizeMin: 8, SizeMax: 16),
        new(OreIds.GoldOre, VeinShape.Vein, Frequency: 0.10f, RequiredRockType: RockTypeIds.Basalt, SizeMin: 8, SizeMax: 16),
    ];

    private static readonly MineralVeinDef[] SedimentaryWetlands =
    [
        new(OreIds.CoalOre, VeinShape.Layer, Frequency: 0.42f, RequiredRockType: RockTypeIds.Sandstone, SizeMin: 20, SizeMax: 40),
        new(OreIds.CoalOre, VeinShape.Layer, Frequency: 0.24f, RequiredRockType: RockTypeIds.Shale, SizeMin: 18, SizeMax: 30),
        new(OreIds.CopperOre, VeinShape.Cluster, Frequency: 0.20f, RequiredRockType: RockTypeIds.Limestone, SizeMin: 8, SizeMax: 18),
        new(OreIds.TinOre, VeinShape.Scattered, Frequency: 0.16f, RequiredRockType: RockTypeIds.Shale, SizeMin: 8, SizeMax: 18),
    ];

    private static readonly MineralVeinDef[] AlluvialBasin =
    [
        new(OreIds.CoalOre, VeinShape.Layer, Frequency: 0.36f, RequiredRockType: RockTypeIds.Sandstone, SizeMin: 18, SizeMax: 36),
        new(OreIds.CopperOre, VeinShape.Cluster, Frequency: 0.26f, RequiredRockType: RockTypeIds.Limestone, SizeMin: 10, SizeMax: 24),
        new(OreIds.TinOre, VeinShape.Scattered, Frequency: 0.24f, RequiredRockType: RockTypeIds.Shale, SizeMin: 10, SizeMax: 20),
        new(OreIds.IronOre, VeinShape.Scattered, Frequency: 0.18f, RequiredRockType: RockTypeIds.Granite, SizeMin: 10, SizeMax: 22),
        new(OreIds.SilverOre, VeinShape.Cluster, Frequency: 0.10f, RequiredRockType: RockTypeIds.Limestone, SizeMin: 8, SizeMax: 14),
    ];

    private static readonly MineralVeinDef[] MetamorphicSpine =
    [
        new(OreIds.IronOre, VeinShape.Vein, Frequency: 0.40f, RequiredRockType: RockTypeIds.Granite, SizeMin: 14, SizeMax: 30),
        new(OreIds.CopperOre, VeinShape.Scattered, Frequency: 0.16f, RequiredRockType: RockTypeIds.Slate, SizeMin: 10, SizeMax: 20),
        new(OreIds.CoalOre, VeinShape.Layer, Frequency: 0.12f, RequiredRockType: RockTypeIds.Slate, SizeMin: 12, SizeMax: 24),
        new(OreIds.SilverOre, VeinShape.Vein, Frequency: 0.24f, RequiredRockType: RockTypeIds.Marble, SizeMin: 10, SizeMax: 20),
        new(OreIds.GoldOre, VeinShape.Cluster, Frequency: 0.10f, RequiredRockType: RockTypeIds.Marble, SizeMin: 8, SizeMax: 16),
    ];

    public static IReadOnlyList<MineralVeinDef> Resolve(string? geologyProfileId)
    {
        return geologyProfileId switch
        {
            GeologyProfileIds.IgneousUplift => IgneousUplift,
            GeologyProfileIds.SedimentaryWetlands => SedimentaryWetlands,
            GeologyProfileIds.AlluvialBasin => AlluvialBasin,
            GeologyProfileIds.MetamorphicSpine => MetamorphicSpine,
            GeologyProfileIds.MixedBedrock => MixedBedrock,
            _ => MixedBedrock,
        };
    }

    public static bool IsOreCompatible(string oreId, string? rockTypeId)
    {
        if (string.IsNullOrWhiteSpace(rockTypeId))
            return false;

        return oreId switch
        {
            OreIds.IronOre => IsAnyRock(rockTypeId, RockTypeIds.Granite, RockTypeIds.Basalt, RockTypeIds.Slate),
            OreIds.CopperOre => IsAnyRock(rockTypeId, RockTypeIds.Limestone, RockTypeIds.Sandstone, RockTypeIds.Shale),
            OreIds.CoalOre => IsAnyRock(rockTypeId, RockTypeIds.Sandstone, RockTypeIds.Shale, RockTypeIds.Slate),
            OreIds.TinOre => IsAnyRock(rockTypeId, RockTypeIds.Shale, RockTypeIds.Slate, RockTypeIds.Limestone),
            OreIds.SilverOre => IsAnyRock(rockTypeId, RockTypeIds.Granite, RockTypeIds.Marble, RockTypeIds.Limestone),
            OreIds.GoldOre => IsAnyRock(rockTypeId, RockTypeIds.Basalt, RockTypeIds.Marble, RockTypeIds.Granite),
            _ => false,
        };
    }

    private static bool IsAnyRock(string rockTypeId, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.Equals(rockTypeId, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

using System;
using System.Collections.Generic;
using DwarfFortress.WorldGen.Ids;

namespace DwarfFortress.WorldGen.Geology;

public static class StrataProfileRegistry
{
    private static readonly IReadOnlyDictionary<string, StrataProfile> Profiles =
        new Dictionary<string, StrataProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [GeologyProfileIds.MixedBedrock] = new StrataProfile(
                GeologyProfileId: GeologyProfileIds.MixedBedrock,
                SeedSalt: 101,
                Layers:
                [
                    new StrataLayer(RockTypeIds.Sandstone, ThicknessMin: 2, ThicknessMax: 3),
                    new StrataLayer(RockTypeIds.Limestone, ThicknessMin: 2, ThicknessMax: 3),
                    new StrataLayer(RockTypeIds.Slate, ThicknessMin: 2, ThicknessMax: 3),
                    new StrataLayer(RockTypeIds.Granite, ThicknessMin: 3, ThicknessMax: 5),
                ],
                AquiferDepthFraction: 0.18f),

            [GeologyProfileIds.IgneousUplift] = new StrataProfile(
                GeologyProfileId: GeologyProfileIds.IgneousUplift,
                SeedSalt: 211,
                Layers:
                [
                    new StrataLayer(RockTypeIds.Basalt, ThicknessMin: 3, ThicknessMax: 5),
                    new StrataLayer(RockTypeIds.Granite, ThicknessMin: 2, ThicknessMax: 4),
                    new StrataLayer(RockTypeIds.Basalt, ThicknessMin: 1, ThicknessMax: 2),
                ],
                AquiferDepthFraction: 0f),

            [GeologyProfileIds.SedimentaryWetlands] = new StrataProfile(
                GeologyProfileId: GeologyProfileIds.SedimentaryWetlands,
                SeedSalt: 307,
                Layers:
                [
                    new StrataLayer(RockTypeIds.Sandstone, ThicknessMin: 3, ThicknessMax: 5),
                    new StrataLayer(RockTypeIds.Shale, ThicknessMin: 2, ThicknessMax: 3),
                    new StrataLayer(RockTypeIds.Limestone, ThicknessMin: 2, ThicknessMax: 4),
                ],
                AquiferDepthFraction: 0.22f),

            [GeologyProfileIds.AlluvialBasin] = new StrataProfile(
                GeologyProfileId: GeologyProfileIds.AlluvialBasin,
                SeedSalt: 401,
                Layers:
                [
                    new StrataLayer(RockTypeIds.Sandstone, ThicknessMin: 2, ThicknessMax: 4),
                    new StrataLayer(RockTypeIds.Limestone, ThicknessMin: 2, ThicknessMax: 3),
                    new StrataLayer(RockTypeIds.Shale, ThicknessMin: 2, ThicknessMax: 3),
                    new StrataLayer(RockTypeIds.Granite, ThicknessMin: 2, ThicknessMax: 4),
                ],
                AquiferDepthFraction: 0.26f),

            [GeologyProfileIds.MetamorphicSpine] = new StrataProfile(
                GeologyProfileId: GeologyProfileIds.MetamorphicSpine,
                SeedSalt: 503,
                Layers:
                [
                    new StrataLayer(RockTypeIds.Marble, ThicknessMin: 2, ThicknessMax: 3),
                    new StrataLayer(RockTypeIds.Slate, ThicknessMin: 3, ThicknessMax: 5),
                    new StrataLayer(RockTypeIds.Granite, ThicknessMin: 1, ThicknessMax: 2),
                ],
                AquiferDepthFraction: 0.10f),
        };

    public static StrataProfile Resolve(string? geologyProfileId)
    {
        if (!string.IsNullOrWhiteSpace(geologyProfileId) &&
            Profiles.TryGetValue(geologyProfileId, out var profile))
        {
            return profile;
        }

        return Profiles[GeologyProfileIds.MixedBedrock];
    }
}

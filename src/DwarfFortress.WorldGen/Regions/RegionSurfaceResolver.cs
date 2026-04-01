using System;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Maps;

namespace DwarfFortress.WorldGen.Regions;

public static class RegionSurfaceResolver
{
    public static string ResolveSurfaceClassId(
        string macroBiomeId,
        string biomeVariantId,
        float slopeNorm,
        bool hasRiver,
        bool hasLake,
        float moistureBand,
        float groundwater,
        float temperatureBand,
        float soilDepth)
    {
        var isAridMacroBiome =
            string.Equals(macroBiomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(macroBiomeId, MacroBiomeIds.WindsweptSteppe, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(macroBiomeId, MacroBiomeIds.Savanna, StringComparison.OrdinalIgnoreCase);

        if (MacroBiomeIds.IsOcean(macroBiomeId))
            return hasRiver || hasLake || groundwater >= 0.64f ? RegionSurfaceClassIds.Mud : RegionSurfaceClassIds.Sand;

        if (string.Equals(macroBiomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase))
            return hasRiver || hasLake || groundwater >= 0.58f ? RegionSurfaceClassIds.Mud : RegionSurfaceClassIds.Sand;

        if (string.Equals(macroBiomeId, MacroBiomeIds.Tundra, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(macroBiomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase))
        {
            if (hasRiver || hasLake || groundwater >= 0.58f)
                return RegionSurfaceClassIds.Soil;
            return RegionSurfaceClassIds.Snow;
        }

        if (RegionBiomeVariantIds.IsMarshVariant(biomeVariantId))
            return RegionSurfaceClassIds.Mud;

        if (RegionBiomeVariantIds.IsRockyVariant(biomeVariantId) ||
            (slopeNorm >= 0.72f && soilDepth <= 0.42f))
        {
            if (groundwater >= 0.62f || (hasRiver && moistureBand >= 0.52f))
                return RegionSurfaceClassIds.Soil;
            return RegionSurfaceClassIds.Stone;
        }

        if (hasRiver || hasLake)
        {
            if (groundwater >= 0.66f || moistureBand >= 0.64f)
                return RegionSurfaceClassIds.Mud;
            if (groundwater >= 0.46f || moistureBand >= 0.45f)
                return RegionSurfaceClassIds.Soil;
        }

        if (groundwater >= 0.72f && moistureBand >= 0.64f && soilDepth >= 0.52f)
            return RegionSurfaceClassIds.Mud;

        if (soilDepth <= 0.26f && slopeNorm >= 0.58f)
            return RegionSurfaceClassIds.Stone;

        if ((moistureBand <= 0.22f && soilDepth <= 0.40f) ||
            (groundwater <= 0.24f && soilDepth <= 0.34f))
        {
            if (isAridMacroBiome && moistureBand <= 0.18f)
                return RegionSurfaceClassIds.Sand;
            return RegionSurfaceClassIds.Soil;
        }

        if (soilDepth <= 0.34f && moistureBand <= 0.36f)
            return RegionSurfaceClassIds.Soil;

        if (groundwater >= 0.62f && moistureBand >= 0.56f)
            return RegionSurfaceClassIds.Soil;

        if (RegionBiomeVariantIds.IsSteppeVariant(biomeVariantId) && moistureBand <= 0.26f)
            return RegionSurfaceClassIds.Soil;

        if (temperatureBand <= 0.20f && moistureBand <= 0.30f)
            return RegionSurfaceClassIds.Snow;

        return RegionSurfaceClassIds.Grass;
    }

    public static string ResolveSurfaceClassId(GeneratedRegionTile tile, string parentMacroBiomeId)
    {
        var normalizedSurfaceClassId = NormalizeSurfaceClassId(tile.SurfaceClassId);
        if (normalizedSurfaceClassId is not null)
            return normalizedSurfaceClassId;

        return ResolveSurfaceClassId(
            parentMacroBiomeId,
            tile.BiomeVariantId,
            tile.Slope / 255f,
            tile.HasRiver,
            tile.HasLake,
            tile.MoistureBand,
            tile.Groundwater,
            tile.TemperatureBand,
            tile.SoilDepth);
    }

    public static string ResolvePreferredSurfaceTileDefId(GeneratedRegionTile tile, string parentMacroBiomeId)
        => ResolveTileDefIdFromSurfaceClass(ResolveSurfaceClassId(tile, parentMacroBiomeId)) ?? GeneratedTileDefIds.Grass;

    public static string? ResolveTileDefIdFromSurfaceClass(string? surfaceClassId)
    {
        var normalizedSurfaceClassId = NormalizeSurfaceClassId(surfaceClassId);
        return normalizedSurfaceClassId switch
        {
            RegionSurfaceClassIds.Grass => GeneratedTileDefIds.Grass,
            RegionSurfaceClassIds.Soil => GeneratedTileDefIds.Soil,
            RegionSurfaceClassIds.Sand => GeneratedTileDefIds.Sand,
            RegionSurfaceClassIds.Mud => GeneratedTileDefIds.Mud,
            RegionSurfaceClassIds.Snow => GeneratedTileDefIds.Snow,
            RegionSurfaceClassIds.Stone => GeneratedTileDefIds.StoneFloor,
            _ => null,
        };
    }

    public static string ResolveAnchorSurfaceClassId(
        string? surfaceClassId,
        string biomeVariantId,
        string parentMacroBiomeId)
    {
        var normalizedSurfaceClassId = NormalizeSurfaceClassId(surfaceClassId);
        if (normalizedSurfaceClassId is not null)
            return normalizedSurfaceClassId;

        if (string.Equals(parentMacroBiomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase))
            return RegionSurfaceClassIds.Sand;

        if (string.Equals(parentMacroBiomeId, MacroBiomeIds.Tundra, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parentMacroBiomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase))
        {
            return RegionSurfaceClassIds.Snow;
        }

        if (RegionBiomeVariantIds.IsMarshVariant(biomeVariantId))
            return RegionSurfaceClassIds.Mud;

        if (RegionBiomeVariantIds.IsHighlandVariant(biomeVariantId) ||
            string.Equals(biomeVariantId, RegionBiomeVariantIds.RockyHighland, StringComparison.OrdinalIgnoreCase))
        {
            return RegionSurfaceClassIds.Stone;
        }

        if (string.Equals(biomeVariantId, RegionBiomeVariantIds.AridBadlands, StringComparison.OrdinalIgnoreCase) ||
            RegionBiomeVariantIds.IsSteppeVariant(biomeVariantId))
        {
            return RegionSurfaceClassIds.Sand;
        }

        if (string.Equals(biomeVariantId, RegionBiomeVariantIds.TemperatePlainsOpen, StringComparison.OrdinalIgnoreCase))
            return RegionSurfaceClassIds.Soil;

        return RegionSurfaceClassIds.Grass;
    }

    public static string? NormalizeSurfaceClassId(string? surfaceClassId)
    {
        if (string.Equals(surfaceClassId, RegionSurfaceClassIds.Grass, StringComparison.OrdinalIgnoreCase))
            return RegionSurfaceClassIds.Grass;
        if (string.Equals(surfaceClassId, RegionSurfaceClassIds.Soil, StringComparison.OrdinalIgnoreCase))
            return RegionSurfaceClassIds.Soil;
        if (string.Equals(surfaceClassId, RegionSurfaceClassIds.Sand, StringComparison.OrdinalIgnoreCase))
            return RegionSurfaceClassIds.Sand;
        if (string.Equals(surfaceClassId, RegionSurfaceClassIds.Mud, StringComparison.OrdinalIgnoreCase))
            return RegionSurfaceClassIds.Mud;
        if (string.Equals(surfaceClassId, RegionSurfaceClassIds.Snow, StringComparison.OrdinalIgnoreCase))
            return RegionSurfaceClassIds.Snow;
        if (string.Equals(surfaceClassId, RegionSurfaceClassIds.Stone, StringComparison.OrdinalIgnoreCase))
            return RegionSurfaceClassIds.Stone;
        return null;
    }
}
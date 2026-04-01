using System;
using System.Collections.Generic;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Maps;
using DwarfFortress.WorldGen.Regions;

namespace DwarfFortress.WorldGen.Local;

public interface ILocalLayerGenerator
{
    GeneratedEmbarkMap Generate(GeneratedRegionMap region, RegionCoord coord, LocalGenerationSettings settings);
}

/// <summary>
/// Adapts region-scale metadata into local embark generation inputs.
/// </summary>
public sealed class LocalLayerGenerator : ILocalLayerGenerator
{
    public GeneratedEmbarkMap Generate(GeneratedRegionMap region, RegionCoord coord, LocalGenerationSettings settings)
    {
        if (settings.Width <= 0) throw new ArgumentOutOfRangeException(nameof(settings.Width));
        if (settings.Height <= 0) throw new ArgumentOutOfRangeException(nameof(settings.Height));
        if (settings.Depth <= 0) throw new ArgumentOutOfRangeException(nameof(settings.Depth));
        if (coord.RegionX < 0 || coord.RegionX >= region.Width || coord.RegionY < 0 || coord.RegionY >= region.Height)
            throw new ArgumentOutOfRangeException(nameof(coord), "Region coordinate is outside region bounds.");

        var regionTile = region.GetTile(coord.RegionX, coord.RegionY);
        var localSeed = SeedHash.Hash(region.Seed, coord.RegionX, coord.RegionY, 47017);
        var riverPortals = BuildLocalRiverPortals(region, coord.RegionX, coord.RegionY);

        var biomeId = settings.BiomeOverrideId ?? ResolveEmbarkBiomeId(regionTile.BiomeVariantId, region.ParentMacroBiomeId);
        var variantTreeBias = ResolveVariantTreeBias(regionTile.BiomeVariantId);
        var macroForestBias = ResolveMacroForestBias(region.ParentMacroBiomeId);
        var parentForestBias = Math.Clamp((region.ParentForestCover - 0.5f) * 0.90f, -0.30f, 0.30f);
        var parentMountainPenalty = Math.Clamp((region.ParentMountainCover - 0.35f) * 0.24f, -0.18f, 0.18f);
        var parentClimateSupport = Math.Clamp(
            ((region.ParentMoistureBand - 0.5f) * 0.16f) +
            (((1f - MathF.Abs((region.ParentTemperatureBand * 2f) - 1f)) - 0.5f) * 0.10f),
            -0.16f,
            0.16f);
        var parentRiverBias = region.ParentHasRiver
            ? Math.Clamp(
                0.04f +
                (region.ParentRiverOrder * 0.008f) +
                MathF.Min(0.10f, region.ParentRiverDischarge * 0.02f),
                0f,
                0.16f)
            : 0f;
        var neighborhoodVegetation = SampleNeighborhoodVegetation(region, coord.RegionX, coord.RegionY);
        var neighborhoodSuitability = SampleNeighborhoodSuitability(region, coord.RegionX, coord.RegionY);
        var forestCoverageTarget = ResolveForestCoverageTarget(
            regionTile,
            region.ParentMacroBiomeId,
            region.ParentForestCover,
            neighborhoodVegetation);
        var hydrologyTreeBias = (regionTile.HasRiver ? 0.10f : 0f) + (regionTile.HasLake ? 0.06f : 0f);
        var treeBias = Math.Clamp(
            settings.TreeDensityBias +
            ((regionTile.VegetationDensity - 0.5f) * 1.05f) +
            ((regionTile.VegetationSuitability - 0.5f) * 0.92f) +
            ((neighborhoodVegetation - 0.5f) * 0.72f) +
            ((neighborhoodSuitability - 0.5f) * 0.70f) +
            ((regionTile.Groundwater - 0.5f) * 0.65f) +
            variantTreeBias +
            hydrologyTreeBias +
            macroForestBias +
            parentForestBias +
            parentClimateSupport +
            parentRiverBias -
            parentMountainPenalty, -0.95f, 0.95f);
        var forestPatchBias = Math.Clamp(
            ((regionTile.VegetationDensity - 0.5f) * 0.90f) +
            ((regionTile.VegetationSuitability - 0.5f) * 0.84f) +
            ((neighborhoodVegetation - 0.5f) * 0.90f) +
            ((neighborhoodSuitability - 0.5f) * 0.84f) +
            (regionTile.HasRiver ? 0.08f : 0f) +
            (regionTile.HasLake ? 0.10f : 0f) +
            macroForestBias +
            (parentForestBias * 0.92f) +
            (parentClimateSupport * 0.55f) +
            (parentRiverBias * 0.65f) -
            (parentMountainPenalty * 0.72f), -0.95f, 0.95f);

        var wetnessBias = Math.Clamp(
            settings.ParentWetnessBias +
            ((regionTile.Groundwater - 0.5f) * 0.90f) +
            ((region.ParentMoistureBand - 0.5f) * 0.42f) +
            (parentRiverBias * 0.50f), -0.95f, 0.95f);

        var soilDepthBias = Math.Clamp(
            settings.ParentSoilDepthBias +
            ((regionTile.SoilDepth - 0.5f) * 0.90f) -
            ((region.ParentRelief - 0.5f) * 0.20f), -0.95f, 0.95f);

        var parentRockinessBias = ((region.ParentMountainCover - 0.5f) * 0.42f) + ((region.ParentRelief - 0.5f) * 0.20f);
        var outcropBias = Math.Clamp(
            settings.OutcropBias +
            ((regionTile.ResourceRichness - 0.5f) * 0.90f) +
            (((regionTile.Slope / 255f) - 0.5f) * 0.60f) +
            ((0.5f - regionTile.SoilDepth) * 0.40f) +
            parentRockinessBias, -0.95f, 0.95f);

        var streamBandBias = settings.StreamBandBias + (regionTile.HasRiver ? 1 : 0);
        if (region.ParentHasRiver)
        {
            if (!regionTile.HasRiver)
            {
                streamBandBias += region.ParentRiverOrder >= 4 ? 2 : 1;
            }
            else if (region.ParentRiverOrder >= 5)
            {
                streamBandBias += 1;
            }

            if (region.ParentRiverDischarge >= 0.60f)
                streamBandBias += 1;
        }
        var marshPoolBias = settings.MarshPoolBias + (regionTile.HasLake ? 3 : 0);
        if (region.ParentHasRiver && region.ParentMoistureBand >= 0.56f)
            marshPoolBias += 1;
        if (RegionBiomeVariantIds.IsMarshVariant(regionTile.BiomeVariantId))
            marshPoolBias += 2;

        var settlementInfluence = Math.Clamp(
            settings.SettlementInfluence +
            (regionTile.HasSettlement ? (0.62f + (regionTile.VegetationDensity * 0.28f)) : 0f) +
            (CountAdjacentSettlements(region, coord.RegionX, coord.RegionY) * 0.08f),
            0f,
            1f);
        var roadInfluence = WorldGenFeatureFlags.EnableRoadGeneration
            ? Math.Clamp(
                settings.RoadInfluence +
                (regionTile.HasRoad ? 0.86f : 0f) +
                (regionTile.HasSettlement ? 0.12f : 0f) +
                (CountAdjacentRoads(region, coord.RegionX, coord.RegionY) * 0.10f),
                0f,
                1f)
            : 0f;
        var settlementAnchors = BuildLocalSettlementAnchors(region, coord.RegionX, coord.RegionY);
        var roadPortals = WorldGenFeatureFlags.EnableRoadGeneration
            ? BuildLocalRoadPortals(region, coord.RegionX, coord.RegionY)
            : null;
        var surfaceTileOverrideId = ResolvePreferredSurfaceTileDefId(regionTile, region.ParentMacroBiomeId);
        var globalRegionX = (coord.WorldX * region.Width) + coord.RegionX;
        var globalRegionY = (coord.WorldY * region.Height) + coord.RegionY;
        var noiseOriginX = globalRegionX * settings.Width;
        var noiseOriginY = globalRegionY * settings.Height;

        bool? stoneSurface = settings.StoneSurfaceOverride;
        if (stoneSurface is null && IsRockyVariant(regionTile.BiomeVariantId))
            stoneSurface = true;

        var localSettings = settings with
        {
            BiomeOverrideId = biomeId,
            TreeDensityBias = treeBias,
            OutcropBias = outcropBias,
            StreamBandBias = streamBandBias,
            MarshPoolBias = marshPoolBias,
            ParentWetnessBias = wetnessBias,
            ParentSoilDepthBias = soilDepthBias,
            GeologyProfileId = settings.GeologyProfileId ?? regionTile.GeologyProfileId,
            StoneSurfaceOverride = stoneSurface,
            RiverPortals = riverPortals,
            ForestPatchBias = forestPatchBias,
            SettlementInfluence = settlementInfluence,
            RoadInfluence = roadInfluence,
            SettlementAnchors = settlementAnchors,
            RoadPortals = roadPortals,
            SurfaceTileOverrideId = surfaceTileOverrideId,
            ForestCoverageTarget = forestCoverageTarget,
            NoiseOriginX = noiseOriginX,
            NoiseOriginY = noiseOriginY,
        };

        return EmbarkGenerator.Generate(localSettings, localSeed);
    }

    private static string ResolveEmbarkBiomeId(string regionBiomeVariant, string? parentMacroBiomeId)
    {
        if (MacroBiomeIds.IsKnown(parentMacroBiomeId))
            return parentMacroBiomeId!;

        return RegionBiomeVariantIds.ResolveMacroBiomeId(regionBiomeVariant);
    }

    private static bool IsRockyVariant(string regionBiomeVariant)
        => RegionBiomeVariantIds.IsRockyVariant(regionBiomeVariant);

    private static float ResolveVariantTreeBias(string regionBiomeVariant)
    {
        if (string.Equals(regionBiomeVariant, RegionBiomeVariantIds.DenseConifer, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(regionBiomeVariant, RegionBiomeVariantIds.ForestedFoothills, StringComparison.OrdinalIgnoreCase))
        {
            return 0.15f;
        }
        if (string.Equals(regionBiomeVariant, RegionBiomeVariantIds.ConiferWoodland, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(regionBiomeVariant, RegionBiomeVariantIds.TemperateWoodland, StringComparison.OrdinalIgnoreCase))
        {
            return 0.10f;
        }
        if (string.Equals(regionBiomeVariant, RegionBiomeVariantIds.RiverValley, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(regionBiomeVariant, RegionBiomeVariantIds.FloodplainMarsh, StringComparison.OrdinalIgnoreCase))
        {
            return 0.08f;
        }
        if (string.Equals(regionBiomeVariant, RegionBiomeVariantIds.TropicalCanopy, StringComparison.OrdinalIgnoreCase))
            return 0.18f;
        if (string.Equals(regionBiomeVariant, RegionBiomeVariantIds.TropicalLowland, StringComparison.OrdinalIgnoreCase))
            return 0.12f;
        if (string.Equals(regionBiomeVariant, RegionBiomeVariantIds.SavannaGrassland, StringComparison.OrdinalIgnoreCase))
            return -0.05f;
        if (string.Equals(regionBiomeVariant, RegionBiomeVariantIds.DrySteppe, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(regionBiomeVariant, RegionBiomeVariantIds.SparseSteppe, StringComparison.OrdinalIgnoreCase))
        {
            return -0.12f;
        }
        if (string.Equals(regionBiomeVariant, RegionBiomeVariantIds.AridBadlands, StringComparison.OrdinalIgnoreCase))
            return -0.20f;
        if (string.Equals(regionBiomeVariant, RegionBiomeVariantIds.AlpineRidge, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(regionBiomeVariant, RegionBiomeVariantIds.RockyHighland, StringComparison.OrdinalIgnoreCase))
        {
            return -0.08f;
        }
        if (string.Equals(regionBiomeVariant, RegionBiomeVariantIds.PolarTundra, StringComparison.OrdinalIgnoreCase))
            return -0.16f;
        if (string.Equals(regionBiomeVariant, RegionBiomeVariantIds.GlacialField, StringComparison.OrdinalIgnoreCase))
            return -0.24f;

        return 0f;
    }

    private static float ResolveMacroForestBias(string macroBiomeId)
    {
        return macroBiomeId switch
        {
            MacroBiomeIds.TropicalRainforest => 0.16f,
            MacroBiomeIds.ConiferForest => 0.14f,
            MacroBiomeIds.BorealForest => 0.13f,
            MacroBiomeIds.MistyMarsh => 0.08f,
            MacroBiomeIds.TemperatePlains => 0.04f,
            MacroBiomeIds.WindsweptSteppe => -0.08f,
            MacroBiomeIds.Savanna => -0.06f,
            MacroBiomeIds.Desert => -0.20f,
            MacroBiomeIds.Tundra => -0.14f,
            MacroBiomeIds.IcePlains => -0.24f,
            MacroBiomeIds.Highland => -0.08f,
            MacroBiomeIds.OceanShallow => -0.18f,
            MacroBiomeIds.OceanDeep => -0.24f,
            _ => 0f,
        };
    }

    private static float ResolveForestCoverageTarget(
        GeneratedRegionTile tile,
        string parentMacroBiomeId,
        float parentForestCover,
        float neighborhoodVegetation)
    {
        var slopeNorm = tile.Slope / 255f;
        var target = Math.Clamp(
            (tile.VegetationDensity * 0.48f) +
            (tile.VegetationSuitability * 0.22f) +
            (Math.Clamp(parentForestCover, 0f, 1f) * 0.20f) +
            ((Math.Clamp(neighborhoodVegetation, 0f, 1f) - 0.5f) * 0.18f) +
            ((tile.Groundwater - 0.5f) * 0.14f) +
            (tile.HasRiver ? 0.04f : 0f) +
            (tile.HasLake ? 0.03f : 0f) -
            (slopeNorm * 0.22f),
            0f,
            0.85f);

        if (MacroBiomeIds.IsOcean(parentMacroBiomeId) ||
            string.Equals(parentMacroBiomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parentMacroBiomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase))
        {
            return MathF.Min(target, 0.08f);
        }

        if (string.Equals(parentMacroBiomeId, MacroBiomeIds.WindsweptSteppe, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parentMacroBiomeId, MacroBiomeIds.Tundra, StringComparison.OrdinalIgnoreCase))
        {
            return MathF.Min(target, 0.20f);
        }

        if (RegionBiomeVariantIds.IsRockyVariant(tile.BiomeVariantId) &&
            !string.Equals(parentMacroBiomeId, MacroBiomeIds.ConiferForest, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parentMacroBiomeId, MacroBiomeIds.BorealForest, StringComparison.OrdinalIgnoreCase))
        {
            return MathF.Min(target, 0.26f);
        }

        return target;
    }

    private static string ResolvePreferredSurfaceTileDefId(GeneratedRegionTile tile, string parentMacroBiomeId)
        => RegionSurfaceResolver.ResolvePreferredSurfaceTileDefId(tile, parentMacroBiomeId);

    private static float SampleNeighborhoodVegetation(GeneratedRegionMap region, int centerX, int centerY)
    {
        var weighted = 0f;
        var weight = 0f;

        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var x = centerX + dx;
            var y = centerY + dy;
            if (x < 0 || y < 0 || x >= region.Width || y >= region.Height)
                continue;

            var isCenter = dx == 0 && dy == 0;
            var isCardinal = dx == 0 || dy == 0;
            var sampleWeight = isCenter ? 1.6f : (isCardinal ? 1.0f : 0.7f);
            weighted += region.GetTile(x, y).VegetationDensity * sampleWeight;
            weight += sampleWeight;
        }

        if (weight <= 0f)
            return region.GetTile(centerX, centerY).VegetationDensity;

        return weighted / weight;
    }

    private static float SampleNeighborhoodSuitability(GeneratedRegionMap region, int centerX, int centerY)
    {
        var weighted = 0f;
        var weight = 0f;

        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var x = centerX + dx;
            var y = centerY + dy;
            if (x < 0 || y < 0 || x >= region.Width || y >= region.Height)
                continue;

            var isCenter = dx == 0 && dy == 0;
            var isCardinal = dx == 0 || dy == 0;
            var sampleWeight = isCenter ? 1.6f : (isCardinal ? 1.0f : 0.7f);
            weighted += region.GetTile(x, y).VegetationSuitability * sampleWeight;
            weight += sampleWeight;
        }

        if (weight <= 0f)
            return region.GetTile(centerX, centerY).VegetationSuitability;

        return weighted / weight;
    }

    private static LocalRiverPortal[]? BuildLocalRiverPortals(GeneratedRegionMap region, int regionX, int regionY)
    {
        var regionTile = region.GetTile(regionX, regionY);
        if (!regionTile.HasRiver)
            return null;

        var edges = CollectPortalEdges(region, regionX, regionY, regionTile.RiverEdges);

        if (edges.Count == 1)
            edges.Add(OppositeEdge(edges[0]));

        if (edges.Count == 0)
        {
            // Fallback for isolated river cells: force a deterministic through-flow pair.
            var hash = SeedHash.Hash(region.Seed, regionX, regionY, 9211);
            var first = (LocalMapEdge)(Math.Abs(hash) % 4);
            var second = first switch
            {
                LocalMapEdge.North => LocalMapEdge.South,
                LocalMapEdge.South => LocalMapEdge.North,
                LocalMapEdge.East => LocalMapEdge.West,
                _ => LocalMapEdge.East,
            };

            edges.Add(first);
            edges.Add(second);
        }

        var portals = new LocalRiverPortal[edges.Count];
        var portalStrength = ResolvePortalStrength(regionTile.RiverDischarge, regionTile.RiverOrder);
        for (var i = 0; i < edges.Count; i++)
        {
            var offset = ResolvePortalOffset(region.Seed, regionX, regionY, edges[i]);
            portals[i] = new LocalRiverPortal(edges[i], offset, Strength: portalStrength);
        }

        return portals;
    }

    private static List<LocalMapEdge> CollectPortalEdges(
        GeneratedRegionMap region,
        int regionX,
        int regionY,
        RegionRiverEdges riverEdges)
    {
        var edges = new List<LocalMapEdge>(4);
        if (riverEdges != RegionRiverEdges.None)
        {
            if (RegionRiverEdgeMask.Has(riverEdges, RegionRiverEdges.North)) edges.Add(LocalMapEdge.North);
            if (RegionRiverEdgeMask.Has(riverEdges, RegionRiverEdges.East)) edges.Add(LocalMapEdge.East);
            if (RegionRiverEdgeMask.Has(riverEdges, RegionRiverEdges.South)) edges.Add(LocalMapEdge.South);
            if (RegionRiverEdgeMask.Has(riverEdges, RegionRiverEdges.West)) edges.Add(LocalMapEdge.West);
            return edges;
        }

        if (HasRiverEdge(region, regionX, regionY - 1, RegionRiverEdges.South)) edges.Add(LocalMapEdge.North);
        if (HasRiverEdge(region, regionX + 1, regionY, RegionRiverEdges.West)) edges.Add(LocalMapEdge.East);
        if (HasRiverEdge(region, regionX, regionY + 1, RegionRiverEdges.North)) edges.Add(LocalMapEdge.South);
        if (HasRiverEdge(region, regionX - 1, regionY, RegionRiverEdges.East)) edges.Add(LocalMapEdge.West);
        if (edges.Count > 0)
            return edges;

        // Backward-compatible fallback for legacy region tiles that only have HasRiver.
        if (HasRiver(region, regionX, regionY - 1)) edges.Add(LocalMapEdge.North);
        if (HasRiver(region, regionX + 1, regionY)) edges.Add(LocalMapEdge.East);
        if (HasRiver(region, regionX, regionY + 1)) edges.Add(LocalMapEdge.South);
        if (HasRiver(region, regionX - 1, regionY)) edges.Add(LocalMapEdge.West);
        return edges;
    }

    private static bool HasRiver(GeneratedRegionMap region, int x, int y)
    {
        if (x < 0 || x >= region.Width || y < 0 || y >= region.Height)
            return false;

        return region.GetTile(x, y).HasRiver;
    }

    private static bool HasRiverEdge(GeneratedRegionMap region, int x, int y, RegionRiverEdges edge)
    {
        if (x < 0 || x >= region.Width || y < 0 || y >= region.Height)
            return false;

        return RegionRiverEdgeMask.Has(region.GetTile(x, y).RiverEdges, edge);
    }

    private static LocalSettlementAnchor[]? BuildLocalSettlementAnchors(GeneratedRegionMap region, int regionX, int regionY)
    {
        var regionTile = region.GetTile(regionX, regionY);
        var anchors = new List<LocalSettlementAnchor>(5);
        var localHasSettlement = regionTile.HasSettlement;

        var northSettlement = HasSettlement(region, regionX, regionY - 1);
        var eastSettlement = HasSettlement(region, regionX + 1, regionY);
        var southSettlement = HasSettlement(region, regionX, regionY + 1);
        var westSettlement = HasSettlement(region, regionX - 1, regionY);

        var settlementNeighbors = 0;
        if (northSettlement) settlementNeighbors++;
        if (eastSettlement) settlementNeighbors++;
        if (southSettlement) settlementNeighbors++;
        if (westSettlement) settlementNeighbors++;

        if (localHasSettlement)
        {
            // Keep the local civic center around map mid and deterministic per region tile.
            var cx = 0.30f + (SeedHash.Unit(region.Seed, regionX, regionY, 5411) * 0.40f);
            var cy = 0.30f + (SeedHash.Unit(region.Seed, regionX, regionY, 5471) * 0.40f);
            var strength = (byte)Math.Clamp(3 + settlementNeighbors, 3, 7);
            anchors.Add(new LocalSettlementAnchor(cx, cy, strength));
        }

        AddBoundarySettlementAnchor(anchors, region.Seed, regionX, regionY, LocalMapEdge.North, northSettlement, localHasSettlement);
        AddBoundarySettlementAnchor(anchors, region.Seed, regionX, regionY, LocalMapEdge.East, eastSettlement, localHasSettlement);
        AddBoundarySettlementAnchor(anchors, region.Seed, regionX, regionY, LocalMapEdge.South, southSettlement, localHasSettlement);
        AddBoundarySettlementAnchor(anchors, region.Seed, regionX, regionY, LocalMapEdge.West, westSettlement, localHasSettlement);

        if (anchors.Count == 0)
            return null;

        return anchors.ToArray();
    }

    private static void AddBoundarySettlementAnchor(
        List<LocalSettlementAnchor> anchors,
        int regionSeed,
        int regionX,
        int regionY,
        LocalMapEdge edge,
        bool hasNeighborSettlement,
        bool localHasSettlement)
    {
        if (!hasNeighborSettlement)
            return;

        var offset = ResolveSettlementPortalOffset(regionSeed, regionX, regionY, edge);
        const float edgeInset = 0.14f;
        var strength = (byte)(localHasSettlement ? 3 : 2);
        var anchor = edge switch
        {
            LocalMapEdge.North => new LocalSettlementAnchor(offset, edgeInset, strength),
            LocalMapEdge.East => new LocalSettlementAnchor(1f - edgeInset, offset, strength),
            LocalMapEdge.South => new LocalSettlementAnchor(offset, 1f - edgeInset, strength),
            LocalMapEdge.West => new LocalSettlementAnchor(edgeInset, offset, strength),
            _ => new LocalSettlementAnchor(0.5f, 0.5f, strength),
        };

        anchors.Add(anchor);
    }

    private static LocalRoadPortal[]? BuildLocalRoadPortals(GeneratedRegionMap region, int regionX, int regionY)
    {
        var regionTile = region.GetTile(regionX, regionY);
        if (!regionTile.HasRoad)
            return null;

        var edges = CollectRoadPortalEdges(region, regionX, regionY, regionTile.RoadEdges);

        if (edges.Count == 1)
            edges.Add(OppositeEdge(edges[0]));

        if (edges.Count == 0)
        {
            // Fallback for isolated road cells: deterministic through-pass pair.
            var hash = SeedHash.Hash(region.Seed, regionX, regionY, 6121);
            var first = (LocalMapEdge)(Math.Abs(hash) % 4);
            var second = first switch
            {
                LocalMapEdge.North => LocalMapEdge.South,
                LocalMapEdge.South => LocalMapEdge.North,
                LocalMapEdge.East => LocalMapEdge.West,
                _ => LocalMapEdge.East,
            };

            edges.Add(first);
            edges.Add(second);
        }

        var width = (byte)Math.Clamp(1 + (regionTile.HasSettlement ? 1 : 0), 1, 2);
        var portals = new LocalRoadPortal[edges.Count];
        for (var i = 0; i < edges.Count; i++)
        {
            var offset = ResolveRoadPortalOffset(region.Seed, regionX, regionY, edges[i]);
            portals[i] = new LocalRoadPortal(edges[i], offset, width);
        }

        return portals;
    }

    private static List<LocalMapEdge> CollectRoadPortalEdges(
        GeneratedRegionMap region,
        int regionX,
        int regionY,
        RegionRoadEdges roadEdges)
    {
        var edges = new List<LocalMapEdge>(4);
        if (roadEdges != RegionRoadEdges.None)
        {
            if (RegionRoadEdgeMask.Has(roadEdges, RegionRoadEdges.North)) edges.Add(LocalMapEdge.North);
            if (RegionRoadEdgeMask.Has(roadEdges, RegionRoadEdges.East)) edges.Add(LocalMapEdge.East);
            if (RegionRoadEdgeMask.Has(roadEdges, RegionRoadEdges.South)) edges.Add(LocalMapEdge.South);
            if (RegionRoadEdgeMask.Has(roadEdges, RegionRoadEdges.West)) edges.Add(LocalMapEdge.West);
            return edges;
        }

        if (HasRoadEdge(region, regionX, regionY - 1, RegionRoadEdges.South)) edges.Add(LocalMapEdge.North);
        if (HasRoadEdge(region, regionX + 1, regionY, RegionRoadEdges.West)) edges.Add(LocalMapEdge.East);
        if (HasRoadEdge(region, regionX, regionY + 1, RegionRoadEdges.North)) edges.Add(LocalMapEdge.South);
        if (HasRoadEdge(region, regionX - 1, regionY, RegionRoadEdges.East)) edges.Add(LocalMapEdge.West);
        if (edges.Count > 0)
            return edges;

        // Backward-compatible fallback for legacy region tiles that only have HasRoad.
        if (HasRoad(region, regionX, regionY - 1)) edges.Add(LocalMapEdge.North);
        if (HasRoad(region, regionX + 1, regionY)) edges.Add(LocalMapEdge.East);
        if (HasRoad(region, regionX, regionY + 1)) edges.Add(LocalMapEdge.South);
        if (HasRoad(region, regionX - 1, regionY)) edges.Add(LocalMapEdge.West);
        return edges;
    }

    private static bool HasSettlement(GeneratedRegionMap region, int x, int y)
    {
        if (x < 0 || x >= region.Width || y < 0 || y >= region.Height)
            return false;

        return region.GetTile(x, y).HasSettlement;
    }

    private static bool HasRoad(GeneratedRegionMap region, int x, int y)
    {
        if (x < 0 || x >= region.Width || y < 0 || y >= region.Height)
            return false;

        return region.GetTile(x, y).HasRoad;
    }

    private static bool HasRoadEdge(GeneratedRegionMap region, int x, int y, RegionRoadEdges edge)
    {
        if (x < 0 || x >= region.Width || y < 0 || y >= region.Height)
            return false;

        return RegionRoadEdgeMask.Has(region.GetTile(x, y).RoadEdges, edge);
    }

    private static LocalMapEdge OppositeEdge(LocalMapEdge edge)
    {
        return edge switch
        {
            LocalMapEdge.North => LocalMapEdge.South,
            LocalMapEdge.East => LocalMapEdge.West,
            LocalMapEdge.South => LocalMapEdge.North,
            _ => LocalMapEdge.East,
        };
    }

    private static int CountAdjacentSettlements(GeneratedRegionMap region, int x, int y)
    {
        var count = 0;
        if (HasSettlement(region, x, y - 1)) count++;
        if (HasSettlement(region, x + 1, y)) count++;
        if (HasSettlement(region, x, y + 1)) count++;
        if (HasSettlement(region, x - 1, y)) count++;
        return count;
    }

    private static int CountAdjacentRoads(GeneratedRegionMap region, int x, int y)
    {
        var count = 0;
        if (HasRoad(region, x, y - 1)) count++;
        if (HasRoad(region, x + 1, y)) count++;
        if (HasRoad(region, x, y + 1)) count++;
        if (HasRoad(region, x - 1, y)) count++;
        return count;
    }

    private static float ResolvePortalOffset(int regionSeed, int regionX, int regionY, LocalMapEdge edge)
    {
        // Canonical boundary keys ensure adjacent region cells resolve the same offset on their shared edge.
        int keyX;
        int keyY;
        int salt;
        switch (edge)
        {
            case LocalMapEdge.North:
                keyX = regionX;
                keyY = regionY - 1;
                salt = 3301;
                break;
            case LocalMapEdge.South:
                keyX = regionX;
                keyY = regionY;
                salt = 3301;
                break;
            case LocalMapEdge.West:
                keyX = regionX - 1;
                keyY = regionY;
                salt = 3371;
                break;
            case LocalMapEdge.East:
                keyX = regionX;
                keyY = regionY;
                salt = 3371;
                break;
            default:
                keyX = regionX;
                keyY = regionY;
                salt = 3391;
                break;
        }

        var unit = SeedHash.Unit(regionSeed, keyX, keyY, salt);
        return 0.15f + (unit * 0.70f);
    }

    private static float ResolveSettlementPortalOffset(int regionSeed, int regionX, int regionY, LocalMapEdge edge)
    {
        int keyX;
        int keyY;
        int salt;
        switch (edge)
        {
            case LocalMapEdge.North:
                keyX = regionX;
                keyY = regionY - 1;
                salt = 5501;
                break;
            case LocalMapEdge.South:
                keyX = regionX;
                keyY = regionY;
                salt = 5501;
                break;
            case LocalMapEdge.West:
                keyX = regionX - 1;
                keyY = regionY;
                salt = 5531;
                break;
            case LocalMapEdge.East:
                keyX = regionX;
                keyY = regionY;
                salt = 5531;
                break;
            default:
                keyX = regionX;
                keyY = regionY;
                salt = 5557;
                break;
        }

        var unit = SeedHash.Unit(regionSeed, keyX, keyY, salt);
        return 0.20f + (unit * 0.60f);
    }

    private static float ResolveRoadPortalOffset(int regionSeed, int regionX, int regionY, LocalMapEdge edge)
    {
        int keyX;
        int keyY;
        int salt;
        switch (edge)
        {
            case LocalMapEdge.North:
                keyX = regionX;
                keyY = regionY - 1;
                salt = 5623;
                break;
            case LocalMapEdge.South:
                keyX = regionX;
                keyY = regionY;
                salt = 5623;
                break;
            case LocalMapEdge.West:
                keyX = regionX - 1;
                keyY = regionY;
                salt = 5653;
                break;
            case LocalMapEdge.East:
                keyX = regionX;
                keyY = regionY;
                salt = 5653;
                break;
            default:
                keyX = regionX;
                keyY = regionY;
                salt = 5689;
                break;
        }

        var unit = SeedHash.Unit(regionSeed, keyX, keyY, salt);
        return 0.18f + (unit * 0.64f);
    }

    private static byte ResolvePortalStrength(float riverDischarge, byte riverOrder)
    {
        var clamped = Math.Clamp(riverDischarge, 0f, 12f);
        if (clamped <= 0f && riverOrder <= 0)
            return 1;

        var scaledDischarge = (int)MathF.Round(clamped / 2f); // 12 discharge ~= strength 6
        var clampedOrder = Math.Clamp(riverOrder, (byte)0, (byte)8);
        var orderBonus = clampedOrder switch
        {
            >= 6 => 2,
            >= 3 => 1,
            _ => 0,
        };

        return (byte)Math.Clamp(scaledDischarge + orderBonus, 1, 8);
    }
}

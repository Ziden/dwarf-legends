using System;
using DwarfFortress.WorldGen.Generation;

namespace DwarfFortress.WorldGen.Local;

public static class LocalGenerationFingerprint
{
    public static int Compute(LocalGenerationSettings settings)
    {
        var hash = 17;
        hash = Mix(hash, settings.Width);
        hash = Mix(hash, settings.Height);
        hash = Mix(hash, settings.Depth);
        hash = MixString(hash, settings.BiomeOverrideId);
        hash = MixNullableFloat(hash, settings.TreeDensityBias);
        hash = MixNullableFloat(hash, settings.OutcropBias);
        hash = Mix(hash, settings.StreamBandBias);
        hash = Mix(hash, settings.MarshPoolBias);
        hash = MixNullableFloat(hash, settings.ParentWetnessBias);
        hash = MixNullableFloat(hash, settings.ParentSoilDepthBias);
        hash = MixString(hash, settings.GeologyProfileId);
        hash = MixNullableBool(hash, settings.StoneSurfaceOverride);
        hash = MixRiverPortals(hash, settings.RiverPortals);
        hash = MixNullableFloat(hash, settings.ForestPatchBias);
        hash = MixNullableFloat(hash, settings.SettlementInfluence);
        hash = MixNullableFloat(hash, settings.RoadInfluence);
        hash = MixSettlementAnchors(hash, settings.SettlementAnchors);
        hash = MixRoadPortals(hash, settings.RoadPortals);
        hash = MixString(hash, settings.SurfaceTileOverrideId);
        hash = MixNullableFloat(hash, settings.ForestCoverageTarget);
        hash = MixEcologyEdges(hash, settings.EcologyEdges);
        hash = Mix(hash, settings.NoiseOriginX);
        hash = Mix(hash, settings.NoiseOriginY);
        hash = MixNullableInt(hash, settings.ContinuitySeed);
        hash = MixSurfaceIntentGrid(hash, settings.SurfaceIntentGrid);
        hash = MixContinuityContract(hash, settings.ContinuityContract);
        hash = MixFieldRaster(hash, settings.FieldRaster);
        return hash;
    }

    public static int Compute(LocalContinuityContract contract)
    {
        var hash = 23;
        hash = MixString(hash, contract.BiomeOverrideId);
        hash = MixNullableFloat(hash, contract.TreeDensityBias);
        hash = MixNullableFloat(hash, contract.OutcropBias);
        hash = MixNullableInt(hash, contract.StreamBandBias);
        hash = MixNullableInt(hash, contract.MarshPoolBias);
        hash = MixNullableFloat(hash, contract.ParentWetnessBias);
        hash = MixNullableFloat(hash, contract.ParentSoilDepthBias);
        hash = MixString(hash, contract.GeologyProfileId);
        hash = MixNullableBool(hash, contract.StoneSurfaceOverride);
        hash = MixRiverPortals(hash, contract.RiverPortals);
        hash = MixNullableFloat(hash, contract.ForestPatchBias);
        hash = MixNullableFloat(hash, contract.SettlementInfluence);
        hash = MixNullableFloat(hash, contract.RoadInfluence);
        hash = MixSettlementAnchors(hash, contract.SettlementAnchors);
        hash = MixRoadPortals(hash, contract.RoadPortals);
        hash = MixString(hash, contract.SurfaceTileOverrideId);
        hash = MixNullableFloat(hash, contract.ForestCoverageTarget);
        hash = MixEcologyEdges(hash, contract.EcologyEdges);
        hash = MixNullableInt(hash, contract.NoiseOriginX);
        hash = MixNullableInt(hash, contract.NoiseOriginY);
        hash = MixNullableInt(hash, contract.ContinuitySeed);
        hash = MixSurfaceIntentGrid(hash, contract.SurfaceIntentGrid);
        return hash;
    }

    private static int MixContinuityContract(int hash, LocalContinuityContract? contract)
    {
        if (!contract.HasValue)
            return Mix(hash, 0);

        hash = Mix(hash, 1);
        return Mix(hash, Compute(contract.Value));
    }

    private static int MixFieldRaster(int hash, LocalEmbarkFieldRaster? fieldRaster)
    {
        if (fieldRaster is null)
            return Mix(hash, 0);

        hash = Mix(hash, 1);
        return Mix(hash, fieldRaster.GetFingerprint());
    }

    private static int MixRiverPortals(int hash, LocalRiverPortal[]? portals)
    {
        if (portals is null)
            return Mix(hash, 0);

        hash = Mix(hash, portals.Length + 1);
        for (var i = 0; i < portals.Length; i++)
        {
            hash = Mix(hash, (int)portals[i].Edge);
            hash = Mix(hash, Quantize(portals[i].NormalizedOffset));
            hash = Mix(hash, portals[i].Strength);
        }

        return hash;
    }

    private static int MixRoadPortals(int hash, LocalRoadPortal[]? portals)
    {
        if (portals is null)
            return Mix(hash, 0);

        hash = Mix(hash, portals.Length + 1);
        for (var i = 0; i < portals.Length; i++)
        {
            hash = Mix(hash, (int)portals[i].Edge);
            hash = Mix(hash, Quantize(portals[i].NormalizedOffset));
            hash = Mix(hash, portals[i].Width);
        }

        return hash;
    }

    private static int MixSettlementAnchors(int hash, LocalSettlementAnchor[]? anchors)
    {
        if (anchors is null)
            return Mix(hash, 0);

        hash = Mix(hash, anchors.Length + 1);
        for (var i = 0; i < anchors.Length; i++)
        {
            hash = Mix(hash, Quantize(anchors[i].NormalizedX));
            hash = Mix(hash, Quantize(anchors[i].NormalizedY));
            hash = Mix(hash, anchors[i].Strength);
        }

        return hash;
    }

    private static int MixSurfaceIntentGrid(int hash, LocalSurfaceIntentGrid? grid)
    {
        if (!grid.HasValue)
            return Mix(hash, 0);

        hash = Mix(hash, 1);
        hash = MixString(hash, grid.Value.NorthWestTileDefId);
        hash = MixString(hash, grid.Value.NorthTileDefId);
        hash = MixString(hash, grid.Value.NorthEastTileDefId);
        hash = MixString(hash, grid.Value.WestTileDefId);
        hash = MixString(hash, grid.Value.CenterTileDefId);
        hash = MixString(hash, grid.Value.EastTileDefId);
        hash = MixString(hash, grid.Value.SouthWestTileDefId);
        hash = MixString(hash, grid.Value.SouthTileDefId);
        hash = MixString(hash, grid.Value.SouthEastTileDefId);
        return hash;
    }

    private static int MixEcologyEdges(int hash, EcologyEdgeDescriptors? edges)
    {
        if (!edges.HasValue)
            return Mix(hash, 0);

        hash = Mix(hash, 1);
        hash = MixEcologyEdgeProfile(hash, edges.Value.North);
        hash = MixEcologyEdgeProfile(hash, edges.Value.East);
        hash = MixEcologyEdgeProfile(hash, edges.Value.South);
        hash = MixEcologyEdgeProfile(hash, edges.Value.West);
        return hash;
    }

    private static int MixEcologyEdgeProfile(int hash, EcologyEdgeProfile profile)
    {
        hash = Mix(hash, Quantize(profile.VegetationDensity));
        hash = Mix(hash, Quantize(profile.VegetationSuitability));
        hash = Mix(hash, Quantize(profile.SoilDepth));
        hash = Mix(hash, Quantize(profile.Groundwater));
        return hash;
    }

    private static int MixString(int hash, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Mix(hash, 0);

        hash = Mix(hash, 1);
        for (var i = 0; i < value.Length; i++)
            hash = Mix(hash, value[i]);
        return hash;
    }

    private static int MixNullableInt(int hash, int? value)
    {
        if (!value.HasValue)
            return Mix(hash, 0);

        hash = Mix(hash, 1);
        return Mix(hash, value.Value);
    }

    private static int MixNullableBool(int hash, bool? value)
    {
        return value switch
        {
            true => Mix(hash, 2),
            false => Mix(hash, 1),
            _ => Mix(hash, 0),
        };
    }

    private static int MixNullableFloat(int hash, float? value)
    {
        if (!value.HasValue)
            return Mix(hash, 0);

        hash = Mix(hash, 1);
        return Mix(hash, Quantize(value.Value));
    }

    private static int Quantize(float value)
        => (int)MathF.Round(value * 10000f);

    private static int Mix(int hash, int value)
        => unchecked((hash * 31) + value);
}

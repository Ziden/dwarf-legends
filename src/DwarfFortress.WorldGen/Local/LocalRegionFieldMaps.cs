using System;

namespace DwarfFortress.WorldGen.Local;

public sealed class LocalRegionFieldMaps
{
    public LocalRegionFieldMaps(
        float[,] vegetationDensity,
        float[,] vegetationSuitability,
        float[,] soilDepth,
        float[,] groundwater,
        float[,] moistureBand,
        float[,] slope,
        float[,] riverInfluence,
        float[,] lakeInfluence,
        float[,] flowAccumulationBand,
        float[,] riverDischargeBand,
        float[,] riverOrderBand,
        float[,] surfaceGrassWeight,
        float[,] surfaceSoilWeight,
        float[,] surfaceSandWeight,
        float[,] surfaceMudWeight,
        float[,] surfaceSnowWeight,
        float[,] surfaceStoneWeight)
    {
        Width = vegetationDensity.GetLength(0);
        Height = vegetationDensity.GetLength(1);

        EnsureSameShape(vegetationSuitability, nameof(vegetationSuitability));
        EnsureSameShape(soilDepth, nameof(soilDepth));
        EnsureSameShape(groundwater, nameof(groundwater));
        EnsureSameShape(moistureBand, nameof(moistureBand));
        EnsureSameShape(slope, nameof(slope));
        EnsureSameShape(riverInfluence, nameof(riverInfluence));
        EnsureSameShape(lakeInfluence, nameof(lakeInfluence));
        EnsureSameShape(flowAccumulationBand, nameof(flowAccumulationBand));
        EnsureSameShape(riverDischargeBand, nameof(riverDischargeBand));
        EnsureSameShape(riverOrderBand, nameof(riverOrderBand));
        EnsureSameShape(surfaceGrassWeight, nameof(surfaceGrassWeight));
        EnsureSameShape(surfaceSoilWeight, nameof(surfaceSoilWeight));
        EnsureSameShape(surfaceSandWeight, nameof(surfaceSandWeight));
        EnsureSameShape(surfaceMudWeight, nameof(surfaceMudWeight));
        EnsureSameShape(surfaceSnowWeight, nameof(surfaceSnowWeight));
        EnsureSameShape(surfaceStoneWeight, nameof(surfaceStoneWeight));

        VegetationDensity = vegetationDensity;
        VegetationSuitability = vegetationSuitability;
        SoilDepth = soilDepth;
        Groundwater = groundwater;
        MoistureBand = moistureBand;
        Slope = slope;
        RiverInfluence = riverInfluence;
        LakeInfluence = lakeInfluence;
        FlowAccumulationBand = flowAccumulationBand;
        RiverDischargeBand = riverDischargeBand;
        RiverOrderBand = riverOrderBand;
        SurfaceGrassWeight = surfaceGrassWeight;
        SurfaceSoilWeight = surfaceSoilWeight;
        SurfaceSandWeight = surfaceSandWeight;
        SurfaceMudWeight = surfaceMudWeight;
        SurfaceSnowWeight = surfaceSnowWeight;
        SurfaceStoneWeight = surfaceStoneWeight;
    }

    public int Width { get; }
    public int Height { get; }
    public float[,] VegetationDensity { get; }
    public float[,] VegetationSuitability { get; }
    public float[,] SoilDepth { get; }
    public float[,] Groundwater { get; }
    public float[,] MoistureBand { get; }
    public float[,] Slope { get; }
    public float[,] RiverInfluence { get; }
    public float[,] LakeInfluence { get; }
    public float[,] FlowAccumulationBand { get; }
    public float[,] RiverDischargeBand { get; }
    public float[,] RiverOrderBand { get; }
    public float[,] SurfaceGrassWeight { get; }
    public float[,] SurfaceSoilWeight { get; }
    public float[,] SurfaceSandWeight { get; }
    public float[,] SurfaceMudWeight { get; }
    public float[,] SurfaceSnowWeight { get; }
    public float[,] SurfaceStoneWeight { get; }

    private void EnsureSameShape(float[,] values, string paramName)
    {
        if (values.GetLength(0) != Width || values.GetLength(1) != Height)
            throw new ArgumentException("Local region field maps must share the same dimensions.", paramName);
    }
}
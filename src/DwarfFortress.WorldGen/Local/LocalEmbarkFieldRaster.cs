using System;

namespace DwarfFortress.WorldGen.Local;

public sealed class LocalEmbarkFieldRaster
{
    public LocalEmbarkFieldRaster(
        float[,] elevation,
        float[,] surfaceWetness,
        float[,] soilDepth,
        float[,] groundwater,
        float[,] slope,
        float[,] drainage,
        float[,] channelPotential,
        float[,] floodplainPotential,
        float[,] vegetationPotential,
        float[,] canopyPotential,
        float[,] understoryPotential,
        float[,] vegetationNeighborhoodSupport,
        float[,] exposedGroundPotential,
        float[,] surfaceGrassWeight,
        float[,] surfaceSoilWeight,
        float[,] surfaceSandWeight,
        float[,] surfaceMudWeight,
        float[,] surfaceSnowWeight,
        float[,] surfaceStoneWeight)
    {
        Width = elevation.GetLength(0);
        Height = elevation.GetLength(1);

        EnsureSameShape(surfaceWetness, nameof(surfaceWetness));
        EnsureSameShape(soilDepth, nameof(soilDepth));
        EnsureSameShape(groundwater, nameof(groundwater));
        EnsureSameShape(slope, nameof(slope));
        EnsureSameShape(drainage, nameof(drainage));
        EnsureSameShape(channelPotential, nameof(channelPotential));
        EnsureSameShape(floodplainPotential, nameof(floodplainPotential));
        EnsureSameShape(vegetationPotential, nameof(vegetationPotential));
        EnsureSameShape(canopyPotential, nameof(canopyPotential));
        EnsureSameShape(understoryPotential, nameof(understoryPotential));
        EnsureSameShape(vegetationNeighborhoodSupport, nameof(vegetationNeighborhoodSupport));
        EnsureSameShape(exposedGroundPotential, nameof(exposedGroundPotential));
        EnsureSameShape(surfaceGrassWeight, nameof(surfaceGrassWeight));
        EnsureSameShape(surfaceSoilWeight, nameof(surfaceSoilWeight));
        EnsureSameShape(surfaceSandWeight, nameof(surfaceSandWeight));
        EnsureSameShape(surfaceMudWeight, nameof(surfaceMudWeight));
        EnsureSameShape(surfaceSnowWeight, nameof(surfaceSnowWeight));
        EnsureSameShape(surfaceStoneWeight, nameof(surfaceStoneWeight));

        Elevation = elevation;
        SurfaceWetness = surfaceWetness;
        SoilDepth = soilDepth;
        Groundwater = groundwater;
        Slope = slope;
        Drainage = drainage;
        ChannelPotential = channelPotential;
        FloodplainPotential = floodplainPotential;
        VegetationPotential = vegetationPotential;
        CanopyPotential = canopyPotential;
        UnderstoryPotential = understoryPotential;
        VegetationNeighborhoodSupport = vegetationNeighborhoodSupport;
        ExposedGroundPotential = exposedGroundPotential;
        SurfaceGrassWeight = surfaceGrassWeight;
        SurfaceSoilWeight = surfaceSoilWeight;
        SurfaceSandWeight = surfaceSandWeight;
        SurfaceMudWeight = surfaceMudWeight;
        SurfaceSnowWeight = surfaceSnowWeight;
        SurfaceStoneWeight = surfaceStoneWeight;
    }

    public int Width { get; }
    public int Height { get; }
    public float[,] Elevation { get; }
    public float[,] SurfaceWetness { get; }
    public float[,] SoilDepth { get; }
    public float[,] Groundwater { get; }
    public float[,] Slope { get; }
    public float[,] Drainage { get; }
    public float[,] ChannelPotential { get; }
    public float[,] FloodplainPotential { get; }
    public float[,] VegetationPotential { get; }
    public float[,] CanopyPotential { get; }
    public float[,] UnderstoryPotential { get; }
    public float[,] VegetationNeighborhoodSupport { get; }
    public float[,] ExposedGroundPotential { get; }
    public float[,] SurfaceGrassWeight { get; }
    public float[,] SurfaceSoilWeight { get; }
    public float[,] SurfaceSandWeight { get; }
    public float[,] SurfaceMudWeight { get; }
    public float[,] SurfaceSnowWeight { get; }
    public float[,] SurfaceStoneWeight { get; }

    public int GetFingerprint()
    {
        var hash = 17;
        hash = Mix(hash, Width);
        hash = Mix(hash, Height);
        hash = MixMap(hash, Elevation);
        hash = MixMap(hash, SurfaceWetness);
        hash = MixMap(hash, SoilDepth);
        hash = MixMap(hash, Groundwater);
        hash = MixMap(hash, Slope);
        hash = MixMap(hash, Drainage);
        hash = MixMap(hash, ChannelPotential);
        hash = MixMap(hash, FloodplainPotential);
        hash = MixMap(hash, VegetationPotential);
        hash = MixMap(hash, CanopyPotential);
        hash = MixMap(hash, UnderstoryPotential);
        hash = MixMap(hash, VegetationNeighborhoodSupport);
        hash = MixMap(hash, ExposedGroundPotential);
        hash = MixMap(hash, SurfaceGrassWeight);
        hash = MixMap(hash, SurfaceSoilWeight);
        hash = MixMap(hash, SurfaceSandWeight);
        hash = MixMap(hash, SurfaceMudWeight);
        hash = MixMap(hash, SurfaceSnowWeight);
        hash = MixMap(hash, SurfaceStoneWeight);
        return hash;
    }

    private void EnsureSameShape(float[,] values, string paramName)
    {
        if (values.GetLength(0) != Width || values.GetLength(1) != Height)
            throw new ArgumentException("Local embark field rasters must share the same dimensions.", paramName);
    }

    private static int MixMap(int hash, float[,] values)
    {
        for (var x = 0; x < values.GetLength(0); x++)
        for (var y = 0; y < values.GetLength(1); y++)
            hash = Mix(hash, Quantize(values[x, y]));

        return hash;
    }

    private static int Quantize(float value)
        => (int)MathF.Round(value * 10000f);

    private static int Mix(int hash, int value)
        => unchecked((hash * 31) + value);
}
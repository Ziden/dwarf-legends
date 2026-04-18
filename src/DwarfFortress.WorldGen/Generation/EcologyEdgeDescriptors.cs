namespace DwarfFortress.WorldGen.Generation;

public readonly record struct EcologyEdgeProfile(
    float VegetationDensity,
    float VegetationSuitability,
    float SoilDepth,
    float Groundwater)
{
    public static EcologyEdgeProfile Neutral => new(0.5f, 0.5f, 0.5f, 0.5f);

    public static EcologyEdgeProfile Blend(EcologyEdgeProfile a, EcologyEdgeProfile b)
        => new(
            VegetationDensity: (a.VegetationDensity + b.VegetationDensity) * 0.5f,
            VegetationSuitability: (a.VegetationSuitability + b.VegetationSuitability) * 0.5f,
            SoilDepth: (a.SoilDepth + b.SoilDepth) * 0.5f,
            Groundwater: (a.Groundwater + b.Groundwater) * 0.5f);
}

public readonly record struct EcologyEdgeDescriptors(
    EcologyEdgeProfile North,
    EcologyEdgeProfile East,
    EcologyEdgeProfile South,
    EcologyEdgeProfile West)
{
    public static EcologyEdgeDescriptors Neutral => new(
        EcologyEdgeProfile.Neutral,
        EcologyEdgeProfile.Neutral,
        EcologyEdgeProfile.Neutral,
        EcologyEdgeProfile.Neutral);
}
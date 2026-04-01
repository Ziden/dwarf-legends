using DwarfFortress.WorldGen.Ids;

namespace DwarfFortress.WorldGen.Regions;

public readonly record struct GeneratedRegionTile(
    string BiomeVariantId,
    byte Slope,
    bool HasRiver,
    bool HasLake,
    float VegetationDensity,
    float ResourceRichness,
    float SoilDepth,
    float Groundwater,
    bool HasRoad,
    bool HasSettlement,
    string GeologyProfileId = GeologyProfileIds.MixedBedrock,
    RegionRiverEdges RiverEdges = RegionRiverEdges.None,
    float RiverDischarge = 0f,
    byte RiverOrder = 0,
    RegionRoadEdges RoadEdges = RegionRoadEdges.None,
    float VegetationSuitability = 0.5f,
    float TemperatureBand = 0.5f,
    float MoistureBand = 0.5f,
    float FlowAccumulationBand = 0f,
    string SurfaceClassId = RegionSurfaceClassIds.Grass)
{
    public static GeneratedRegionTile Empty => new(
        BiomeVariantId: RegionBiomeVariantIds.TemperatePlainsOpen,
        SurfaceClassId: RegionSurfaceClassIds.Grass,
        Slope: 0,
        HasRiver: false,
        HasLake: false,
        VegetationDensity: 0.5f,
        ResourceRichness: 0.5f,
        SoilDepth: 0.5f,
        Groundwater: 0.5f,
        HasRoad: false,
        HasSettlement: false,
        GeologyProfileId: GeologyProfileIds.MixedBedrock,
        RiverEdges: RegionRiverEdges.None,
        RiverDischarge: 0f,
        RiverOrder: 0,
        RoadEdges: RegionRoadEdges.None,
        VegetationSuitability: 0.5f,
        TemperatureBand: 0.5f,
        MoistureBand: 0.5f,
        FlowAccumulationBand: 0f);
}

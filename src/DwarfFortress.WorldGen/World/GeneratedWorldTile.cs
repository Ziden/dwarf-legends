using DwarfFortress.WorldGen.Ids;

namespace DwarfFortress.WorldGen.World;

public readonly record struct GeneratedWorldTile(
    string MacroBiomeId,
    float ElevationBand,
    float TemperatureBand,
    float MoistureBand,
    float DrainageBand,
    string GeologyProfileId,
    float FactionPressure,
    float FlowAccumulation = 0f,
    bool HasRiver = false,
    WorldRiverEdges RiverEdges = WorldRiverEdges.None,
    int RiverBasinId = 0,
    float RiverDischarge = 0f,
    byte RiverOrder = 0,
    float ForestCover = 0f,
    float Relief = 0f,
    float MountainCover = 0f,
    bool HasRoad = false,
    WorldRoadEdges RoadEdges = WorldRoadEdges.None)
{
    public static GeneratedWorldTile Empty => new(
        MacroBiomeId: MacroBiomeIds.TemperatePlains,
        ElevationBand: 0.5f,
        TemperatureBand: 0.5f,
        MoistureBand: 0.5f,
        DrainageBand: 0.5f,
        GeologyProfileId: GeologyProfileIds.MixedBedrock,
        FactionPressure: 0.5f,
        FlowAccumulation: 0f,
        HasRiver: false,
        RiverEdges: WorldRiverEdges.None,
        RiverBasinId: 0,
        RiverDischarge: 0f,
        RiverOrder: 0,
        ForestCover: 0.5f,
        Relief: 0.5f,
        MountainCover: 0f,
        HasRoad: false,
        RoadEdges: WorldRoadEdges.None);
}

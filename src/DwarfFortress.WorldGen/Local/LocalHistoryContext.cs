using System;
using DwarfFortress.WorldGen.Generation;

namespace DwarfFortress.WorldGen.Local;

public readonly record struct LocalHistoryContext(
    string? OwnerCivilizationId,
    string? TerritoryOwnerCivilizationId,
    LocalHistorySite? PrimarySite,
    LocalHistorySite[] NearbySites,
    LocalHistoryRoad[] NearbyRoads)
{
    public static LocalHistoryContext Empty => new(
        null,
        null,
        null,
        Array.Empty<LocalHistorySite>(),
        Array.Empty<LocalHistoryRoad>());

    public bool HasContinuity =>
        !string.IsNullOrWhiteSpace(OwnerCivilizationId) ||
        !string.IsNullOrWhiteSpace(TerritoryOwnerCivilizationId) ||
        PrimarySite is not null ||
        NearbySites is { Length: > 0 } ||
        NearbyRoads is { Length: > 0 };
}

public readonly record struct LocalHistorySite(
    string Id,
    string Name,
    string Kind,
    string OwnerCivilizationId,
    int WorldX,
    int WorldY,
    float Development,
    float Security);

public readonly record struct LocalHistoryRoad(
    string Id,
    string OwnerCivilizationId,
    string? FromSiteId,
    string? ToSiteId,
    int DistanceFromEmbark,
    LocalMapEdge[] PortalEdges);
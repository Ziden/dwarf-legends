using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

public sealed class HousingSystem : IGameSystem
{
    public string SystemId => SystemIds.HousingSystem;
    public int UpdateOrder => 5;
    public bool IsEnabled { get; set; } = true;

    private GameContext? _ctx;
    private bool _dirty = true;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        ctx.EventBus.On<BuildingPlacedEvent>(_ => _dirty = true);
        ctx.EventBus.On<BuildingConstructionCompletedEvent>(_ => _dirty = true);
        ctx.EventBus.On<BuildingRemovedEvent>(_ => _dirty = true);
        ctx.EventBus.On<EntitySpawnedEvent>(OnEntitySpawned);
        ctx.EventBus.On<EntityKilledEvent>(_ => _dirty = true);
    }

    public void Tick(float delta)
    {
        if (!_dirty)
            return;

        ReconcileAssignments();
        _dirty = false;
    }

    public void OnSave(SaveWriter w) { }

    public void OnLoad(SaveReader r)
    {
        _dirty = true;
    }

    public IReadOnlyList<Dwarf> GetResidents(int buildingId)
    {
        var registry = _ctx?.TryGet<EntityRegistry>();
        if (registry is null)
            return [];

        return registry.GetAlive<Dwarf>()
            .Where(dwarf => dwarf.Residence.HomeBuildingId == buildingId)
            .OrderBy(dwarf => dwarf.Id)
            .ToArray();
    }

    private void OnEntitySpawned(EntitySpawnedEvent e)
    {
        if (string.Equals(e.DefId, DefIds.Dwarf, System.StringComparison.OrdinalIgnoreCase))
            _dirty = true;
    }

    private void ReconcileAssignments()
    {
        var ctx = _ctx;
        if (ctx is null)
            return;

        var registry = ctx.TryGet<EntityRegistry>();
        var buildingSystem = ctx.TryGet<BuildingSystem>();
        var dataManager = ctx.TryGet<DataManager>();
        var map = ctx.TryGet<WorldMap>();
        if (registry is null || buildingSystem is null || dataManager is null || map is null)
            return;

        var houses = buildingSystem.GetAll()
            .Where(building => building.IsComplete)
            .Select(building => (Building: building, Def: dataManager.Buildings.GetOrNull(building.BuildingDefId)))
            .Where(pair => pair.Def is { ResidenceCapacity: > 0 })
            .Select(pair => new HouseInfo(pair.Building, pair.Def!))
            .OrderBy(pair => pair.Building.Id)
            .ToArray();

        var housesById = houses.ToDictionary(house => house.Building.Id);
        var occupancy = houses.ToDictionary(house => house.Building.Id, _ => new List<Dwarf>());
        var dwarves = registry.GetAlive<Dwarf>().OrderBy(dwarf => dwarf.Id).ToArray();

        foreach (var dwarf in dwarves)
        {
            if (dwarf.Components.TryGet<ResidenceComponent>() is not { } residence || residence.HomeBuildingId < 0)
                continue;

            if (!housesById.TryGetValue(residence.HomeBuildingId, out var house))
            {
                residence.HomeBuildingId = -1;
                continue;
            }

            occupancy[house.Building.Id].Add(dwarf);
        }

        foreach (var house in houses)
        {
            var residents = occupancy[house.Building.Id]
                .OrderBy(dwarf => dwarf.Id)
                .ToArray();

            if (residents.Length <= house.Definition.ResidenceCapacity)
                continue;

            foreach (var displaced in residents.Skip(house.Definition.ResidenceCapacity))
            {
                displaced.Residence.HomeBuildingId = -1;
                occupancy[house.Building.Id].Remove(displaced);
            }
        }

        foreach (var dwarf in dwarves.Where(dwarf => dwarf.Residence.HomeBuildingId < 0))
        {
            var best = ChooseBestHouse(dwarf, houses, occupancy, map);
            if (best is null)
                continue;

            dwarf.Residence.HomeBuildingId = best.Building.Id;
            occupancy[best.Building.Id].Add(dwarf);
        }
    }

    private static HouseInfo? ChooseBestHouse(
        Dwarf dwarf,
        IReadOnlyList<HouseInfo> houses,
        IReadOnlyDictionary<int, List<Dwarf>> occupancy,
        WorldMap map)
    {
        HouseInfo? bestHouse = null;
        var bestOccupancy = int.MaxValue;
        var bestPathLength = int.MaxValue;

        foreach (var house in houses)
        {
            var residentCount = occupancy[house.Building.Id].Count;
            if (residentCount >= house.Definition.ResidenceCapacity)
                continue;

            var pathLength = ResolveReachableEntryDistance(dwarf.Position.Position, house, map);
            if (pathLength is null)
                continue;

            if (residentCount < bestOccupancy ||
                (residentCount == bestOccupancy && pathLength.Value < bestPathLength) ||
                (residentCount == bestOccupancy && pathLength.Value == bestPathLength && (bestHouse is null || house.Building.Id < bestHouse.Building.Id)))
            {
                bestHouse = house;
                bestOccupancy = residentCount;
                bestPathLength = pathLength.Value;
            }
        }

        return bestHouse;
    }

    private static int? ResolveReachableEntryDistance(Vec3i origin, HouseInfo house, WorldMap map)
    {
        var entryPoints = BuildingPlacementGeometry.GetEntryPoints(house.Definition, house.Building.Origin, house.Building.Rotation);
        var best = int.MaxValue;

        foreach (var entry in entryPoints)
        {
            var path = Pathfinder.FindPath(map, origin, entry.TilePosition);
            if (path.Count == 0)
                continue;

            if (path.Count < best)
                best = path.Count;
        }

        return best == int.MaxValue ? null : best;
    }

    private sealed record HouseInfo(PlacedBuildingData Building, BuildingDef Definition);
}

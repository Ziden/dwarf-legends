using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

public static class FortressLocationIds
{
    public const string EmbarkCenter = "embark_center";
    public const string ClosestDrink = "closest_drink";
}

public sealed class FortressLocationSystem : IGameSystem
{
    private const string SaveKey = "fortress_locations";

    private readonly Dictionary<string, Vec3i> _locations = new(StringComparer.Ordinal);
    private GameContext? _ctx;

    public string SystemId => SystemIds.FortressLocationSystem;
    public int UpdateOrder => 2;
    public bool IsEnabled { get; set; } = true;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
    }

    public void Tick(float delta)
    {
    }

    public void OnSave(SaveWriter writer)
    {
        var locations = new List<LocationDto>(_locations.Count);
        foreach (var entry in _locations)
        {
            locations.Add(new LocationDto
            {
                Id = entry.Key,
                X = entry.Value.X,
                Y = entry.Value.Y,
                Z = entry.Value.Z,
            });
        }

        writer.Write(SaveKey, locations);
    }

    public void OnLoad(SaveReader reader)
    {
        _locations.Clear();
        var locations = reader.TryRead<List<LocationDto>>(SaveKey);
        if (locations is null)
            return;

        foreach (var location in locations)
        {
            if (string.IsNullOrWhiteSpace(location.Id))
                continue;

            _locations[location.Id] = new Vec3i(location.X, location.Y, location.Z);
        }
    }

    public IReadOnlyDictionary<string, Vec3i> GetAllLocations() => _locations;

    public void SetLocation(string locationId, Vec3i position)
    {
        _locations[locationId] = position;
    }

    public bool RemoveLocation(string locationId)
        => _locations.Remove(locationId);

    public bool TryGetLocation(string locationId, out Vec3i position)
        => _locations.TryGetValue(locationId, out position);

    public void InitializeDefaultLocations(Vec3i embarkCenter)
    {
        SetLocation(FortressLocationIds.EmbarkCenter, embarkCenter);
        RefreshClosestDrinkLocation();
    }

    public bool RefreshClosestDrinkLocation()
    {
        if (!TryGetLocation(FortressLocationIds.EmbarkCenter, out var embarkCenter))
        {
            RemoveLocation(FortressLocationIds.ClosestDrink);
            return false;
        }

        var map = _ctx?.TryGet<WorldMap>();
        if (map is null)
        {
            RemoveLocation(FortressLocationIds.ClosestDrink);
            return false;
        }

        var searchRadius = Math.Max(map.Width, map.Height);
        if (!DrinkSourceLocator.TryFindNearestDrinkablePosition(map, embarkCenter, searchRadius, out var drinkStand)
            || !DrinkSourceLocator.TryResolveDrinkTile(map, drinkStand, out var closestDrink))
        {
            RemoveLocation(FortressLocationIds.ClosestDrink);
            return false;
        }

        SetLocation(FortressLocationIds.ClosestDrink, closestDrink);
        return true;
    }

    public bool TryGetClosestDrinkLocation(out Vec3i drinkTile)
    {
        drinkTile = default;
        var map = _ctx?.TryGet<WorldMap>();
        if (map is null)
            return false;

        if (_locations.TryGetValue(FortressLocationIds.ClosestDrink, out var storedDrink)
            && DrinkSourceLocator.IsDrinkableWaterTile(map, storedDrink))
        {
            drinkTile = storedDrink;
            return true;
        }

        if (!RefreshClosestDrinkLocation())
            return false;

        return _locations.TryGetValue(FortressLocationIds.ClosestDrink, out drinkTile);
    }

    private sealed class LocationDto
    {
        public string Id { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }
}
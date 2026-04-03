using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

/// <summary>
/// Advances wild vegetation growth, yield cycles, and seed spread.
/// Plants are tile-linked state rather than full entities so large surface maps stay cheap to update.
/// </summary>
public sealed class VegetationSystem : IGameSystem
{
    public string SystemId => SystemIds.VegetationSystem;
    public int UpdateOrder => 6;
    public bool IsEnabled { get; set; } = true;

    private readonly Random _rng = new(87123);
    private readonly HashSet<Vec3i> _trackedPlantTiles = new();
    private readonly List<Vec3i> _processBuffer = new();
    private readonly Dictionary<Vec3i, bool> _nearbyWaterCache = new();
    private GameContext? _ctx;
    private SimulationProfiler? _profiler;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        _profiler = ctx.Profiler;
        ctx.EventBus.On<TileChangedEvent>(OnTileChanged);
        RebuildTrackedPlants();
    }

    public void Tick(float delta)
    {
        if (delta <= 0f)
            return;

        var map = _ctx!.Get<WorldMap>();
        var data = _ctx.Get<DataManager>();
        if (data.Plants.Count == 0 || map.Depth <= 0 || _trackedPlantTiles.Count == 0)
            return;

        using (_profiler?.Measure("collect_active_plants") ?? default)
        {
            _processBuffer.Clear();
            _processBuffer.AddRange(_trackedPlantTiles);
            _processBuffer.Sort(ComparePositions);
        }

        using (_profiler?.Measure("process_active_plants") ?? default)
        foreach (var pos in _processBuffer)
        {
            var tile = map.GetTile(pos);
            if (!tile.HasPlant || string.IsNullOrWhiteSpace(tile.PlantDefId))
                continue;

            var tileChanged = false;

            var plantDef = data.Plants.GetOrNull(tile.PlantDefId);
            if (plantDef is null)
            {
                ClearPlantState(ref tile);
                tileChanged = true;
                map.SetTile(pos, tile);
                continue;
            }

            if (!IsHostStillValid(tile, plantDef))
            {
                ClearPlantState(ref tile);
                tileChanged = true;
                map.SetTile(pos, tile);
                continue;
            }

            var growthRate = ResolveGrowthRate(map, pos, plantDef);
            tile.PlantGrowthProgressSeconds += delta * growthRate;
            tileChanged = true;

            if (tile.PlantGrowthStage < plantDef.MaxGrowthStage)
            {
                while (tile.PlantGrowthStage < plantDef.MaxGrowthStage &&
                       tile.PlantGrowthProgressSeconds >= plantDef.SecondsPerStage)
                {
                    tile.PlantGrowthProgressSeconds -= plantDef.SecondsPerStage;
                    tile.PlantGrowthStage++;
                    if (tile.PlantGrowthStage > PlantGrowthStages.Seed)
                        tile.PlantSeedLevel = 0;
                }

                tile.PlantYieldLevel = tile.PlantGrowthStage >= PlantGrowthStages.Mature ? (byte)1 : (byte)0;
                if (tileChanged)
                    map.SetTile(pos, tile);
                continue;
            }

            if (plantDef.FruitItemDefId is not null && tile.PlantYieldLevel == 0 && tile.PlantGrowthProgressSeconds >= plantDef.FruitingCycleSeconds)
            {
                tile.PlantGrowthProgressSeconds = 0f;
                tile.PlantYieldLevel = 1;
            }
            else if (plantDef.FruitItemDefId is null)
            {
                tile.PlantYieldLevel = 1;
            }

            if (plantDef.HostKind == PlantHostKind.Ground)
                TrySpreadSeeds(map, data, pos, tile, plantDef, delta);

            if (tileChanged)
                map.SetTile(pos, tile);
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r)
    {
        _nearbyWaterCache.Clear();
        RebuildTrackedPlants();
    }

    private static bool IsHostStillValid(TileData tile, PlantDef plantDef)
    {
        if (plantDef.HostKind == PlantHostKind.Tree)
        {
            if (tile.TileDefId != TileDefIds.Tree)
                return false;
            if (plantDef.SupportedTreeSpeciesIds.Count == 0)
                return true;

            return tile.TreeSpeciesId is not null && plantDef.SupportedTreeSpeciesIds.Contains(tile.TreeSpeciesId, StringComparer.OrdinalIgnoreCase);
        }

        if (tile.FluidType != FluidType.None || tile.FluidLevel > 0)
            return false;
        if (!tile.IsPassable)
            return false;
        if (plantDef.AllowedGroundTileDefIds.Count == 0)
            return true;

        return plantDef.AllowedGroundTileDefIds.Contains(tile.TileDefId, StringComparer.OrdinalIgnoreCase);
    }

    private float ResolveGrowthRate(WorldMap map, Vec3i pos, PlantDef plantDef)
    {
        var nearbyWater = HasWaterWithinRadius(map, pos, 2);
        var rate = 1f;

        if (plantDef.PrefersNearWater)
            rate *= nearbyWater ? 1.18f : 0.65f;

        if (plantDef.PrefersFarFromWater)
            rate *= nearbyWater ? 0.72f : 1.08f;

        return Math.Clamp(rate, 0.35f, 1.35f);
    }

    private void TrySpreadSeeds(WorldMap map, DataManager data, Vec3i origin, TileData tile, PlantDef plantDef, float delta)
    {
        // No floor — the rate is proportional to 1/FruitingCycleSeconds so it scales correctly
        // regardless of tick size, producing ~SeedSpreadChance seeds per fruiting cycle on average.
        var chance = plantDef.SeedSpreadChance * (delta / Math.Max(1f, plantDef.FruitingCycleSeconds));
        if (_rng.NextDouble() > chance)
            return;

        var minRadius = Math.Max(1, plantDef.SeedSpreadRadiusMin);
        var maxRadius = Math.Max(minRadius + 1, plantDef.SeedSpreadRadiusMax);

        // Check local plant density before spreading — skip if the area is already saturated.
        // This prevents large uniform carpets of the same plant from forming over time.
        var nearbyPlantCount = CountNearbyPlants(map, origin, maxRadius);
        var maxNearbyPlants = 4; // Allow at most 4 plants within spread radius
        if (nearbyPlantCount >= maxNearbyPlants)
            return;

        var placed = false;

        for (var attempt = 0; attempt < 12 && !placed; attempt++)
        {
            // Pick a random distance in [minRadius, maxRadius] and a random angle so seeds
            // are distributed uniformly across the full spread ring, not clustered near the origin.
            var dist = minRadius + _rng.NextDouble() * (maxRadius - minRadius);
            var angle = _rng.NextDouble() * Math.PI * 2.0;
            var dx = (int)Math.Round(dist * Math.Cos(angle));
            var dy = (int)Math.Round(dist * Math.Sin(angle));
            if (dx == 0 && dy == 0)
                continue;

            var targetPos = new Vec3i(origin.X + dx, origin.Y + dy, origin.Z);
            if (!map.IsInBounds(targetPos))
                continue;

            var target = map.GetTile(targetPos);
            if (target.HasPlant)
                continue;
            if (!IsHostStillValid(target, plantDef))
                continue;

            // Double-check: don't place a seed if it would be adjacent to too many plants of the same type.
            if (CountNearbyPlantsOfKind(map, targetPos, plantDef.Id, radius: 2) >= 2)
                continue;

            target.PlantDefId = plantDef.Id;
            target.PlantGrowthStage = PlantGrowthStages.Seed;
            target.PlantGrowthProgressSeconds = 0f;
            target.PlantYieldLevel = 0;
            target.PlantSeedLevel = 1;
            map.SetTile(targetPos, target);
            placed = true;
        }

        if (placed)
            tile.PlantSeedLevel = 1;
    }

    private static int CountNearbyPlants(WorldMap map, Vec3i origin, int radius)
    {
        var count = 0;
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            if (dx == 0 && dy == 0)
                continue;
            var pos = new Vec3i(origin.X + dx, origin.Y + dy, origin.Z);
            if (!map.IsInBounds(pos))
                continue;
            if (map.GetTile(pos).HasPlant)
                count++;
        }
        return count;
    }

    private static int CountNearbyPlantsOfKind(WorldMap map, Vec3i origin, string plantDefId, int radius)
    {
        var count = 0;
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            if (dx == 0 && dy == 0)
                continue;
            var pos = new Vec3i(origin.X + dx, origin.Y + dy, origin.Z);
            if (!map.IsInBounds(pos))
                continue;
            var t = map.GetTile(pos);
            if (t.HasPlant && string.Equals(t.PlantDefId, plantDefId, StringComparison.OrdinalIgnoreCase))
                count++;
        }
        return count;
    }

    private void OnTileChanged(TileChangedEvent e)
    {
        UpdateTrackedPlant(e.Pos, e.NewTile);

        if (HasWaterSignature(e.OldTile) == HasWaterSignature(e.NewTile))
            return;

        for (var dx = -2; dx <= 2; dx++)
        for (var dy = -2; dy <= 2; dy++)
            _nearbyWaterCache.Remove(new Vec3i(e.Pos.X + dx, e.Pos.Y + dy, e.Pos.Z));
    }

    private void RebuildTrackedPlants()
    {
        _trackedPlantTiles.Clear();

        var map = _ctx?.TryGet<WorldMap>();
        if (map is null || map.Depth <= 0)
            return;

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var pos = new Vec3i(x, y, 0);
            var tile = map.GetTile(pos);
            if (tile.HasPlant)
                _trackedPlantTiles.Add(pos);
        }
    }

    private void UpdateTrackedPlant(Vec3i pos, TileData tile)
    {
        if (tile.HasPlant)
            _trackedPlantTiles.Add(pos);
        else
            _trackedPlantTiles.Remove(pos);
    }

    private static bool HasWaterSignature(TileData tile)
        => (tile.FluidType == FluidType.Water && tile.FluidLevel > 0) || tile.TileDefId == TileDefIds.Water;

    private static int ComparePositions(Vec3i left, Vec3i right)
    {
        var x = left.X.CompareTo(right.X);
        if (x != 0)
            return x;

        var y = left.Y.CompareTo(right.Y);
        if (y != 0)
            return y;

        return left.Z.CompareTo(right.Z);
    }

    private bool HasWaterWithinRadius(WorldMap map, Vec3i pos, int radius)
    {
        if (_nearbyWaterCache.TryGetValue(pos, out var cached))
            return cached;

        var hasWater = HasWaterWithinRadiusSlow(map, pos, radius);
        _nearbyWaterCache[pos] = hasWater;
        return hasWater;
    }

    private static bool HasWaterWithinRadiusSlow(WorldMap map, Vec3i pos, int radius)
    {
        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            if (dx == 0 && dy == 0)
                continue;

            var sample = new Vec3i(pos.X + dx, pos.Y + dy, pos.Z);
            if (!map.IsInBounds(sample))
                continue;

            var tile = map.GetTile(sample);
            if (tile.FluidType == FluidType.Water && tile.FluidLevel > 0)
                return true;
            if (tile.TileDefId == TileDefIds.Water)
                return true;
        }

        return false;
    }

    private static void ClearPlantState(ref TileData tile)
    {
        tile.PlantDefId = null;
        tile.PlantGrowthStage = 0;
        tile.PlantGrowthProgressSeconds = 0f;
        tile.PlantYieldLevel = 0;
        tile.PlantSeedLevel = 0;
    }
}
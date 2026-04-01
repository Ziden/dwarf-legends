using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.History;
using DwarfFortress.WorldGen.Local;
using DwarfFortress.WorldGen.Maps;
using DwarfFortress.WorldGen.Regions;
using DwarfFortress.WorldGen.World;

namespace DwarfFortress.GameLogic.World;

public readonly record struct MapGenerationSettings(
    int WorldWidth,
    int WorldHeight,
    int RegionWidth,
    int RegionHeight,
    bool EnableHistory = true,
    int SimulatedHistoryYears = 125)
{
    public static MapGenerationSettings Default => new(
        WorldWidth: 64,
        WorldHeight: 64,
        RegionWidth: 32,
        RegionHeight: 32,
        EnableHistory: true,
        SimulatedHistoryYears: 125);
}

public readonly record struct GeneratedEmbarkContext(
    int Seed,
    WorldCoord WorldCoord,
    RegionCoord RegionCoord,
    string MacroBiomeId,
    string RegionBiomeVariantId,
    string EffectiveBiomeId);

public interface IMapGenerationService
{
    GeneratedWorldMap GetOrCreateWorld(int seed, MapGenerationSettings settings);
    GeneratedWorldHistory? GetOrCreateHistory(int seed, MapGenerationSettings settings);
    GeneratedRegionMap GetOrCreateRegion(int seed, WorldCoord worldCoord, MapGenerationSettings settings);
    GeneratedEmbarkMap GetOrCreateLocal(int seed, RegionCoord regionCoord, LocalGenerationSettings settings, MapGenerationSettings generationSettings);
    RegionCoord ResolveDefaultRegionCoord(int seed, MapGenerationSettings settings);
    GeneratedEmbarkContext GenerateAndApplyEmbark(
        WorldMap targetMap,
        int seed,
        LocalGenerationSettings settings,
        string? biomeOverrideId = null,
        MapGenerationSettings? generationSettings = null);
}

/// <summary>
/// Caches and orchestrates hierarchical world -> region -> local generation.
/// </summary>
public sealed class MapGenerationService : IGameSystem, IMapGenerationService
{
    private const string LastContextSaveKey = "map_generation_last_context";
    private const int HistorySeedSalt = 74119;

    private readonly IWorldLayerGenerator _worldGenerator;
    private readonly IHistorySimulator _historySimulator;
    private readonly IRegionLayerGenerator _regionGenerator;
    private readonly ILocalLayerGenerator _localGenerator;

    private readonly Dictionary<WorldCacheKey, GeneratedWorldMap> _worldCache = new();
    private readonly Dictionary<HistoryCacheKey, GeneratedWorldHistory> _historyCache = new();
    private readonly Dictionary<RegionCacheKey, GeneratedRegionMap> _regionCache = new();
    private readonly Dictionary<LocalCacheKey, GeneratedEmbarkMap> _localCache = new();

    public string SystemId => SystemIds.MapGenerationService;
    public int UpdateOrder => 2;
    public bool IsEnabled { get; set; } = true;

    public GeneratedEmbarkContext? LastGeneratedEmbark { get; private set; }
    public GeneratedEmbarkMap? LastGeneratedLocalMap { get; private set; }
    public GeneratedWorldHistory? LastGeneratedHistory { get; private set; }

    public MapGenerationService(
        IWorldLayerGenerator? worldGenerator = null,
        IHistorySimulator? historySimulator = null,
        IRegionLayerGenerator? regionGenerator = null,
        ILocalLayerGenerator? localGenerator = null)
    {
        _worldGenerator = worldGenerator ?? new WorldLayerGenerator();
        _historySimulator = historySimulator ?? new HistorySimulator();
        _regionGenerator = regionGenerator ?? new RegionLayerGenerator();
        _localGenerator = localGenerator ?? new LocalLayerGenerator();
    }

    public void Initialize(GameContext ctx) { }
    public void Tick(float delta) { }

    public void OnSave(SaveWriter writer)
    {
        if (LastGeneratedEmbark is { } context)
            writer.Write(LastContextSaveKey, context);
    }

    public void OnLoad(SaveReader reader)
    {
        ClearCaches();
        LastGeneratedEmbark = reader.TryRead<GeneratedEmbarkContext>(LastContextSaveKey);
        LastGeneratedLocalMap = null;
        LastGeneratedHistory = null;
    }

    public GeneratedWorldMap GetOrCreateWorld(int seed, MapGenerationSettings settings)
    {
        ValidateSettings(settings);
        var key = new WorldCacheKey(seed, settings.WorldWidth, settings.WorldHeight);

        if (_worldCache.TryGetValue(key, out var cached))
            return cached;

        var generated = _worldGenerator.Generate(seed, settings.WorldWidth, settings.WorldHeight);
        _worldCache[key] = generated;
        return generated;
    }

    public GeneratedWorldHistory? GetOrCreateHistory(int seed, MapGenerationSettings settings)
    {
        ValidateSettings(settings);
        if (!settings.EnableHistory || settings.SimulatedHistoryYears <= 0)
            return null;

        var key = new HistoryCacheKey(
            seed,
            settings.WorldWidth,
            settings.WorldHeight,
            settings.SimulatedHistoryYears);
        if (_historyCache.TryGetValue(key, out var cached))
            return cached;

        var world = GetOrCreateWorld(seed, settings);
        var historySeed = MixSeed(seed, settings.WorldWidth, settings.WorldHeight, HistorySeedSalt);
        var generated = _historySimulator.Simulate(
            world,
            historySeed,
            simulatedYearsOverride: settings.SimulatedHistoryYears);

        _historyCache[key] = generated;
        return generated;
    }

    public GeneratedRegionMap GetOrCreateRegion(int seed, WorldCoord worldCoord, MapGenerationSettings settings)
    {
        ValidateSettings(settings);
        var world = GetOrCreateWorld(seed, settings);
        if (worldCoord.X < 0 || worldCoord.X >= world.Width || worldCoord.Y < 0 || worldCoord.Y >= world.Height)
            throw new ArgumentOutOfRangeException(nameof(worldCoord), "World coordinate is outside world bounds.");

        var key = new RegionCacheKey(
            seed,
            settings.WorldWidth,
            settings.WorldHeight,
            worldCoord.X,
            worldCoord.Y,
            settings.RegionWidth,
            settings.RegionHeight,
            settings.EnableHistory,
            settings.EnableHistory ? settings.SimulatedHistoryYears : 0);

        if (_regionCache.TryGetValue(key, out var cached))
            return cached;

        var history = GetOrCreateHistory(seed, settings);
        var generated = _regionGenerator.Generate(world, worldCoord, settings.RegionWidth, settings.RegionHeight, history);
        _regionCache[key] = generated;
        return generated;
    }

    public GeneratedEmbarkMap GetOrCreateLocal(
        int seed,
        RegionCoord regionCoord,
        LocalGenerationSettings settings,
        MapGenerationSettings generationSettings)
    {
        ValidateSettings(generationSettings);
        if (settings.Width <= 0) throw new ArgumentOutOfRangeException(nameof(settings.Width));
        if (settings.Height <= 0) throw new ArgumentOutOfRangeException(nameof(settings.Height));
        if (settings.Depth <= 0) throw new ArgumentOutOfRangeException(nameof(settings.Depth));

        var worldCoord = new WorldCoord(regionCoord.WorldX, regionCoord.WorldY);
        var region = GetOrCreateRegion(seed, worldCoord, generationSettings);
        if (regionCoord.RegionX < 0 || regionCoord.RegionX >= region.Width ||
            regionCoord.RegionY < 0 || regionCoord.RegionY >= region.Height)
            throw new ArgumentOutOfRangeException(nameof(regionCoord), "Region coordinate is outside region bounds.");

        var key = LocalCacheKey.From(seed, generationSettings, regionCoord, settings);
        if (_localCache.TryGetValue(key, out var cached))
            return cached;

        var generated = _localGenerator.Generate(region, regionCoord, settings);
        _localCache[key] = generated;
        return generated;
    }

    public RegionCoord ResolveDefaultRegionCoord(int seed, MapGenerationSettings settings)
    {
        var world = GetOrCreateWorld(seed, settings);
        var wx = PositiveMod(seed * 31 + 17, world.Width);
        var wy = PositiveMod(seed * 17 + 31, world.Height);

        var region = GetOrCreateRegion(seed, new WorldCoord(wx, wy), settings);
        var rx = PositiveMod(seed * 13 + wx * 7, region.Width);
        var ry = PositiveMod(seed * 19 + wy * 11, region.Height);

        return new RegionCoord(wx, wy, rx, ry);
    }

    public GeneratedEmbarkContext GenerateAndApplyEmbark(
        WorldMap targetMap,
        int seed,
        LocalGenerationSettings settings,
        string? biomeOverrideId = null,
        MapGenerationSettings? generationSettings = null)
    {
        if (targetMap is null) throw new ArgumentNullException(nameof(targetMap));
        var resolvedGenerationSettings = generationSettings ?? MapGenerationSettings.Default;
        var regionCoord = ResolveDefaultRegionCoord(seed, resolvedGenerationSettings);

        var effectiveSettings = settings with
        {
            BiomeOverrideId = biomeOverrideId ?? settings.BiomeOverrideId,
        };

        var localMap = GetOrCreateLocal(seed, regionCoord, effectiveSettings, resolvedGenerationSettings);
        WorldGenerator.ApplyGeneratedEmbark(targetMap, localMap);

        var world = GetOrCreateWorld(seed, resolvedGenerationSettings);
        var region = GetOrCreateRegion(seed, new WorldCoord(regionCoord.WorldX, regionCoord.WorldY), resolvedGenerationSettings);
        var worldTile = world.GetTile(regionCoord.WorldX, regionCoord.WorldY);
        var regionTile = region.GetTile(regionCoord.RegionX, regionCoord.RegionY);

        var context = new GeneratedEmbarkContext(
            Seed: seed,
            WorldCoord: new WorldCoord(regionCoord.WorldX, regionCoord.WorldY),
            RegionCoord: regionCoord,
            MacroBiomeId: worldTile.MacroBiomeId,
            RegionBiomeVariantId: regionTile.BiomeVariantId,
            EffectiveBiomeId: effectiveSettings.BiomeOverrideId ?? worldTile.MacroBiomeId);

        LastGeneratedEmbark = context;
        LastGeneratedLocalMap = localMap;
        LastGeneratedHistory = GetOrCreateHistory(seed, resolvedGenerationSettings);
        return context;
    }

    private void ClearCaches()
    {
        _worldCache.Clear();
        _historyCache.Clear();
        _regionCache.Clear();
        _localCache.Clear();
        LastGeneratedLocalMap = null;
        LastGeneratedHistory = null;
    }

    private static void ValidateSettings(MapGenerationSettings settings)
    {
        if (settings.WorldWidth <= 0) throw new ArgumentOutOfRangeException(nameof(settings.WorldWidth));
        if (settings.WorldHeight <= 0) throw new ArgumentOutOfRangeException(nameof(settings.WorldHeight));
        if (settings.RegionWidth <= 0) throw new ArgumentOutOfRangeException(nameof(settings.RegionWidth));
        if (settings.RegionHeight <= 0) throw new ArgumentOutOfRangeException(nameof(settings.RegionHeight));
        if (settings.SimulatedHistoryYears < 0) throw new ArgumentOutOfRangeException(nameof(settings.SimulatedHistoryYears));
    }

    private static int PositiveMod(int value, int modulus)
    {
        if (modulus <= 0) throw new ArgumentOutOfRangeException(nameof(modulus));
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static int MixSeed(int a, int b, int c, int d)
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + a;
            hash = (hash * 31) + b;
            hash = (hash * 31) + c;
            hash = (hash * 31) + d;
            return hash;
        }
    }

    private readonly record struct WorldCacheKey(int Seed, int Width, int Height);
    private readonly record struct HistoryCacheKey(int Seed, int WorldWidth, int WorldHeight, int SimulatedYears);

    private readonly record struct RegionCacheKey(
        int Seed,
        int WorldWidth,
        int WorldHeight,
        int WorldX,
        int WorldY,
        int RegionWidth,
        int RegionHeight,
        bool HistoryEnabled,
        int HistoryYears);

    private readonly record struct LocalCacheKey(
        int Seed,
        int WorldWidth,
        int WorldHeight,
        int RegionWidth,
        int RegionHeight,
        bool HistoryEnabled,
        int HistoryYears,
        int WorldX,
        int WorldY,
        int RegionX,
        int RegionY,
        int Width,
        int Height,
        int Depth,
        string? BiomeOverrideId,
        int TreeBiasMilli,
        int OutcropBiasMilli,
        int StreamBandBias,
        int MarshPoolBias,
        int WetnessBiasMilli,
        int SoilDepthBiasMilli,
        int ForestPatchBiasMilli,
        int SettlementInfluenceMilli,
        int RoadInfluenceMilli,
        int StoneSurfaceMode)
    {
        public static LocalCacheKey From(
            int seed,
            MapGenerationSettings generationSettings,
            RegionCoord regionCoord,
            LocalGenerationSettings localSettings)
            => new(
                seed,
                generationSettings.WorldWidth,
                generationSettings.WorldHeight,
                generationSettings.RegionWidth,
                generationSettings.RegionHeight,
                generationSettings.EnableHistory,
                generationSettings.EnableHistory ? generationSettings.SimulatedHistoryYears : 0,
                regionCoord.WorldX,
                regionCoord.WorldY,
                regionCoord.RegionX,
                regionCoord.RegionY,
                localSettings.Width,
                localSettings.Height,
                localSettings.Depth,
                localSettings.BiomeOverrideId,
                Quantize(localSettings.TreeDensityBias),
                Quantize(localSettings.OutcropBias),
                localSettings.StreamBandBias,
                localSettings.MarshPoolBias,
                Quantize(localSettings.ParentWetnessBias),
                Quantize(localSettings.ParentSoilDepthBias),
                Quantize(localSettings.ForestPatchBias),
                Quantize(localSettings.SettlementInfluence),
                Quantize(localSettings.RoadInfluence),
                localSettings.StoneSurfaceOverride switch
                {
                    true => 1,
                    false => 0,
                    _ => -1,
                });

        private static int Quantize(float value)
            => (int)MathF.Round(Math.Clamp(value, -1f, 1f) * 1000f);
    }
}

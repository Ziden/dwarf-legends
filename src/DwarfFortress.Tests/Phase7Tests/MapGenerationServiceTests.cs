using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Local;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class MapGenerationServiceTests
{
    [Fact]
    public void GetOrCreate_Caches_World_Region_And_Local_Maps()
    {
        var service = new MapGenerationService();
        var generationSettings = MapGenerationSettings.Default;
        var worldA = service.GetOrCreateWorld(seed: 42, settings: generationSettings);
        var worldB = service.GetOrCreateWorld(seed: 42, settings: generationSettings);
        Assert.Same(worldA, worldB);

        var regionCoord = service.ResolveDefaultRegionCoord(seed: 42, settings: generationSettings);
        var worldCoord = new WorldCoord(regionCoord.WorldX, regionCoord.WorldY);
        var regionA = service.GetOrCreateRegion(seed: 42, worldCoord: worldCoord, settings: generationSettings);
        var regionB = service.GetOrCreateRegion(seed: 42, worldCoord: worldCoord, settings: generationSettings);
        Assert.Same(regionA, regionB);

        var localSettings = new LocalGenerationSettings(48, 48, 8);
        var localA = service.GetOrCreateLocal(
            seed: 42,
            regionCoord: regionCoord,
            settings: localSettings,
            generationSettings: generationSettings);
        var localB = service.GetOrCreateLocal(
            seed: 42,
            regionCoord: regionCoord,
            settings: localSettings,
            generationSettings: generationSettings);
        Assert.Same(localA, localB);
    }

    [Fact]
    public void GenerateAndApplyEmbark_PopulatesWorldMap_And_TracksContext()
    {
        var map = new WorldMap();
        var service = new MapGenerationService();
        var localSettings = new LocalGenerationSettings(48, 48, 8, BiomeOverrideId: MacroBiomeIds.ConiferForest);

        var context = service.GenerateAndApplyEmbark(
            targetMap: map,
            seed: 77,
            settings: localSettings,
            biomeOverrideId: MacroBiomeIds.ConiferForest);

        Assert.Equal(48, map.Width);
        Assert.Equal(48, map.Height);
        Assert.Equal(8, map.Depth);
        Assert.Equal(MacroBiomeIds.ConiferForest, context.EffectiveBiomeId);
        Assert.True(service.LastGeneratedEmbark.HasValue);
        Assert.Equal(context, service.LastGeneratedEmbark!.Value);
        Assert.NotNull(service.LastGeneratedLocalMap);
        Assert.NotNull(service.LastGeneratedHistory);

        var centerTile = map.GetTile(new Vec3i(map.Width / 2, map.Height / 2, 0));
        Assert.NotEqual(TileDefIds.Empty, centerTile.TileDefId);
    }

    [Fact]
    public void GetOrCreateLocal_GeneratesBiomeDrivenCreatureSpawns()
    {
        var service = new MapGenerationService();
        var generationSettings = MapGenerationSettings.Default;
        var regionCoord = service.ResolveDefaultRegionCoord(seed: 321, settings: generationSettings);
        var localSettings = new LocalGenerationSettings(
            Width: 48,
            Height: 48,
            Depth: 8,
            BiomeOverrideId: MacroBiomeIds.TropicalRainforest);

        var local = service.GetOrCreateLocal(
            seed: 321,
            regionCoord: regionCoord,
            settings: localSettings,
            generationSettings: generationSettings);

        Assert.NotEmpty(local.CreatureSpawns);
    }

    [Fact]
    public void GetOrCreateHistory_WhenEnabled_CachesGeneratedHistory()
    {
        var service = new MapGenerationService();
        var settings = MapGenerationSettings.Default with
        {
            EnableHistory = true,
            SimulatedHistoryYears = 24,
        };

        var a = service.GetOrCreateHistory(seed: 77, settings: settings);
        var b = service.GetOrCreateHistory(seed: 77, settings: settings);

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Same(a, b);
        Assert.Equal(24, a!.SimulatedYears);
        Assert.NotEmpty(a.Civilizations);
    }

    [Fact]
    public void GetOrCreateHistory_WhenDisabled_ReturnsNull()
    {
        var service = new MapGenerationService();
        var settings = MapGenerationSettings.Default with
        {
            EnableHistory = false,
            SimulatedHistoryYears = 120,
        };

        var history = service.GetOrCreateHistory(seed: 77, settings: settings);
        Assert.Null(history);
    }

    [Fact]
    public void GetOrCreateRegion_HistorySettings_AffectCacheIdentity()
    {
        var service = new MapGenerationService();
        var withoutHistory = MapGenerationSettings.Default with
        {
            EnableHistory = false,
        };
        var withHistory = MapGenerationSettings.Default with
        {
            EnableHistory = true,
            SimulatedHistoryYears = 28,
        };

        var worldCoord = new WorldCoord(0, 0);
        var regionNoHistory = service.GetOrCreateRegion(seed: 109, worldCoord: worldCoord, settings: withoutHistory);
        var regionWithHistory = service.GetOrCreateRegion(seed: 109, worldCoord: worldCoord, settings: withHistory);

        Assert.NotSame(regionNoHistory, regionWithHistory);
    }
}

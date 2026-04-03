using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.History;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Local;
using DwarfFortress.WorldGen.Maps;
using DwarfFortress.WorldGen.Regions;
using DwarfFortress.WorldGen.Story;
using DwarfFortress.WorldGen.World;

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

    [Fact]
    public void GenerateAndApplyEmbark_PassesLocalHistoryContinuityToLocalGenerator()
    {
        var capturingLocalGenerator = new CapturingLocalLayerGenerator();
        var historySimulator = new MutableHistorySimulator();
        var service = new MapGenerationService(
            historySimulator: historySimulator,
            localGenerator: capturingLocalGenerator);
        var generationSettings = MapGenerationSettings.Default with
        {
            WorldWidth = 2,
            WorldHeight = 1,
            RegionWidth = 4,
            RegionHeight = 4,
            SimulatedHistoryYears = 12,
        };

        var worldCoord = new WorldCoord(0, 0);
        var neighborCoord = new WorldCoord(1, 0);
        var expectedPortalEdge = LocalMapEdge.East;
        historySimulator.History = new GeneratedWorldHistory
        {
            Seed = 9001,
            SimulatedYears = 12,
            Sites =
            [
                new SiteRecord
                {
                    Id = "site_alpha",
                    Name = "Alpha Hold",
                    Kind = "fortress",
                    OwnerCivilizationId = "civ_alpha",
                    Location = worldCoord,
                    Development = 0.86f,
                    Security = 0.74f,
                },
                new SiteRecord
                {
                    Id = "site_beta",
                    Name = "Beta Hamlet",
                    Kind = "hamlet",
                    OwnerCivilizationId = "civ_alpha",
                    Location = neighborCoord,
                    Development = 0.48f,
                    Security = 0.52f,
                },
            ],
            Roads =
            [
                new RoadRecord
                {
                    Id = "road_alpha_beta",
                    OwnerCivilizationId = "civ_alpha",
                    FromSiteId = "site_alpha",
                    ToSiteId = "site_beta",
                    Path = [worldCoord, neighborCoord],
                },
            ],
            TerritoryByTile = new Dictionary<WorldCoord, string>
            {
                [worldCoord] = "civ_alpha",
            },
        };
        var regionCoord = service.ResolveDefaultRegionCoord(seed: 41, settings: generationSettings);

        var context = service.GenerateAndApplyEmbark(
            targetMap: new WorldMap(),
            seed: 41,
            settings: new LocalGenerationSettings(32, 32, 6),
            generationSettings: generationSettings);

        Assert.True(capturingLocalGenerator.LastHistoryContext is { HasContinuity: true });
        var localHistory = capturingLocalGenerator.LastHistoryContext!.Value;
        Assert.Equal("civ_alpha", localHistory.OwnerCivilizationId);
        Assert.Equal("civ_alpha", localHistory.TerritoryOwnerCivilizationId);
        Assert.True(localHistory.PrimarySite is { Id: "site_alpha" });
        Assert.Contains(localHistory.NearbySites, site => site.Id == "site_beta");
        Assert.Contains(localHistory.NearbyRoads, road =>
            road.Id == "road_alpha_beta" && road.PortalEdges.Contains(expectedPortalEdge));

        Assert.True(context.LocalHistory is { HasContinuity: true });
        Assert.Equal(localHistory.OwnerCivilizationId, context.LocalHistory!.Value.OwnerCivilizationId);
        Assert.Equal(localHistory.PrimarySite!.Value.Id, context.LocalHistory!.Value.PrimarySite!.Value.Id);
    }

    private sealed class MutableHistorySimulator : IHistorySimulator
    {
        public GeneratedWorldHistory History { get; set; } = new();

        public GeneratedWorldHistory Simulate(
            GeneratedWorldMap world,
            int seed,
            WorldLoreConfig? config = null,
            int? simulatedYearsOverride = null)
            => History;

        public GeneratedWorldHistoryTimeline SimulateTimeline(
            GeneratedWorldMap world,
            int seed,
            WorldLoreConfig? config = null,
            int? simulatedYearsOverride = null)
            => throw new NotSupportedException();
    }

    private sealed class CapturingLocalLayerGenerator : ILocalLayerGenerator
    {
        public LocalHistoryContext? LastHistoryContext { get; private set; }

        public GeneratedEmbarkMap Generate(
            GeneratedRegionMap region,
            RegionCoord coord,
            LocalGenerationSettings settings,
            LocalHistoryContext? historyContext = null)
        {
            LastHistoryContext = historyContext;
            return new GeneratedEmbarkMap(settings.Width, settings.Height, settings.Depth);
        }
    }
}

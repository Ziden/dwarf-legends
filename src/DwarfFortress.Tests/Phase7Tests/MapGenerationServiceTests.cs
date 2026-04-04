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

    [Theory]
    [InlineData(7)]
    [InlineData(17)]
    [InlineData(42)]
    public void GenerateAndApplyEmbark_Selects_Site_With_Nearby_Water_And_Food(int seed)
    {
        var map = new WorldMap();
        var service = new MapGenerationService();

        service.GenerateAndApplyEmbark(
            targetMap: map,
            seed: seed,
            settings: new LocalGenerationSettings(48, 48, 8));

        Assert.NotNull(service.LastGeneratedLocalMap);
        var survey = SurveyEmbarkResources(service.LastGeneratedLocalMap!);

        Assert.True(survey.HasNearbyWater, $"Expected nearby water for seed {seed}.");
        Assert.True(survey.NearbyFoodSources >= 2, $"Expected nearby forage for seed {seed}, got {survey.NearbyFoodSources} sources.");
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
        var context = service.GenerateAndApplyEmbark(
            targetMap: new WorldMap(),
            seed: 41,
            settings: new LocalGenerationSettings(32, 32, 6),
            generationSettings: generationSettings);

        Assert.True(capturingLocalGenerator.LastHistoryContext is { HasContinuity: true });
        var localHistory = capturingLocalGenerator.LastHistoryContext!.Value;
        Assert.Contains(localHistory.NearbySites, site => site.Id is "site_alpha" or "site_beta");
        Assert.Contains(localHistory.NearbyRoads, road => road.Id == "road_alpha_beta");

        Assert.True(context.LocalHistory is { HasContinuity: true });
        Assert.Equal(localHistory.OwnerCivilizationId, context.LocalHistory!.Value.OwnerCivilizationId);
        Assert.Equal(localHistory.TerritoryOwnerCivilizationId, context.LocalHistory.Value.TerritoryOwnerCivilizationId);
        Assert.Equal(localHistory.PrimarySite?.Id, context.LocalHistory.Value.PrimarySite?.Id);
        Assert.Equal(localHistory.NearbySites.Select(site => site.Id), context.LocalHistory.Value.NearbySites.Select(site => site.Id));
        Assert.Equal(localHistory.NearbyRoads.Select(road => road.Id), context.LocalHistory.Value.NearbyRoads.Select(road => road.Id));
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

    private static EmbarkResourceSurvey SurveyEmbarkResources(GeneratedEmbarkMap map)
    {
        var originX = map.Width / 2;
        var originY = map.Height / 2;
        var searchRadius = 18;
        var visited = new bool[map.Width, map.Height];
        var queue = new Queue<(int X, int Y, int Distance)>();
        var nearbyFoodSources = new HashSet<int>();
        var foundNearbyWater = false;

        if (!IsWalkableSurface(map, originX, originY))
            return new EmbarkResourceSurvey(false, 0);

        visited[originX, originY] = true;
        queue.Enqueue((originX, originY, 0));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Distance > searchRadius)
                continue;

            ScanResourceTile(map, current.X, current.Y, ref foundNearbyWater, nearbyFoodSources);
            ScanResourceTile(map, current.X + 1, current.Y, ref foundNearbyWater, nearbyFoodSources);
            ScanResourceTile(map, current.X - 1, current.Y, ref foundNearbyWater, nearbyFoodSources);
            ScanResourceTile(map, current.X, current.Y + 1, ref foundNearbyWater, nearbyFoodSources);
            ScanResourceTile(map, current.X, current.Y - 1, ref foundNearbyWater, nearbyFoodSources);

            foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
            {
                var nx = current.X + dx;
                var ny = current.Y + dy;
                if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height || visited[nx, ny])
                    continue;
                if (!IsWalkableSurface(map, nx, ny))
                    continue;

                visited[nx, ny] = true;
                queue.Enqueue((nx, ny, current.Distance + 1));
            }
        }

        return new EmbarkResourceSurvey(foundNearbyWater, nearbyFoodSources.Count);
    }

    private static void ScanResourceTile(
        GeneratedEmbarkMap map,
        int x,
        int y,
        ref bool foundNearbyWater,
        ISet<int> nearbyFoodSources)
    {
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            return;

        var tile = map.GetTile(x, y, 0);
        if ((tile.FluidType == GeneratedFluidType.Water || tile.TileDefId == GeneratedTileDefIds.Water) && tile.FluidLevel > 0)
            foundNearbyWater = true;

        if (string.IsNullOrWhiteSpace(tile.PlantDefId) || tile.PlantYieldLevel == 0 || tile.PlantGrowthStage < GeneratedPlantGrowthStages.Mature)
            return;

        nearbyFoodSources.Add((y * map.Width) + x);
    }

    private static bool IsWalkableSurface(GeneratedEmbarkMap map, int x, int y)
    {
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            return false;

        var tile = map.GetTile(x, y, 0);
        if (!tile.IsPassable)
            return false;
        if (tile.FluidType == GeneratedFluidType.Magma || tile.TileDefId == GeneratedTileDefIds.Magma)
            return false;
        if (tile.FluidType == GeneratedFluidType.Water || tile.TileDefId == GeneratedTileDefIds.Water)
            return tile.FluidLevel <= WorldMap.MaxWadeableWaterLevel;

        return true;
    }

    private readonly record struct EmbarkResourceSurvey(bool HasNearbyWater, int NearbyFoodSources);
}

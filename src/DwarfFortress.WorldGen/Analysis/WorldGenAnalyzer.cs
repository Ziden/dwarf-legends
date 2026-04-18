using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Local;
using DwarfFortress.WorldGen.Maps;
using DwarfFortress.WorldGen.Regions;
using DwarfFortress.WorldGen.Story;
using DwarfFortress.WorldGen.World;

namespace DwarfFortress.WorldGen.Analysis;

public static class WorldGenAnalyzer
{
    private readonly record struct ForestMobilityMetrics(
        int TreeTiles,
        float TreeDensity,
        int OpeningTiles,
        int ReachableOpeningTiles,
        float OpeningRatio,
        float ReachableOpeningRatio);

    public static MapMetrics AnalyzeMap(GeneratedEmbarkMap map)
    {
        var surfaceTiles = map.Width * map.Height;
        var passable = 0;
        var trees = 0;
        var water = 0;
        var walls = 0;

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (tile.IsPassable) passable++;
            if (tile.TileDefId == GeneratedTileDefIds.Tree) trees++;
            if (tile.TileDefId == GeneratedTileDefIds.Water) water++;
            if (!tile.IsPassable) walls++;
        }

        return new MapMetrics(
            Width: map.Width,
            Height: map.Height,
            Depth: map.Depth,
            SurfaceTiles: surfaceTiles,
            PassableSurfaceTiles: passable,
            PassableSurfaceRatio: surfaceTiles == 0 ? 0f : passable / (float)surfaceTiles,
            TreeTiles: trees,
            WaterTiles: water,
            WallTiles: walls,
            BordersPassable: BordersArePassable(map),
            CornerPathExists: HasCornerToCornerSurfacePath(map));
    }

    public static EmbarkStageReport AnalyzeEmbarkStages(GeneratedEmbarkMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var diagnostics = map.Diagnostics ?? throw new InvalidOperationException(
            "Embark stage diagnostics are not available for this map.");

        var stageSnapshots = diagnostics.StageSnapshots;
        var expectedStages = GetExpectedEmbarkStageOrder();
        var actualStages = stageSnapshots.Select(s => s.StageId).ToArray();
        var surfaceTileCount = map.Width * map.Height;
        var undergroundTileCount = surfaceTileCount * Math.Max(0, map.Depth - 1);
        var mapMetrics = AnalyzeMap(map);
        var forestMobility = MeasureForestMobility(map);
        var forestMobilityApplies = forestMobility.TreeDensity >= 0.16f;

        var inputsSnapshot = stageSnapshots.Count > 0
            ? stageSnapshots[0]
            : default;
        var playabilitySnapshot = stageSnapshots.FirstOrDefault(s => s.StageId == EmbarkGenerationStageId.Playability);
        var populationSnapshot = stageSnapshots.FirstOrDefault(s => s.StageId == EmbarkGenerationStageId.Population);

        var budgets = new List<DepthBudgetResult>
        {
            Budget(
                "Embark Stage Coverage",
                stageSnapshots.Count == expectedStages.Length,
                $"captured {stageSnapshots.Count} stages, expected {expectedStages.Length}"),
            Budget(
                "Embark Stage Order",
                actualStages.SequenceEqual(expectedStages),
                $"sequence=[{string.Join(", ", actualStages)}], expected=[{string.Join(", ", expectedStages)}]"),
            Budget(
                "Embark Inputs Baseline",
                inputsSnapshot.StageId == EmbarkGenerationStageId.Inputs &&
                inputsSnapshot.SurfacePassableTiles == surfaceTileCount &&
                inputsSnapshot.SurfaceWaterTiles == 0 &&
                inputsSnapshot.SurfaceTreeTiles == 0 &&
                inputsSnapshot.SurfaceWallTiles == 0 &&
                inputsSnapshot.UndergroundPassableTiles == undergroundTileCount &&
                inputsSnapshot.AquiferTiles == 0 &&
                inputsSnapshot.OreTiles == 0 &&
                inputsSnapshot.MagmaTiles == 0 &&
                inputsSnapshot.CreatureSpawnCount == 0,
                $"surfacePassable={inputsSnapshot.SurfacePassableTiles}/{surfaceTileCount}, undergroundPassable={inputsSnapshot.UndergroundPassableTiles}/{undergroundTileCount}, water={inputsSnapshot.SurfaceWaterTiles}, trees={inputsSnapshot.SurfaceTreeTiles}, walls={inputsSnapshot.SurfaceWallTiles}, aquifer={inputsSnapshot.AquiferTiles}, ore={inputsSnapshot.OreTiles}, magma={inputsSnapshot.MagmaTiles}, spawns={inputsSnapshot.CreatureSpawnCount}"),
            Budget(
                "Population Stage Non-Destructive",
                playabilitySnapshot.StageId == EmbarkGenerationStageId.Playability &&
                populationSnapshot.StageId == EmbarkGenerationStageId.Population &&
                populationSnapshot.SurfacePassableTiles == playabilitySnapshot.SurfacePassableTiles &&
                populationSnapshot.SurfaceWaterTiles == playabilitySnapshot.SurfaceWaterTiles &&
                populationSnapshot.SurfaceTreeTiles == playabilitySnapshot.SurfaceTreeTiles &&
                populationSnapshot.SurfaceWallTiles == playabilitySnapshot.SurfaceWallTiles &&
                populationSnapshot.UndergroundPassableTiles == playabilitySnapshot.UndergroundPassableTiles &&
                populationSnapshot.AquiferTiles == playabilitySnapshot.AquiferTiles &&
                populationSnapshot.OreTiles == playabilitySnapshot.OreTiles &&
                populationSnapshot.MagmaTiles == playabilitySnapshot.MagmaTiles &&
                populationSnapshot.CreatureSpawnCount >= playabilitySnapshot.CreatureSpawnCount,
                $"playability spawns={playabilitySnapshot.CreatureSpawnCount}, population spawns={populationSnapshot.CreatureSpawnCount}"),
            Budget(
                "Embark Final Snapshot Consistency",
                populationSnapshot.StageId == EmbarkGenerationStageId.Population &&
                populationSnapshot.SurfacePassableTiles == mapMetrics.PassableSurfaceTiles &&
                populationSnapshot.SurfaceWaterTiles == mapMetrics.WaterTiles &&
                populationSnapshot.SurfaceTreeTiles == mapMetrics.TreeTiles &&
                populationSnapshot.SurfaceWallTiles == mapMetrics.WallTiles &&
                populationSnapshot.CreatureSpawnCount == map.CreatureSpawns.Count,
                $"final snapshot surfacePassable={populationSnapshot.SurfacePassableTiles}, water={populationSnapshot.SurfaceWaterTiles}, trees={populationSnapshot.SurfaceTreeTiles}, walls={populationSnapshot.SurfaceWallTiles}, spawns={populationSnapshot.CreatureSpawnCount}"),
            Budget(
                "Forest Mobility Openings",
                !forestMobilityApplies || (forestMobility.OpeningRatio >= 0.005f && forestMobility.ReachableOpeningRatio >= 0.45f),
                forestMobilityApplies
                    ? $"treeDensity={forestMobility.TreeDensity:0.000}, openings={forestMobility.OpeningTiles}, openingRatio={forestMobility.OpeningRatio:0.000}, reachableOpeningRatio={forestMobility.ReachableOpeningRatio:0.000}, expected openingRatio >= 0.005 and reachable >= 0.450"
                    : $"skipped (treeDensity={forestMobility.TreeDensity:0.000} below dense-forest threshold)"),
        };

        return new EmbarkStageReport(
            Seed: diagnostics.Seed,
            SurfaceTileCount: surfaceTileCount,
            UndergroundTileCount: undergroundTileCount,
            StageSnapshots: stageSnapshots,
            Budgets: budgets);
    }

    public static LoreMetrics AnalyzeLore(WorldLoreState lore)
    {
        var treaty = 0;
        var raid = 0;
        var skirmish = 0;
        var crisis = 0;
        var founding = 0;

        foreach (var evt in lore.History)
        {
            switch (evt.Type)
            {
                case HistoricalEventTypeIds.Treaty:
                    treaty++;
                    break;
                case HistoricalEventTypeIds.Raid:
                    raid++;
                    break;
                case HistoricalEventTypeIds.Skirmish:
                    skirmish++;
                    break;
                case HistoricalEventTypeIds.Crisis:
                    crisis++;
                    break;
                case HistoricalEventTypeIds.Founding:
                    founding++;
                    break;
            }
        }

        var hostileEventCount = raid + skirmish;
        var eventCount = lore.History.Count;
        var hostileEventRatio = eventCount == 0 ? 0f : hostileEventCount / (float)eventCount;
        var relationCount = lore.FactionRelations.Count;
        var alliedRelationCount = lore.FactionRelations.Count(r => r.Stance == RelationStanceIds.Ally);
        var neutralRelationCount = lore.FactionRelations.Count(r => r.Stance == RelationStanceIds.Neutral);
        var hostileRelationCount = lore.FactionRelations.Count(r => r.Stance == RelationStanceIds.Hostile);

        var growingSiteCount = lore.Sites.Count(s => s.Status == SiteStatusIds.Growing);
        var stableSiteCount = lore.Sites.Count(s => s.Status == SiteStatusIds.Stable);
        var decliningSiteCount = lore.Sites.Count(s => s.Status == SiteStatusIds.Declining);
        var ruinedSiteCount = lore.Sites.Count(s => s.Status == SiteStatusIds.Ruined);
        var fortifiedSiteCount = lore.Sites.Count(s => s.Status == SiteStatusIds.Fortified);
        var avgDevelopment = lore.Sites.Count == 0 ? 0f : lore.Sites.Average(s => s.Development);
        var avgSecurity = lore.Sites.Count == 0 ? 0f : lore.Sites.Average(s => s.Security);

        return new LoreMetrics(
            BiomeId: lore.BiomeId,
            SimulatedYears: lore.SimulatedYears,
            FactionCount: lore.Factions.Count,
            HostileFactionCount: lore.Factions.Count(f => f.IsHostile),
            RelationCount: relationCount,
            AlliedRelationCount: alliedRelationCount,
            NeutralRelationCount: neutralRelationCount,
            HostileRelationCount: hostileRelationCount,
            SiteCount: lore.Sites.Count,
            GrowingSiteCount: growingSiteCount,
            StableSiteCount: stableSiteCount,
            DecliningSiteCount: decliningSiteCount,
            RuinedSiteCount: ruinedSiteCount,
            FortifiedSiteCount: fortifiedSiteCount,
            AvgSiteDevelopment: (float)avgDevelopment,
            AvgSiteSecurity: (float)avgSecurity,
            EventCount: eventCount,
            TreatyCount: treaty,
            RaidCount: raid,
            SkirmishCount: skirmish,
            CrisisCount: crisis,
            FoundingCount: founding,
            HostileEventRatio: hostileEventRatio,
            Threat: lore.Threat,
            Prosperity: lore.Prosperity);
    }

    public static DepthReport AnalyzeDepthSamples(
        int seedStart = 0,
        int seedCount = 25,
        int width = 48,
        int height = 48,
        int depth = 8,
        WorldLoreConfig? loreConfig = null)
    {
        if (seedCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(seedCount), "seedCount must be greater than zero.");

        var effectiveConfig = WorldLoreConfig.WithDefaults(loreConfig);
        var mapMetrics = new List<MapMetrics>(seedCount);
        var loreMetrics = new List<LoreMetrics>(seedCount);
        var biomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < seedCount; i++)
        {
            var seed = seedStart + i;
            var lore = WorldLoreGenerator.Generate(seed, width, height, depth, effectiveConfig);
            var map = EmbarkGenerator.Generate(width, height, depth, seed, lore.BiomeId);

            var mapReport = AnalyzeMap(map);
            var loreReport = AnalyzeLore(lore);

            mapMetrics.Add(mapReport);
            loreMetrics.Add(loreReport);
            biomes.Add(lore.BiomeId);
        }

        var avgPassable = mapMetrics.Average(m => m.PassableSurfaceRatio);
        var avgFactions = loreMetrics.Average(l => l.FactionCount);
        var avgHostileFactions = loreMetrics.Average(l => l.HostileFactionCount);
        var avgRelations = loreMetrics.Average(l => l.RelationCount);
        var avgHostileRelations = loreMetrics.Average(l => l.HostileRelationCount);
        var avgSites = loreMetrics.Average(l => l.SiteCount);
        var avgGrowingSites = loreMetrics.Average(l => l.GrowingSiteCount);
        var avgDecliningSites = loreMetrics.Average(l => l.DecliningSiteCount);
        var avgRuinedSites = loreMetrics.Average(l => l.RuinedSiteCount);
        var avgFortifiedSites = loreMetrics.Average(l => l.FortifiedSiteCount);
        var avgSiteDevelopment = loreMetrics.Average(l => l.AvgSiteDevelopment);
        var avgSiteSecurity = loreMetrics.Average(l => l.AvgSiteSecurity);
        var avgEvents = loreMetrics.Average(l => l.EventCount);
        var avgHostileEventRatio = loreMetrics.Average(l => l.HostileEventRatio);
        var avgThreat = loreMetrics.Average(l => l.Threat);
        var avgProsperity = loreMetrics.Average(l => l.Prosperity);

        var budgets = new List<DepthBudgetResult>
        {
            Budget(
                "Map Passability",
                avgPassable >= 0.70f && avgPassable <= 1.00f,
                $"avg={avgPassable:0.000}, expected 0.70..1.00"),
            Budget(
                "Map Connectivity",
                mapMetrics.All(m => m.CornerPathExists && m.BordersPassable),
                "all samples must keep borders passable and corner path reachable"),
            Budget(
                "Biome Diversity",
                biomes.Count >= Math.Min(3, seedCount),
                $"distinct biomes={biomes.Count}, expected at least {Math.Min(3, seedCount)}"),
            Budget(
                "Faction Variety",
                avgFactions >= 3.0f,
                $"avg factions={avgFactions:0.00}, expected >= 3.00"),
            Budget(
                "Relation Dynamics",
                avgRelations >= 3.0f && avgHostileRelations >= 1.0f,
                $"avg relations={avgRelations:0.00}, avg hostile relations={avgHostileRelations:0.00}, expected >= 3.00 and >= 1.00"),
            Budget(
                "Site Density",
                avgSites >= 5.0f,
                $"avg sites={avgSites:0.00}, expected >= 5.00"),
            Budget(
                "Site Evolution",
                (avgGrowingSites + avgDecliningSites + avgRuinedSites + avgFortifiedSites) >= 1.0f,
                $"avg non-stable sites={avgGrowingSites + avgDecliningSites + avgRuinedSites + avgFortifiedSites:0.00}, expected >= 1.00"),
            Budget(
                "Site Health Bounds",
                avgSiteDevelopment >= 0.02f && avgSiteDevelopment <= 0.95f && avgSiteSecurity >= 0.15f && avgSiteSecurity <= 0.95f,
                $"avg development={avgSiteDevelopment:0.000}, avg security={avgSiteSecurity:0.000}, expected development 0.02..0.95 and security 0.15..0.95"),
            Budget(
                "History Density",
                avgEvents >= 120.0f,
                $"avg history events={avgEvents:0.00}, expected >= 120.00"),
            Budget(
                "Conflict Balance",
                avgHostileEventRatio >= 0.15f && avgHostileEventRatio <= 0.75f,
                $"avg hostile-event ratio={avgHostileEventRatio:0.000}, expected 0.15..0.75"),
            Budget(
                "Threat/Prosperity Balance",
                avgThreat >= 0.20f && avgThreat <= 1.00f && avgProsperity >= 0.20f && avgProsperity <= 1.00f,
                $"avg threat={avgThreat:0.000}, avg prosperity={avgProsperity:0.000}, expected both 0.20..1.00"),
        };

        return new DepthReport(
            SeedCount: seedCount,
            DistinctBiomeCount: biomes.Count,
            AvgPassableSurfaceRatio: (float)avgPassable,
            AvgFactionCount: (float)avgFactions,
            AvgHostileFactionCount: (float)avgHostileFactions,
            AvgRelationCount: (float)avgRelations,
            AvgHostileRelationCount: (float)avgHostileRelations,
            AvgSiteCount: (float)avgSites,
            AvgGrowingSiteCount: (float)avgGrowingSites,
            AvgDecliningSiteCount: (float)avgDecliningSites,
            AvgRuinedSiteCount: (float)avgRuinedSites,
            AvgFortifiedSiteCount: (float)avgFortifiedSites,
            AvgSiteDevelopment: (float)avgSiteDevelopment,
            AvgSiteSecurity: (float)avgSiteSecurity,
            AvgEventCount: (float)avgEvents,
            AvgHostileEventRatio: (float)avgHostileEventRatio,
            AvgThreat: (float)avgThreat,
            AvgProsperity: (float)avgProsperity,
            Budgets: budgets);
    }

    public static WorldPipelineReport AnalyzePipelineSamples(
        int seedStart = 0,
        int seedCount = 6,
        int worldWidth = 24,
        int worldHeight = 24,
        int regionWidth = 16,
        int regionHeight = 16,
        int sampledRegionsPerWorld = 8,
        int localWidth = 48,
        int localHeight = 48,
        int localDepth = 8,
        bool ensureBiomeCoverage = false,
        int maxAdditionalSeeds = 12)
    {
        if (seedCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(seedCount), "seedCount must be greater than zero.");
        if (sampledRegionsPerWorld <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampledRegionsPerWorld), "sampledRegionsPerWorld must be greater than zero.");
        if (maxAdditionalSeeds < 0)
            throw new ArgumentOutOfRangeException(nameof(maxAdditionalSeeds), "maxAdditionalSeeds cannot be negative.");

        var worldGenerator = new WorldLayerGenerator();
        var regionGenerator = new RegionLayerGenerator();
        var localGenerator = new LocalLayerGenerator();

        var worldRiverPairs = 0;
        var worldRiverMismatches = 0;
        var worldRoadPairs = 0;
        var worldRoadMismatches = 0;
        var worldLandTiles = 0;
        var worldTropicalLandTiles = 0;
        var worldSavannaLandTiles = 0;
        var worldDesertLandTiles = 0;
        var worldSteppeLandTiles = 0;
        var worldColdLandTiles = 0;

        var regionRiverPairs = 0;
        var regionRiverMismatches = 0;
        var regionRoadPairs = 0;
        var regionRoadMismatches = 0;
        var localBoundarySamples = 0;
        var localSurfaceBoundaryMismatches = 0;
        var localWaterBoundaryMismatches = 0;
        var localEcologyBoundaryMismatches = 0;
        var localTreeBoundaryMismatches = 0;
        const int localBoundaryBandWidth = 4;
        var localBoundaryBandSamples = 0;
        var localSurfaceBoundaryBandMismatches = 0;
        var localWaterBoundaryBandMismatches = 0;
        var localEcologyBoundaryBandMismatches = 0;
        var localTreeBoundaryBandMismatches = 0;

        var sampledRegionCount = 0;
        var regionVegetation = new List<float>(seedCount * 1024);
        var regionGroundwater = new List<float>(seedCount * 1024);
        var regionSuitability = new List<float>(seedCount * 1024);
        var localTreeDensity = new List<float>(seedCount * sampledRegionsPerWorld);
        var localSuitability = new List<float>(seedCount * sampledRegionsPerWorld);
        var localEmbarkStageReportCount = 0;
        var localEmbarkStageDiagnosticsPresentCount = 0;
        var localEmbarkStagePassCount = 0;
        string? firstFailingLocalStageDetail = null;
        var localTreeDensityByMacro = new Dictionary<string, List<float>>(StringComparer.OrdinalIgnoreCase);
        var localLargestPatchRatioByMacro = new Dictionary<string, List<float>>(StringComparer.OrdinalIgnoreCase);
        var localOpeningRatioByMacro = new Dictionary<string, List<float>>(StringComparer.OrdinalIgnoreCase);
        var localReachableOpeningRatioByMacro = new Dictionary<string, List<float>>(StringComparer.OrdinalIgnoreCase);
        var worldForestRegionSignals = new List<float>(seedCount * sampledRegionsPerWorld);
        var regionAvgVegetationSignals = new List<float>(seedCount * sampledRegionsPerWorld);
        var worldForestLocalSignals = new List<float>(seedCount * sampledRegionsPerWorld);
        var worldMountainRegionSignals = new List<float>(seedCount * sampledRegionsPerWorld);
        var regionAvgSlopeSignals = new List<float>(seedCount * sampledRegionsPerWorld);
        var alignedRegionMacroTiles = 0;
        var totalRegionMacroTiles = 0;
        var evaluatedSeedCount = 0;
        var targetSeedCount = seedCount;
        var maxSeedEvaluations = seedCount + maxAdditionalSeeds;
        var coverageStagnationCount = 0;
        var coverageScore = 0;
        const int coverageStagnationLimit = 6;
        var observedDenseForestMacro = false;
        var observedTropicalMacro = false;
        var observedAridMacro = false;

        for (var i = 0; i < maxSeedEvaluations; i++)
        {
            var seed = seedStart + i;
            evaluatedSeedCount++;
            var world = worldGenerator.Generate(seed, worldWidth, worldHeight);
            AccumulateWorldBiomeDistribution(
                world,
                ref worldLandTiles,
                ref worldTropicalLandTiles,
                ref worldSavannaLandTiles,
                ref worldDesertLandTiles,
                ref worldSteppeLandTiles,
                ref worldColdLandTiles);
            ObserveMacroCoverage(
                world,
                ref observedDenseForestMacro,
                ref observedTropicalMacro,
                ref observedAridMacro);
            AccumulateWorldEdgeContinuity(
                world,
                ref worldRiverPairs,
                ref worldRiverMismatches,
                ref worldRoadPairs,
                ref worldRoadMismatches);

            var coords = BuildSampleWorldCoords(world, seed, sampledRegionsPerWorld);
            sampledRegionCount += coords.Length;

            var regionCache = new Dictionary<WorldCoord, GeneratedRegionMap>();
            var localCache = new Dictionary<(int WorldX, int WorldY, int RegionX, int RegionY), GeneratedEmbarkMap>();
            var localSettings = new LocalGenerationSettings(localWidth, localHeight, localDepth);
            foreach (var coord in coords)
            {
                var region = GetOrGenerateRegion(regionCache, regionGenerator, world, coord, regionWidth, regionHeight);
                var parent = world.GetTile(coord.X, coord.Y);
                AccumulateRegionEcology(region, regionVegetation, regionGroundwater, regionSuitability);
                AccumulateRegionMacroAlignment(region, parent.MacroBiomeId, ref alignedRegionMacroTiles, ref totalRegionMacroTiles);
                worldForestRegionSignals.Add(parent.ForestCover);
                regionAvgVegetationSignals.Add(AverageRegionVegetation(region));
                worldMountainRegionSignals.Add(parent.MountainCover);
                regionAvgSlopeSignals.Add(AverageRegionSlope(region));

                if (coord.X + 1 < worldWidth)
                {
                    var eastCoord = new WorldCoord(coord.X + 1, coord.Y);
                    var eastRegion = GetOrGenerateRegion(regionCache, regionGenerator, world, eastCoord, regionWidth, regionHeight);
                    AccumulateRegionBoundaryContinuity(
                        region,
                        eastRegion,
                        isEastNeighbor: true,
                        ref regionRiverPairs,
                        ref regionRiverMismatches,
                        ref regionRoadPairs,
                        ref regionRoadMismatches);
                }

                if (coord.Y + 1 < worldHeight)
                {
                    var southCoord = new WorldCoord(coord.X, coord.Y + 1);
                    var southRegion = GetOrGenerateRegion(regionCache, regionGenerator, world, southCoord, regionWidth, regionHeight);
                    AccumulateRegionBoundaryContinuity(
                        region,
                        southRegion,
                        isEastNeighbor: false,
                        ref regionRiverPairs,
                        ref regionRiverMismatches,
                        ref regionRoadPairs,
                        ref regionRoadMismatches);
                }

                var centerX = region.Width / 2;
                var centerY = region.Height / 2;
                var localCoord = new RegionCoord(coord.X, coord.Y, centerX, centerY);
                var local = GetOrGenerateLocal(localCache, localGenerator, region, localCoord, localSettings);

                localEmbarkStageReportCount++;
                if (local.Diagnostics is not null)
                    localEmbarkStageDiagnosticsPresentCount++;

                var localStageReport = AnalyzeEmbarkStages(local);
                if (localStageReport.Passed)
                {
                    localEmbarkStagePassCount++;
                }
                else if (string.IsNullOrWhiteSpace(firstFailingLocalStageDetail))
                {
                    var failures = string.Join(", ", localStageReport.Budgets.Where(b => !b.Passed).Select(b => $"{b.Name}: {b.Detail}"));
                    firstFailingLocalStageDetail = $"first failure at world=({coord.X},{coord.Y}) local=({centerX},{centerY}) seed={localStageReport.Seed}: {failures}";
                }

                var treeCount = CountSurfaceTiles(local, GeneratedTileDefIds.Tree);
                var treeDensity = treeCount / (float)(local.Width * local.Height);
                var largestTreePatch = CountLargestConnectedSurfacePatch(local, GeneratedTileDefIds.Tree);
                var largestTreePatchRatio = largestTreePatch / (float)(local.Width * local.Height);
                var forestMobility = MeasureForestMobility(local);
                localTreeDensity.Add(treeDensity);
                localSuitability.Add(region.GetTile(centerX, centerY).VegetationSuitability);
                worldForestLocalSignals.Add(parent.ForestCover);
                AddMacroSample(localTreeDensityByMacro, parent.MacroBiomeId, treeDensity);
                AddMacroSample(localLargestPatchRatioByMacro, parent.MacroBiomeId, largestTreePatchRatio);
                AddMacroSample(localOpeningRatioByMacro, parent.MacroBiomeId, forestMobility.OpeningRatio);
                AddMacroSample(localReachableOpeningRatioByMacro, parent.MacroBiomeId, forestMobility.ReachableOpeningRatio);

                var eastNeighbor = TryGetAdjacentLocal(
                    localCache,
                    regionCache,
                    localGenerator,
                    regionGenerator,
                    world,
                    region,
                    localCoord,
                    dx: 1,
                    dy: 0,
                    regionWidth,
                    regionHeight,
                    localSettings);
                AccumulateLocalBoundaryContinuity(
                    local,
                    eastNeighbor,
                    isEastNeighbor: true,
                    ref localBoundarySamples,
                    ref localSurfaceBoundaryMismatches,
                    ref localWaterBoundaryMismatches,
                    ref localEcologyBoundaryMismatches,
                    ref localTreeBoundaryMismatches);
                AccumulateLocalBoundaryBandContinuity(
                    local,
                    eastNeighbor,
                    isEastNeighbor: true,
                    bandWidth: localBoundaryBandWidth,
                    ref localBoundaryBandSamples,
                    ref localSurfaceBoundaryBandMismatches,
                    ref localWaterBoundaryBandMismatches,
                    ref localEcologyBoundaryBandMismatches,
                    ref localTreeBoundaryBandMismatches);

                var southNeighbor = TryGetAdjacentLocal(
                    localCache,
                    regionCache,
                    localGenerator,
                    regionGenerator,
                    world,
                    region,
                    localCoord,
                    dx: 0,
                    dy: 1,
                    regionWidth,
                    regionHeight,
                    localSettings);
                AccumulateLocalBoundaryContinuity(
                    local,
                    southNeighbor,
                    isEastNeighbor: false,
                    ref localBoundarySamples,
                    ref localSurfaceBoundaryMismatches,
                    ref localWaterBoundaryMismatches,
                    ref localEcologyBoundaryMismatches,
                    ref localTreeBoundaryMismatches);
                AccumulateLocalBoundaryBandContinuity(
                    local,
                    southNeighbor,
                    isEastNeighbor: false,
                    bandWidth: localBoundaryBandWidth,
                    ref localBoundaryBandSamples,
                    ref localSurfaceBoundaryBandMismatches,
                    ref localWaterBoundaryBandMismatches,
                    ref localEcologyBoundaryBandMismatches,
                    ref localTreeBoundaryBandMismatches);
            }

            if (evaluatedSeedCount < targetSeedCount)
                continue;

            if (!ensureBiomeCoverage)
                break;

            if (HasMacroSamples(
                    localTreeDensityByMacro,
                    MacroBiomeIds.ConiferForest,
                    MacroBiomeIds.BorealForest,
                    MacroBiomeIds.TropicalRainforest) &&
                HasMacroSamples(localTreeDensityByMacro, MacroBiomeIds.TropicalRainforest) &&
                HasMacroSamples(localTreeDensityByMacro, MacroBiomeIds.WindsweptSteppe, MacroBiomeIds.Desert, MacroBiomeIds.Savanna))
            {
                break;
            }

            var nextCoverageScore =
                (HasMacroSamples(localTreeDensityByMacro, MacroBiomeIds.ConiferForest, MacroBiomeIds.BorealForest, MacroBiomeIds.TropicalRainforest) ? 1 : 0) +
                (HasMacroSamples(localTreeDensityByMacro, MacroBiomeIds.TropicalRainforest) ? 1 : 0) +
                (HasMacroSamples(localTreeDensityByMacro, MacroBiomeIds.WindsweptSteppe, MacroBiomeIds.Desert, MacroBiomeIds.Savanna) ? 1 : 0);

            if (nextCoverageScore > coverageScore)
            {
                coverageScore = nextCoverageScore;
                coverageStagnationCount = 0;
            }
            else
            {
                coverageStagnationCount++;
                if (coverageStagnationCount >= coverageStagnationLimit)
                    break;
            }
        }

        var worldRiverMismatchRatio = Ratio(worldRiverMismatches, worldRiverPairs);
        var worldRoadMismatchRatio = Ratio(worldRoadMismatches, worldRoadPairs);
        var regionRiverMismatchRatio = Ratio(regionRiverMismatches, regionRiverPairs);
        var regionRoadMismatchRatio = Ratio(regionRoadMismatches, regionRoadPairs);
        var localSurfaceBoundaryMismatchRatio = Ratio(localSurfaceBoundaryMismatches, localBoundarySamples);
        var localWaterBoundaryMismatchRatio = Ratio(localWaterBoundaryMismatches, localBoundarySamples);
        var localEcologyBoundaryMismatchRatio = Ratio(localEcologyBoundaryMismatches, localBoundarySamples);
        var localTreeBoundaryMismatchRatio = Ratio(localTreeBoundaryMismatches, localBoundarySamples);
        var localSurfaceBoundaryBandMismatchRatio = Ratio(localSurfaceBoundaryBandMismatches, localBoundaryBandSamples);
        var localWaterBoundaryBandMismatchRatio = Ratio(localWaterBoundaryBandMismatches, localBoundaryBandSamples);
        var localEcologyBoundaryBandMismatchRatio = Ratio(localEcologyBoundaryBandMismatches, localBoundaryBandSamples);
        var localTreeBoundaryBandMismatchRatio = Ratio(localTreeBoundaryBandMismatches, localBoundaryBandSamples);
        var tropicalLandShare = Ratio(worldTropicalLandTiles, worldLandTiles);
        var aridLandShare = Ratio(worldDesertLandTiles + worldSteppeLandTiles + worldSavannaLandTiles, worldLandTiles);
        var coldLandShare = Ratio(worldColdLandTiles, worldLandTiles);
        var desertLandShare = Ratio(worldDesertLandTiles, worldLandTiles);

        var vegetationGroundwaterCorrelation = PearsonCorrelation(regionVegetation, regionGroundwater);
        var vegetationSuitabilityCorrelation = PearsonCorrelation(regionVegetation, regionSuitability);
        var localTreeSuitabilityCorrelation = PearsonCorrelation(localTreeDensity, localSuitability);
        var regionParentMacroAlignmentRatio = Ratio(alignedRegionMacroTiles, totalRegionMacroTiles);
        var worldForestRegionVegetationCorrelation = PearsonCorrelation(worldForestRegionSignals, regionAvgVegetationSignals);
        var worldForestLocalTreeDensityCorrelation = PearsonCorrelation(worldForestLocalSignals, localTreeDensity);
        var worldMountainRegionSlopeCorrelation = PearsonCorrelation(worldMountainRegionSignals, regionAvgSlopeSignals);
        var avgLocalTreeDensity = localTreeDensity.Count == 0 ? 0f : localTreeDensity.Average();

        var denseForestSamples = CollectMacroSamples(
            localTreeDensityByMacro,
            MacroBiomeIds.ConiferForest,
            MacroBiomeIds.BorealForest,
            MacroBiomeIds.TropicalRainforest);
        var tropicalSamples = CollectMacroSamples(localTreeDensityByMacro, MacroBiomeIds.TropicalRainforest);
        var aridSamples = CollectMacroSamples(
            localTreeDensityByMacro,
            MacroBiomeIds.WindsweptSteppe,
            MacroBiomeIds.Desert,
            MacroBiomeIds.Savanna);
        var denseForestPatchSamples = CollectMacroSamples(
            localLargestPatchRatioByMacro,
            MacroBiomeIds.ConiferForest,
            MacroBiomeIds.BorealForest,
            MacroBiomeIds.TropicalRainforest);
        var denseForestOpeningSamples = CollectMacroSamples(
            localOpeningRatioByMacro,
            MacroBiomeIds.ConiferForest,
            MacroBiomeIds.BorealForest,
            MacroBiomeIds.TropicalRainforest);
        var denseForestReachableOpeningSamples = CollectMacroSamples(
            localReachableOpeningRatioByMacro,
            MacroBiomeIds.ConiferForest,
            MacroBiomeIds.BorealForest,
            MacroBiomeIds.TropicalRainforest);

        var denseForestMedianTreeDensity = Median(denseForestSamples);
        var tropicalMedianTreeDensity = Median(tropicalSamples);
        var aridMedianTreeDensity = Median(aridSamples);
        var denseForestMedianLargestPatchRatio = Median(denseForestPatchSamples);
        var denseForestMedianOpeningRatio = Median(denseForestOpeningSamples);
        var denseForestMedianReachableOpeningRatio = Median(denseForestReachableOpeningSamples);
        var hasDenseForestCoverage = denseForestSamples.Count > 0;
        var hasTropicalCoverage = tropicalSamples.Count > 0;
        var hasAridCoverage = aridSamples.Count > 0;
        var denseCoveragePass = !ensureBiomeCoverage || hasDenseForestCoverage || !observedDenseForestMacro;
        var tropicalCoveragePass = !ensureBiomeCoverage || hasTropicalCoverage || !observedTropicalMacro;
        var aridCoveragePass = !ensureBiomeCoverage || hasAridCoverage || !observedAridMacro;

        var budgets = new List<DepthBudgetResult>
        {
            Budget(
                "World River Edge Continuity",
                worldRiverMismatchRatio <= 0.08f,
                $"mismatch ratio={worldRiverMismatchRatio:0.000}, expected <= 0.080"),
            Budget(
                "World Road Edge Continuity",
                worldRoadMismatchRatio <= 0.14f,
                $"mismatch ratio={worldRoadMismatchRatio:0.000}, expected <= 0.140"),
            Budget(
                "Region River Edge Continuity",
                regionRiverMismatchRatio <= 0.08f,
                $"mismatch ratio={regionRiverMismatchRatio:0.000}, expected <= 0.080"),
            Budget(
                "Region Road Edge Continuity",
                regionRoadMismatchRatio <= 0.12f,
                $"mismatch ratio={regionRoadMismatchRatio:0.000}, expected <= 0.120"),
            Budget(
                "Region Parent Macro Alignment",
                regionParentMacroAlignmentRatio >= 0.70f,
                $"alignment ratio={regionParentMacroAlignmentRatio:0.000}, expected >= 0.700"),
            Budget(
                "Region Vegetation-Groundwater Correlation",
                vegetationGroundwaterCorrelation >= 0.20f,
                $"correlation={vegetationGroundwaterCorrelation:0.000}, expected >= 0.200"),
            Budget(
                "Region Vegetation-Suitability Correlation",
                vegetationSuitabilityCorrelation >= 0.40f,
                $"correlation={vegetationSuitabilityCorrelation:0.000}, expected >= 0.400"),
            Budget(
                "World Forest to Region Vegetation Response",
                worldForestRegionVegetationCorrelation >= 0.20f,
                $"correlation={worldForestRegionVegetationCorrelation:0.000}, expected >= 0.200"),
            Budget(
                "World Forest to Local Tree Density Response",
                worldForestLocalTreeDensityCorrelation >= 0.08f,
                $"correlation={worldForestLocalTreeDensityCorrelation:0.000}, expected >= 0.080"),
            Budget(
                "World Mountain to Region Slope Response",
                worldMountainRegionSlopeCorrelation >= 0.12f,
                $"correlation={worldMountainRegionSlopeCorrelation:0.000}, expected >= 0.120"),
            Budget(
                "World Tropical Land Share",
                tropicalLandShare <= 0.42f,
                $"share={tropicalLandShare:0.000}, expected <= 0.420"),
            Budget(
                "World Arid Land Share",
                aridLandShare <= 0.58f,
                $"share={aridLandShare:0.000}, expected <= 0.580"),
            Budget(
                "World Cold Land Share",
                coldLandShare <= 0.52f,
                $"share={coldLandShare:0.000}, expected <= 0.520"),
            Budget(
                "World Desert Land Share",
                desertLandShare <= 0.34f,
                $"share={desertLandShare:0.000}, expected <= 0.340"),
            Budget(
                "Dense Forest Coverage",
                denseCoveragePass,
                CoverageDetail(
                    "dense forest",
                    denseForestSamples.Count,
                    hasDenseForestCoverage,
                    observedDenseForestMacro,
                    ensureBiomeCoverage)),
            Budget(
                "Tropical Coverage",
                tropicalCoveragePass,
                CoverageDetail(
                    "tropical",
                    tropicalSamples.Count,
                    hasTropicalCoverage,
                    observedTropicalMacro,
                    ensureBiomeCoverage)),
            Budget(
                "Arid Coverage",
                aridCoveragePass,
                CoverageDetail(
                    "arid",
                    aridSamples.Count,
                    hasAridCoverage,
                    observedAridMacro,
                    ensureBiomeCoverage)),
            BudgetWithSamples(
                "Dense Forest Canopy Median",
                denseForestSamples.Count,
                denseForestMedianTreeDensity >= 0.16f,
                $"median={denseForestMedianTreeDensity:0.000} across {denseForestSamples.Count} samples, expected >= 0.160"),
            BudgetWithSamples(
                "Tropical Canopy Median",
                tropicalSamples.Count,
                tropicalMedianTreeDensity >= 0.24f,
                $"median={tropicalMedianTreeDensity:0.000} across {tropicalSamples.Count} samples, expected >= 0.240"),
            BudgetWithSamples(
                "Arid Canopy Upper Bound",
                aridSamples.Count,
                aridMedianTreeDensity <= 0.14f,
                $"median={aridMedianTreeDensity:0.000} across {aridSamples.Count} samples, expected <= 0.140"),
            BudgetWithSamples(
                "Dense Forest Patch Coherence",
                denseForestPatchSamples.Count,
                denseForestMedianLargestPatchRatio >= 0.020f,
                $"median largest-patch ratio={denseForestMedianLargestPatchRatio:0.000} across {denseForestPatchSamples.Count} samples, expected >= 0.020"),
            BudgetWithSamples(
                "Dense Forest Opening Median",
                denseForestOpeningSamples.Count,
                denseForestMedianOpeningRatio >= 0.008f,
                $"median opening ratio={denseForestMedianOpeningRatio:0.000} across {denseForestOpeningSamples.Count} samples, expected >= 0.008"),
            BudgetWithSamples(
                "Dense Forest Opening Reachability",
                denseForestReachableOpeningSamples.Count,
                denseForestMedianReachableOpeningRatio >= 0.75f,
                $"median reachable-opening ratio={denseForestMedianReachableOpeningRatio:0.000} across {denseForestReachableOpeningSamples.Count} samples, expected >= 0.750"),
            BudgetWithSamples(
                "Local Surface Edge Continuity",
                localBoundarySamples,
                localSurfaceBoundaryMismatchRatio <= 0.05f,
                $"mismatch ratio={localSurfaceBoundaryMismatchRatio:0.000} across {localBoundarySamples} edge samples, expected <= 0.050"),
            BudgetWithSamples(
                "Local Water Edge Continuity",
                localBoundarySamples,
                localWaterBoundaryMismatchRatio <= 0.03f,
                $"mismatch ratio={localWaterBoundaryMismatchRatio:0.000} across {localBoundarySamples} edge samples, expected <= 0.030"),
            BudgetWithSamples(
                "Local Ecology Edge Continuity",
                localBoundarySamples,
                localEcologyBoundaryMismatchRatio <= 0.12f,
                $"mismatch ratio={localEcologyBoundaryMismatchRatio:0.000} across {localBoundarySamples} edge samples, expected <= 0.120"),
            BudgetWithSamples(
                "Local Tree Edge Continuity",
                localBoundarySamples,
                localTreeBoundaryMismatchRatio <= 0.10f,
                $"mismatch ratio={localTreeBoundaryMismatchRatio:0.000} across {localBoundarySamples} edge samples, expected <= 0.100"),
            BudgetWithSamples(
                "Local Surface Seam Band Continuity",
                localBoundaryBandSamples,
                localSurfaceBoundaryBandMismatchRatio <= 0.18f,
                $"mismatch ratio={localSurfaceBoundaryBandMismatchRatio:0.000} across {localBoundaryBandSamples} band samples, expected <= 0.180"),
            BudgetWithSamples(
                "Local Water Seam Band Continuity",
                localBoundaryBandSamples,
                localWaterBoundaryBandMismatchRatio <= 0.06f,
                $"mismatch ratio={localWaterBoundaryBandMismatchRatio:0.000} across {localBoundaryBandSamples} band samples, expected <= 0.060"),
            BudgetWithSamples(
                "Local Ecology Seam Band Continuity",
                localBoundaryBandSamples,
                localEcologyBoundaryBandMismatchRatio <= 0.25f,
                $"mismatch ratio={localEcologyBoundaryBandMismatchRatio:0.000} across {localBoundaryBandSamples} band samples, expected <= 0.250"),
            BudgetWithSamples(
                "Local Tree Seam Band Continuity",
                localBoundaryBandSamples,
                localTreeBoundaryBandMismatchRatio <= 0.16f,
                $"mismatch ratio={localTreeBoundaryBandMismatchRatio:0.000} across {localBoundaryBandSamples} band samples, expected <= 0.160"),
            Budget(
                "Local Tree-Suitability Correlation",
                localTreeSuitabilityCorrelation >= 0.15f,
                $"correlation={localTreeSuitabilityCorrelation:0.000}, expected >= 0.150"),
            Budget(
                "Local Tree Density Bounds",
                avgLocalTreeDensity >= 0.03f && avgLocalTreeDensity <= 0.62f,
                $"avg tree density={avgLocalTreeDensity:0.000}, expected 0.030..0.620"),
            Budget(
                "Embark Stage Diagnostics Coverage",
                localEmbarkStageDiagnosticsPresentCount == localEmbarkStageReportCount,
                $"diagnostics present for {localEmbarkStageDiagnosticsPresentCount}/{localEmbarkStageReportCount} sampled local embarks"),
            Budget(
                "Embark Stage Diagnostics Pass",
                localEmbarkStagePassCount == localEmbarkStageReportCount,
                localEmbarkStagePassCount == localEmbarkStageReportCount
                    ? $"passing stage reports={localEmbarkStagePassCount}/{localEmbarkStageReportCount}"
                    : $"passing stage reports={localEmbarkStagePassCount}/{localEmbarkStageReportCount}; {firstFailingLocalStageDetail}"),
        };

        return new WorldPipelineReport(
            SeedCount: seedCount,
            EvaluatedSeedCount: evaluatedSeedCount,
            BiomeCoverageRequested: ensureBiomeCoverage,
            SampledRegionCount: sampledRegionCount,
            WorldRiverEdgeMismatchRatio: worldRiverMismatchRatio,
            WorldRoadEdgeMismatchRatio: worldRoadMismatchRatio,
            RegionRiverEdgeMismatchRatio: regionRiverMismatchRatio,
            RegionRoadEdgeMismatchRatio: regionRoadMismatchRatio,
            LocalBoundarySampleCount: localBoundarySamples,
            LocalSurfaceBoundaryMismatchRatio: localSurfaceBoundaryMismatchRatio,
            LocalWaterBoundaryMismatchRatio: localWaterBoundaryMismatchRatio,
            LocalEcologyBoundaryMismatchRatio: localEcologyBoundaryMismatchRatio,
            LocalTreeBoundaryMismatchRatio: localTreeBoundaryMismatchRatio,
            RegionParentMacroAlignmentRatio: regionParentMacroAlignmentRatio,
            RegionVegetationGroundwaterCorrelation: vegetationGroundwaterCorrelation,
            RegionVegetationSuitabilityCorrelation: vegetationSuitabilityCorrelation,
            WorldForestRegionVegetationCorrelation: worldForestRegionVegetationCorrelation,
            WorldForestLocalTreeDensityCorrelation: worldForestLocalTreeDensityCorrelation,
            WorldMountainRegionSlopeCorrelation: worldMountainRegionSlopeCorrelation,
            TropicalLandShare: tropicalLandShare,
            AridLandShare: aridLandShare,
            ColdLandShare: coldLandShare,
            DesertLandShare: desertLandShare,
            DenseForestSampleCount: denseForestSamples.Count,
            TropicalSampleCount: tropicalSamples.Count,
            AridSampleCount: aridSamples.Count,
            DenseForestMedianTreeDensity: denseForestMedianTreeDensity,
            TropicalMedianTreeDensity: tropicalMedianTreeDensity,
            AridMedianTreeDensity: aridMedianTreeDensity,
            DenseForestMedianLargestPatchRatio: denseForestMedianLargestPatchRatio,
            DenseForestMedianOpeningRatio: denseForestMedianOpeningRatio,
            DenseForestMedianReachableOpeningRatio: denseForestMedianReachableOpeningRatio,
            DenseForestCoverageAchieved: hasDenseForestCoverage,
            TropicalCoverageAchieved: hasTropicalCoverage,
            AridCoverageAchieved: hasAridCoverage,
            LocalTreeSuitabilityCorrelation: localTreeSuitabilityCorrelation,
            AvgLocalTreeDensity: avgLocalTreeDensity,
            Budgets: budgets,
            LocalBoundaryBandSampleCount: localBoundaryBandSamples,
            LocalSurfaceBoundaryBandMismatchRatio: localSurfaceBoundaryBandMismatchRatio,
            LocalWaterBoundaryBandMismatchRatio: localWaterBoundaryBandMismatchRatio,
            LocalEcologyBoundaryBandMismatchRatio: localEcologyBoundaryBandMismatchRatio,
            LocalTreeBoundaryBandMismatchRatio: localTreeBoundaryBandMismatchRatio);
    }

    private static DepthBudgetResult Budget(string name, bool pass, string detail)
        => new(name, pass, detail);

    private static EmbarkGenerationStageId[] GetExpectedEmbarkStageOrder()
        =>
        [
            EmbarkGenerationStageId.Inputs,
            EmbarkGenerationStageId.SurfaceShape,
            EmbarkGenerationStageId.UndergroundStructure,
            EmbarkGenerationStageId.Hydrology,
            EmbarkGenerationStageId.Ecology,
            EmbarkGenerationStageId.HydrologyPolish,
            EmbarkGenerationStageId.CivilizationOverlay,
            EmbarkGenerationStageId.Playability,
            EmbarkGenerationStageId.Population,
        ];

    private static DepthBudgetResult BudgetWithSamples(string name, int sampleCount, bool pass, string detail)
    {
        if (sampleCount <= 0)
            return new DepthBudgetResult(name, true, "skipped (no matching biome samples)");
        return new DepthBudgetResult(name, pass, detail);
    }

    private static float Ratio(int numerator, int denominator)
        => denominator <= 0 ? 0f : numerator / (float)denominator;

    private static GeneratedRegionMap GetOrGenerateRegion(
        Dictionary<WorldCoord, GeneratedRegionMap> cache,
        RegionLayerGenerator generator,
        GeneratedWorldMap world,
        WorldCoord coord,
        int regionWidth,
        int regionHeight)
    {
        if (cache.TryGetValue(coord, out var existing))
            return existing;

        var generated = generator.Generate(world, coord, regionWidth, regionHeight);
        cache[coord] = generated;
        return generated;
    }

    private static void AccumulateWorldBiomeDistribution(
        GeneratedWorldMap world,
        ref int landTiles,
        ref int tropicalTiles,
        ref int savannaTiles,
        ref int desertTiles,
        ref int steppeTiles,
        ref int coldTiles)
    {
        for (var y = 0; y < world.Height; y++)
        for (var x = 0; x < world.Width; x++)
        {
            var macroBiomeId = world.GetTile(x, y).MacroBiomeId;
            if (MacroBiomeIds.IsOcean(macroBiomeId))
                continue;

            landTiles++;
            if (string.Equals(macroBiomeId, MacroBiomeIds.TropicalRainforest, StringComparison.OrdinalIgnoreCase))
                tropicalTiles++;
            if (string.Equals(macroBiomeId, MacroBiomeIds.Savanna, StringComparison.OrdinalIgnoreCase))
                savannaTiles++;
            if (string.Equals(macroBiomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase))
                desertTiles++;
            if (string.Equals(macroBiomeId, MacroBiomeIds.WindsweptSteppe, StringComparison.OrdinalIgnoreCase))
                steppeTiles++;
            if (IsColdMacro(macroBiomeId))
                coldTiles++;
        }
    }

    private static void AccumulateWorldEdgeContinuity(
        GeneratedWorldMap world,
        ref int riverPairs,
        ref int riverMismatches,
        ref int roadPairs,
        ref int roadMismatches)
    {
        for (var y = 0; y < world.Height; y++)
        for (var x = 0; x < world.Width; x++)
        {
            var tile = world.GetTile(x, y);

            if (x + 1 < world.Width)
            {
                var east = world.GetTile(x + 1, y);
                CountWorldRiverMismatch(tile, east, WorldRiverEdges.East, WorldRiverEdges.West, ref riverPairs, ref riverMismatches);
                CountWorldRoadMismatch(tile, east, WorldRoadEdges.East, WorldRoadEdges.West, ref roadPairs, ref roadMismatches);
            }

            if (y + 1 < world.Height)
            {
                var south = world.GetTile(x, y + 1);
                CountWorldRiverMismatch(tile, south, WorldRiverEdges.South, WorldRiverEdges.North, ref riverPairs, ref riverMismatches);
                CountWorldRoadMismatch(tile, south, WorldRoadEdges.South, WorldRoadEdges.North, ref roadPairs, ref roadMismatches);
            }
        }
    }

    private static void CountWorldRiverMismatch(
        GeneratedWorldTile tile,
        GeneratedWorldTile neighbor,
        WorldRiverEdges localEdge,
        WorldRiverEdges neighborEdge,
        ref int pairCount,
        ref int mismatchCount)
    {
        var localHas = WorldRiverEdgeMask.Has(tile.RiverEdges, localEdge);
        var neighborHas = WorldRiverEdgeMask.Has(neighbor.RiverEdges, neighborEdge);
        if (!localHas && !neighborHas)
            return;

        pairCount++;
        if (localHas != neighborHas)
            mismatchCount++;
    }

    private static void CountWorldRoadMismatch(
        GeneratedWorldTile tile,
        GeneratedWorldTile neighbor,
        WorldRoadEdges localEdge,
        WorldRoadEdges neighborEdge,
        ref int pairCount,
        ref int mismatchCount)
    {
        var localHas = WorldRoadEdgeMask.Has(tile.RoadEdges, localEdge);
        var neighborHas = WorldRoadEdgeMask.Has(neighbor.RoadEdges, neighborEdge);
        if (!localHas && !neighborHas)
            return;

        pairCount++;
        if (localHas != neighborHas)
            mismatchCount++;
    }

    private static void AccumulateRegionBoundaryContinuity(
        GeneratedRegionMap region,
        GeneratedRegionMap neighbor,
        bool isEastNeighbor,
        ref int riverPairs,
        ref int riverMismatches,
        ref int roadPairs,
        ref int roadMismatches)
    {
        if (isEastNeighbor)
        {
            for (var y = 0; y < region.Height; y++)
            {
                var local = region.GetTile(region.Width - 1, y);
                var adjacent = neighbor.GetTile(0, y);
                CountRegionRiverMismatch(local, adjacent, RegionRiverEdges.East, RegionRiverEdges.West, ref riverPairs, ref riverMismatches);
                CountRegionRoadMismatch(local, adjacent, RegionRoadEdges.East, RegionRoadEdges.West, ref roadPairs, ref roadMismatches);
            }

            return;
        }

        for (var x = 0; x < region.Width; x++)
        {
            var local = region.GetTile(x, region.Height - 1);
            var adjacent = neighbor.GetTile(x, 0);
            CountRegionRiverMismatch(local, adjacent, RegionRiverEdges.South, RegionRiverEdges.North, ref riverPairs, ref riverMismatches);
            CountRegionRoadMismatch(local, adjacent, RegionRoadEdges.South, RegionRoadEdges.North, ref roadPairs, ref roadMismatches);
        }
    }

    private static void CountRegionRiverMismatch(
        GeneratedRegionTile tile,
        GeneratedRegionTile neighbor,
        RegionRiverEdges localEdge,
        RegionRiverEdges neighborEdge,
        ref int pairCount,
        ref int mismatchCount)
    {
        var localHas = tile.HasRiver && RegionRiverEdgeMask.Has(tile.RiverEdges, localEdge);
        var neighborHas = neighbor.HasRiver && RegionRiverEdgeMask.Has(neighbor.RiverEdges, neighborEdge);
        if (!localHas && !neighborHas)
            return;

        pairCount++;
        if (localHas != neighborHas)
            mismatchCount++;
    }

    private static void AccumulateLocalBoundaryContinuity(
        GeneratedEmbarkMap local,
        GeneratedEmbarkMap? neighbor,
        bool isEastNeighbor,
        ref int sampleCount,
        ref int surfaceMismatchCount,
        ref int waterMismatchCount,
        ref int ecologyMismatchCount,
        ref int treeMismatchCount)
    {
        if (neighbor is null)
            return;

        var comparison = EmbarkBoundaryContinuity.CompareBoundary(local, neighbor, isEastNeighbor);
        sampleCount += comparison.SampleCount;
        surfaceMismatchCount += comparison.SurfaceFamilyMismatchCount;
        waterMismatchCount += comparison.WaterMismatchCount;
        ecologyMismatchCount += comparison.EcologyMismatchCount;
        treeMismatchCount += comparison.TreeMismatchCount;
    }

    private static void AccumulateLocalBoundaryBandContinuity(
        GeneratedEmbarkMap local,
        GeneratedEmbarkMap? neighbor,
        bool isEastNeighbor,
        int bandWidth,
        ref int sampleCount,
        ref int surfaceMismatchCount,
        ref int waterMismatchCount,
        ref int ecologyMismatchCount,
        ref int treeMismatchCount)
    {
        if (neighbor is null)
            return;

        var comparison = EmbarkBoundaryContinuity.CompareBoundaryBand(local, neighbor, isEastNeighbor, bandWidth);
        sampleCount += comparison.SampleCount;
        surfaceMismatchCount += comparison.SurfaceFamilyMismatchCount;
        waterMismatchCount += comparison.WaterMismatchCount;
        ecologyMismatchCount += comparison.EcologyMismatchCount;
        treeMismatchCount += comparison.TreeMismatchCount;
    }

    private static void CountRegionRoadMismatch(
        GeneratedRegionTile tile,
        GeneratedRegionTile neighbor,
        RegionRoadEdges localEdge,
        RegionRoadEdges neighborEdge,
        ref int pairCount,
        ref int mismatchCount)
    {
        var localHas = tile.HasRoad && RegionRoadEdgeMask.Has(tile.RoadEdges, localEdge);
        var neighborHas = neighbor.HasRoad && RegionRoadEdgeMask.Has(neighbor.RoadEdges, neighborEdge);
        if (!localHas && !neighborHas)
            return;

        pairCount++;
        if (localHas != neighborHas)
            mismatchCount++;
    }

    private static void AccumulateRegionEcology(
        GeneratedRegionMap region,
        List<float> vegetation,
        List<float> groundwater,
        List<float> suitability)
    {
        for (var y = 0; y < region.Height; y++)
        for (var x = 0; x < region.Width; x++)
        {
            var tile = region.GetTile(x, y);
            vegetation.Add(tile.VegetationDensity);
            groundwater.Add(tile.Groundwater);
            suitability.Add(tile.VegetationSuitability);
        }
    }

    private static void AccumulateRegionMacroAlignment(
        GeneratedRegionMap region,
        string parentMacroBiomeId,
        ref int aligned,
        ref int total)
    {
        for (var y = 0; y < region.Height; y++)
        for (var x = 0; x < region.Width; x++)
        {
            var variantId = region.GetTile(x, y).BiomeVariantId;
            total++;
            if (IsRegionVariantCompatibleWithMacro(variantId, parentMacroBiomeId))
                aligned++;
        }
    }

    private static bool IsRegionVariantCompatibleWithMacro(string variantId, string parentMacroBiomeId)
    {
        var resolvedMacro = RegionBiomeVariantIds.ResolveMacroBiomeId(variantId);
        if (string.Equals(resolvedMacro, parentMacroBiomeId, StringComparison.OrdinalIgnoreCase))
            return true;

        // Floodplains/river valleys are transitional and considered valid for non-ocean parents.
        if (!MacroBiomeIds.IsOcean(parentMacroBiomeId) &&
            (string.Equals(variantId, RegionBiomeVariantIds.FloodplainMarsh, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(variantId, RegionBiomeVariantIds.ReedMarsh, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(variantId, RegionBiomeVariantIds.RiverValley, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Cold evergreen families intentionally share regional morphology.
        if (string.Equals(parentMacroBiomeId, MacroBiomeIds.BorealForest, StringComparison.OrdinalIgnoreCase) &&
            RegionBiomeVariantIds.IsConiferVariant(variantId))
        {
            return true;
        }

        if (string.Equals(parentMacroBiomeId, MacroBiomeIds.Savanna, StringComparison.OrdinalIgnoreCase) &&
            (RegionBiomeVariantIds.IsSteppeVariant(variantId) ||
             string.Equals(variantId, RegionBiomeVariantIds.SavannaGrassland, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (string.Equals(parentMacroBiomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase) &&
            (RegionBiomeVariantIds.IsSteppeVariant(variantId) ||
             string.Equals(variantId, RegionBiomeVariantIds.AridBadlands, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (string.Equals(parentMacroBiomeId, MacroBiomeIds.Tundra, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(variantId, RegionBiomeVariantIds.PolarTundra, StringComparison.OrdinalIgnoreCase) ||
             RegionBiomeVariantIds.IsHighlandVariant(variantId)))
        {
            return true;
        }

        if (string.Equals(parentMacroBiomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(variantId, RegionBiomeVariantIds.GlacialField, StringComparison.OrdinalIgnoreCase) ||
             RegionBiomeVariantIds.IsHighlandVariant(variantId)))
        {
            return true;
        }

        return false;
    }

    private static float AverageRegionVegetation(GeneratedRegionMap region)
    {
        var total = 0f;
        var count = region.Width * region.Height;
        if (count <= 0)
            return 0f;

        for (var y = 0; y < region.Height; y++)
        for (var x = 0; x < region.Width; x++)
            total += region.GetTile(x, y).VegetationDensity;

        return total / count;
    }

    private static float AverageRegionSlope(GeneratedRegionMap region)
    {
        var total = 0f;
        var count = region.Width * region.Height;
        if (count <= 0)
            return 0f;

        for (var y = 0; y < region.Height; y++)
        for (var x = 0; x < region.Width; x++)
            total += region.GetTile(x, y).Slope / 255f;

        return total / count;
    }

    private static GeneratedEmbarkMap GetOrGenerateLocal(
        Dictionary<(int WorldX, int WorldY, int RegionX, int RegionY), GeneratedEmbarkMap> localCache,
        LocalLayerGenerator localGenerator,
        GeneratedRegionMap region,
        RegionCoord coord,
        LocalGenerationSettings settings)
    {
        var key = (coord.WorldX, coord.WorldY, coord.RegionX, coord.RegionY);
        if (localCache.TryGetValue(key, out var local))
            return local;

        local = localGenerator.Generate(region, coord, settings);
        localCache[key] = local;
        return local;
    }

    private static GeneratedEmbarkMap? TryGetAdjacentLocal(
        Dictionary<(int WorldX, int WorldY, int RegionX, int RegionY), GeneratedEmbarkMap> localCache,
        Dictionary<WorldCoord, GeneratedRegionMap> regionCache,
        LocalLayerGenerator localGenerator,
        RegionLayerGenerator regionGenerator,
        GeneratedWorldMap world,
        GeneratedRegionMap region,
        RegionCoord origin,
        int dx,
        int dy,
        int regionWidth,
        int regionHeight,
        LocalGenerationSettings settings)
    {
        var worldX = origin.WorldX;
        var worldY = origin.WorldY;
        var regionX = origin.RegionX + dx;
        var regionY = origin.RegionY + dy;
        var targetRegion = region;

        if (regionX < 0)
        {
            if (worldX == 0)
                return null;

            worldX--;
            regionX = regionWidth - 1;
            targetRegion = GetOrGenerateRegion(regionCache, regionGenerator, world, new WorldCoord(worldX, worldY), regionWidth, regionHeight);
        }
        else if (regionX >= regionWidth)
        {
            if (worldX + 1 >= world.Width)
                return null;

            worldX++;
            regionX = 0;
            targetRegion = GetOrGenerateRegion(regionCache, regionGenerator, world, new WorldCoord(worldX, worldY), regionWidth, regionHeight);
        }

        if (regionY < 0)
        {
            if (worldY == 0)
                return null;

            worldY--;
            regionY = regionHeight - 1;
            targetRegion = GetOrGenerateRegion(regionCache, regionGenerator, world, new WorldCoord(worldX, worldY), regionWidth, regionHeight);
        }
        else if (regionY >= regionHeight)
        {
            if (worldY + 1 >= world.Height)
                return null;

            worldY++;
            regionY = 0;
            targetRegion = GetOrGenerateRegion(regionCache, regionGenerator, world, new WorldCoord(worldX, worldY), regionWidth, regionHeight);
        }

        return GetOrGenerateLocal(
            localCache,
            localGenerator,
            targetRegion,
            new RegionCoord(worldX, worldY, regionX, regionY),
            settings);
    }

    private static WorldCoord[] BuildSampleWorldCoords(GeneratedWorldMap world, int seed, int targetCount)
    {
        var width = world.Width;
        var height = world.Height;
        var maxSamples = Math.Min(targetCount, width * height);
        if (maxSamples <= 0)
            return Array.Empty<WorldCoord>();

        var coords = new List<WorldCoord>(maxSamples);
        var seen = new HashSet<WorldCoord>();

        void TryAddCoord(WorldCoord coord)
        {
            if (coords.Count >= maxSamples)
                return;
            if (coord.X < 0 || coord.Y < 0 || coord.X >= width || coord.Y >= height)
                return;
            if (!seen.Add(coord))
                return;

            coords.Add(coord);
        }

        // Keep one center + one corner anchor for broad coverage.
        TryAddCoord(new WorldCoord(width / 2, height / 2));
        TryAddCoord(new WorldCoord(0, 0));

        var denseForestCandidates = new List<WorldCoord>(32);
        var tropicalCandidates = new List<WorldCoord>(24);
        var aridCandidates = new List<WorldCoord>(24);
        var mountainCandidates = new List<WorldCoord>(24);
        var riverCandidates = new List<WorldCoord>(24);
        WorldCoord? peakMountainCoord = null;
        var peakMountainCover = float.MinValue;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var tile = world.GetTile(x, y);
            var coord = new WorldCoord(x, y);

            if (IsDenseForestMacro(tile.MacroBiomeId))
                denseForestCandidates.Add(coord);
            if (string.Equals(tile.MacroBiomeId, MacroBiomeIds.TropicalRainforest, StringComparison.OrdinalIgnoreCase))
                tropicalCandidates.Add(coord);
            if (IsAridMacro(tile.MacroBiomeId))
                aridCandidates.Add(coord);
            if (string.Equals(tile.MacroBiomeId, MacroBiomeIds.Highland, StringComparison.OrdinalIgnoreCase) ||
                tile.MountainCover >= 0.48f)
            {
                mountainCandidates.Add(coord);
            }

            if (tile.MountainCover > peakMountainCover)
            {
                peakMountainCover = tile.MountainCover;
                peakMountainCoord = coord;
            }

            if (tile.HasRiver)
                riverCandidates.Add(coord);
        }

        AddStratifiedCandidate(denseForestCandidates, seed, 12011, TryAddCoord);
        AddStratifiedCandidate(tropicalCandidates, seed, 12029, TryAddCoord);
        AddStratifiedCandidate(aridCandidates, seed, 12047, TryAddCoord);
        if (peakMountainCoord is not null)
            TryAddCoord(peakMountainCoord.Value);
        AddStratifiedCandidate(mountainCandidates, seed, 12071, TryAddCoord);
        AddStratifiedCandidate(riverCandidates, seed, 12089, TryAddCoord);

        // Edge anchors improve continuity checks across neighboring regions.
        TryAddCoord(new WorldCoord(width - 1, height - 1));
        TryAddCoord(new WorldCoord(width - 1, 0));
        TryAddCoord(new WorldCoord(0, height - 1));

        var rng = new Random(unchecked((seed * 73856093) ^ (width * 19349663) ^ (height * 83492791)));
        var attempts = 0;
        var maxAttempts = Math.Max(32, maxSamples * 20);
        while (coords.Count < maxSamples && attempts < maxAttempts)
        {
            TryAddCoord(new WorldCoord(rng.Next(0, width), rng.Next(0, height)));
            attempts++;
        }

        if (coords.Count < maxSamples)
        {
            var total = width * height;
            var start = PositiveHash(SeedHash.Hash(seed, width, height, 12113)) % total;
            for (var offset = 0; offset < total && coords.Count < maxSamples; offset++)
            {
                var idx = (start + offset) % total;
                TryAddCoord(new WorldCoord(idx % width, idx / width));
            }
        }

        return coords.ToArray();
    }

    private static void AddStratifiedCandidate(List<WorldCoord> candidates, int seed, int salt, Action<WorldCoord> add)
    {
        if (candidates.Count == 0)
            return;

        var idx = PositiveHash(SeedHash.Hash(seed, candidates.Count, salt, 12139)) % candidates.Count;
        add(candidates[idx]);
    }

    private static int PositiveHash(int value)
        => value & int.MaxValue;

    private static bool IsDenseForestMacro(string macroBiomeId)
        => string.Equals(macroBiomeId, MacroBiomeIds.ConiferForest, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(macroBiomeId, MacroBiomeIds.BorealForest, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(macroBiomeId, MacroBiomeIds.TropicalRainforest, StringComparison.OrdinalIgnoreCase);

    private static bool IsAridMacro(string macroBiomeId)
        => string.Equals(macroBiomeId, MacroBiomeIds.WindsweptSteppe, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(macroBiomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(macroBiomeId, MacroBiomeIds.Savanna, StringComparison.OrdinalIgnoreCase);

    private static bool IsColdMacro(string macroBiomeId)
        => string.Equals(macroBiomeId, MacroBiomeIds.Tundra, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(macroBiomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(macroBiomeId, MacroBiomeIds.BorealForest, StringComparison.OrdinalIgnoreCase);

    private static int CountSurfaceTiles(GeneratedEmbarkMap map, string tileDefId)
    {
        var count = 0;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (string.Equals(map.GetTile(x, y, 0).TileDefId, tileDefId, StringComparison.OrdinalIgnoreCase))
                count++;
        }

        return count;
    }

    private static int CountLargestConnectedSurfacePatch(GeneratedEmbarkMap map, string tileDefId)
    {
        if (map.Width <= 0 || map.Height <= 0)
            return 0;

        var visited = new bool[map.Width, map.Height];
        var queue = new Queue<(int X, int Y)>();
        var largest = 0;

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (visited[x, y] || !string.Equals(map.GetTile(x, y, 0).TileDefId, tileDefId, StringComparison.OrdinalIgnoreCase))
                continue;

            visited[x, y] = true;
            queue.Enqueue((x, y));
            var patchSize = 0;

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                patchSize++;

                EnqueueSurfacePatchNeighbor(map, visited, queue, cx + 1, cy, tileDefId);
                EnqueueSurfacePatchNeighbor(map, visited, queue, cx - 1, cy, tileDefId);
                EnqueueSurfacePatchNeighbor(map, visited, queue, cx, cy + 1, tileDefId);
                EnqueueSurfacePatchNeighbor(map, visited, queue, cx, cy - 1, tileDefId);
            }

            if (patchSize > largest)
                largest = patchSize;
        }

        return largest;
    }

    private static ForestMobilityMetrics MeasureForestMobility(GeneratedEmbarkMap map)
    {
        var surfaceTiles = map.Width * map.Height;
        if (surfaceTiles <= 0)
            return new ForestMobilityMetrics(0, 0f, 0, 0, 0f, 0f);

        var treeTiles = 0;
        var openingTiles = 0;
        var openingMask = new bool[map.Width, map.Height];

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y, 0);
            if (string.Equals(tile.TileDefId, GeneratedTileDefIds.Tree, StringComparison.OrdinalIgnoreCase))
            {
                treeTiles++;
                continue;
            }

            if (!IsForestOpeningTile(map, x, y))
                continue;

            openingMask[x, y] = true;
            openingTiles++;
        }

        var treeDensity = treeTiles / (float)surfaceTiles;
        if (openingTiles <= 0)
            return new ForestMobilityMetrics(treeTiles, treeDensity, 0, 0, 0f, 0f);

        var reachableOpeningTiles = CountOpeningTilesInLargestTraversableSurfaceComponent(map, openingMask);
        return new ForestMobilityMetrics(
            treeTiles,
            treeDensity,
            openingTiles,
            reachableOpeningTiles,
            openingTiles / (float)surfaceTiles,
            reachableOpeningTiles / (float)openingTiles);
    }

    private static bool IsForestOpeningTile(GeneratedEmbarkMap map, int x, int y)
    {
        var tile = map.GetTile(x, y, 0);
        if (!IsTraversableSurfaceTile(tile))
            return false;

        return CountAdjacentSurfaceTrees(map, x, y) >= 3;
    }

    private static int CountAdjacentSurfaceTrees(GeneratedEmbarkMap map, int x, int y)
    {
        var count = 0;
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0)
                continue;

            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
                continue;
            if (string.Equals(map.GetTile(nx, ny, 0).TileDefId, GeneratedTileDefIds.Tree, StringComparison.OrdinalIgnoreCase))
                count++;
        }

        return count;
    }

    private static int CountOpeningTilesInLargestTraversableSurfaceComponent(GeneratedEmbarkMap map, bool[,] openingMask)
    {
        var visited = new bool[map.Width, map.Height];
        var queue = new Queue<(int X, int Y)>();
        var bestOpeningCount = 0;

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (visited[x, y])
                continue;
            if (!IsTraversableSurfaceTile(map.GetTile(x, y, 0)))
                continue;

            visited[x, y] = true;
            queue.Enqueue((x, y));
            var openingCount = 0;

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                if (openingMask[cx, cy])
                    openingCount++;

                EnqueueTraversableSurfaceNeighbor(map, visited, queue, cx + 1, cy);
                EnqueueTraversableSurfaceNeighbor(map, visited, queue, cx - 1, cy);
                EnqueueTraversableSurfaceNeighbor(map, visited, queue, cx, cy + 1);
                EnqueueTraversableSurfaceNeighbor(map, visited, queue, cx, cy - 1);
            }

            if (openingCount > bestOpeningCount)
                bestOpeningCount = openingCount;
        }

        return bestOpeningCount;
    }

    private static void EnqueueTraversableSurfaceNeighbor(
        GeneratedEmbarkMap map,
        bool[,] visited,
        Queue<(int X, int Y)> queue,
        int x,
        int y)
    {
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            return;
        if (visited[x, y])
            return;
        if (!IsTraversableSurfaceTile(map.GetTile(x, y, 0)))
            return;

        visited[x, y] = true;
        queue.Enqueue((x, y));
    }

    private static bool IsTraversableSurfaceTile(GeneratedTile tile)
    {
        if (!tile.IsPassable)
            return false;
        if (string.Equals(tile.TileDefId, GeneratedTileDefIds.Tree, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(tile.TileDefId, GeneratedTileDefIds.Water, StringComparison.OrdinalIgnoreCase))
            return false;
        if (tile.FluidType is GeneratedFluidType.Water or GeneratedFluidType.Magma)
            return false;

        return true;
    }

    private static void EnqueueSurfacePatchNeighbor(
        GeneratedEmbarkMap map,
        bool[,] visited,
        Queue<(int X, int Y)> queue,
        int x,
        int y,
        string tileDefId)
    {
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            return;
        if (visited[x, y])
            return;
        if (!string.Equals(map.GetTile(x, y, 0).TileDefId, tileDefId, StringComparison.OrdinalIgnoreCase))
            return;

        visited[x, y] = true;
        queue.Enqueue((x, y));
    }

    private static void AddMacroSample(
        Dictionary<string, List<float>> buckets,
        string macroBiomeId,
        float sample)
    {
        if (!buckets.TryGetValue(macroBiomeId, out var list))
        {
            list = new List<float>(8);
            buckets[macroBiomeId] = list;
        }

        list.Add(sample);
    }

    private static bool HasMacroSamples(
        Dictionary<string, List<float>> buckets,
        params string[] macroBiomeIds)
    {
        for (var i = 0; i < macroBiomeIds.Length; i++)
        {
            if (!buckets.TryGetValue(macroBiomeIds[i], out var bucket))
                continue;
            if (bucket.Count > 0)
                return true;
        }

        return false;
    }

    private static void ObserveMacroCoverage(
        GeneratedWorldMap world,
        ref bool observedDenseForestMacro,
        ref bool observedTropicalMacro,
        ref bool observedAridMacro)
    {
        if (observedDenseForestMacro && observedTropicalMacro && observedAridMacro)
            return;

        for (var y = 0; y < world.Height; y++)
        for (var x = 0; x < world.Width; x++)
        {
            var macro = world.GetTile(x, y).MacroBiomeId;
            if (!observedDenseForestMacro && IsDenseForestMacro(macro))
                observedDenseForestMacro = true;
            if (!observedTropicalMacro && string.Equals(macro, MacroBiomeIds.TropicalRainforest, StringComparison.OrdinalIgnoreCase))
                observedTropicalMacro = true;
            if (!observedAridMacro && IsAridMacro(macro))
                observedAridMacro = true;

            if (observedDenseForestMacro && observedTropicalMacro && observedAridMacro)
                return;
        }
    }

    private static string CoverageDetail(
        string label,
        int sampleCount,
        bool sampled,
        bool observedMacro,
        bool coverageRequested)
    {
        if (!coverageRequested)
            return $"samples={sampleCount}, coverage check disabled";
        if (sampled)
            return $"samples={sampleCount}, coverage achieved";
        if (!observedMacro)
            return $"samples={sampleCount}, no {label} macro biome observed in evaluated seeds";

        return $"samples={sampleCount}, {label} macro observed but no sampled region captured it";
    }

    private static List<float> CollectMacroSamples(
        Dictionary<string, List<float>> buckets,
        params string[] macroBiomeIds)
    {
        var samples = new List<float>(32);
        for (var i = 0; i < macroBiomeIds.Length; i++)
        {
            if (!buckets.TryGetValue(macroBiomeIds[i], out var bucket))
                continue;

            samples.AddRange(bucket);
        }

        return samples;
    }

    private static float Median(List<float> values)
    {
        if (values.Count == 0)
            return 0f;

        values.Sort();
        var mid = values.Count / 2;
        if ((values.Count % 2) == 1)
            return values[mid];

        return (values[mid - 1] + values[mid]) * 0.5f;
    }

    private static float PearsonCorrelation(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        if (a.Count != b.Count || a.Count < 2)
            return 0f;

        var count = a.Count;
        double sumA = 0;
        double sumB = 0;
        for (var i = 0; i < count; i++)
        {
            sumA += a[i];
            sumB += b[i];
        }

        var meanA = sumA / count;
        var meanB = sumB / count;
        double covariance = 0;
        double varianceA = 0;
        double varianceB = 0;

        for (var i = 0; i < count; i++)
        {
            var da = a[i] - meanA;
            var db = b[i] - meanB;
            covariance += da * db;
            varianceA += da * da;
            varianceB += db * db;
        }

        if (varianceA <= 1e-9 || varianceB <= 1e-9)
            return 0f;

        var denom = Math.Sqrt(varianceA * varianceB);
        if (denom <= 1e-9)
            return 0f;

        return Math.Clamp((float)(covariance / denom), -1f, 1f);
    }

    private static bool BordersArePassable(GeneratedEmbarkMap map)
    {
        for (var x = 0; x < map.Width; x++)
        {
            if (!map.GetTile(x, 0, 0).IsPassable) return false;
            if (!map.GetTile(x, map.Height - 1, 0).IsPassable) return false;
        }

        for (var y = 0; y < map.Height; y++)
        {
            if (!map.GetTile(0, y, 0).IsPassable) return false;
            if (!map.GetTile(map.Width - 1, y, 0).IsPassable) return false;
        }

        return true;
    }

    private static bool HasCornerToCornerSurfacePath(GeneratedEmbarkMap map)
    {
        if (map.Width <= 0 || map.Height <= 0)
            return false;
        if (!map.GetTile(0, 0, 0).IsPassable || !map.GetTile(map.Width - 1, map.Height - 1, 0).IsPassable)
            return false;

        var visited = new bool[map.Width, map.Height];
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((0, 0));
        visited[0, 0] = true;

        var dirs = new (int X, int Y)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            if (x == map.Width - 1 && y == map.Height - 1)
                return true;

            foreach (var (dx, dy) in dirs)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || nx >= map.Width || ny < 0 || ny >= map.Height)
                    continue;
                if (visited[nx, ny] || !map.GetTile(nx, ny, 0).IsPassable)
                    continue;

                visited[nx, ny] = true;
                queue.Enqueue((nx, ny));
            }
        }

        return false;
    }
}

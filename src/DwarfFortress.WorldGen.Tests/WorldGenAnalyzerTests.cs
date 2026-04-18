using DwarfFortress.WorldGen.Analysis;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Maps;
using DwarfFortress.WorldGen.Story;
using DwarfFortress.WorldGen.World;
using System.Linq;

namespace DwarfFortress.WorldGen.Tests;

public sealed class WorldGenAnalyzerTests
{
    [Fact]
    public void AnalyzeMap_ReportsExpectedSafetySignals()
    {
        var lore = WorldLoreGenerator.Generate(seed: 8, width: 48, height: 48, depth: 8);
        var map = EmbarkGenerator.Generate(width: 48, height: 48, depth: 8, seed: 8, biomeId: lore.BiomeId);

        var metrics = WorldGenAnalyzer.AnalyzeMap(map);

        Assert.True(metrics.BordersPassable);
        Assert.True(metrics.CornerPathExists);
        Assert.InRange(metrics.PassableSurfaceRatio, 0.68f, 1.00f);
    }

    [Fact]
    public void AnalyzeEmbarkStages_DefaultDiagnosticsBudgets_Pass()
    {
        var map = EmbarkGenerator.Generate(width: 48, height: 48, depth: 8, seed: 42, biomeId: MacroBiomeIds.ConiferForest);

        var report = WorldGenAnalyzer.AnalyzeEmbarkStages(map);

        Assert.True(report.Passed, "Expected embark stage diagnostics budgets to pass on current generator baseline.");
        Assert.Equal(42, report.Seed);
        Assert.Equal(map.Width * map.Height, report.SurfaceTileCount);
        Assert.Equal(map.Width * map.Height * (map.Depth - 1), report.UndergroundTileCount);
        Assert.Equal(map.Diagnostics!.StageSnapshots.Count, report.StageSnapshots.Count);
    }

    [Fact]
    public void AnalyzeLore_ComputesEventBreakdown()
    {
        var lore = new WorldLoreState
        {
            BiomeId = MacroBiomeIds.TemperatePlains,
            Factions =
            [
                new FactionLoreState { Id = "civ_a", IsHostile = false },
                new FactionLoreState { Id = "civ_b", IsHostile = true },
            ],
            FactionRelations =
            [
                new FactionRelationLoreState { FactionAId = "civ_a", FactionBId = "civ_b", Stance = RelationStanceIds.Hostile },
            ],
            Sites =
            [
                new SiteLoreState { Id = "site_a", Status = SiteStatusIds.Growing, Development = 0.7f, Security = 0.4f },
                new SiteLoreState { Id = "site_b", Status = SiteStatusIds.Fortified, Development = 0.5f, Security = 0.9f },
            ],
            History =
            [
                new HistoricalEventLoreState { Type = HistoricalEventTypeIds.Treaty },
                new HistoricalEventLoreState { Type = HistoricalEventTypeIds.Raid },
                new HistoricalEventLoreState { Type = HistoricalEventTypeIds.Skirmish },
                new HistoricalEventLoreState { Type = HistoricalEventTypeIds.Crisis },
                new HistoricalEventLoreState { Type = HistoricalEventTypeIds.Founding },
            ],
        };

        var metrics = WorldGenAnalyzer.AnalyzeLore(lore);

        Assert.Equal(lore.BiomeId, metrics.BiomeId);
        Assert.Equal(lore.Factions.Count, metrics.FactionCount);
        Assert.Equal(lore.FactionRelations.Count, metrics.RelationCount);
        Assert.Equal(lore.Sites.Count, metrics.SiteCount);
        Assert.Equal(lore.History.Count, metrics.EventCount);
        Assert.Equal(1, metrics.TreatyCount);
        Assert.Equal(1, metrics.RaidCount);
        Assert.Equal(1, metrics.SkirmishCount);
        Assert.Equal(1, metrics.CrisisCount);
        Assert.Equal(1, metrics.FoundingCount);
        Assert.Equal(1, metrics.HostileFactionCount);
        Assert.Equal(1, metrics.HostileRelationCount);
        Assert.Equal(1, metrics.GrowingSiteCount);
        Assert.Equal(1, metrics.FortifiedSiteCount);
        Assert.InRange(metrics.HostileEventRatio, 0f, 1f);
        Assert.InRange(metrics.AvgSiteDevelopment, 0f, 1f);
        Assert.InRange(metrics.AvgSiteSecurity, 0f, 1f);
    }

    [Fact]
    public void AnalyzeDepthSamples_DefaultBudgets_Pass()
    {
        var report = WorldGenAnalyzer.AnalyzeDepthSamples(seedStart: 0, seedCount: 30, width: 48, height: 48, depth: 8);

        Assert.True(
            report.Passed,
            $"Expected default depth budgets to pass on current generator baseline. Failing budgets: {string.Join(" | ", report.Budgets.Where(b => !b.Passed).Select(b => $"{b.Name}: {b.Detail}"))}");
    }

    [Fact]
    public void AnalyzePipelineSamples_DefaultBudgets_Pass()
    {
        const int seedStart = 32;
        const int seedCount = 5;
        const int worldWidth = 20;
        const int worldHeight = 20;
        const int regionWidth = 16;
        const int regionHeight = 16;
        const int sampledRegionsPerWorld = 7;
        const int localWidth = 48;
        const int localHeight = 48;
        const int localDepth = 8;

        var report = WorldGenAnalyzer.AnalyzePipelineSamples(
            seedStart: seedStart,
            seedCount: seedCount,
            worldWidth: worldWidth,
            worldHeight: worldHeight,
            regionWidth: regionWidth,
            regionHeight: regionHeight,
            sampledRegionsPerWorld: sampledRegionsPerWorld,
            localWidth: localWidth,
            localHeight: localHeight,
            localDepth: localDepth);

        Assert.True(
            report.Passed,
            $"Expected layered worldgen pipeline budgets to pass on current generator baseline. Failing budgets: {string.Join(" | ", report.Budgets.Where(b => !b.Passed).Select(b => $"{b.Name}: {b.Detail}"))}");
        Assert.InRange(report.RegionParentMacroAlignmentRatio, 0f, 1f);
        Assert.InRange(report.RegionVegetationSuitabilityCorrelation, 0f, 1f);
        Assert.InRange(report.LocalTreeSuitabilityCorrelation, -1f, 1f);
        Assert.InRange(report.LocalSurfaceBoundaryMismatchRatio, 0f, 1f);
        Assert.InRange(report.LocalWaterBoundaryMismatchRatio, 0f, 1f);
        Assert.InRange(report.LocalEcologyBoundaryMismatchRatio, 0f, 1f);
        Assert.InRange(report.LocalTreeBoundaryMismatchRatio, 0f, 1f);
        Assert.InRange(report.LocalSurfaceBoundaryBandMismatchRatio, 0f, 1f);
        Assert.InRange(report.LocalWaterBoundaryBandMismatchRatio, 0f, 1f);
        Assert.InRange(report.LocalEcologyBoundaryBandMismatchRatio, 0f, 1f);
        Assert.InRange(report.LocalTreeBoundaryBandMismatchRatio, 0f, 1f);
        Assert.InRange(report.WorldForestRegionVegetationCorrelation, -1f, 1f);
        Assert.InRange(report.WorldForestLocalTreeDensityCorrelation, -1f, 1f);
        Assert.InRange(report.WorldMountainRegionSlopeCorrelation, -1f, 1f);
        Assert.InRange(report.TropicalLandShare, 0f, 1f);
        Assert.InRange(report.AridLandShare, 0f, 1f);
        Assert.InRange(report.ColdLandShare, 0f, 1f);
        Assert.InRange(report.DesertLandShare, 0f, 1f);
        Assert.Equal(seedCount, report.SeedCount);
        Assert.True(report.EvaluatedSeedCount >= seedCount);
        Assert.False(report.BiomeCoverageRequested);
        Assert.InRange(report.LocalBoundarySampleCount, 0, int.MaxValue);
        Assert.InRange(report.LocalBoundaryBandSampleCount, 0, int.MaxValue);
        Assert.InRange(report.DenseForestSampleCount, 0, int.MaxValue);
        Assert.InRange(report.TropicalSampleCount, 0, int.MaxValue);
        Assert.InRange(report.AridSampleCount, 0, int.MaxValue);
        Assert.InRange(report.DenseForestMedianTreeDensity, 0f, 1f);
        Assert.InRange(report.TropicalMedianTreeDensity, 0f, 1f);
        Assert.InRange(report.AridMedianTreeDensity, 0f, 1f);
        Assert.InRange(report.DenseForestMedianLargestPatchRatio, 0f, 1f);
        Assert.Equal(report.DenseForestSampleCount > 0, report.DenseForestCoverageAchieved);
        Assert.Equal(report.TropicalSampleCount > 0, report.TropicalCoverageAchieved);
        Assert.Equal(report.AridSampleCount > 0, report.AridCoverageAchieved);

        var diagnosticsCoverageBudget = report.Budgets.Single(b => b.Name == "Embark Stage Diagnostics Coverage");
        var diagnosticsPassBudget = report.Budgets.Single(b => b.Name == "Embark Stage Diagnostics Pass");
        var surfaceEdgeBudget = report.Budgets.Single(b => b.Name == "Local Surface Edge Continuity");
        var waterEdgeBudget = report.Budgets.Single(b => b.Name == "Local Water Edge Continuity");
        var ecologyBandBudget = report.Budgets.Single(b => b.Name == "Local Ecology Seam Band Continuity");
        var treeBandBudget = report.Budgets.Single(b => b.Name == "Local Tree Seam Band Continuity");
        Assert.True(diagnosticsCoverageBudget.Passed);
        Assert.True(diagnosticsPassBudget.Passed);
        Assert.True(surfaceEdgeBudget.Passed);
        Assert.True(waterEdgeBudget.Passed);
        Assert.True(ecologyBandBudget.Passed);
        Assert.True(treeBandBudget.Passed);

        var worldGenerator = new WorldLayerGenerator();
        var hasDenseForest = false;
        var hasTropical = false;
        var hasArid = false;

        for (var i = 0; i < seedCount; i++)
        {
            var world = worldGenerator.Generate(seedStart + i, worldWidth, worldHeight);
            for (var y = 0; y < world.Height; y++)
            for (var x = 0; x < world.Width; x++)
            {
                var macro = world.GetTile(x, y).MacroBiomeId;
                hasDenseForest |= string.Equals(macro, MacroBiomeIds.ConiferForest, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(macro, MacroBiomeIds.BorealForest, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(macro, MacroBiomeIds.TropicalRainforest, StringComparison.OrdinalIgnoreCase);
                hasTropical |= string.Equals(macro, MacroBiomeIds.TropicalRainforest, StringComparison.OrdinalIgnoreCase);
                hasArid |= string.Equals(macro, MacroBiomeIds.WindsweptSteppe, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(macro, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(macro, MacroBiomeIds.Savanna, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (hasDenseForest)
            Assert.True(report.DenseForestSampleCount > 0, "Dense forest macro biomes exist in sampled worlds but no dense-forest samples were collected.");
        if (hasTropical)
            Assert.True(report.TropicalSampleCount > 0, "Tropical macro biomes exist in sampled worlds but no tropical samples were collected.");
        if (hasArid)
            Assert.True(report.AridSampleCount > 0, "Arid macro biomes exist in sampled worlds but no arid samples were collected.");
    }

    [Fact]
    public void AnalyzePipelineSamples_WhenCoverageRequested_ReportsCoverageBudgets()
    {
        var report = WorldGenAnalyzer.AnalyzePipelineSamples(
            seedStart: 0,
            seedCount: 3,
            worldWidth: 16,
            worldHeight: 16,
            sampledRegionsPerWorld: 5,
            ensureBiomeCoverage: true,
            maxAdditionalSeeds: 40);

        Assert.True(report.BiomeCoverageRequested);
        Assert.True(report.EvaluatedSeedCount >= report.SeedCount);
        Assert.True(report.EvaluatedSeedCount <= report.SeedCount + 40);

        var denseBudget = report.Budgets.Single(b => b.Name == "Dense Forest Coverage");
        var tropicalBudget = report.Budgets.Single(b => b.Name == "Tropical Coverage");
        var aridBudget = report.Budgets.Single(b => b.Name == "Arid Coverage");

        if (report.DenseForestCoverageAchieved)
            Assert.True(denseBudget.Passed);
        if (!denseBudget.Passed)
            Assert.False(report.DenseForestCoverageAchieved);

        if (report.TropicalCoverageAchieved)
            Assert.True(tropicalBudget.Passed);
        if (!tropicalBudget.Passed)
            Assert.False(report.TropicalCoverageAchieved);

        if (report.AridCoverageAchieved)
            Assert.True(aridBudget.Passed);
        if (!aridBudget.Passed)
            Assert.False(report.AridCoverageAchieved);
    }
}

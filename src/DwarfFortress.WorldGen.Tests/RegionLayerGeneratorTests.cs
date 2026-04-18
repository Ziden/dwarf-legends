using System.Text;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.History;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Regions;
using DwarfFortress.WorldGen.World;

namespace DwarfFortress.WorldGen.Tests;

public sealed class RegionLayerGeneratorTests
{
    [Fact]
    public void Generate_SameInput_ProducesDeterministicRegion()
    {
        var world = new WorldLayerGenerator().Generate(seed: 9, width: 12, height: 12);
        var gen = new RegionLayerGenerator();
        var coord = new WorldCoord(4, 7);

        var a = gen.Generate(world, coord, regionWidth: 24, regionHeight: 20);
        var b = gen.Generate(world, coord, regionWidth: 24, regionHeight: 20);

        for (var x = 0; x < a.Width; x++)
        for (var y = 0; y < a.Height; y++)
            Assert.Equal(a.GetTile(x, y), b.GetTile(x, y));
    }

    [Fact]
    public void Generate_ChangesAcrossWorldCoordinates()
    {
        var world = new WorldLayerGenerator().Generate(seed: 9, width: 12, height: 12);
        var gen = new RegionLayerGenerator();

        var a = gen.Generate(world, new WorldCoord(1, 1), regionWidth: 20, regionHeight: 20);
        var b = gen.Generate(world, new WorldCoord(9, 9), regionWidth: 20, regionHeight: 20);

        Assert.NotEqual(Fingerprint(a), Fingerprint(b));
    }

    [Fact]
    public void Generate_ProducesBoundedAndUsableRegionSignals()
    {
        var world = new WorldLayerGenerator().Generate(seed: 104, width: 10, height: 10);
        var gen = new RegionLayerGenerator();
        var region = gen.Generate(world, new WorldCoord(3, 4), regionWidth: 32, regionHeight: 32);

        Assert.Equal(3, region.WorldCoord.X);
        Assert.Equal(4, region.WorldCoord.Y);

        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
        {
            var tile = region.GetTile(x, y);
            Assert.False(string.IsNullOrWhiteSpace(tile.BiomeVariantId));
            Assert.True(RegionSurfaceClassIds.IsKnown(tile.SurfaceClassId), $"Unknown surface class '{tile.SurfaceClassId}' at ({x},{y}).");
            Assert.InRange(tile.Slope, byte.MinValue, byte.MaxValue);
            Assert.InRange(tile.VegetationDensity, 0f, 1f);
            Assert.InRange(tile.ResourceRichness, 0f, 1f);
            Assert.InRange(tile.SoilDepth, 0f, 1f);
            Assert.InRange(tile.Groundwater, 0f, 1f);
            Assert.InRange(tile.TemperatureBand, 0f, 1f);
            Assert.InRange(tile.MoistureBand, 0f, 1f);
            Assert.InRange(tile.FlowAccumulationBand, 0f, 1f);
            Assert.InRange(tile.RiverDischarge, 0f, 12f);
            AssertEcologyProfileBounds(tile.EcologyEdges.North);
            AssertEcologyProfileBounds(tile.EcologyEdges.East);
            AssertEcologyProfileBounds(tile.EcologyEdges.South);
            AssertEcologyProfileBounds(tile.EcologyEdges.West);
            if (tile.HasRiver)
            {
                Assert.True(tile.RiverDischarge >= 1f);
                Assert.InRange(tile.RiverOrder, (byte)1, (byte)8);
                Assert.NotEqual(RegionRiverEdges.None, tile.RiverEdges);
            }
            else
            {
                Assert.Equal((byte)0, tile.RiverOrder);
            }
        }
    }

    [Fact]
    public void Generate_HydrologyTiles_HaveHigherGroundwaterThanDryLand()
    {
        var world = new GeneratedWorldMap(seed: 515, width: 1, height: 1);
        world.SetTile(0, 0, new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.TemperatePlains,
            ElevationBand: 0.42f,
            TemperatureBand: 0.52f,
            MoistureBand: 0.82f,
            DrainageBand: 0.90f,
            GeologyProfileId: GeologyProfileIds.AlluvialBasin,
            FactionPressure: 0.20f,
            FlowAccumulation: 20f,
            HasRiver: true,
            RiverEdges: WorldRiverEdges.North | WorldRiverEdges.South,
            RiverDischarge: 5.5f));

        var region = new RegionLayerGenerator().Generate(world, new WorldCoord(0, 0), regionWidth: 32, regionHeight: 32);
        var waterGroundwater = 0f;
        var waterSoil = 0f;
        var waterSlope = 0f;
        var waterCount = 0;
        var landGroundwater = 0f;
        var landSoil = 0f;
        var landSlope = 0f;
        var landCount = 0;

        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
        {
            var tile = region.GetTile(x, y);
            if (tile.HasRiver || tile.HasLake)
            {
                waterGroundwater += tile.Groundwater;
                waterSoil += tile.SoilDepth;
                waterSlope += tile.Slope / 255f;
                waterCount++;
                continue;
            }

            landGroundwater += tile.Groundwater;
            landSoil += tile.SoilDepth;
            landSlope += tile.Slope / 255f;
            landCount++;
        }

        Assert.True(waterCount > 0, "Expected at least one hydrology tile in region.");
        Assert.True(landCount > 0, "Expected at least one dry land tile in region.");

        var avgWaterGroundwater = waterGroundwater / waterCount;
        var avgLandGroundwater = landGroundwater / landCount;
        var avgWaterSoil = waterSoil / waterCount;
        var avgLandSoil = landSoil / landCount;
        var avgWaterSlope = waterSlope / waterCount;
        var avgLandSlope = landSlope / landCount;

        Assert.True(
            avgWaterGroundwater > avgLandGroundwater,
            $"Expected wetter groundwater near hydrology tiles ({avgWaterGroundwater:F3} vs {avgLandGroundwater:F3}).");
        Assert.True(
            avgWaterSoil >= avgLandSoil,
            $"Expected equal-or-higher soil depth near hydrology tiles ({avgWaterSoil:F3} vs {avgLandSoil:F3}).");
        Assert.True(
            avgWaterSlope <= avgLandSlope,
            $"Expected river/lake corridors to be flatter than surrounding terrain ({avgWaterSlope:F3} vs {avgLandSlope:F3}).");
    }

    [Fact]
    public void Generate_WetAndDryParents_ProduceDistinctSurfaceClassMixes()
    {
        var world = new GeneratedWorldMap(seed: 529, width: 2, height: 1);
        world.SetTile(0, 0, new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.TropicalRainforest,
            ElevationBand: 0.46f,
            TemperatureBand: 0.72f,
            MoistureBand: 0.92f,
            DrainageBand: 0.84f,
            GeologyProfileId: GeologyProfileIds.AlluvialBasin,
            FactionPressure: 0.24f,
            FlowAccumulation: 12f,
            HasRiver: true,
            RiverEdges: WorldRiverEdges.North | WorldRiverEdges.South,
            RiverDischarge: 6.8f,
            RiverOrder: 5,
            ForestCover: 0.92f,
            Relief: 0.28f,
            MountainCover: 0.14f));
        world.SetTile(1, 0, new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.Desert,
            ElevationBand: 0.54f,
            TemperatureBand: 0.84f,
            MoistureBand: 0.10f,
            DrainageBand: 0.08f,
            GeologyProfileId: GeologyProfileIds.MixedBedrock,
            FactionPressure: 0.24f,
            FlowAccumulation: 1.2f,
            HasRiver: false,
            RiverEdges: WorldRiverEdges.None,
            RiverDischarge: 0f,
            RiverOrder: 0,
            ForestCover: 0.02f,
            Relief: 0.26f,
            MountainCover: 0.10f));

        var generator = new RegionLayerGenerator();
        var wetRegion = generator.Generate(world, new WorldCoord(0, 0), regionWidth: 32, regionHeight: 32);
        var dryRegion = generator.Generate(world, new WorldCoord(1, 0), regionWidth: 32, regionHeight: 32);

        var wetSoilOrMud = CountSurfaceClasses(wetRegion, RegionSurfaceClassIds.Soil) + CountSurfaceClasses(wetRegion, RegionSurfaceClassIds.Mud);
        var drySoilOrMud = CountSurfaceClasses(dryRegion, RegionSurfaceClassIds.Soil) + CountSurfaceClasses(dryRegion, RegionSurfaceClassIds.Mud);
        var drySand = CountSurfaceClasses(dryRegion, RegionSurfaceClassIds.Sand);
        var wetSand = CountSurfaceClasses(wetRegion, RegionSurfaceClassIds.Sand);

        Assert.True(
            wetSoilOrMud > drySoilOrMud,
            $"Expected wet parent tile to produce more soil/mud classes ({wetSoilOrMud} vs {drySoilOrMud}).");
        Assert.True(
            drySand > wetSand,
            $"Expected dry parent tile to produce more sand classes ({drySand} vs {wetSand}).");
    }

    [Fact]
    public void Generate_RiverTilesAreNotCollapsedIntoSingleRow()
    {
        var world = new WorldLayerGenerator().Generate(seed: 41, width: 16, height: 16);
        var gen = new RegionLayerGenerator();

        GeneratedRegionMap? wettestRegion = null;
        var mostRiverTiles = 0;

        for (var wx = 0; wx < world.Width; wx++)
        for (var wy = 0; wy < world.Height; wy++)
        {
            var region = gen.Generate(world, new WorldCoord(wx, wy), regionWidth: 32, regionHeight: 32);
            var riverTiles = CountRiverTiles(region);
            if (riverTiles <= mostRiverTiles)
                continue;

            mostRiverTiles = riverTiles;
            wettestRegion = region;
        }

        Assert.NotNull(wettestRegion);
        Assert.True(mostRiverTiles >= 4, $"Expected at least one region with visible rivers, got max={mostRiverTiles}.");

        var uniqueRows = new HashSet<int>();
        for (var x = 0; x < wettestRegion!.Width; x++)
        for (var y = 0; y < wettestRegion.Height; y++)
        {
            if (wettestRegion.GetTile(x, y).HasRiver)
                uniqueRows.Add(y);
        }

        var minimumDistinctRows = Math.Max(2, Math.Min(5, mostRiverTiles / 2));
        Assert.True(
            uniqueRows.Count >= minimumDistinctRows,
            $"Expected river network to span multiple rows, got {uniqueRows.Count} unique rows for {mostRiverTiles} river tiles.");
    }

    [Fact]
    public void Generate_AdjacentWorldTiles_ProduceContinuousRegionEdges()
    {
        var world = new WorldLayerGenerator().Generate(seed: 908, width: 12, height: 12);
        var gen = new RegionLayerGenerator();

        var leftRegion = gen.Generate(world, new WorldCoord(4, 6), regionWidth: 24, regionHeight: 24);
        var rightRegion = gen.Generate(world, new WorldCoord(5, 6), regionWidth: 24, regionHeight: 24);

        var vegetationDiffTotal = 0f;
        var slopeDiffTotal = 0f;
        for (var y = 0; y < leftRegion.Height; y++)
        {
            var leftEdge = leftRegion.GetTile(leftRegion.Width - 1, y);
            var rightEdge = rightRegion.GetTile(0, y);

            vegetationDiffTotal += MathF.Abs(leftEdge.VegetationDensity - rightEdge.VegetationDensity);
            slopeDiffTotal += MathF.Abs((leftEdge.Slope / 255f) - (rightEdge.Slope / 255f));
        }

        var rows = leftRegion.Height;
        var avgVegetationDiff = vegetationDiffTotal / rows;
        var avgSlopeDiff = slopeDiffTotal / rows;

        Assert.True(
            avgVegetationDiff <= 0.16f,
            $"Expected adjacent region edge vegetation to be coherent, got avg diff {avgVegetationDiff:F3}.");
        Assert.True(
            avgSlopeDiff <= 0.18f,
            $"Expected adjacent region edge slope to be coherent, got avg diff {avgSlopeDiff:F3}.");
    }

    [Fact]
    public void Generate_ContrastingAdjacentWorldTiles_CreateTransitionBandsNearRegionBorders()
    {
        var world = new GeneratedWorldMap(seed: 921, width: 2, height: 1);
        world.SetTile(0, 0, new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.Highland,
            ElevationBand: 0.78f,
            TemperatureBand: 0.46f,
            MoistureBand: 0.34f,
            DrainageBand: 0.28f,
            GeologyProfileId: GeologyProfileIds.MixedBedrock,
            FactionPressure: 0.20f,
            FlowAccumulation: 4.2f,
            HasRiver: false,
            RiverEdges: WorldRiverEdges.None,
            RiverDischarge: 0f,
            RiverOrder: 0,
            ForestCover: 0.12f,
            Relief: 0.82f,
            MountainCover: 0.86f));
        world.SetTile(1, 0, new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.TemperatePlains,
            ElevationBand: 0.48f,
            TemperatureBand: 0.56f,
            MoistureBand: 0.62f,
            DrainageBand: 0.54f,
            GeologyProfileId: GeologyProfileIds.AlluvialBasin,
            FactionPressure: 0.20f,
            FlowAccumulation: 7.4f,
            HasRiver: false,
            RiverEdges: WorldRiverEdges.None,
            RiverDischarge: 0f,
            RiverOrder: 0,
            ForestCover: 0.58f,
            Relief: 0.30f,
            MountainCover: 0.08f));

        var generator = new RegionLayerGenerator();
        var highlandRegion = generator.Generate(world, new WorldCoord(0, 0), regionWidth: 36, regionHeight: 36);
        var plainsRegion = generator.Generate(world, new WorldCoord(1, 0), regionWidth: 36, regionHeight: 36);
        const int band = 5;

        var highlandCenterVegetation = AverageVegetation(highlandRegion, highlandRegion.Width / 2 - 2, highlandRegion.Width / 2 + 2);
        var highlandEastEdgeVegetation = AverageVegetation(highlandRegion, highlandRegion.Width - band, highlandRegion.Width - 1);
        var plainsCenterVegetation = AverageVegetation(plainsRegion, plainsRegion.Width / 2 - 2, plainsRegion.Width / 2 + 2);
        var plainsWestEdgeVegetation = AverageVegetation(plainsRegion, 0, band - 1);

        Assert.True(
            highlandEastEdgeVegetation > highlandCenterVegetation + 0.04f,
            $"Expected highland region east edge to transition toward neighboring plains vegetation ({highlandEastEdgeVegetation:F3} vs {highlandCenterVegetation:F3}).");
        Assert.True(
            plainsWestEdgeVegetation < plainsCenterVegetation - 0.03f,
            $"Expected plains region west edge to transition toward neighboring highland dryness ({plainsWestEdgeVegetation:F3} vs {plainsCenterVegetation:F3}).");
    }

    [Fact]
    public void Generate_WorldRiverEdgeContract_SharesBorderRiverCrossing()
    {
        var world = new GeneratedWorldMap(seed: 77, width: 2, height: 1);
        var leftTile = new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.TemperatePlains,
            ElevationBand: 0.55f,
            TemperatureBand: 0.52f,
            MoistureBand: 0.64f,
            DrainageBand: 0.86f,
            GeologyProfileId: GeologyProfileIds.AlluvialBasin,
            FactionPressure: 0.5f,
            FlowAccumulation: 19f,
            HasRiver: true,
            RiverEdges: WorldRiverEdges.East,
            RiverDischarge: 2.75f);
        var rightTile = leftTile with
        {
            RiverEdges = WorldRiverEdges.West,
            RiverDischarge = 5.25f,
        };

        world.SetTile(0, 0, leftTile);
        world.SetTile(1, 0, rightTile);

        var generator = new RegionLayerGenerator();
        var left = generator.Generate(world, new WorldCoord(0, 0), regionWidth: 32, regionHeight: 32);
        var right = generator.Generate(world, new WorldCoord(1, 0), regionWidth: 32, regionHeight: 32);

        var sharedCrossings = 0;
        for (var y = 0; y < left.Height; y++)
        {
            var leftEdge = left.GetTile(left.Width - 1, y);
            var rightEdge = right.GetTile(0, y);
            if (leftEdge.HasRiver && rightEdge.HasRiver)
            {
                sharedCrossings++;
                Assert.True(
                    MathF.Abs(leftEdge.RiverDischarge - rightEdge.RiverDischarge) <= 0.001f,
                    $"Expected shared border discharge match, got left={leftEdge.RiverDischarge:F3}, right={rightEdge.RiverDischarge:F3} at y={y}.");
                Assert.True(
                    leftEdge.RiverDischarge >= 5f,
                    $"Expected border discharge to preserve strong parent contract, got {leftEdge.RiverDischarge:F3}.");
            }
        }

        Assert.True(
            sharedCrossings >= 1,
            $"Expected at least one shared river crossing on region border, got {sharedCrossings}.");
    }

    [Fact]
    public void Generate_WorldRoadEdgeContract_SharesBorderRoadCrossing()
    {
        var world = new GeneratedWorldMap(seed: 1017, width: 2, height: 1);
        var leftTile = new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.TemperatePlains,
            ElevationBand: 0.56f,
            TemperatureBand: 0.52f,
            MoistureBand: 0.64f,
            DrainageBand: 0.72f,
            GeologyProfileId: GeologyProfileIds.MixedBedrock,
            FactionPressure: 0.62f,
            FlowAccumulation: 4f,
            HasRiver: false,
            RiverEdges: WorldRiverEdges.None,
            HasRoad: true,
            RoadEdges: WorldRoadEdges.East);
        var rightTile = leftTile with
        {
            RoadEdges = WorldRoadEdges.West,
        };

        world.SetTile(0, 0, leftTile);
        world.SetTile(1, 0, rightTile);

        var generator = new RegionLayerGenerator();
        var left = generator.Generate(world, new WorldCoord(0, 0), regionWidth: 32, regionHeight: 32);
        var right = generator.Generate(world, new WorldCoord(1, 0), regionWidth: 32, regionHeight: 32);

        var sharedCrossings = 0;
        for (var y = 0; y < left.Height; y++)
        {
            var leftEdge = left.GetTile(left.Width - 1, y);
            var rightEdge = right.GetTile(0, y);
            var leftHas = leftEdge.HasRoad && RegionRoadEdgeMask.Has(leftEdge.RoadEdges, RegionRoadEdges.East);
            var rightHas = rightEdge.HasRoad && RegionRoadEdgeMask.Has(rightEdge.RoadEdges, RegionRoadEdges.West);

            Assert.Equal(leftHas, rightHas);
            if (leftHas && rightHas)
                sharedCrossings++;
        }

        Assert.True(
            sharedCrossings >= 1,
            $"Expected at least one shared road crossing on region border, got {sharedCrossings}.");
    }

    [Fact]
    public void Generate_WorldRoadNorthSouthContract_CreatesConnectedRegionRoadCorridor()
    {
        var world = new GeneratedWorldMap(seed: 1707, width: 1, height: 3);
        var centerTile = new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.TemperatePlains,
            ElevationBand: 0.54f,
            TemperatureBand: 0.52f,
            MoistureBand: 0.34f,
            DrainageBand: 0.30f,
            GeologyProfileId: GeologyProfileIds.MixedBedrock,
            FactionPressure: 0.74f,
            FlowAccumulation: 2f,
            HasRiver: false,
            RiverEdges: WorldRiverEdges.None,
            HasRoad: true,
            RoadEdges: WorldRoadEdges.North | WorldRoadEdges.South);
        world.SetTile(0, 0, centerTile with { RoadEdges = WorldRoadEdges.South });
        world.SetTile(0, 1, centerTile);
        world.SetTile(0, 2, centerTile with { RoadEdges = WorldRoadEdges.North });

        var region = new RegionLayerGenerator().Generate(world, new WorldCoord(0, 1), regionWidth: 32, regionHeight: 32);

        var northStarts = new List<(int X, int Y)>();
        var southTargets = new HashSet<int>();
        for (var x = 0; x < region.Width; x++)
        {
            var north = region.GetTile(x, 0);
            if (north.HasRoad && RegionRoadEdgeMask.Has(north.RoadEdges, RegionRoadEdges.North))
                northStarts.Add((x, 0));

            var south = region.GetTile(x, region.Height - 1);
            if (south.HasRoad && RegionRoadEdgeMask.Has(south.RoadEdges, RegionRoadEdges.South))
                southTargets.Add(((region.Height - 1) * region.Width) + x);
        }

        Assert.True(northStarts.Count > 0, "Expected at least one north road contract entry.");
        Assert.True(southTargets.Count > 0, "Expected at least one south road contract entry.");

        var visited = new bool[region.Width * region.Height];
        var queue = new Queue<int>();
        foreach (var (sx, sy) in northStarts)
        {
            var idx = (sy * region.Width) + sx;
            visited[idx] = true;
            queue.Enqueue(idx);
        }

        var reachedSouth = false;
        while (queue.Count > 0 && !reachedSouth)
        {
            var idx = queue.Dequeue();
            if (southTargets.Contains(idx))
            {
                reachedSouth = true;
                break;
            }

            var x = idx % region.Width;
            var y = idx / region.Width;
            EnqueueIfRoad(region, visited, queue, x + 1, y);
            EnqueueIfRoad(region, visited, queue, x - 1, y);
            EnqueueIfRoad(region, visited, queue, x, y + 1);
            EnqueueIfRoad(region, visited, queue, x, y - 1);
        }

        Assert.True(reachedSouth, "Expected world north/south road contract to form a connected region road corridor.");
    }

    [Fact]
    public void Generate_HistoryRoadAcrossWorldTiles_SharesBorderRoadCrossing()
    {
        var world = new GeneratedWorldMap(seed: 744, width: 2, height: 1);
        var tile = new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.TemperatePlains,
            ElevationBand: 0.58f,
            TemperatureBand: 0.52f,
            MoistureBand: 0.62f,
            DrainageBand: 0.66f,
            GeologyProfileId: GeologyProfileIds.MixedBedrock,
            FactionPressure: 0.40f,
            FlowAccumulation: 4f,
            HasRiver: false,
            RiverEdges: WorldRiverEdges.None,
            RiverDischarge: 0f);
        world.SetTile(0, 0, tile);
        world.SetTile(1, 0, tile);

        var westCoord = new WorldCoord(0, 0);
        var eastCoord = new WorldCoord(1, 0);
        var history = new GeneratedWorldHistory
        {
            Seed = 1001,
            SimulatedYears = 48,
            Sites =
            [
                new SiteRecord
                {
                    Id = "west_hold",
                    Name = "West Hold",
                    Kind = "fortress",
                    OwnerCivilizationId = "civ_ax",
                    Location = westCoord,
                    Development = 0.82f,
                    Security = 0.64f,
                },
                new SiteRecord
                {
                    Id = "east_town",
                    Name = "East Town",
                    Kind = "town",
                    OwnerCivilizationId = "civ_ax",
                    Location = eastCoord,
                    Development = 0.72f,
                    Security = 0.61f,
                },
            ],
            Roads =
            [
                new RoadRecord
                {
                    Id = "west_east_road",
                    OwnerCivilizationId = "civ_ax",
                    FromSiteId = "west_hold",
                    ToSiteId = "east_town",
                    Path =
                    [
                        westCoord,
                        eastCoord,
                    ],
                },
            ],
        };

        var generator = new RegionLayerGenerator();
        var westRegion = generator.Generate(world, westCoord, regionWidth: 30, regionHeight: 30, history);
        var eastRegion = generator.Generate(world, eastCoord, regionWidth: 30, regionHeight: 30, history);

        var sharedCrossings = 0;
        for (var y = 0; y < westRegion.Height; y++)
        {
            var westEdge = westRegion.GetTile(westRegion.Width - 1, y);
            var eastEdge = eastRegion.GetTile(0, y);
            var westHasContract = westEdge.HasRoad && RegionRoadEdgeMask.Has(westEdge.RoadEdges, RegionRoadEdges.East);
            var eastHasContract = eastEdge.HasRoad && RegionRoadEdgeMask.Has(eastEdge.RoadEdges, RegionRoadEdges.West);

            Assert.Equal(westHasContract, eastHasContract);
            if (westHasContract && eastHasContract)
                sharedCrossings++;
        }

        Assert.True(
            sharedCrossings >= 1,
            $"Expected at least one shared road crossing on region border, got {sharedCrossings}.");
    }

    [Fact]
    public void Generate_RiverEdgeMasks_AreReciprocalForInteriorNeighbors()
    {
        var world = new GeneratedWorldMap(seed: 808, width: 3, height: 1);
        var riverTile = new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.TemperatePlains,
            ElevationBand: 0.54f,
            TemperatureBand: 0.52f,
            MoistureBand: 0.63f,
            DrainageBand: 0.88f,
            GeologyProfileId: GeologyProfileIds.AlluvialBasin,
            FactionPressure: 0.5f,
            FlowAccumulation: 16f,
            HasRiver: true,
            RiverEdges: WorldRiverEdges.East | WorldRiverEdges.West,
            RiverDischarge: 4f);
        world.SetTile(0, 0, riverTile with { RiverEdges = WorldRiverEdges.East });
        world.SetTile(1, 0, riverTile);
        world.SetTile(2, 0, riverTile with { RiverEdges = WorldRiverEdges.West });

        var region = new RegionLayerGenerator().Generate(world, new WorldCoord(1, 0), regionWidth: 28, regionHeight: 28);

        var riverTiles = 0;
        var reciprocalChecks = 0;
        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
        {
            var tile = region.GetTile(x, y);
            if (!tile.HasRiver)
                continue;

            riverTiles++;

            if (x + 1 < region.Width && RegionRiverEdgeMask.Has(tile.RiverEdges, RegionRiverEdges.East))
            {
                var east = region.GetTile(x + 1, y);
                Assert.True(
                    RegionRiverEdgeMask.Has(east.RiverEdges, RegionRiverEdges.West),
                    $"Missing reciprocal west edge at ({x + 1},{y}).");
                reciprocalChecks++;
            }

            if (x - 1 >= 0 && RegionRiverEdgeMask.Has(tile.RiverEdges, RegionRiverEdges.West))
            {
                var west = region.GetTile(x - 1, y);
                Assert.True(
                    RegionRiverEdgeMask.Has(west.RiverEdges, RegionRiverEdges.East),
                    $"Missing reciprocal east edge at ({x - 1},{y}).");
                reciprocalChecks++;
            }

            if (y + 1 < region.Height && RegionRiverEdgeMask.Has(tile.RiverEdges, RegionRiverEdges.South))
            {
                var south = region.GetTile(x, y + 1);
                Assert.True(
                    RegionRiverEdgeMask.Has(south.RiverEdges, RegionRiverEdges.North),
                    $"Missing reciprocal north edge at ({x},{y + 1}).");
                reciprocalChecks++;
            }

            if (y - 1 >= 0 && RegionRiverEdgeMask.Has(tile.RiverEdges, RegionRiverEdges.North))
            {
                var north = region.GetTile(x, y - 1);
                Assert.True(
                    RegionRiverEdgeMask.Has(north.RiverEdges, RegionRiverEdges.South),
                    $"Missing reciprocal south edge at ({x},{y - 1}).");
                reciprocalChecks++;
            }
        }

        Assert.True(riverTiles > 0, "Expected at least one river tile in sampled region.");
        Assert.True(reciprocalChecks > 0, "Expected at least one reciprocal interior river-edge validation.");
    }

    [Fact]
    public void Generate_RoadEdgeMasks_AreReciprocalForInteriorNeighbors()
    {
        var world = new WorldLayerGenerator().Generate(seed: 2807, width: 12, height: 12);
        var coord = new WorldCoord(6, 6);
        var history = new GeneratedWorldHistory
        {
            Seed = 4182,
            SimulatedYears = 32,
            Sites =
            [
                new SiteRecord
                {
                    Id = "road_test_a",
                    Name = "Road Test A",
                    Kind = "fortress",
                    OwnerCivilizationId = "civ_test",
                    Location = coord,
                    Development = 0.82f,
                    Security = 0.68f,
                },
                new SiteRecord
                {
                    Id = "road_test_b",
                    Name = "Road Test B",
                    Kind = "hamlet",
                    OwnerCivilizationId = "civ_test",
                    Location = coord,
                    Development = 0.58f,
                    Security = 0.54f,
                },
            ],
            Roads =
            [
                new RoadRecord
                {
                    Id = "road_test_path",
                    OwnerCivilizationId = "civ_test",
                    FromSiteId = "road_test_a",
                    ToSiteId = "road_test_b",
                    Path =
                    [
                        new WorldCoord(5, 6),
                        coord,
                        new WorldCoord(7, 6),
                    ],
                },
            ],
        };

        var region = new RegionLayerGenerator().Generate(world, coord, regionWidth: 30, regionHeight: 30, history);

        var roadTiles = 0;
        var reciprocalChecks = 0;
        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
        {
            var tile = region.GetTile(x, y);
            if (!tile.HasRoad)
                continue;

            roadTiles++;

            if (x + 1 < region.Width && RegionRoadEdgeMask.Has(tile.RoadEdges, RegionRoadEdges.East))
            {
                var east = region.GetTile(x + 1, y);
                Assert.True(
                    east.HasRoad && RegionRoadEdgeMask.Has(east.RoadEdges, RegionRoadEdges.West),
                    $"Missing reciprocal road west edge at ({x + 1},{y}).");
                reciprocalChecks++;
            }

            if (x - 1 >= 0 && RegionRoadEdgeMask.Has(tile.RoadEdges, RegionRoadEdges.West))
            {
                var west = region.GetTile(x - 1, y);
                Assert.True(
                    west.HasRoad && RegionRoadEdgeMask.Has(west.RoadEdges, RegionRoadEdges.East),
                    $"Missing reciprocal road east edge at ({x - 1},{y}).");
                reciprocalChecks++;
            }

            if (y + 1 < region.Height && RegionRoadEdgeMask.Has(tile.RoadEdges, RegionRoadEdges.South))
            {
                var south = region.GetTile(x, y + 1);
                Assert.True(
                    south.HasRoad && RegionRoadEdgeMask.Has(south.RoadEdges, RegionRoadEdges.North),
                    $"Missing reciprocal road north edge at ({x},{y + 1}).");
                reciprocalChecks++;
            }

            if (y - 1 >= 0 && RegionRoadEdgeMask.Has(tile.RoadEdges, RegionRoadEdges.North))
            {
                var north = region.GetTile(x, y - 1);
                Assert.True(
                    north.HasRoad && RegionRoadEdgeMask.Has(north.RoadEdges, RegionRoadEdges.South),
                    $"Missing reciprocal road south edge at ({x},{y - 1}).");
                reciprocalChecks++;
            }
        }

        Assert.True(roadTiles > 0, "Expected at least one road tile in sampled region.");
        Assert.True(reciprocalChecks > 0, "Expected at least one reciprocal interior road-edge validation.");
    }

    [Fact]
    public void Generate_RiverOrder_IsPresentAndCorrelatesWithDischarge()
    {
        var world = new GeneratedWorldMap(seed: 944, width: 1, height: 1);
        world.SetTile(0, 0, new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.TemperatePlains,
            ElevationBand: 0.54f,
            TemperatureBand: 0.52f,
            MoistureBand: 0.78f,
            DrainageBand: 0.92f,
            GeologyProfileId: GeologyProfileIds.AlluvialBasin,
            FactionPressure: 0.2f,
            FlowAccumulation: 28f,
            HasRiver: true,
            RiverEdges: WorldRiverEdges.North | WorldRiverEdges.South,
            RiverDischarge: 8f,
            RiverOrder: 5));

        var region = new RegionLayerGenerator().Generate(world, new WorldCoord(0, 0), regionWidth: 32, regionHeight: 32);
        var hasHigherOrder = false;
        var orderTotals = new float[9];
        var orderCounts = new int[9];

        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
        {
            var tile = region.GetTile(x, y);
            if (!tile.HasRiver)
                continue;

            Assert.InRange(tile.RiverOrder, (byte)1, (byte)8);
            if (tile.RiverOrder >= 2)
                hasHigherOrder = true;

            var order = Math.Clamp(tile.RiverOrder, (byte)1, (byte)8);
            orderTotals[order] += tile.RiverDischarge;
            orderCounts[order]++;
        }

        Assert.True(hasHigherOrder, "Expected region river network to include order >= 2 tiles.");

        for (byte order = 1; order < 8; order++)
        {
            if (orderCounts[order] == 0 || orderCounts[order + 1] == 0)
                continue;

            var avg = orderTotals[order] / orderCounts[order];
            var nextAvg = orderTotals[order + 1] / orderCounts[order + 1];
            Assert.True(
                nextAvg >= (avg * 0.75f),
                $"Expected higher order region rivers to trend stronger: order {order} avg={avg:F2}, order {order + 1} avg={nextAvg:F2}.");
        }
    }

    [Fact]
    public void Generate_OceanParent_ProducesWaterOnlyRegionSignals()
    {
        var world = new GeneratedWorldMap(seed: 500, width: 1, height: 1);
        world.SetTile(0, 0, new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.OceanDeep,
            ElevationBand: 0.10f,
            TemperatureBand: 0.52f,
            MoistureBand: 0.84f,
            DrainageBand: 0.70f,
            GeologyProfileId: GeologyProfileIds.MixedBedrock,
            FactionPressure: 0.05f,
            FlowAccumulation: 0f,
            HasRiver: false,
            RiverEdges: WorldRiverEdges.None));

        var region = new RegionLayerGenerator().Generate(world, new WorldCoord(0, 0), regionWidth: 24, regionHeight: 24);
        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
        {
            var tile = region.GetTile(x, y);
            Assert.Equal(RegionBiomeVariantIds.OpenOcean, tile.BiomeVariantId);
            Assert.True(tile.HasLake);
            Assert.False(tile.HasRiver);
            Assert.False(tile.HasRoad);
            Assert.False(tile.HasSettlement);
            Assert.Equal(RegionRiverEdges.None, tile.RiverEdges);
            Assert.Equal(RegionRoadEdges.None, tile.RoadEdges);
            Assert.Equal(0f, tile.RiverDischarge);
        }
    }

    [Fact]
    public void Generate_TropicalParent_DoesNotCollapseIntoTemperateVariants()
    {
        var world = new GeneratedWorldMap(seed: 501, width: 1, height: 1);
        world.SetTile(0, 0, new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.TropicalRainforest,
            ElevationBand: 0.50f,
            TemperatureBand: 0.84f,
            MoistureBand: 0.90f,
            DrainageBand: 0.78f,
            GeologyProfileId: GeologyProfileIds.AlluvialBasin,
            FactionPressure: 0.20f,
            FlowAccumulation: 8f,
            HasRiver: false,
            RiverEdges: WorldRiverEdges.None,
            ForestCover: 0.92f,
            Relief: 0.20f,
            MountainCover: 0.08f));

        var region = new RegionLayerGenerator().Generate(world, new WorldCoord(0, 0), regionWidth: 24, regionHeight: 24);
        var tropicalLikeTiles = 0;

        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
        {
            var variant = region.GetTile(x, y).BiomeVariantId;
            Assert.NotEqual(RegionBiomeVariantIds.TemperateWoodland, variant);
            Assert.NotEqual(RegionBiomeVariantIds.TemperatePlainsOpen, variant);

            if (string.Equals(variant, RegionBiomeVariantIds.TropicalCanopy, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(variant, RegionBiomeVariantIds.TropicalLowland, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(variant, RegionBiomeVariantIds.FloodplainMarsh, StringComparison.OrdinalIgnoreCase))
            {
                tropicalLikeTiles++;
            }
        }

        Assert.True(tropicalLikeTiles > 0, "Expected tropical parent to produce tropical/floodplain region variants.");
    }

    [Fact]
    public void Generate_DesertParent_UsesAridRegionVariants()
    {
        var world = new GeneratedWorldMap(seed: 502, width: 1, height: 1);
        world.SetTile(0, 0, new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.Desert,
            ElevationBand: 0.58f,
            TemperatureBand: 0.82f,
            MoistureBand: 0.08f,
            DrainageBand: 0.16f,
            GeologyProfileId: GeologyProfileIds.IgneousUplift,
            FactionPressure: 0.22f,
            FlowAccumulation: 3f,
            HasRiver: false,
            RiverEdges: WorldRiverEdges.None,
            ForestCover: 0.01f,
            Relief: 0.46f,
            MountainCover: 0.22f));

        var region = new RegionLayerGenerator().Generate(world, new WorldCoord(0, 0), regionWidth: 24, regionHeight: 24);
        var aridTiles = 0;

        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
        {
            var variant = region.GetTile(x, y).BiomeVariantId;
            Assert.NotEqual(RegionBiomeVariantIds.TemperateWoodland, variant);
            Assert.NotEqual(RegionBiomeVariantIds.DenseConifer, variant);
            Assert.NotEqual(RegionBiomeVariantIds.ConiferWoodland, variant);

            if (string.Equals(variant, RegionBiomeVariantIds.DrySteppe, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(variant, RegionBiomeVariantIds.SteppeScrub, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(variant, RegionBiomeVariantIds.AridBadlands, StringComparison.OrdinalIgnoreCase))
            {
                aridTiles++;
            }
        }

        Assert.True(aridTiles > 0, "Expected desert parent to produce arid region variants.");
    }

    [Fact]
    public void Generate_PrunesTinyInteriorRiverFragments()
    {
        var world = new GeneratedWorldMap(seed: 1902, width: 1, height: 1);
        world.SetTile(0, 0, new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.TemperatePlains,
            ElevationBand: 0.56f,
            TemperatureBand: 0.58f,
            MoistureBand: 0.84f,
            DrainageBand: 0.95f,
            GeologyProfileId: GeologyProfileIds.AlluvialBasin,
            FactionPressure: 0.15f,
            FlowAccumulation: 18f,
            HasRiver: true,
            RiverEdges: WorldRiverEdges.None,
            RiverDischarge: 4.5f));

        var region = new RegionLayerGenerator().Generate(world, new WorldCoord(0, 0), regionWidth: 32, regionHeight: 32);

        var visited = new bool[region.Width * region.Height];
        var queue = new Queue<int>();
        var components = 0;

        for (var y = 0; y < region.Height; y++)
        for (var x = 0; x < region.Width; x++)
        {
            var start = (y * region.Width) + x;
            if (visited[start] || !region.GetTile(x, y).HasRiver)
                continue;

            components++;
            queue.Clear();
            queue.Enqueue(start);
            visited[start] = true;

            var size = 0;
            var touchesBoundary = false;
            var maxDischarge = 0f;

            while (queue.Count > 0)
            {
                var idx = queue.Dequeue();
                var cx = idx % region.Width;
                var cy = idx / region.Width;
                var tile = region.GetTile(cx, cy);

                size++;
                if (tile.RiverDischarge > maxDischarge)
                    maxDischarge = tile.RiverDischarge;
                if (cx == 0 || cy == 0 || cx == region.Width - 1 || cy == region.Height - 1)
                    touchesBoundary = true;

                EnqueueIfRiver(region, visited, queue, cx + 1, cy);
                EnqueueIfRiver(region, visited, queue, cx - 1, cy);
                EnqueueIfRiver(region, visited, queue, cx, cy + 1);
                EnqueueIfRiver(region, visited, queue, cx, cy - 1);
            }

            if (!touchesBoundary)
                Assert.True(
                    size >= 4 || maxDischarge >= 3.5f,
                    $"Unexpected tiny interior river component: size={size}, maxDischarge={maxDischarge:F2}.");
        }

        Assert.True(components > 0, "Expected high-drainage parent tile to produce at least one region river component.");
    }

    [Fact]
    public void Generate_RiverTiles_DoNotUseDiagonalOnlyConnectivity()
    {
        var generator = new RegionLayerGenerator();
        var worldGenerator = new WorldLayerGenerator();

        for (var seed = 300; seed < 306; seed++)
        {
            var world = worldGenerator.Generate(seed: seed, width: 8, height: 8);
            for (var wx = 0; wx < world.Width; wx++)
            for (var wy = 0; wy < world.Height; wy++)
            {
                var region = generator.Generate(world, new WorldCoord(wx, wy), regionWidth: 24, regionHeight: 24);

                for (var x = 0; x < region.Width; x++)
                for (var y = 0; y < region.Height; y++)
                {
                    var tile = region.GetTile(x, y);
                    if (!tile.HasRiver)
                        continue;

                    Assert.NotEqual(
                        RegionRiverEdges.None,
                        tile.RiverEdges);
                }
            }
        }
    }

    [Fact]
    public void Generate_RegionVegetation_TracksParentWorldForestSignal()
    {
        var world = new GeneratedWorldMap(seed: 2048, width: 2, height: 1);
        world.SetTile(0, 0, new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.ConiferForest,
            ElevationBand: 0.52f,
            TemperatureBand: 0.45f,
            MoistureBand: 0.72f,
            DrainageBand: 0.68f,
            GeologyProfileId: GeologyProfileIds.MixedBedrock,
            FactionPressure: 0.22f,
            FlowAccumulation: 8f,
            HasRiver: false,
            RiverEdges: WorldRiverEdges.None,
            ForestCover: 0.88f,
            Relief: 0.24f,
            MountainCover: 0.20f));
        world.SetTile(1, 0, new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.WindsweptSteppe,
            ElevationBand: 0.52f,
            TemperatureBand: 0.62f,
            MoistureBand: 0.22f,
            DrainageBand: 0.28f,
            GeologyProfileId: GeologyProfileIds.MixedBedrock,
            FactionPressure: 0.22f,
            FlowAccumulation: 3f,
            HasRiver: false,
            RiverEdges: WorldRiverEdges.None,
            ForestCover: 0.08f,
            Relief: 0.24f,
            MountainCover: 0.20f));

        var generator = new RegionLayerGenerator();
        var lush = generator.Generate(world, new WorldCoord(0, 0), regionWidth: 32, regionHeight: 32);
        var dry = generator.Generate(world, new WorldCoord(1, 0), regionWidth: 32, regionHeight: 32);

        var lushVegetation = 0f;
        var dryVegetation = 0f;
        var lushCount = 0;
        var dryCount = 0;

        for (var x = 0; x < lush.Width; x++)
        for (var y = 0; y < lush.Height; y++)
        {
            lushVegetation += lush.GetTile(x, y).VegetationDensity;
            dryVegetation += dry.GetTile(x, y).VegetationDensity;
            lushCount++;
            dryCount++;
        }

        var lushAverage = lushVegetation / lushCount;
        var dryAverage = dryVegetation / dryCount;
        Assert.True(
            lushAverage > dryAverage + 0.12f,
            $"Expected high-forest parent world tile to produce greener region average ({lushAverage:F3} vs {dryAverage:F3}).");
    }

    [Fact]
    public void Generate_RegionVegetationSuitability_TracksHydrologyAndClimate()
    {
        var world = new GeneratedWorldMap(seed: 2291, width: 2, height: 1);
        world.SetTile(0, 0, new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.TropicalRainforest,
            ElevationBand: 0.46f,
            TemperatureBand: 0.74f,
            MoistureBand: 0.92f,
            DrainageBand: 0.84f,
            GeologyProfileId: GeologyProfileIds.AlluvialBasin,
            FactionPressure: 0.20f,
            FlowAccumulation: 12f,
            HasRiver: true,
            RiverEdges: WorldRiverEdges.North | WorldRiverEdges.South,
            RiverDischarge: 4.4f,
            ForestCover: 0.94f,
            Relief: 0.18f,
            MountainCover: 0.08f));
        world.SetTile(1, 0, new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.Desert,
            ElevationBand: 0.50f,
            TemperatureBand: 0.82f,
            MoistureBand: 0.08f,
            DrainageBand: 0.14f,
            GeologyProfileId: GeologyProfileIds.IgneousUplift,
            FactionPressure: 0.20f,
            FlowAccumulation: 2f,
            HasRiver: false,
            RiverEdges: WorldRiverEdges.None,
            RiverDischarge: 0f,
            ForestCover: 0.02f,
            Relief: 0.18f,
            MountainCover: 0.08f));

        var generator = new RegionLayerGenerator();
        var lush = generator.Generate(world, new WorldCoord(0, 0), regionWidth: 32, regionHeight: 32);
        var arid = generator.Generate(world, new WorldCoord(1, 0), regionWidth: 32, regionHeight: 32);

        var lushSuitability = 0f;
        var aridSuitability = 0f;
        var lushCount = 0;
        var aridCount = 0;

        for (var x = 0; x < lush.Width; x++)
        for (var y = 0; y < lush.Height; y++)
        {
            lushSuitability += lush.GetTile(x, y).VegetationSuitability;
            aridSuitability += arid.GetTile(x, y).VegetationSuitability;
            lushCount++;
            aridCount++;
        }

        var lushAverage = lushSuitability / lushCount;
        var aridAverage = aridSuitability / aridCount;
        Assert.True(
            lushAverage > aridAverage + 0.16f,
            $"Expected wet climate region to have much higher vegetation suitability ({lushAverage:F3} vs {aridAverage:F3}).");
    }

    [Fact]
    public void Generate_RegionCenter_StaysAnchoredToParentWorldTileSignal()
    {
        var world = new GeneratedWorldMap(seed: 3186, width: 3, height: 1);
        var dryTile = new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.Desert,
            ElevationBand: 0.50f,
            TemperatureBand: 0.80f,
            MoistureBand: 0.10f,
            DrainageBand: 0.12f,
            GeologyProfileId: GeologyProfileIds.MixedBedrock,
            FactionPressure: 0.25f,
            FlowAccumulation: 2f,
            HasRiver: false,
            RiverEdges: WorldRiverEdges.None,
            ForestCover: 0.02f,
            Relief: 0.22f,
            MountainCover: 0.05f);
        var lushTile = new GeneratedWorldTile(
            MacroBiomeId: MacroBiomeIds.TropicalRainforest,
            ElevationBand: 0.50f,
            TemperatureBand: 0.70f,
            MoistureBand: 0.94f,
            DrainageBand: 0.86f,
            GeologyProfileId: GeologyProfileIds.MixedBedrock,
            FactionPressure: 0.25f,
            FlowAccumulation: 12f,
            HasRiver: false,
            RiverEdges: WorldRiverEdges.None,
            ForestCover: 0.95f,
            Relief: 0.22f,
            MountainCover: 0.05f);

        world.SetTile(0, 0, dryTile);
        world.SetTile(1, 0, lushTile);
        world.SetTile(2, 0, dryTile);

        var region = new RegionLayerGenerator().Generate(world, new WorldCoord(1, 0), regionWidth: 36, regionHeight: 36);
        var centerStart = (region.Width / 2) - 2;
        var centerEnd = (region.Width / 2) + 2;
        var edgeSpan = 4;

        var centerVegetation = 0f;
        var centerCount = 0;
        for (var x = centerStart; x <= centerEnd; x++)
        for (var y = 0; y < region.Height; y++)
        {
            centerVegetation += region.GetTile(x, y).VegetationDensity;
            centerCount++;
        }

        var leftEdgeVegetation = 0f;
        var leftEdgeCount = 0;
        for (var x = 0; x < edgeSpan; x++)
        for (var y = 0; y < region.Height; y++)
        {
            leftEdgeVegetation += region.GetTile(x, y).VegetationDensity;
            leftEdgeCount++;
        }

        var rightEdgeVegetation = 0f;
        var rightEdgeCount = 0;
        for (var x = region.Width - edgeSpan; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
        {
            rightEdgeVegetation += region.GetTile(x, y).VegetationDensity;
            rightEdgeCount++;
        }

        var centerAverage = centerVegetation / centerCount;
        var edgeAverage = ((leftEdgeVegetation / leftEdgeCount) + (rightEdgeVegetation / rightEdgeCount)) * 0.5f;
        Assert.True(
            centerAverage > edgeAverage + 0.08f,
            $"Expected center of region to stay anchored to parent world tile vegetation ({centerAverage:F3} vs edge avg {edgeAverage:F3}).");
    }

    [Fact]
    public void Generate_WithHistoryOverlay_AddsDeterministicSettlementsAndRoads()
    {
        var world = new WorldLayerGenerator().Generate(seed: 2113, width: 10, height: 10);
        var coord = new WorldCoord(5, 5);
        var history = new GeneratedWorldHistory
        {
            Seed = 9001,
            SimulatedYears = 64,
            Sites =
            [
                new SiteRecord
                {
                    Id = "site_alpha",
                    Name = "Alpha Hold",
                    Kind = "fortress",
                    OwnerCivilizationId = "civ_alpha",
                    Location = coord,
                    Development = 0.86f,
                    Security = 0.70f,
                },
                new SiteRecord
                {
                    Id = "site_beta",
                    Name = "Beta Hamlet",
                    Kind = "hamlet",
                    OwnerCivilizationId = "civ_alpha",
                    Location = coord,
                    Development = 0.55f,
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
                    Path = [coord],
                },
            ],
        };

        var generator = new RegionLayerGenerator();
        var baseline = generator.Generate(world, coord, regionWidth: 28, regionHeight: 28);
        var withHistoryA = generator.Generate(world, coord, regionWidth: 28, regionHeight: 28, history);
        var withHistoryB = generator.Generate(world, coord, regionWidth: 28, regionHeight: 28, history);

        Assert.Equal(Fingerprint(withHistoryA), Fingerprint(withHistoryB));

        var baselineSettlements = CountSettlements(baseline);
        var overlaySettlements = CountSettlements(withHistoryA);
        var baselineRoads = CountRoads(baseline);
        var overlayRoads = CountRoads(withHistoryA);

        Assert.True(
            overlaySettlements > baselineSettlements,
            $"Expected history site overlay to add settlements ({overlaySettlements} vs {baselineSettlements}).");
        Assert.True(
            overlayRoads > baselineRoads,
            $"Expected history road overlay to add roads ({overlayRoads} vs {baselineRoads}).");
    }

    private static void EnqueueIfRiver(GeneratedRegionMap region, bool[] visited, Queue<int> queue, int x, int y)
    {
        if (x < 0 || y < 0 || x >= region.Width || y >= region.Height)
            return;

        var idx = (y * region.Width) + x;
        if (visited[idx] || !region.GetTile(x, y).HasRiver)
            return;

        visited[idx] = true;
        queue.Enqueue(idx);
    }

    private static void EnqueueIfRoad(GeneratedRegionMap region, bool[] visited, Queue<int> queue, int x, int y)
    {
        if (x < 0 || y < 0 || x >= region.Width || y >= region.Height)
            return;

        var idx = (y * region.Width) + x;
        if (visited[idx] || !region.GetTile(x, y).HasRoad)
            return;

        visited[idx] = true;
        queue.Enqueue(idx);
    }

    private static string Fingerprint(GeneratedRegionMap map)
    {
        var sb = new StringBuilder(map.Width * map.Height * 8);
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y);
            sb.Append(tile.BiomeVariantId).Append('|')
              .Append(tile.SurfaceClassId).Append('|')
              .Append(tile.Slope).Append('|')
              .Append(tile.HasRiver ? '1' : '0')
              .Append(tile.HasLake ? '1' : '0')
              .Append(tile.HasRoad ? '1' : '0')
              .Append(tile.HasSettlement ? '1' : '0')
              .Append((byte)tile.RiverEdges).Append('|')
              .Append((byte)tile.RoadEdges).Append(';');
        }

        return sb.ToString();
    }

    private static void AssertEcologyProfileBounds(EcologyEdgeProfile profile)
    {
        Assert.InRange(profile.VegetationDensity, 0f, 1f);
        Assert.InRange(profile.VegetationSuitability, 0f, 1f);
        Assert.InRange(profile.SoilDepth, 0f, 1f);
        Assert.InRange(profile.Groundwater, 0f, 1f);
    }

    private static int CountRiverTiles(GeneratedRegionMap map)
    {
        var count = 0;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (map.GetTile(x, y).HasRiver)
                count++;
        }

        return count;
    }

    private static int CountSurfaceClasses(GeneratedRegionMap map, string surfaceClassId)
    {
        var count = 0;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (string.Equals(map.GetTile(x, y).SurfaceClassId, surfaceClassId, StringComparison.OrdinalIgnoreCase))
                count++;
        }

        return count;
    }

    private static float AverageVegetation(GeneratedRegionMap map, int minX, int maxX)
    {
        var sum = 0f;
        var count = 0;
        for (var x = Math.Clamp(minX, 0, map.Width - 1); x <= Math.Clamp(maxX, 0, map.Width - 1); x++)
        for (var y = 0; y < map.Height; y++)
        {
            sum += map.GetTile(x, y).VegetationDensity;
            count++;
        }

        return count == 0 ? 0f : (sum / count);
    }


    private static int CountSettlements(GeneratedRegionMap map)
    {
        var count = 0;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (map.GetTile(x, y).HasSettlement)
                count++;
        }

        return count;
    }

    private static int CountRoads(GeneratedRegionMap map)
    {
        var count = 0;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (map.GetTile(x, y).HasRoad)
                count++;
        }

        return count;
    }
}

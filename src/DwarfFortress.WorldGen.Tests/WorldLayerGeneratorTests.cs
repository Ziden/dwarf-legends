using System.Collections.Generic;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.World;

namespace DwarfFortress.WorldGen.Tests;

public sealed class WorldLayerGeneratorTests
{
    [Fact]
    public void Generate_SameSeedAndSize_ProducesDeterministicWorld()
    {
        var gen = new WorldLayerGenerator();
        var a = gen.Generate(seed: 77, width: 24, height: 16);
        var b = gen.Generate(seed: 77, width: 24, height: 16);

        for (var x = 0; x < a.Width; x++)
        for (var y = 0; y < a.Height; y++)
            Assert.Equal(a.GetTile(x, y), b.GetTile(x, y));
    }

    [Fact]
    public void Generate_UsesKnownBiomeIdsAndBoundedBands()
    {
        var gen = new WorldLayerGenerator();
        var map = gen.Generate(seed: 1234, width: 32, height: 32);
        var allowedBiomes = new HashSet<string>(MacroBiomeIds.All, StringComparer.OrdinalIgnoreCase);

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y);
            Assert.Contains(tile.MacroBiomeId, allowedBiomes);
            Assert.InRange(tile.ElevationBand, 0f, 1f);
            Assert.InRange(tile.TemperatureBand, 0f, 1f);
            Assert.InRange(tile.MoistureBand, 0f, 1f);
            Assert.InRange(tile.DrainageBand, 0f, 1f);
            Assert.InRange(tile.FactionPressure, 0f, 1f);
            Assert.InRange(tile.ForestCover, 0f, 1f);
            Assert.InRange(tile.Relief, 0f, 1f);
            Assert.InRange(tile.MountainCover, 0f, 1f);
            Assert.True(tile.FlowAccumulation >= 0f);
            if (tile.HasRiver)
            {
                Assert.NotEqual(WorldRiverEdges.None, tile.RiverEdges);
                Assert.True(tile.RiverBasinId > 0);
                Assert.InRange(tile.RiverDischarge, 1f, 12f);
                Assert.InRange(tile.RiverOrder, (byte)1, (byte)8);
            }
            else
            {
                Assert.Equal(0, tile.RiverBasinId);
                Assert.Equal(0f, tile.RiverDischarge);
                Assert.Equal((byte)0, tile.RiverOrder);
            }

            if (tile.HasRoad)
            {
                Assert.NotEqual(WorldRoadEdges.None, tile.RoadEdges);
                Assert.False(MacroBiomeIds.IsOcean(tile.MacroBiomeId));
            }
            else
            {
                Assert.Equal(WorldRoadEdges.None, tile.RoadEdges);
            }

            Assert.False(string.IsNullOrWhiteSpace(tile.GeologyProfileId));
        }
    }

    [Fact]
    public void Generate_RiverOrder_IsMonotonicDownstreamAndShowsHierarchy()
    {
        var map = new WorldLayerGenerator().Generate(seed: 2601, width: 64, height: 64);
        var countByOrder = new int[9];
        var dischargeByOrder = new float[9];
        var hasHigherOrderRiver = false;

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y);
            if (!tile.HasRiver)
                continue;

            if (tile.RiverOrder >= 2)
                hasHigherOrderRiver = true;

            var order = Math.Clamp(tile.RiverOrder, (byte)1, (byte)8);
            countByOrder[order]++;
            dischargeByOrder[order] += tile.RiverDischarge;
        }

        Assert.True(hasHigherOrderRiver, "Expected at least one river tile with order >= 2.");

        for (byte order = 1; order < 8; order++)
        {
            if (countByOrder[order] == 0 || countByOrder[order + 1] == 0)
                continue;

            var avg = dischargeByOrder[order] / countByOrder[order];
            var nextAvg = dischargeByOrder[order + 1] / countByOrder[order + 1];
            Assert.True(
                nextAvg >= (avg * 0.80f),
                $"Expected higher order rivers to trend stronger: order {order} avg={avg:F2}, order {order + 1} avg={nextAvg:F2}.");
        }
    }

    [Fact]
    public void Generate_RiverEdgeMasks_AreReciprocalAcrossNeighbors()
    {
        var map = new WorldLayerGenerator().Generate(seed: 445, width: 48, height: 48);
        var riverTiles = 0;

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y);
            if (!tile.HasRiver)
                continue;

            riverTiles++;
            if (WorldRiverEdgeMask.Has(tile.RiverEdges, WorldRiverEdges.East))
            {
                Assert.True(x + 1 < map.Width);
                Assert.True(
                    WorldRiverEdgeMask.Has(map.GetTile(x + 1, y).RiverEdges, WorldRiverEdges.West),
                    $"Missing reciprocal river edge at ({x + 1},{y}) west.");
            }

            if (WorldRiverEdgeMask.Has(tile.RiverEdges, WorldRiverEdges.West))
            {
                Assert.True(x - 1 >= 0);
                Assert.True(
                    WorldRiverEdgeMask.Has(map.GetTile(x - 1, y).RiverEdges, WorldRiverEdges.East),
                    $"Missing reciprocal river edge at ({x - 1},{y}) east.");
            }

            if (WorldRiverEdgeMask.Has(tile.RiverEdges, WorldRiverEdges.North))
            {
                Assert.True(y - 1 >= 0);
                Assert.True(
                    WorldRiverEdgeMask.Has(map.GetTile(x, y - 1).RiverEdges, WorldRiverEdges.South),
                    $"Missing reciprocal river edge at ({x},{y - 1}) south.");
            }

            if (WorldRiverEdgeMask.Has(tile.RiverEdges, WorldRiverEdges.South))
            {
                Assert.True(y + 1 < map.Height);
                Assert.True(
                    WorldRiverEdgeMask.Has(map.GetTile(x, y + 1).RiverEdges, WorldRiverEdges.North),
                    $"Missing reciprocal river edge at ({x},{y + 1}) north.");
            }
        }

        Assert.True(riverTiles > 0, "Expected world generation to produce at least one river tile.");
    }

    [Fact]
    public void Generate_RoadEdgeMasks_AreReciprocalAcrossNeighbors()
    {
        var map = new WorldLayerGenerator().Generate(seed: 1455, width: 48, height: 48);
        var roadTiles = 0;

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y);
            if (!tile.HasRoad)
                continue;

            roadTiles++;
            if (WorldRoadEdgeMask.Has(tile.RoadEdges, WorldRoadEdges.East))
            {
                Assert.True(x + 1 < map.Width);
                Assert.True(
                    WorldRoadEdgeMask.Has(map.GetTile(x + 1, y).RoadEdges, WorldRoadEdges.West),
                    $"Missing reciprocal road edge at ({x + 1},{y}) west.");
            }

            if (WorldRoadEdgeMask.Has(tile.RoadEdges, WorldRoadEdges.West))
            {
                Assert.True(x - 1 >= 0);
                Assert.True(
                    WorldRoadEdgeMask.Has(map.GetTile(x - 1, y).RoadEdges, WorldRoadEdges.East),
                    $"Missing reciprocal road edge at ({x - 1},{y}) east.");
            }

            if (WorldRoadEdgeMask.Has(tile.RoadEdges, WorldRoadEdges.North))
            {
                Assert.True(y - 1 >= 0);
                Assert.True(
                    WorldRoadEdgeMask.Has(map.GetTile(x, y - 1).RoadEdges, WorldRoadEdges.South),
                    $"Missing reciprocal road edge at ({x},{y - 1}) south.");
            }

            if (WorldRoadEdgeMask.Has(tile.RoadEdges, WorldRoadEdges.South))
            {
                Assert.True(y + 1 < map.Height);
                Assert.True(
                    WorldRoadEdgeMask.Has(map.GetTile(x, y + 1).RoadEdges, WorldRoadEdges.North),
                    $"Missing reciprocal road edge at ({x},{y + 1}) north.");
            }
        }

        Assert.True(roadTiles > 0, "Expected world generation to produce at least one road tile.");
    }

    [Fact]
    public void Generate_ConnectedRiverNeighbors_ShareBasinId()
    {
        var map = new WorldLayerGenerator().Generate(seed: 1717, width: 64, height: 64);
        var checkedPairs = 0;

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var tile = map.GetTile(x, y);
            if (!tile.HasRiver || tile.RiverBasinId <= 0)
                continue;

            if (x + 1 < map.Width)
            {
                var east = map.GetTile(x + 1, y);
                if (east.HasRiver &&
                    WorldRiverEdgeMask.Has(tile.RiverEdges, WorldRiverEdges.East) &&
                    WorldRiverEdgeMask.Has(east.RiverEdges, WorldRiverEdges.West))
                {
                    Assert.Equal(tile.RiverBasinId, east.RiverBasinId);
                    checkedPairs++;
                }
            }

            if (y + 1 < map.Height)
            {
                var south = map.GetTile(x, y + 1);
                if (south.HasRiver &&
                    WorldRiverEdgeMask.Has(tile.RiverEdges, WorldRiverEdges.South) &&
                    WorldRiverEdgeMask.Has(south.RiverEdges, WorldRiverEdges.North))
                {
                    Assert.Equal(tile.RiverBasinId, south.RiverBasinId);
                    checkedPairs++;
                }
            }
        }

        Assert.True(checkedPairs > 0, "Expected to validate at least one connected river neighbor pair.");
    }

    [Fact]
    public void Generate_ProducesCoolerPolesThanEquator_OnAverage()
    {
        const int width = 64;
        const int height = 64;
        var map = new WorldLayerGenerator().Generate(seed: 2026, width: width, height: height);

        var poleTemp = 0f;
        var equatorTemp = 0f;
        var poleCount = 0;
        var equatorCount = 0;

        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            var tile = map.GetTile(x, y);
            if (y < 10 || y >= height - 10)
            {
                poleTemp += tile.TemperatureBand;
                poleCount++;
            }

            if (Math.Abs(y - (height / 2)) <= 5)
            {
                equatorTemp += tile.TemperatureBand;
                equatorCount++;
            }
        }

        var poleAverage = poleTemp / poleCount;
        var equatorAverage = equatorTemp / equatorCount;

        Assert.True(
            equatorAverage > poleAverage + 0.08f,
            $"Expected equator to be warmer than poles, got equator={equatorAverage:F3}, poles={poleAverage:F3}.");
    }

    [Fact]
    public void Generate_SeedSweep_ProducesWarmWetAndWarmAridBiomes()
    {
        var generator = new WorldLayerGenerator();
        var tropicalWorlds = 0;
        var desertWorlds = 0;
        var savannaWorlds = 0;

        for (var seed = 0; seed < 30; seed++)
        {
            var map = generator.Generate(seed: seed, width: 32, height: 32);
            var hasTropical = false;
            var hasDesert = false;
            var hasSavanna = false;

            for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                var biome = map.GetTile(x, y).MacroBiomeId;
                hasTropical |= string.Equals(biome, MacroBiomeIds.TropicalRainforest, StringComparison.OrdinalIgnoreCase);
                hasDesert |= string.Equals(biome, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase);
                hasSavanna |= string.Equals(biome, MacroBiomeIds.Savanna, StringComparison.OrdinalIgnoreCase);
            }

            if (hasTropical)
                tropicalWorlds++;
            if (hasDesert)
                desertWorlds++;
            if (hasSavanna)
                savannaWorlds++;
        }

        Assert.True(
            tropicalWorlds >= 1,
            $"Expected at least one tropical world in seed sweep, got {tropicalWorlds}/30.");
        Assert.True(
            desertWorlds >= 1,
            $"Expected at least one desert world in seed sweep, got {desertWorlds}/30.");
        Assert.True(
            savannaWorlds >= 4,
            $"Expected several savanna worlds in seed sweep, got {savannaWorlds}/30.");
    }

    [Fact]
    public void Generate_OceanTiles_RespectElevationAndNeverCarryRivers()
    {
        var generator = new WorldLayerGenerator();
        var oceanTiles = 0;

        for (var seed = 100; seed < 106; seed++)
        {
            var map = generator.Generate(seed: seed, width: 72, height: 72);
            for (var x = 0; x < map.Width; x++)
            for (var y = 0; y < map.Height; y++)
            {
                var tile = map.GetTile(x, y);
                if (!MacroBiomeIds.IsOcean(tile.MacroBiomeId))
                    continue;

                oceanTiles++;
                Assert.True(tile.ElevationBand <= 0.28f + 0.0001f);
                Assert.False(tile.HasRiver);
                Assert.Equal(WorldRiverEdges.None, tile.RiverEdges);
                Assert.False(tile.HasRoad);
                Assert.Equal(WorldRoadEdges.None, tile.RoadEdges);
                Assert.Equal(0f, tile.ForestCover);
            }
        }

        Assert.True(oceanTiles > 0, "Expected sampled world seeds to include at least one ocean tile.");
    }

    [Fact]
    public void Generate_ForestAndMountainSignals_CorrelateWithBiomeAndRelief()
    {
        var generator = new WorldLayerGenerator();
        var map = generator.Generate(seed: 9025, width: 72, height: 72);

        var forestSamples = 0;
        var forestCoverSum = 0f;
        var aridSamples = 0;
        var aridForestCoverSum = 0f;
        var plainsForestCoverSum = 0f;
        var highlandSamples = 0;
        var highlandMountainSum = 0f;
        var plainsSamples = 0;
        var plainsMountainSum = 0f;

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var tile = map.GetTile(x, y);
            switch (tile.MacroBiomeId)
            {
                case MacroBiomeIds.ConiferForest:
                case MacroBiomeIds.BorealForest:
                case MacroBiomeIds.TropicalRainforest:
                    forestSamples++;
                    forestCoverSum += tile.ForestCover;
                    break;
                case MacroBiomeIds.WindsweptSteppe:
                case MacroBiomeIds.Desert:
                    aridSamples++;
                    aridForestCoverSum += tile.ForestCover;
                    break;
                case MacroBiomeIds.Highland:
                    highlandSamples++;
                    highlandMountainSum += tile.MountainCover;
                    break;
                case MacroBiomeIds.TemperatePlains:
                    plainsSamples++;
                    plainsForestCoverSum += tile.ForestCover;
                    plainsMountainSum += tile.MountainCover;
                    break;
            }
        }

        Assert.True(forestSamples > 0, "Expected sampled world to contain at least one forest biome tile.");
        Assert.True(highlandSamples > 0, "Expected sampled world to contain at least one highland tile.");
        Assert.True(plainsSamples > 0, "Expected sampled world to contain at least one temperate plains tile.");

        var forestAvg = forestCoverSum / forestSamples;
        var aridAvg = aridSamples > 0
            ? aridForestCoverSum / aridSamples
            : (plainsForestCoverSum / plainsSamples);
        var highlandMountainAvg = highlandMountainSum / highlandSamples;
        var plainsMountainAvg = plainsMountainSum / plainsSamples;

        Assert.True(
            forestAvg > aridAvg + 0.12f,
            $"Expected forest biomes to carry higher forest cover than arid biomes ({forestAvg:F3} vs {aridAvg:F3}).");

        Assert.True(
            highlandMountainAvg > plainsMountainAvg + 0.10f,
            $"Expected highland tiles to carry higher mountain cover than plains ({highlandMountainAvg:F3} vs {plainsMountainAvg:F3}).");
    }

    [Fact]
    public void Generate_RiverComponentsPreferOutletConnectivity()
    {
        var map = new WorldLayerGenerator().Generate(seed: 6124, width: 64, height: 64);
        var visited = new bool[map.Width * map.Height];
        var connectedComponents = 0;
        var largestComponentSize = 0;
        var largestComponentTouchesOutlet = false;

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var idx = y * map.Width + x;
            if (visited[idx] || !map.GetTile(x, y).HasRiver)
                continue;

            connectedComponents++;
            var queue = new Queue<(int X, int Y)>();
            queue.Enqueue((x, y));
            visited[idx] = true;

            var size = 0;
            var touchesOutlet = false;
            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                var tile = map.GetTile(cx, cy);
                size++;

                if (cx == 0 || cy == 0 || cx == map.Width - 1 || cy == map.Height - 1)
                    touchesOutlet = true;

                if (TryVisitConnectedRiver(map, visited, queue, cx, cy, tile, WorldRiverEdges.North, 0, -1, WorldRiverEdges.South))
                    touchesOutlet = touchesOutlet || cy == 1;
                if (TryVisitConnectedRiver(map, visited, queue, cx, cy, tile, WorldRiverEdges.East, 1, 0, WorldRiverEdges.West))
                    touchesOutlet = touchesOutlet || cx == map.Width - 2;
                if (TryVisitConnectedRiver(map, visited, queue, cx, cy, tile, WorldRiverEdges.South, 0, 1, WorldRiverEdges.North))
                    touchesOutlet = touchesOutlet || cy == map.Height - 2;
                if (TryVisitConnectedRiver(map, visited, queue, cx, cy, tile, WorldRiverEdges.West, -1, 0, WorldRiverEdges.East))
                    touchesOutlet = touchesOutlet || cx == 1;
            }

            if (size >= 5)
            {
                if (size > largestComponentSize)
                {
                    largestComponentSize = size;
                    largestComponentTouchesOutlet = touchesOutlet;
                }
            }
        }

        Assert.True(connectedComponents > 0, "Expected at least one connected river component.");
        Assert.True(largestComponentSize >= 5, "Expected at least one sizeable river component.");
        Assert.True(
            largestComponentTouchesOutlet,
            $"Expected largest river component to touch map outlet; size={largestComponentSize}.");
    }

    [Fact]
    public void Generate_RiverTileDistribution_IsNotPinnedToTopBand()
    {
        var generator = new WorldLayerGenerator();
        const int width = 64;
        const int height = 64;
        var totalRiverTiles = 0;
        var sumX = 0f;
        var sumY = 0f;

        for (var seed = 710; seed < 730; seed++)
        {
            var map = generator.Generate(seed: seed, width: width, height: height);
            for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                if (!map.GetTile(x, y).HasRiver)
                    continue;

                totalRiverTiles++;
                sumX += x;
                sumY += y;
            }
        }

        Assert.True(totalRiverTiles > 0, "Expected sampled seeds to generate river tiles.");

        var meanX = (sumX / totalRiverTiles) / (width - 1f);
        var meanY = (sumY / totalRiverTiles) / (height - 1f);

        Assert.InRange(meanX, 0.25f, 0.75f);
        Assert.InRange(meanY, 0.30f, 0.70f);
    }

    [Fact]
    public void Generate_ForestCover_StaysMoreCoherentAcrossNeighbors_ThanDistantTiles()
    {
        var map = new WorldLayerGenerator().Generate(seed: 9620, width: 72, height: 72);
        var neighborDiffSum = 0f;
        var neighborPairs = 0;
        var distantDiffSum = 0f;
        var distantPairs = 0;

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var tile = map.GetTile(x, y);
            if (MacroBiomeIds.IsOcean(tile.MacroBiomeId))
                continue;

            if (x + 1 < map.Width)
            {
                var east = map.GetTile(x + 1, y);
                if (!MacroBiomeIds.IsOcean(east.MacroBiomeId))
                {
                    neighborDiffSum += MathF.Abs(tile.ForestCover - east.ForestCover);
                    neighborPairs++;
                }
            }

            if (x + 9 < map.Width && y + 9 < map.Height)
            {
                var far = map.GetTile(x + 9, y + 9);
                if (!MacroBiomeIds.IsOcean(far.MacroBiomeId))
                {
                    distantDiffSum += MathF.Abs(tile.ForestCover - far.ForestCover);
                    distantPairs++;
                }
            }
        }

        Assert.True(neighborPairs > 0, "Expected non-ocean neighboring tiles for forest continuity check.");
        Assert.True(distantPairs > 0, "Expected non-ocean distant tiles for forest continuity check.");

        var neighborAvgDiff = neighborDiffSum / neighborPairs;
        var distantAvgDiff = distantDiffSum / distantPairs;
        Assert.True(
            neighborAvgDiff + 0.03f < distantAvgDiff,
            $"Expected neighboring forest cover to be smoother than distant samples ({neighborAvgDiff:F3} vs {distantAvgDiff:F3}).");
    }

    [Fact]
    public void Generate_MacroBiomes_AvoidExcessiveIsolatedLandSingles()
    {
        var generator = new WorldLayerGenerator();
        var isolatedLandTiles = 0;
        var totalLandTiles = 0;

        for (var seed = 1200; seed < 1208; seed++)
        {
            var map = generator.Generate(seed: seed, width: 64, height: 64);
            for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                var tile = map.GetTile(x, y);
                if (MacroBiomeIds.IsOcean(tile.MacroBiomeId))
                    continue;

                totalLandTiles++;
                var sameCardinal = 0;
                if (x > 0 && string.Equals(map.GetTile(x - 1, y).MacroBiomeId, tile.MacroBiomeId, StringComparison.OrdinalIgnoreCase))
                    sameCardinal++;
                if (x + 1 < map.Width && string.Equals(map.GetTile(x + 1, y).MacroBiomeId, tile.MacroBiomeId, StringComparison.OrdinalIgnoreCase))
                    sameCardinal++;
                if (y > 0 && string.Equals(map.GetTile(x, y - 1).MacroBiomeId, tile.MacroBiomeId, StringComparison.OrdinalIgnoreCase))
                    sameCardinal++;
                if (y + 1 < map.Height && string.Equals(map.GetTile(x, y + 1).MacroBiomeId, tile.MacroBiomeId, StringComparison.OrdinalIgnoreCase))
                    sameCardinal++;

                if (sameCardinal == 0)
                    isolatedLandTiles++;
            }
        }

        Assert.True(totalLandTiles > 0, "Expected sampled worlds to include land tiles.");
        var isolatedRatio = isolatedLandTiles / (float)totalLandTiles;
        Assert.True(
            isolatedRatio <= 0.17f,
            $"Expected isolated-land ratio <= 0.17, got {isolatedRatio:F3} ({isolatedLandTiles}/{totalLandTiles}).");
    }

    private static bool TryVisitConnectedRiver(
        GeneratedWorldMap map,
        bool[] visited,
        Queue<(int X, int Y)> queue,
        int x,
        int y,
        GeneratedWorldTile tile,
        WorldRiverEdges localEdge,
        int dx,
        int dy,
        WorldRiverEdges oppositeEdge)
    {
        if (!WorldRiverEdgeMask.Has(tile.RiverEdges, localEdge))
            return false;

        var nx = x + dx;
        var ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
            return false;

        var neighbor = map.GetTile(nx, ny);
        if (!neighbor.HasRiver || !WorldRiverEdgeMask.Has(neighbor.RiverEdges, oppositeEdge))
            return false;

        var nIdx = ny * map.Width + nx;
        if (!visited[nIdx])
        {
            visited[nIdx] = true;
            queue.Enqueue((nx, ny));
        }

        return true;
    }
}

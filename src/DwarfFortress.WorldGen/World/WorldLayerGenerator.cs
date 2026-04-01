using System;
using System.Collections.Generic;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Ids;

namespace DwarfFortress.WorldGen.World;

public interface IWorldLayerGenerator
{
    GeneratedWorldMap Generate(int seed, int width = 64, int height = 64);
}

public sealed class WorldLayerGenerator : IWorldLayerGenerator
{
    private const float OceanDeepThreshold = 0.16f;
    private const float OceanShallowThreshold = 0.28f;

    private static readonly (int Dx, int Dy)[] NeighborOffsets =
    [
        (0, -1),
        (1, 0),
        (0, 1),
        (-1, 0),
    ];

    private static readonly (int Dx, int Dy)[] SurroundingNeighborOffsets =
    [
        (-1, -1), (0, -1), (1, -1),
        (-1, 0),           (1, 0),
        (-1, 1),  (0, 1),  (1, 1),
    ];

    public GeneratedWorldMap Generate(int seed, int width = 64, int height = 64)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        var map = new GeneratedWorldMap(seed, width, height);
        var cellCount = width * height;
        var elevation = new float[cellCount];
        var temperature = new float[cellCount];
        var moisture = new float[cellCount];
        var runoff = new float[cellCount];
        var drainage = new float[cellCount];
        var ridges = new float[cellCount];
        var factionPressure = new float[cellCount];
        var relief = new float[cellCount];
        var mountainCover = new float[cellCount];
        var macroBiome = new string[cellCount];
        var forestCover = new float[cellCount];
        var rawForestCover = new float[cellCount];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var idx = IndexOf(x, y, width);
            var fx = width <= 1 ? 0f : x / (float)(width - 1);
            var fy = height <= 1 ? 0f : y / (float)(height - 1);
            var latitude = MathF.Abs((fy * 2f) - 1f);
            var equatorProximity = 1f - latitude;
            var equatorialCore = Math.Clamp((equatorProximity - 0.56f) / 0.44f, 0f, 1f);
            var subtropicalDryBelt = 1f - Math.Clamp(MathF.Abs(latitude - 0.33f) / 0.26f, 0f, 1f);

            var continentalness = CoherentNoise.DomainWarpedFractal2D(
                seed, fx * 2.4f, fy * 2.4f, octaves: 5, lacunarity: 2f, gain: 0.53f, warpStrength: 0.42f, salt: 101);
            var ridge = CoherentNoise.Ridged2D(
                seed, fx * 5.2f, fy * 5.2f, octaves: 4, lacunarity: 2.1f, gain: 0.52f, salt: 211);
            var uplift = CoherentNoise.Fractal2D(
                seed, fx * 3.8f, fy * 3.8f, octaves: 3, lacunarity: 2f, gain: 0.5f, salt: 307);

            var rawElevation =
                (continentalness * 0.58f) +
                (ridge * 0.27f) +
                (uplift * 0.15f);
            elevation[idx] = Math.Clamp((rawElevation * 1.10f) - 0.12f, 0f, 1f);
            ridges[idx] = ridge;

            var temperatureNoise = CoherentNoise.Fractal2D(
                seed, fx * 2.7f, fy * 2.7f, octaves: 4, lacunarity: 2f, gain: 0.5f, salt: 401);
            temperature[idx] = Math.Clamp(
                (equatorProximity * 0.52f) +
                (temperatureNoise * 0.31f) +
                (equatorialCore * 0.23f) -
                (elevation[idx] * 0.31f), 0f, 1f);

            var moistureNoise = CoherentNoise.DomainWarpedFractal2D(
                seed, fx * 3.1f, fy * 3.1f, octaves: 4, lacunarity: 2f, gain: 0.5f, warpStrength: 0.26f, salt: 503);
            var rainShadow = MathF.Max(0f, elevation[idx] - 0.62f) * 0.40f;
            moisture[idx] = Math.Clamp(
                (moistureNoise * 0.56f) +
                (equatorProximity * 0.08f) +
                (equatorialCore * 0.26f) +
                ((1f - elevation[idx]) * 0.08f) -
                (subtropicalDryBelt * 0.12f) -
                rainShadow, 0f, 1f);

            runoff[idx] = Math.Clamp(
                (moisture[idx] * 0.55f) +
                (elevation[idx] * 0.25f) +
                (ridge * 0.20f), 0f, 1f);

            var drainageNoise = CoherentNoise.Fractal2D(
                seed, fx * 4.1f, fy * 4.1f, octaves: 3, lacunarity: 2f, gain: 0.5f, salt: 607);
            drainage[idx] = Math.Clamp(
                (runoff[idx] * 0.65f) +
                ((1f - ridge) * 0.20f) +
                (drainageNoise * 0.15f), 0f, 1f);

            var factionNoise = CoherentNoise.Fractal2D(
                seed, fx * 1.7f, fy * 1.7f, octaves: 3, lacunarity: 2f, gain: 0.55f, salt: 701);
            factionPressure[idx] = Math.Clamp(
                (factionNoise * 0.65f) +
                ((1f - elevation[idx]) * 0.20f) +
                ((1f - latitude) * 0.15f), 0f, 1f);
        }

        var downstream = BuildFlowDirections(elevation, width, height, seed);
        var accumulation = BuildFlowAccumulation(runoff, downstream);
        var hasRiver = BuildRiverMask(elevation, drainage, accumulation, downstream, width, height);
        var riverEdges = BuildRiverEdgeMasks(hasRiver, downstream, width);
        for (var i = 0; i < hasRiver.Length; i++)
        {
            if (hasRiver[i] && riverEdges[i] == WorldRiverEdges.None)
                hasRiver[i] = false;
        }

        var riverBasinIds = BuildRiverBasinIds(hasRiver, downstream);
        var riverOrder = BuildRiverOrder(hasRiver, downstream);
        var riverDischarge = BuildRiverDischarge(hasRiver, accumulation, riverOrder, width, height);
        BuildReliefAndMountainCover(elevation, ridges, relief, mountainCover, width, height);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var idx = IndexOf(x, y, width);
            var biome = ResolveMacroBiomeId(elevation[idx], temperature[idx], moisture[idx], ridges[idx]);
            macroBiome[idx] = biome;
            rawForestCover[idx] = ResolveForestCover(
                biome,
                elevation[idx],
                temperature[idx],
                moisture[idx],
                drainage[idx],
                relief[idx],
                hasRiver[idx]);
        }

        ApplyMacroBiomeContinuity(macroBiome, elevation, temperature, moisture, ridges, width, height);

        Array.Copy(rawForestCover, forestCover, cellCount);
        ApplyForestContinuity(
            forestCover,
            rawForestCover,
            macroBiome,
            moisture,
            drainage,
            relief,
            hasRiver,
            width,
            height);
        var roadEdges = WorldGenFeatureFlags.EnableRoadGeneration
            ? BuildRoadEdgeMasks(seed, macroBiome, elevation, relief, factionPressure, hasRiver, width, height)
            : new WorldRoadEdges[cellCount];
        var hasRoad = new bool[cellCount];
        for (var i = 0; i < cellCount; i++)
            hasRoad[i] = roadEdges[i] != WorldRoadEdges.None;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var idx = IndexOf(x, y, width);
            map.SetTile(x, y, new GeneratedWorldTile(
                MacroBiomeId: macroBiome[idx],
                ElevationBand: elevation[idx],
                TemperatureBand: temperature[idx],
                MoistureBand: moisture[idx],
                DrainageBand: drainage[idx],
                GeologyProfileId: ResolveGeologyProfileId(elevation[idx], moisture[idx], runoff[idx], ridges[idx]),
                FactionPressure: factionPressure[idx],
                FlowAccumulation: accumulation[idx],
                HasRiver: hasRiver[idx],
                RiverEdges: riverEdges[idx],
                RiverBasinId: riverBasinIds[idx],
                RiverDischarge: riverDischarge[idx],
                RiverOrder: riverOrder[idx],
                ForestCover: forestCover[idx],
                Relief: relief[idx],
                MountainCover: mountainCover[idx],
                HasRoad: hasRoad[idx],
                RoadEdges: roadEdges[idx]));
        }

        return map;
    }

    private static int[] BuildFlowDirections(float[] elevation, int width, int height, int seed)
    {
        var outlets = BuildWorldOutletMask(elevation, width, height);
        var routingElevation = HydrologySolver.BuildFilledElevation(
            elevation,
            width,
            height,
            idx => outlets[idx],
            NeighborOffsets);

        return HydrologySolver.BuildFlowDirections(
            routingElevation,
            width,
            height,
            seed,
            NeighborOffsets,
            idx => outlets[idx]);
    }

    private static float[] BuildFlowAccumulation(float[] runoff, int[] downstream)
    {
        var contribution = new float[runoff.Length];
        for (var i = 0; i < contribution.Length; i++)
            contribution[i] = 1f + (runoff[i] * 0.75f);

        return HydrologySolver.BuildFlowAccumulation(downstream, contribution);
    }

    private static bool[] BuildWorldOutletMask(float[] elevation, int width, int height)
    {
        var outlets = new bool[elevation.Length];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var idx = IndexOf(x, y, width);
            if (IsBorderCell(x, y, width, height) || elevation[idx] <= OceanShallowThreshold)
                outlets[idx] = true;
        }

        return outlets;
    }

    private static bool[] BuildRiverMask(
        float[] elevation,
        float[] drainage,
        float[] accumulation,
        int[] downstream,
        int width,
        int height)
    {
        var river = new bool[elevation.Length];
        var globalThreshold = Math.Max(3f, (width * height) * 0.0105f);

        for (var idx = 0; idx < elevation.Length; idx++)
        {
            if (downstream[idx] < 0 || elevation[idx] > 0.96f || elevation[idx] < OceanShallowThreshold)
                continue;

            var localThreshold = globalThreshold * Math.Clamp(1.35f - (drainage[idx] * 0.70f), 0.58f, 1.35f);
            river[idx] = accumulation[idx] >= localThreshold;
        }

        var upstream = BuildUpstreamIndices(downstream);
        var order = new int[accumulation.Length];
        for (var i = 0; i < order.Length; i++)
            order[i] = i;

        // Extend river channels downstream so major streams remain continuous to outlets.
        Array.Sort(order, (a, b) => accumulation[b].CompareTo(accumulation[a]));
        foreach (var idx in order)
        {
            if (!river[idx])
                continue;

            var next = downstream[idx];
            if (next >= 0 && elevation[next] >= OceanShallowThreshold)
                river[next] = true;
        }

        // Allow natural tributaries to grow into major channels where upstream flow remains meaningful.
        var queue = new Queue<int>(river.Length);
        for (var idx = 0; idx < river.Length; idx++)
        {
            if (river[idx])
                queue.Enqueue(idx);
        }

        while (queue.Count > 0)
        {
            var idx = queue.Dequeue();
            var parents = upstream[idx];
            for (var i = 0; i < parents.Count; i++)
            {
                var upstreamIdx = parents[i];
                if (river[upstreamIdx])
                    continue;
                if (elevation[upstreamIdx] <= OceanShallowThreshold || elevation[upstreamIdx] > 0.96f)
                    continue;

                var tributaryThreshold = globalThreshold * Math.Clamp(0.82f - (drainage[upstreamIdx] * 0.28f), 0.32f, 0.90f);
                if (accumulation[upstreamIdx] < tributaryThreshold)
                    continue;

                river[upstreamIdx] = true;
                queue.Enqueue(upstreamIdx);
            }
        }

        PruneSmallInlandRiverComponents(river, elevation, accumulation, downstream, width, height, globalThreshold);
        return river;
    }

    private static List<int>[] BuildUpstreamIndices(int[] downstream)
    {
        var upstream = new List<int>[downstream.Length];
        for (var idx = 0; idx < upstream.Length; idx++)
            upstream[idx] = [];

        for (var idx = 0; idx < downstream.Length; idx++)
        {
            var next = downstream[idx];
            if (next >= 0 && next < downstream.Length && next != idx)
                upstream[next].Add(idx);
        }

        return upstream;
    }

    private static void PruneSmallInlandRiverComponents(
        bool[] river,
        float[] elevation,
        float[] accumulation,
        int[] downstream,
        int width,
        int height,
        float globalThreshold)
    {
        var visited = new bool[river.Length];
        var queue = new Queue<int>(32);
        var component = new List<int>(32);

        for (var idx = 0; idx < river.Length; idx++)
        {
            if (!river[idx] || visited[idx])
                continue;

            component.Clear();
            queue.Clear();
            queue.Enqueue(idx);
            visited[idx] = true;

            var touchesOutlet = false;
            var peakAccumulation = 0f;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);

                if (accumulation[current] > peakAccumulation)
                    peakAccumulation = accumulation[current];
                if (IsRiverOutletCell(current, elevation, downstream, width, height))
                    touchesOutlet = true;

                var x = current % width;
                var y = current / width;
                foreach (var (dx, dy) in NeighborOffsets)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;

                    var neighbor = IndexOf(nx, ny, width);
                    if (!river[neighbor] || visited[neighbor])
                        continue;

                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }

            var keep = touchesOutlet ||
                       component.Count >= 4 ||
                       peakAccumulation >= (globalThreshold * 1.15f);
            if (keep)
                continue;

            for (var i = 0; i < component.Count; i++)
                river[component[i]] = false;
        }
    }

    private static bool IsRiverOutletCell(int idx, float[] elevation, int[] downstream, int width, int height)
    {
        var x = idx % width;
        var y = idx / width;
        if (IsBorderCell(x, y, width, height) || elevation[idx] <= OceanShallowThreshold)
            return true;

        var next = downstream[idx];
        return next < 0 || next >= downstream.Length;
    }

    private static WorldRiverEdges[] BuildRiverEdgeMasks(bool[] hasRiver, int[] downstream, int width)
    {
        var masks = new WorldRiverEdges[hasRiver.Length];

        for (var idx = 0; idx < hasRiver.Length; idx++)
        {
            if (!hasRiver[idx])
                continue;

            var next = downstream[idx];
            if (next < 0 || !hasRiver[next])
                continue;

            Connect(masks, idx, next, width);
        }

        return masks;
    }

    private static WorldRoadEdges[] BuildRoadEdgeMasks(
        int seed,
        string[] macroBiome,
        float[] elevation,
        float[] relief,
        float[] factionPressure,
        bool[] hasRiver,
        int width,
        int height)
    {
        var masks = new WorldRoadEdges[macroBiome.Length];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var idx = IndexOf(x, y, width);
            if (MacroBiomeIds.IsOcean(macroBiome[idx]) || hasRiver[idx])
                continue;

            if (x + 1 < width)
                TryConnectRoadPair(seed, x, y, x + 1, y, macroBiome, elevation, relief, factionPressure, hasRiver, width, masks);
            if (y + 1 < height)
                TryConnectRoadPair(seed, x, y, x, y + 1, macroBiome, elevation, relief, factionPressure, hasRiver, width, masks);
        }

        PruneSmallRoadComponents(masks, width, height);
        return masks;
    }

    private static void TryConnectRoadPair(
        int seed,
        int x,
        int y,
        int nx,
        int ny,
        string[] macroBiome,
        float[] elevation,
        float[] relief,
        float[] factionPressure,
        bool[] hasRiver,
        int width,
        WorldRoadEdges[] masks)
    {
        var idx = IndexOf(x, y, width);
        var next = IndexOf(nx, ny, width);
        if (MacroBiomeIds.IsOcean(macroBiome[idx]) || MacroBiomeIds.IsOcean(macroBiome[next]))
            return;
        if (hasRiver[idx] || hasRiver[next])
            return;

        var localPotential = ResolveRoadPotential(macroBiome[idx], elevation[idx], relief[idx], factionPressure[idx]);
        var neighborPotential = ResolveRoadPotential(macroBiome[next], elevation[next], relief[next], factionPressure[next]);
        if (localPotential < 0.42f || neighborPotential < 0.42f)
            return;

        var pairPotential = (localPotential + neighborPotential) * 0.5f;
        var threshold = pairPotential switch
        {
            >= 0.84f => 0.93f,
            >= 0.72f => 0.76f,
            >= 0.62f => 0.58f,
            _ => 0.36f,
        };

        var keyX = Math.Min(x, nx);
        var keyY = Math.Min(y, ny);
        var salt = y == ny ? 19019 : 19037;
        var jitter = SeedHash.Unit(seed, keyX, keyY, salt);
        if (jitter > threshold)
            return;

        ConnectRoad(masks, idx, next, width);
    }

    private static float ResolveRoadPotential(string macroBiomeId, float elevation, float relief, float factionPressure)
    {
        var terrainSuitability =
            ((1f - relief) * 0.52f) +
            ((1f - MathF.Abs(elevation - 0.48f)) * 0.20f) +
            ((1f - MathF.Max(0f, elevation - 0.72f)) * 0.08f);
        var basePotential = (factionPressure * 0.60f) + (terrainSuitability * 0.40f);
        var biomeBias = macroBiomeId switch
        {
            MacroBiomeIds.TemperatePlains => 0.10f,
            MacroBiomeIds.ConiferForest => 0.06f,
            MacroBiomeIds.BorealForest => 0.04f,
            MacroBiomeIds.MistyMarsh => -0.06f,
            MacroBiomeIds.Highland => -0.12f,
            MacroBiomeIds.Desert => -0.16f,
            MacroBiomeIds.Tundra => -0.10f,
            MacroBiomeIds.IcePlains => -0.24f,
            MacroBiomeIds.WindsweptSteppe => -0.04f,
            _ => 0f,
        };

        return Math.Clamp(basePotential + biomeBias, 0f, 1f);
    }

    private static void PruneSmallRoadComponents(WorldRoadEdges[] masks, int width, int height)
    {
        var visited = new bool[masks.Length];
        var queue = new Queue<int>(32);
        var component = new List<int>(32);

        for (var idx = 0; idx < masks.Length; idx++)
        {
            if (visited[idx] || masks[idx] == WorldRoadEdges.None)
                continue;

            queue.Clear();
            component.Clear();
            queue.Enqueue(idx);
            visited[idx] = true;

            var touchesBoundary = false;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);

                var x = current % width;
                var y = current / width;
                if (IsBorderCell(x, y, width, height))
                    touchesBoundary = true;

                EnqueueRoadNeighbor(masks, visited, queue, x, y, width, WorldRoadEdges.North, 0, -1, WorldRoadEdges.South);
                EnqueueRoadNeighbor(masks, visited, queue, x, y, width, WorldRoadEdges.East, 1, 0, WorldRoadEdges.West);
                EnqueueRoadNeighbor(masks, visited, queue, x, y, width, WorldRoadEdges.South, 0, 1, WorldRoadEdges.North);
                EnqueueRoadNeighbor(masks, visited, queue, x, y, width, WorldRoadEdges.West, -1, 0, WorldRoadEdges.East);
            }

            var keep = touchesBoundary || component.Count >= 3;
            if (keep)
                continue;

            for (var i = 0; i < component.Count; i++)
                masks[component[i]] = WorldRoadEdges.None;
        }
    }

    private static void EnqueueRoadNeighbor(
        WorldRoadEdges[] masks,
        bool[] visited,
        Queue<int> queue,
        int x,
        int y,
        int width,
        WorldRoadEdges localEdge,
        int dx,
        int dy,
        WorldRoadEdges oppositeEdge)
    {
        var idx = IndexOf(x, y, width);
        if (!WorldRoadEdgeMask.Has(masks[idx], localEdge))
            return;

        var nx = x + dx;
        var ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= width || ny >= (masks.Length / width))
            return;

        var neighbor = IndexOf(nx, ny, width);
        if (!WorldRoadEdgeMask.Has(masks[neighbor], oppositeEdge))
            return;
        if (visited[neighbor])
            return;

        visited[neighbor] = true;
        queue.Enqueue(neighbor);
    }

    private static int[] BuildRiverBasinIds(bool[] hasRiver, int[] downstream)
    {
        var basinIds = new int[hasRiver.Length];
        var outletCache = new int[hasRiver.Length];
        Array.Fill(outletCache, int.MinValue);

        for (var idx = 0; idx < hasRiver.Length; idx++)
        {
            if (!hasRiver[idx])
                outletCache[idx] = -1;
        }

        var outlets = new HashSet<int>();
        for (var idx = 0; idx < hasRiver.Length; idx++)
        {
            if (!hasRiver[idx])
                continue;

            var outlet = ResolveOutlet(idx, hasRiver, downstream, outletCache);
            if (outlet >= 0)
                outlets.Add(outlet);
        }

        var orderedOutlets = new List<int>(outlets);
        orderedOutlets.Sort();

        var outletToBasin = new Dictionary<int, int>(orderedOutlets.Count);
        for (var i = 0; i < orderedOutlets.Count; i++)
            outletToBasin[orderedOutlets[i]] = i + 1;

        for (var idx = 0; idx < hasRiver.Length; idx++)
        {
            if (!hasRiver[idx])
                continue;

            var outlet = ResolveOutlet(idx, hasRiver, downstream, outletCache);
            basinIds[idx] = outletToBasin.GetValueOrDefault(outlet, 0);
        }

        return basinIds;
    }

    private static int ResolveOutlet(int start, bool[] hasRiver, int[] downstream, int[] outletCache)
    {
        if (!hasRiver[start])
            return -1;

        var cached = outletCache[start];
        if (cached != int.MinValue)
            return cached;

        var trail = new List<int>(16);
        var seen = new HashSet<int>();
        var current = start;
        var resolvedOutlet = -1;

        for (var step = 0; step < hasRiver.Length; step++)
        {
            if (current < 0 || current >= hasRiver.Length || !hasRiver[current])
            {
                resolvedOutlet = trail.Count > 0 ? trail[^1] : -1;
                break;
            }

            var cachedCurrent = outletCache[current];
            if (cachedCurrent != int.MinValue)
            {
                resolvedOutlet = cachedCurrent;
                break;
            }

            if (!seen.Add(current))
            {
                resolvedOutlet = current;
                break;
            }

            trail.Add(current);

            var next = downstream[current];
            if (next < 0 || next >= hasRiver.Length || !hasRiver[next])
            {
                resolvedOutlet = current;
                break;
            }

            current = next;
        }

        if (resolvedOutlet < 0 && trail.Count > 0)
            resolvedOutlet = trail[^1];

        foreach (var idx in trail)
            outletCache[idx] = resolvedOutlet;

        return resolvedOutlet;
    }

    private static byte[] BuildRiverOrder(bool[] hasRiver, int[] downstream)
    {
        var order = new byte[hasRiver.Length];
        var upstream = BuildUpstreamIndices(downstream);
        var indegree = new int[hasRiver.Length];
        var maxUpstreamOrder = new byte[hasRiver.Length];
        var maxUpstreamOrderCount = new byte[hasRiver.Length];
        var queue = new Queue<int>(hasRiver.Length);

        for (var idx = 0; idx < hasRiver.Length; idx++)
        {
            if (!hasRiver[idx])
                continue;

            var next = downstream[idx];
            if (next >= 0 && next < hasRiver.Length && hasRiver[next] && next != idx)
                indegree[next]++;
        }

        for (var idx = 0; idx < hasRiver.Length; idx++)
        {
            if (hasRiver[idx] && indegree[idx] == 0)
                queue.Enqueue(idx);
        }

        while (queue.Count > 0)
        {
            var idx = queue.Dequeue();
            if (!hasRiver[idx])
                continue;

            var localOrder = maxUpstreamOrder[idx] == 0
                ? (byte)1
                : (byte)Math.Clamp(
                    maxUpstreamOrder[idx] + (maxUpstreamOrderCount[idx] >= 2 ? 1 : 0),
                    1,
                    8);
            order[idx] = localOrder;

            var next = downstream[idx];
            if (next < 0 || next >= hasRiver.Length || !hasRiver[next] || next == idx)
                continue;

            if (localOrder > maxUpstreamOrder[next])
            {
                maxUpstreamOrder[next] = localOrder;
                maxUpstreamOrderCount[next] = 1;
            }
            else if (localOrder == maxUpstreamOrder[next] && maxUpstreamOrderCount[next] < byte.MaxValue)
            {
                maxUpstreamOrderCount[next]++;
            }

            indegree[next]--;
            if (indegree[next] == 0)
                queue.Enqueue(next);
        }

        // Fallback for any unresolved river tiles (should be rare with acyclic downstream graphs).
        for (var idx = 0; idx < hasRiver.Length; idx++)
        {
            if (hasRiver[idx] && order[idx] == 0)
            {
                var maxUpstream = 0;
                var sameMaxCount = 0;
                var parents = upstream[idx];
                for (var i = 0; i < parents.Count; i++)
                {
                    var parentIdx = parents[i];
                    if (!hasRiver[parentIdx])
                        continue;

                    var parentOrder = order[parentIdx] == 0 ? 1 : order[parentIdx];
                    if (parentOrder > maxUpstream)
                    {
                        maxUpstream = parentOrder;
                        sameMaxCount = 1;
                    }
                    else if (parentOrder == maxUpstream)
                    {
                        sameMaxCount++;
                    }
                }

                order[idx] = maxUpstream <= 0
                    ? (byte)1
                    : (byte)Math.Clamp(maxUpstream + (sameMaxCount >= 2 ? 1 : 0), 1, 8);
            }
        }

        return order;
    }

    private static float[] BuildRiverDischarge(bool[] hasRiver, float[] accumulation, byte[] riverOrder, int width, int height)
    {
        var discharge = new float[hasRiver.Length];
        var threshold = Math.Max(3f, (width * height) * 0.0105f);

        for (var idx = 0; idx < hasRiver.Length; idx++)
        {
            if (!hasRiver[idx])
                continue;

            var normalized = accumulation[idx] / threshold;
            var order = Math.Max(1, (int)riverOrder[idx]);
            var orderBoost = 1f + ((order - 1) * 0.22f);
            discharge[idx] = Math.Clamp(normalized * orderBoost, 1f, 12f);
        }

        return discharge;
    }

    private static void Connect(WorldRiverEdges[] masks, int idx, int next, int width)
    {
        var x = idx % width;
        var y = idx / width;
        var nx = next % width;
        var ny = next / width;
        var dx = Math.Sign(nx - x);
        var dy = Math.Sign(ny - y);

        if (dx != 0 && dy != 0)
        {
            // Diagonal flow is collapsed onto one cardinal edge to keep border contracts unambiguous.
            if (((idx ^ next) & 1) == 0)
                dy = 0;
            else
                dx = 0;
        }

        if (dx > 0)
        {
            masks[idx] |= WorldRiverEdges.East;
            masks[next] |= WorldRiverEdges.West;
        }
        else if (dx < 0)
        {
            masks[idx] |= WorldRiverEdges.West;
            masks[next] |= WorldRiverEdges.East;
        }

        if (dy > 0)
        {
            masks[idx] |= WorldRiverEdges.South;
            masks[next] |= WorldRiverEdges.North;
        }
        else if (dy < 0)
        {
            masks[idx] |= WorldRiverEdges.North;
            masks[next] |= WorldRiverEdges.South;
        }
    }

    private static void ConnectRoad(WorldRoadEdges[] masks, int idx, int next, int width)
    {
        var x = idx % width;
        var y = idx / width;
        var nx = next % width;
        var ny = next / width;
        var dx = Math.Sign(nx - x);
        var dy = Math.Sign(ny - y);

        if (dx > 0)
        {
            masks[idx] |= WorldRoadEdges.East;
            masks[next] |= WorldRoadEdges.West;
        }
        else if (dx < 0)
        {
            masks[idx] |= WorldRoadEdges.West;
            masks[next] |= WorldRoadEdges.East;
        }

        if (dy > 0)
        {
            masks[idx] |= WorldRoadEdges.South;
            masks[next] |= WorldRoadEdges.North;
        }
        else if (dy < 0)
        {
            masks[idx] |= WorldRoadEdges.North;
            masks[next] |= WorldRoadEdges.South;
        }
    }

    private static bool IsBorderCell(int x, int y, int width, int height)
        => x == 0 || y == 0 || x == width - 1 || y == height - 1;

    private static int IndexOf(int x, int y, int width)
        => y * width + x;

    private static string ResolveMacroBiomeId(float elevation, float temperature, float moisture, float ridges)
    {
        if (elevation <= OceanDeepThreshold)
            return MacroBiomeIds.OceanDeep;
        if (elevation <= OceanShallowThreshold)
            return MacroBiomeIds.OceanShallow;

        if (elevation >= 0.80f || (elevation >= 0.70f && ridges >= 0.74f))
            return MacroBiomeIds.Highland;

        if (temperature <= 0.11f)
            return MacroBiomeIds.IcePlains;
        if (temperature <= 0.23f && moisture <= 0.46f)
            return MacroBiomeIds.Tundra;
        if (temperature <= 0.44f && moisture >= 0.54f)
            return MacroBiomeIds.BorealForest;

        if (moisture >= 0.70f && temperature >= 0.58f && elevation <= 0.72f)
            return MacroBiomeIds.TropicalRainforest;

        var aridity = Math.Clamp(
            ((1f - moisture) * 0.72f) +
            (temperature * 0.28f) +
            (MathF.Max(0f, ridges - 0.52f) * 0.22f), 0f, 1f);

        if (temperature >= 0.58f && moisture <= 0.38f && aridity >= 0.68f)
            return MacroBiomeIds.Desert;
        if (temperature >= 0.54f && moisture <= 0.50f && aridity >= 0.58f)
            return MacroBiomeIds.Savanna;

        if (moisture >= 0.72f && temperature >= 0.28f)
            return MacroBiomeIds.MistyMarsh;
        if (moisture >= 0.54f && temperature <= 0.60f)
            return MacroBiomeIds.ConiferForest;
        if (moisture <= 0.24f || temperature >= 0.82f)
            return MacroBiomeIds.WindsweptSteppe;
        return MacroBiomeIds.TemperatePlains;
    }

    private static string ResolveGeologyProfileId(float elevation, float moisture, float runoff, float ridges)
    {
        if (ridges >= 0.68f && elevation >= 0.62f)
            return GeologyProfileIds.MetamorphicSpine;
        if (runoff >= 0.70f && moisture >= 0.55f)
            return GeologyProfileIds.AlluvialBasin;
        if (elevation >= 0.70f && moisture <= 0.42f)
            return GeologyProfileIds.IgneousUplift;
        if (moisture >= 0.70f)
            return GeologyProfileIds.SedimentaryWetlands;
        return GeologyProfileIds.MixedBedrock;
    }

    private static void ApplyMacroBiomeContinuity(
        string[] macroBiome,
        float[] elevation,
        float[] temperature,
        float[] moisture,
        float[] ridges,
        int width,
        int height)
    {
        if (macroBiome.Length == 0)
            return;

        var scratch = new string[macroBiome.Length];
        Array.Copy(macroBiome, scratch, macroBiome.Length);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var idx = IndexOf(x, y, width);
            var current = macroBiome[idx];
            if (MacroBiomeIds.IsOcean(current))
            {
                scratch[idx] = current;
                continue;
            }

            var preferred = ResolveMacroBiomeId(elevation[idx], temperature[idx], moisture[idx], ridges[idx]);
            if (string.Equals(preferred, MacroBiomeIds.Highland, StringComparison.OrdinalIgnoreCase) &&
                elevation[idx] >= 0.76f)
            {
                scratch[idx] = current;
                continue;
            }

            var sameCardinal = CountSameCardinalNeighbors(macroBiome, current, x, y, width, height);
            if (sameCardinal >= 2)
            {
                scratch[idx] = current;
                continue;
            }

            if (!TryResolveDominantNeighborBiome(macroBiome, x, y, width, height, out var dominant, out var dominantWeight))
            {
                scratch[idx] = current;
                continue;
            }

            if (dominantWeight < 4)
            {
                scratch[idx] = current;
                continue;
            }

            if (string.Equals(dominant, current, StringComparison.OrdinalIgnoreCase))
            {
                scratch[idx] = current;
                continue;
            }

            if (!IsMacroBiomeTransitionAllowed(current, dominant, preferred))
            {
                scratch[idx] = current;
                continue;
            }

            scratch[idx] = dominant;
        }

        Array.Copy(scratch, macroBiome, macroBiome.Length);
    }

    private static int CountSameCardinalNeighbors(
        string[] macroBiome,
        string biomeId,
        int x,
        int y,
        int width,
        int height)
    {
        var same = 0;
        for (var i = 0; i < NeighborOffsets.Length; i++)
        {
            var (dx, dy) = NeighborOffsets[i];
            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                continue;

            var neighbor = macroBiome[IndexOf(nx, ny, width)];
            if (string.Equals(neighbor, biomeId, StringComparison.OrdinalIgnoreCase))
                same++;
        }

        return same;
    }

    private static bool TryResolveDominantNeighborBiome(
        string[] macroBiome,
        int x,
        int y,
        int width,
        int height,
        out string dominantBiomeId,
        out int dominantWeight)
    {
        var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < SurroundingNeighborOffsets.Length; i++)
        {
            var (dx, dy) = SurroundingNeighborOffsets[i];
            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                continue;

            var neighbor = macroBiome[IndexOf(nx, ny, width)];
            if (MacroBiomeIds.IsOcean(neighbor))
                continue;

            var weight = (dx == 0 || dy == 0) ? 2 : 1;
            votes.TryGetValue(neighbor, out var existing);
            votes[neighbor] = existing + weight;
        }

        dominantBiomeId = string.Empty;
        dominantWeight = 0;
        foreach (var (biomeId, weight) in votes)
        {
            if (weight <= dominantWeight)
                continue;

            dominantWeight = weight;
            dominantBiomeId = biomeId;
        }

        return dominantWeight > 0 && !string.IsNullOrWhiteSpace(dominantBiomeId);
    }

    private static bool IsMacroBiomeTransitionAllowed(string sourceBiomeId, string targetBiomeId, string preferredBiomeId)
    {
        if (MacroBiomeIds.IsOcean(sourceBiomeId) || MacroBiomeIds.IsOcean(targetBiomeId))
            return false;

        if (string.Equals(sourceBiomeId, targetBiomeId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(targetBiomeId, preferredBiomeId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (IsSameMacroBiomeFamily(preferredBiomeId, targetBiomeId))
            return true;

        return IsSameMacroBiomeFamily(sourceBiomeId, targetBiomeId);
    }

    private static bool IsSameMacroBiomeFamily(string left, string right)
    {
        return ResolveMacroBiomeFamily(left) == ResolveMacroBiomeFamily(right);
    }

    private static int ResolveMacroBiomeFamily(string biomeId)
    {
        if (string.Equals(biomeId, MacroBiomeIds.Highland, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (string.Equals(biomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.Tundra, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.BorealForest, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (string.Equals(biomeId, MacroBiomeIds.TropicalRainforest, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.MistyMarsh, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(biomeId, MacroBiomeIds.Savanna, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.WindsweptSteppe, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return 4;
    }

    private static void BuildReliefAndMountainCover(
        float[] elevation,
        float[] ridges,
        float[] relief,
        float[] mountainCover,
        int width,
        int height)
    {
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var idx = IndexOf(x, y, width);
            var localMin = elevation[idx];
            var localMax = elevation[idx];
            foreach (var (dx, dy) in SurroundingNeighborOffsets)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;

                var n = elevation[IndexOf(nx, ny, width)];
                if (n < localMin)
                    localMin = n;
                if (n > localMax)
                    localMax = n;
            }

            var localRange = localMax - localMin;
            var rugged = Math.Clamp(
                (localRange * 2.7f) +
                (MathF.Max(0f, elevation[idx] - 0.50f) * 0.22f) +
                (ridges[idx] * 0.30f), 0f, 1f);
            relief[idx] = rugged;

            mountainCover[idx] = Math.Clamp(
                (MathF.Max(0f, elevation[idx] - 0.58f) * 1.35f) +
                (MathF.Max(0f, ridges[idx] - 0.52f) * 0.92f) +
                (localRange * 1.25f), 0f, 1f);
        }
    }

    private static void ApplyForestContinuity(
        float[] forestCover,
        float[] rawForestCover,
        string[] macroBiome,
        float[] moisture,
        float[] drainage,
        float[] relief,
        bool[] hasRiver,
        int width,
        int height)
    {
        if (forestCover.Length == 0)
            return;

        var scratch = new float[forestCover.Length];
        const int passes = 2;

        for (var pass = 0; pass < passes; pass++)
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var idx = IndexOf(x, y, width);
                if (MacroBiomeIds.IsOcean(macroBiome[idx]))
                {
                    scratch[idx] = 0f;
                    continue;
                }

                var weighted = forestCover[idx] * 1.35f;
                var weight = 1.35f;
                foreach (var (dx, dy) in SurroundingNeighborOffsets)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;

                    var neighborIdx = IndexOf(nx, ny, width);
                    if (MacroBiomeIds.IsOcean(macroBiome[neighborIdx]))
                        continue;

                    var isCardinal = dx == 0 || dy == 0;
                    var neighborWeight = isCardinal ? 0.52f : 0.34f;
                    weighted += forestCover[neighborIdx] * neighborWeight;
                    weight += neighborWeight;
                }

                var neighborhood = weight <= 0f ? forestCover[idx] : (weighted / weight);
                var hydroBoost = hasRiver[idx] ? 0.07f : 0f;
                var moistureBias =
                    ((moisture[idx] - 0.5f) * 0.18f) +
                    ((drainage[idx] - 0.5f) * 0.08f);
                var reliefPenalty = MathF.Max(0f, relief[idx] - 0.66f) * 0.18f;

                scratch[idx] = Math.Clamp(
                    (rawForestCover[idx] * 0.58f) +
                    (neighborhood * 0.42f) +
                    hydroBoost +
                    moistureBias -
                    reliefPenalty, 0f, 1f);
            }

            Array.Copy(scratch, forestCover, forestCover.Length);
        }
    }

    private static float ResolveForestCover(
        string macroBiomeId,
        float elevation,
        float temperature,
        float moisture,
        float drainage,
        float relief,
        bool hasRiver)
    {
        if (MacroBiomeIds.IsOcean(macroBiomeId))
            return 0f;

        var riparianBoost = hasRiver ? 0.16f : 0f;
        var moistureSupport =
            (moisture * 0.58f) +
            (drainage * 0.20f) +
            ((1f - elevation) * 0.12f) +
            riparianBoost;

        var reliefPenalty = relief * 0.20f;
        var alpinePenalty = MathF.Max(0f, elevation - 0.72f) * 0.45f;
        var heatPenalty = MathF.Max(0f, temperature - 0.86f) * 0.28f;
        var coldPenalty = MathF.Max(0f, 0.08f - temperature) * 0.22f;

        var biomeMultiplier = macroBiomeId switch
        {
            MacroBiomeIds.TropicalRainforest => 1.28f,
            MacroBiomeIds.ConiferForest => 1.18f,
            MacroBiomeIds.BorealForest => 1.12f,
            MacroBiomeIds.MistyMarsh => 0.94f,
            MacroBiomeIds.TemperatePlains => 0.72f,
            MacroBiomeIds.Highland => 0.34f,
            MacroBiomeIds.Savanna => 0.42f,
            MacroBiomeIds.Desert => 0.06f,
            MacroBiomeIds.Tundra => 0.18f,
            MacroBiomeIds.IcePlains => 0f,
            MacroBiomeIds.WindsweptSteppe => 0.22f,
            _ => 0.55f,
        };

        var raw = (moistureSupport * biomeMultiplier) - reliefPenalty - alpinePenalty - heatPenalty - coldPenalty;
        return Math.Clamp(raw, 0f, 1f);
    }
}

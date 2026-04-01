using System;
using System.Collections.Generic;

namespace DwarfFortress.WorldGen.Generation;

/// <summary>
/// Shared deterministic hydrology helpers used by world and region generation.
/// </summary>
internal static class HydrologySolver
{
    public static float[] BuildFilledElevation(
        float[] elevation,
        int width,
        int height,
        Func<int, bool>? isOpenOutlet,
        (int Dx, int Dy)[] neighborOffsets)
    {
        if (elevation is null) throw new ArgumentNullException(nameof(elevation));
        if (neighborOffsets is null) throw new ArgumentNullException(nameof(neighborOffsets));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (elevation.Length != width * height)
            throw new ArgumentException("Elevation buffer length does not match width*height.", nameof(elevation));

        var filled = (float[])elevation.Clone();
        if (filled.Length == 0)
            return filled;

        const float epsilon = 0.0001f;
        var maxIterations = Math.Max(4, width * height);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var changed = false;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var idx = IndexOf(x, y, width);
                if (isOpenOutlet?.Invoke(idx) == true)
                    continue;

                var minNeighbor = float.MaxValue;
                var hasNeighbor = false;

                foreach (var (dx, dy) in neighborOffsets)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                        continue;

                    var nIdx = IndexOf(nx, ny, width);
                    var value = filled[nIdx];
                    if (value < minNeighbor)
                        minNeighbor = value;
                    hasNeighbor = true;
                }

                if (!hasNeighbor)
                    continue;

                var target = minNeighbor + epsilon;
                if (filled[idx] + epsilon >= target)
                    continue;

                filled[idx] = target;
                changed = true;
            }

            if (!changed)
                break;
        }

        return filled;
    }

    public static int[] BuildFlowDirections(
        float[] routingElevation,
        int width,
        int height,
        int seed,
        (int Dx, int Dy)[] neighborOffsets,
        Func<int, bool>? isOpenOutlet)
    {
        if (routingElevation is null) throw new ArgumentNullException(nameof(routingElevation));
        if (neighborOffsets is null) throw new ArgumentNullException(nameof(neighborOffsets));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (routingElevation.Length != width * height)
            throw new ArgumentException("Elevation buffer length does not match width*height.", nameof(routingElevation));

        var downstream = new int[routingElevation.Length];
        Array.Fill(downstream, -1);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var idx = IndexOf(x, y, width);
            if (isOpenOutlet?.Invoke(idx) == true)
                continue;

            var current = routingElevation[idx];
            var bestIdx = -1;
            var bestElevation = float.MaxValue;
            var foundLower = false;

            foreach (var (dx, dy) in neighborOffsets)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    continue;

                var nIdx = IndexOf(nx, ny, width);
                var candidate = routingElevation[nIdx];
                var isLower = candidate < current - 0.0005f;

                if (isLower)
                {
                    if (!foundLower ||
                        candidate < bestElevation - 0.0005f ||
                        (MathF.Abs(candidate - bestElevation) <= 0.0005f &&
                         ShouldPreferNeighbor(seed, idx, nIdx, bestIdx)))
                    {
                        bestIdx = nIdx;
                        bestElevation = candidate;
                    }

                    foundLower = true;
                    continue;
                }

                if (foundLower)
                    continue;

                if (candidate < bestElevation - 0.0005f)
                {
                    bestIdx = nIdx;
                    bestElevation = candidate;
                    continue;
                }

                if (MathF.Abs(candidate - bestElevation) <= 0.0005f &&
                    (bestIdx < 0 || nIdx < bestIdx))
                {
                    bestIdx = nIdx;
                    bestElevation = candidate;
                }
            }

            if (!foundLower && bestIdx >= idx)
                bestIdx = -1;

            downstream[idx] = bestIdx;
        }

        return downstream;
    }

    public static float[] BuildFlowAccumulation(int[] downstream, float[] baseContribution)
    {
        if (downstream is null) throw new ArgumentNullException(nameof(downstream));
        if (baseContribution is null) throw new ArgumentNullException(nameof(baseContribution));
        if (downstream.Length != baseContribution.Length)
            throw new ArgumentException("Downstream and base contribution arrays must have identical lengths.");

        var accumulation = new float[downstream.Length];
        var indegree = new int[downstream.Length];

        for (var idx = 0; idx < accumulation.Length; idx++)
            accumulation[idx] = MathF.Max(0f, baseContribution[idx]);

        for (var idx = 0; idx < downstream.Length; idx++)
        {
            var next = downstream[idx];
            if (next >= 0 && next < downstream.Length && next != idx)
                indegree[next]++;
        }

        var queue = new Queue<int>(downstream.Length);
        for (var idx = 0; idx < indegree.Length; idx++)
        {
            if (indegree[idx] == 0)
                queue.Enqueue(idx);
        }

        while (queue.Count > 0)
        {
            var idx = queue.Dequeue();
            var next = downstream[idx];
            if (next < 0 || next >= downstream.Length || next == idx)
                continue;

            accumulation[next] += accumulation[idx];
            indegree[next]--;
            if (indegree[next] == 0)
                queue.Enqueue(next);
        }

        return accumulation;
    }

    private static bool ShouldPreferNeighbor(int seed, int sourceIdx, int candidateIdx, int currentBestIdx)
    {
        if (currentBestIdx < 0)
            return true;

        var candidate = SeedHash.Unit(seed, candidateIdx, sourceIdx, 1931);
        var current = SeedHash.Unit(seed, currentBestIdx, sourceIdx, 1931);
        if (MathF.Abs(candidate - current) <= 0.000001f)
            return candidateIdx < currentBestIdx;

        return candidate < current;
    }

    private static int IndexOf(int x, int y, int width)
        => y * width + x;
}

using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.World;

/// <summary>
/// A* pathfinder over the 3D WorldMap.
/// Handles horizontal movement and legal vertical movement through the world's traversal connectors.
/// Uses struct-based nodes to minimize GC allocations during pathfinding.
/// Thread-safe for reads; the WorldMap must not change during pathfinding.
/// </summary>
public static class Pathfinder
{
    /// <summary>
    /// Struct-based node to avoid heap allocations during pathfinding.
    /// </summary>
    private struct Node : IComparable<Node>
    {
        public Vec3i Pos;
        public float G;       // cost from start
        public float H;       // heuristic to goal
        public float F => G + H;
        public int ParentIndex; // index into the node pool, -1 if none

        public int CompareTo(Node other) => F.CompareTo(other.F);
    }

    /// <summary>
    /// Find the shortest walkable path from <paramref name="start"/> to <paramref name="goal"/>.
    /// Returns an empty list if no path exists.
    /// </summary>
    public static IReadOnlyList<Vec3i> FindPath(
        WorldMap map,
        Vec3i    start,
        Vec3i    goal,
        int      maxNodes = 10_000)
        => FindPath(map, start, goal, canSwim: false, requiresSwimming: false, isBlocked: null, maxNodes);

    /// <summary>
    /// Find the shortest walkable path from <paramref name="start"/> to <paramref name="goal"/>,
    /// while treating tiles that satisfy <paramref name="isBlocked"/> as temporarily unavailable.
    /// </summary>
    public static IReadOnlyList<Vec3i> FindPath(
        WorldMap           map,
        Vec3i              start,
        Vec3i              goal,
        Func<Vec3i, bool>? isBlocked,
        int                maxNodes = 10_000)
        => FindPath(map, start, goal, canSwim: false, requiresSwimming: false, isBlocked, maxNodes);

    /// <summary>
    /// Find the shortest traversable path from <paramref name="start"/> to <paramref name="goal"/>
    /// using traversal rules (walking/swimming) for the moving entity.
    /// </summary>
    public static IReadOnlyList<Vec3i> FindPath(
        WorldMap map,
        Vec3i    start,
        Vec3i    goal,
        bool     canSwim,
        bool     requiresSwimming,
        int      maxNodes = 10_000)
        => FindPath(map, start, goal, canSwim, requiresSwimming, isBlocked: null, maxNodes);

    /// <summary>
    /// Find the shortest traversable path from <paramref name="start"/> to <paramref name="goal"/>
    /// using traversal rules (walking/swimming) for the moving entity and an optional dynamic blocker.
    /// </summary>
    public static IReadOnlyList<Vec3i> FindPath(
        WorldMap           map,
        Vec3i              start,
        Vec3i              goal,
        bool               canSwim,
        bool               requiresSwimming,
        Func<Vec3i, bool>? isBlocked,
        int                maxNodes = 10_000)
    {
        if (isBlocked?.Invoke(goal) == true && goal != start) return Array.Empty<Vec3i>();
        if (!map.IsTraversable(goal, canSwim, requiresSwimming)) return Array.Empty<Vec3i>();
        if (start == goal) return new[] { start };

        // Use pooled arrays to minimize allocations
        // Pre-allocate node pool sized to maxNodes
        var nodePool = new Node[maxNodes];
        var nodeCount = 0;

        // Open set: indices into nodePool, managed as a simple priority list
        // Using a List with manual sorting is faster than SortedSet for small-medium sets
        var open = new List<int>(maxNodes);
        var closed = new HashSet<Vec3i>(maxNodes);
        var posToIndex = new Dictionary<Vec3i, int>(maxNodes);
        var neighbors = new List<Vec3i>(6);

        // Create start node
        var startIdx = nodeCount++;
        nodePool[startIdx] = new Node
        {
            Pos = start,
            G = 0,
            H = Heuristic(start, goal),
            ParentIndex = -1
        };
        open.Add(startIdx);
        posToIndex[start] = startIdx;

        int explored = 0;

        while (open.Count > 0 && explored < maxNodes)
        {
            // Find node with lowest F score
            var bestIdx = 0;
            var bestF = nodePool[open[0]].F;
            for (int i = 1; i < open.Count; i++)
            {
                var f = nodePool[open[i]].F;
                if (f < bestF)
                {
                    bestF = f;
                    bestIdx = i;
                }
            }

            var currentIdx = open[bestIdx];
            open.RemoveAt(bestIdx);
            var current = nodePool[currentIdx];
            explored++;

            if (current.Pos == goal)
                return ReconstructPath(nodePool, currentIdx);

            closed.Add(current.Pos);

            map.CollectTraversableNeighbors(current.Pos, canSwim, requiresSwimming, neighbors);
            foreach (var neighbourPos in neighbors)
            {
                if (isBlocked?.Invoke(neighbourPos) == true) continue;
                if (closed.Contains(neighbourPos)) continue;

                float g = current.G + MoveCost(current.Pos, neighbourPos);

                if (posToIndex.TryGetValue(neighbourPos, out var existingIdx))
                {
                    // Node already in open set — check if new path is better
                    if (g < nodePool[existingIdx].G)
                    {
                        nodePool[existingIdx].G = g;
                        nodePool[existingIdx].ParentIndex = currentIdx;
                        // No need to re-sort; F will be lower and we'll find it next iteration
                    }
                }
                else if (nodeCount < maxNodes)
                {
                    // Add new node
                    var newIdx = nodeCount++;
                    nodePool[newIdx] = new Node
                    {
                        Pos = neighbourPos,
                        G = g,
                        H = Heuristic(neighbourPos, goal),
                        ParentIndex = currentIdx
                    };
                    open.Add(newIdx);
                    posToIndex[neighbourPos] = newIdx;
                }
            }
        }

        return Array.Empty<Vec3i>(); // no path found
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static float Heuristic(Vec3i a, Vec3i b)
        // Manhattan distance; z-movement is more expensive because it requires an explicit vertical connector.
        => MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y) + MathF.Abs(a.Z - b.Z) * 2f;

    private static float MoveCost(Vec3i from, Vec3i to)
        => from.Z != to.Z ? 2.0f : 1.0f; // vertical traversal costs more

    private static IReadOnlyList<Vec3i> ReconstructPath(Node[] pool, int goalIdx)
    {
        // Count path length first
        int count = 0;
        for (int i = goalIdx; i >= 0; i = pool[i].ParentIndex)
            count++;

        var path = new Vec3i[count];
        int idx = count - 1;
        for (int i = goalIdx; i >= 0; i = pool[i].ParentIndex)
        {
            path[idx--] = pool[i].Pos;
        }
        return path;
    }
}

using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

/// <summary>
/// Shared presentation cache for smoothed entity positions and reusable
/// client-side render metadata used by the 3D world.
/// </summary>
public sealed class RenderCache
{
    public const int TileSize = 64;
    public const float RenderSmoothing = 14f;

    public Dictionary<int, Vector2> DwarfPositions { get; } = new();
    public Dictionary<int, Vector2> DwarfPreviousPositions { get; } = new();
    public Dictionary<int, Vector2> CreaturePositions { get; } = new();
    public Dictionary<int, Vector2> CreaturePreviousPositions { get; } = new();
    public Dictionary<int, Vector2> ItemPositions { get; } = new();
    public HashSet<int> AliveDwarfIds { get; } = new();
    public HashSet<int> AliveCreatureIds { get; } = new();
    public HashSet<int> AliveItemIds { get; } = new();
    public Dictionary<string, WaterEffectStyle> CreatureWaterEffectStyles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Clear()
    {
        DwarfPositions.Clear();
        DwarfPreviousPositions.Clear();
        CreaturePositions.Clear();
        CreaturePreviousPositions.Clear();
        ItemPositions.Clear();
        AliveDwarfIds.Clear();
        AliveCreatureIds.Clear();
        AliveItemIds.Clear();
        CreatureWaterEffectStyles.Clear();
    }

    public static Vector2 WorldToScreenCenter(Vec3i pos)
        => new(pos.X * TileSize + TileSize / 2f, pos.Y * TileSize + TileSize / 2f);

    public void UpdateEntityRenderPosition(
        int id,
        Vec3i pos,
        float delta,
        float yOffset,
        Dictionary<int, Vector2> cache,
        Dictionary<int, Vector2>? previousCache,
        HashSet<int> aliveIds)
    {
        aliveIds.Add(id);
        var target = WorldToScreenCenter(pos) + new Vector2(0f, yOffset);
        if (!cache.TryGetValue(id, out var current))
        {
            cache[id] = target;
            if (previousCache is not null)
                previousCache[id] = target;
            return;
        }

        if (previousCache is not null)
            previousCache[id] = current;

        cache[id] = current.Lerp(target, 1f - Mathf.Exp(-RenderSmoothing * delta));
    }

    public void RemoveStaleRenderPositions(
        Dictionary<int, Vector2> cache,
        Dictionary<int, Vector2>? previousCache,
        HashSet<int> aliveIds)
    {
        var staleIds = new List<int>();
        foreach (var id in cache.Keys)
        {
            if (!aliveIds.Contains(id))
                staleIds.Add(id);
        }

        foreach (var staleId in staleIds)
        {
            cache.Remove(staleId);
            previousCache?.Remove(staleId);
        }
    }

    public Vector2 GetSmoothedEntityCenter(Dictionary<int, Vector2> cache, int entityId, Vec3i pos, float yOffset)
    {
        if (cache.TryGetValue(entityId, out var drawPos))
            return drawPos;

        drawPos = WorldToScreenCenter(pos) + new Vector2(0f, yOffset);
        cache[entityId] = drawPos;
        return drawPos;
    }

    public static Vector2 ResolveEntityMotionVector(Dictionary<int, Vector2> previousCache, int entityId, Vector2 currentPos)
    {
        if (!previousCache.TryGetValue(entityId, out var previousPos))
            return Vector2.Zero;

        return currentPos - previousPos;
    }

    public readonly record struct WaterEffectStyle(
        float RippleScale,
        float BubbleScale,
        float WakeScale,
        float SubmergeScale,
        float MotionThreshold,
        bool SuppressBubbles,
        WaterWakePattern WakePattern);

    public enum WaterWakePattern
    {
        Default = 0,
        SwimV = 1,
    }
}

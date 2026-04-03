using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

public readonly record struct MovementPresentationSegment(
    Vec3i OldPos,
    Vec3i NewPos,
    float DurationSeconds,
    long Sequence);

/// <summary>
/// Publishes canonical movement presentation segments for client-side interpolation.
/// Segments are recorded from simulation movement events and can be overridden by
/// systems that know an exact duration for a completed step.
/// </summary>
public sealed class MovementPresentationSystem : IGameSystem
{
    private const float MinimumDurationSeconds = 0.05f;
    private const float MaximumDurationSeconds = 8f;
    private const float DefaultItemDurationSeconds = 0.12f;

    public string SystemId => SystemIds.MovementPresentationSystem;
    public int UpdateOrder => 10;
    public bool IsEnabled { get; set; } = true;

    private readonly Dictionary<int, MovementPresentationSegment> _entitySegments = new();
    private readonly Dictionary<int, MovementPresentationSegment> _itemSegments = new();

    private GameContext? _ctx;
    private long _nextSequence = 1;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        ctx.EventBus.On<EntityMovedEvent>(OnEntityMoved);
        ctx.EventBus.On<ItemMovedEvent>(OnItemMoved);
    }

    public void Tick(float delta) { }

    public void OnSave(SaveWriter w) { }

    public void OnLoad(SaveReader r)
    {
        _entitySegments.Clear();
        _itemSegments.Clear();
        _nextSequence = 1;
    }

    public bool TryGetEntitySegment(int entityId, out MovementPresentationSegment segment)
        => _entitySegments.TryGetValue(entityId, out segment);

    public bool TryGetItemSegment(int itemId, out MovementPresentationSegment segment)
        => _itemSegments.TryGetValue(itemId, out segment);

    public void RecordEntityMovement(int entityId, Vec3i oldPos, Vec3i newPos, float durationSeconds)
        => _entitySegments[entityId] = CreateSegment(oldPos, newPos, durationSeconds);

    public void RecordItemMovement(int itemId, Vec3i oldPos, Vec3i newPos, float durationSeconds)
        => _itemSegments[itemId] = CreateSegment(oldPos, newPos, durationSeconds);

    public void OverrideLatestEntityDuration(int entityId, float durationSeconds)
    {
        if (!_entitySegments.TryGetValue(entityId, out var segment))
            return;

        _entitySegments[entityId] = segment with
        {
            DurationSeconds = NormalizeDurationSeconds(segment.OldPos, segment.NewPos, durationSeconds),
        };
    }

    private void OnEntityMoved(EntityMovedEvent ev)
    {
        if (MatchesLatestSegment(_entitySegments, ev.EntityId, ev.OldPos, ev.NewPos))
            return;

        RecordEntityMovement(ev.EntityId, ev.OldPos, ev.NewPos, ResolveDefaultEntityDurationSeconds(ev.EntityId));
    }

    private void OnItemMoved(ItemMovedEvent ev)
    {
        if (MatchesLatestSegment(_itemSegments, ev.ItemId, ev.OldPos, ev.NewPos))
            return;

        RecordItemMovement(ev.ItemId, ev.OldPos, ev.NewPos, DefaultItemDurationSeconds);
    }

    private MovementPresentationSegment CreateSegment(Vec3i oldPos, Vec3i newPos, float durationSeconds)
        => new(oldPos, newPos, NormalizeDurationSeconds(oldPos, newPos, durationSeconds), _nextSequence++);

    private float ResolveDefaultEntityDurationSeconds(int entityId)
    {
        var entity = _ctx?.TryGet<EntityRegistry>()?.TryGetById(entityId);
        if (entity is null)
            return 0f;

        if (entity.Components.Has<StatComponent>())
        {
            var speed = Math.Max(0.2f, entity.Components.Get<StatComponent>().Speed.Value);
            return 1f / speed;
        }

        return 1f;
    }

    private static bool MatchesLatestSegment(
        IReadOnlyDictionary<int, MovementPresentationSegment> segments,
        int id,
        Vec3i oldPos,
        Vec3i newPos)
    {
        return segments.TryGetValue(id, out var segment)
            && segment.OldPos == oldPos
            && segment.NewPos == newPos;
    }

    private static float NormalizeDurationSeconds(Vec3i oldPos, Vec3i newPos, float durationSeconds)
    {
        if (oldPos == newPos)
            return 0f;

        if (oldPos.ManhattanDistanceTo(newPos) != 1)
            return 0f;

        return Math.Clamp(durationSeconds, MinimumDurationSeconds, MaximumDurationSeconds);
    }
}
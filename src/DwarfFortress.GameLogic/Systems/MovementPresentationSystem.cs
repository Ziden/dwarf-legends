using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

public enum MovementPresentationMotionKind : byte
{
    Linear = 0,
    Jump = 1,
}

public enum MovementPresentationAnchorKind : byte
{
    Tile = 0,
    Carrier = 1,
}

public readonly record struct MovementPresentationSegment(
    Vec3i OldPos,
    Vec3i NewPos,
    float DurationSeconds,
    long Sequence,
    MovementPresentationMotionKind MotionKind = MovementPresentationMotionKind.Linear,
    MovementPresentationAnchorKind StartAnchor = MovementPresentationAnchorKind.Tile,
    int StartAnchorEntityId = -1,
    MovementPresentationAnchorKind EndAnchor = MovementPresentationAnchorKind.Tile,
    int EndAnchorEntityId = -1,
    float ArcHeight = 0f);

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
    private const float DefaultItemCarryTransitionDurationSeconds = 0.32f;
    private const float DefaultItemCarryArcHeight = 0.34f;

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
        ctx.EventBus.On<ItemPickedUpEvent>(OnItemPickedUp);
        ctx.EventBus.On<ItemDroppedEvent>(OnItemDropped);
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

    public void RecordItemMovement(
        int itemId,
        Vec3i oldPos,
        Vec3i newPos,
        float durationSeconds,
        MovementPresentationMotionKind motionKind = MovementPresentationMotionKind.Linear,
        MovementPresentationAnchorKind startAnchor = MovementPresentationAnchorKind.Tile,
        int startAnchorEntityId = -1,
        MovementPresentationAnchorKind endAnchor = MovementPresentationAnchorKind.Tile,
        int endAnchorEntityId = -1,
        float arcHeight = 0f)
        => _itemSegments[itemId] = CreateSegment(
            oldPos,
            newPos,
            durationSeconds,
            motionKind,
            startAnchor,
            startAnchorEntityId,
            endAnchor,
            endAnchorEntityId,
            arcHeight);

    public void OverrideLatestEntityDuration(int entityId, float durationSeconds)
    {
        if (!_entitySegments.TryGetValue(entityId, out var segment))
            return;

        _entitySegments[entityId] = segment with
        {
            DurationSeconds = NormalizeDurationSeconds(segment.OldPos, segment.NewPos, durationSeconds, segment.MotionKind),
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

    private void OnItemPickedUp(ItemPickedUpEvent ev)
    {
        RecordItemMovement(
            ev.ItemId,
            ev.PreviousPosition,
            ev.Position,
            DefaultItemCarryTransitionDurationSeconds,
            MovementPresentationMotionKind.Jump,
            MovementPresentationAnchorKind.Tile,
            -1,
            MovementPresentationAnchorKind.Carrier,
            ev.CarrierEntityId,
            DefaultItemCarryArcHeight);
    }

    private void OnItemDropped(ItemDroppedEvent ev)
    {
        if (ev.CarrierEntityId < 0)
            return;

        RecordItemMovement(
            ev.ItemId,
            ev.PreviousPosition,
            ev.Position,
            DefaultItemCarryTransitionDurationSeconds,
            MovementPresentationMotionKind.Jump,
            MovementPresentationAnchorKind.Carrier,
            ev.CarrierEntityId,
            MovementPresentationAnchorKind.Tile,
            -1,
            DefaultItemCarryArcHeight);
    }

    private MovementPresentationSegment CreateSegment(
        Vec3i oldPos,
        Vec3i newPos,
        float durationSeconds,
        MovementPresentationMotionKind motionKind = MovementPresentationMotionKind.Linear,
        MovementPresentationAnchorKind startAnchor = MovementPresentationAnchorKind.Tile,
        int startAnchorEntityId = -1,
        MovementPresentationAnchorKind endAnchor = MovementPresentationAnchorKind.Tile,
        int endAnchorEntityId = -1,
        float arcHeight = 0f)
        => new(
            oldPos,
            newPos,
            NormalizeDurationSeconds(oldPos, newPos, durationSeconds, motionKind),
            _nextSequence++,
            motionKind,
            startAnchor,
            startAnchorEntityId,
            endAnchor,
            endAnchorEntityId,
            Math.Max(0f, arcHeight));

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

    private static float NormalizeDurationSeconds(Vec3i oldPos, Vec3i newPos, float durationSeconds, MovementPresentationMotionKind motionKind)
    {
        if (motionKind == MovementPresentationMotionKind.Linear)
        {
            if (oldPos == newPos)
                return 0f;

            if (oldPos.ManhattanDistanceTo(newPos) != 1)
                return 0f;
        }

        return Math.Clamp(durationSeconds, MinimumDurationSeconds, MaximumDurationSeconds);
    }
}

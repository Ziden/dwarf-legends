using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Godot;

public sealed class GameFeedbackController
{
    private const float FxLifetimeSeconds = 0.55f;

    private readonly Node _owner;
    private readonly List<WorldFx> _worldFx = new();
    private readonly Dictionary<int, ActivityPulse> _dwarfActivityPulses = new();
    private readonly Dictionary<int, ActivityPulse> _buildingActivityPulses = new();
    private readonly Dictionary<Vec3i, ActivityPulse> _tileActivityPulses = new();

    private WorldMap? _map;
    private WorldQuerySystem? _query;
    private bool _isBound;

    public GameFeedbackController(Node owner)
    {
        _owner = owner;
    }

    public void Bind(GameSimulation simulation, WorldMap map, WorldQuerySystem query)
    {
        _map = map;
        _query = query;

        if (_isBound)
            return;

        var bus = simulation.EventBus;
        bus.On<JobWorkStartedEvent>(StartWorkActivityAnimation);
        bus.On<JobWorkStoppedEvent>(StopWorkActivityAnimation);

        // Need critical events - show floating text like Dwarf Fortress
        bus.On<NeedCriticalEvent>(e =>
        {
            var registry = simulation.Context.Get<EntityRegistry>();
            if (registry.TryGetById<Dwarf>(e.EntityId, out var dwarf) && dwarf is not null)
            {
                var needName = HumanizeId(e.NeedId);
                var color = e.NeedId switch
                {
                    NeedIds.Hunger => new Color(1f, 0.55f, 0.25f, 1f),
                    NeedIds.Thirst => new Color(0.35f, 0.65f, 1f, 1f),
                    NeedIds.Sleep => new Color(0.75f, 0.55f, 1f, 1f),
                    _ => new Color(1f, 1f, 0.5f, 1f),
                };
                SpawnWorldFx($"{dwarf.FirstName}: {needName}!", dwarf.Position.Position, color, e.EntityId);
            }
        });

        bus.On<JobCompletedEvent>(e =>
        {
            switch (e.JobDefId)
            {
                case JobDefIds.MineTile:
                    TriggerDwarfActivityPulse(e.DwarfId, new Color(1f, 0.78f, 0.24f, 1f), 1.14f, -9f, 0.95f);
                    TriggerTileActivityPulse(e.TargetPos, new Color(1f, 0.72f, 0.18f, 1f), 1.22f, 0.9f);
                    break;

                case JobDefIds.CutTree:
                    TriggerDwarfActivityPulse(e.DwarfId, new Color(0.50f, 0.95f, 0.38f, 1f), 1.12f, -8f, 0.85f);
                    TriggerTileActivityPulse(e.TargetPos, new Color(0.38f, 0.92f, 0.30f, 1f), 1.18f, 0.8f);
                    break;

                case JobDefIds.HarvestPlant:
                    TriggerDwarfActivityPulse(e.DwarfId, new Color(0.55f, 1f, 0.40f, 1f), 1.16f, -10f, 1.0f);
                    TriggerTileBoingPulse(e.TargetPos, new Color(0.35f, 0.95f, 0.25f, 1f));
                    SpawnWorldFx("Harvested!", e.TargetPos, new Color(0.45f, 1f, 0.35f, 1f), e.DwarfId);
                    break;

                case JobDefIds.Eat:
                    TriggerDwarfActivityPulse(e.DwarfId, new Color(1f, 0.82f, 0.35f, 1f), 1.10f, -7f, 0.70f);
                    SpawnWorldFx("Nom!", e.TargetPos, new Color(1f, 0.78f, 0.30f, 1f), e.DwarfId);
                    break;

                case JobDefIds.Drink:
                    TriggerDwarfActivityPulse(e.DwarfId, new Color(0.40f, 0.75f, 1f, 1f), 1.08f, -6f, 0.65f);
                    SpawnWorldFx("Gulp", e.TargetPos, new Color(0.40f, 0.80f, 1f, 1f), e.DwarfId);
                    break;
            }
        });

        bus.On<RecipeCraftedEvent>(e =>
        {
            var workshop = _query?.GetBuildingView(e.WorkshopId);
            var pos = workshop?.Origin ?? Vec3i.Zero;
            TriggerDwarfActivityPulse(e.DwarfId, new Color(0.58f, 1f, 0.68f, 1f), 1.10f, -7f, 0.75f);
            TriggerBuildingActivityPulse(e.WorkshopId, new Color(0.55f, 1f, 0.70f, 1f), 1.07f, -5f, 0.70f);
            TriggerTileActivityPulse(pos, new Color(0.55f, 1f, 0.70f, 1f), 1.10f, 0.65f);
            SpawnWorldFx($"Crafted {HumanizeId(e.RecipeId)}", pos, new Color(0.45f, 1f, 0.55f, 1f), e.DwarfId);
        });

        bus.On<ItemPickedUpEvent>(e =>
            SpawnWorldFx($"Picked up {HumanizeId(e.ItemDefId)}", e.Position, new Color(1f, 0.9f, 0.45f, 1f)));

        bus.On<ItemDroppedEvent>(e =>
            SpawnWorldFx($"Dropped {HumanizeId(e.ItemDefId)}", e.Position, new Color(0.55f, 0.9f, 1f, 1f)));

        bus.On<EntityMovedEvent>(e =>
        {
            if (_map is null || e.OldPos.Z == e.NewPos.Z)
                return;

            var oldTile = _map.GetTile(e.OldPos);
            var newTile = _map.GetTile(e.NewPos);
            if (oldTile.TileDefId != TileDefIds.Staircase && newTile.TileDefId != TileDefIds.Staircase)
                return;

            var descending = e.NewPos.Z > e.OldPos.Z;
            SpawnWorldFx(descending ? "Into stairs" : "Out of stairs", e.OldPos,
                descending ? new Color(1f, 0.86f, 0.35f, 1f) : new Color(0.58f, 0.96f, 1f, 1f));
            SpawnWorldFx(descending ? "Descend" : "Ascend", e.NewPos,
                descending ? new Color(1f, 0.64f, 0.28f, 1f) : new Color(0.62f, 1f, 0.72f, 1f));
        });

        bus.On<EntityDiedEvent>(e =>
        {
            var color = e.IsDwarf ? Colors.Red : new Color(0.86f, 0.46f, 0.30f, 1f);
            SpawnWorldFx("Death", e.Position, color);
        });

        _isBound = true;
    }

    public void Update(float delta)
    {
        for (int index = _worldFx.Count - 1; index >= 0; index--)
        {
            var fx = _worldFx[index];
            fx.TimeLeft -= delta;
            if (fx.TimeLeft <= 0f)
            {
                _worldFx.RemoveAt(index);
                continue;
            }

            _worldFx[index] = fx;
        }
    }

    public void DrawWorldFx(
        CanvasItem canvas,
        int currentZ,
        Func<Vec3i, Vector2> worldToScreenCenter,
        Func<int, Vec3i, Vector2?>? resolveSmoothedEntityCenter = null)
    {
        foreach (var fx in _worldFx.Where(fx => fx.Position.Z == currentZ))
        {
            var progress = 1f - (fx.TimeLeft / fx.Duration);
            var alpha = Mathf.Clamp(fx.TimeLeft / fx.Duration, 0f, 1f);
            var baseCenter = fx.FollowEntityId >= 0 && resolveSmoothedEntityCenter is not null
                ? resolveSmoothedEntityCenter(fx.FollowEntityId, fx.Position) ?? worldToScreenCenter(fx.Position)
                : worldToScreenCenter(fx.Position);
            var center = baseCenter + new Vector2(0f, -progress * 24f);
            var ringColor = new Color(fx.Color.R, fx.Color.G, fx.Color.B, alpha * 0.4f);
            canvas.DrawArc(center, 16f + progress * 12f, 0f, Mathf.Tau, 24, ringColor, 2f);

            var font = ThemeDB.FallbackFont;
            var textColor = new Color(fx.Color.R, fx.Color.G, fx.Color.B, alpha);
            var shadowColor = new Color(0f, 0f, 0f, alpha * 0.55f);
            var textPos = center + new Vector2(-font.GetStringSize(fx.Text).X / 2f, -18f);
            canvas.DrawString(font, textPos + new Vector2(1f, 1f), fx.Text, fontSize: 14, modulate: shadowColor);
            canvas.DrawString(font, textPos, fx.Text, fontSize: 14, modulate: textColor);
        }
    }

    public void DrawTilePulse(CanvasItem canvas, Rect2 rect, Vec3i position)
    {
        if (!_tileActivityPulses.TryGetValue(position, out var pulse) || pulse.Flash <= 0.01f)
            return;

        var fillColor = pulse.WithAlpha(0.12f * pulse.Flash);
        var ringColor = pulse.WithAlpha(0.72f * pulse.Flash);
        var ringRect = ScaleRect(rect.Grow(-10f), pulse.Ring);
        canvas.DrawRect(rect, fillColor);
        canvas.DrawRect(ringRect, ringColor, false, 2.5f);
    }

    public Rect2 ApplyBuildingTransform(int buildingId, Rect2 rect)
    {
        if (!_buildingActivityPulses.TryGetValue(buildingId, out var pulse))
            return rect;

        return ScaleRect(rect, pulse.Scale, new Vector2(0f, pulse.Lift));
    }

    public void DrawBuildingPulse(CanvasItem canvas, int buildingId, Rect2 rect)
    {
        if (!_buildingActivityPulses.TryGetValue(buildingId, out var pulse) || pulse.Flash <= 0.01f)
            return;

        canvas.DrawRect(rect.Grow(4f + pulse.Ring * 8f), pulse.WithAlpha(0.16f * pulse.Flash), false, 3f);
    }

    public (Vector2 Center, Vector2 Size) ApplyDwarfTransform(int dwarfId, Vector2 center, Vector2 size)
    {
        if (!_dwarfActivityPulses.TryGetValue(dwarfId, out var pulse))
            return (center, size);

        return (center + new Vector2(0f, pulse.Lift), size * pulse.Scale);
    }

    public void DrawDwarfPulse(CanvasItem canvas, int dwarfId, Vector2 center)
    {
        if (!_dwarfActivityPulses.TryGetValue(dwarfId, out var pulse) || pulse.Flash <= 0.01f)
            return;

        canvas.DrawCircle(center, 11f + pulse.Ring * 8f, pulse.WithAlpha(0.18f * pulse.Flash));
    }

    private void SpawnWorldFx(string text, Vec3i position, Color color, int followEntityId = -1)
        => _worldFx.Add(new WorldFx(text, position, color, FxLifetimeSeconds, FxLifetimeSeconds, followEntityId));

    private void StartWorkActivityAnimation(JobWorkStartedEvent e)
    {
        switch (e.AnimationHint)
        {
            case "mining":
                StartLoopPulse(_dwarfActivityPulses, e.DwarfId, new Color(1f, 0.76f, 0.20f, 1f),
                    1.02f, 1.08f, -2f, -8f, 0.20f, 0.44f, 0.94f, 1.16f, 0.16f);
                StartLoopPulse(_tileActivityPulses, e.TargetPos, new Color(1f, 0.68f, 0.14f, 1f),
                    1f, 1f, 0f, 0f, 0.18f, 0.42f, 0.96f, 1.10f, 0.18f);
                break;

            case "wood_cutting":
                StartLoopPulse(_dwarfActivityPulses, e.DwarfId, new Color(0.42f, 0.92f, 0.26f, 1f),
                    1.01f, 1.12f, -1f, -11f, 0.16f, 0.34f, 0.92f, 1.20f, 0.14f);
                StartLoopPulse(_tileActivityPulses, e.TargetPos, new Color(0.34f, 0.88f, 0.24f, 1f),
                    1f, 1f, 0f, 0f, 0.12f, 0.26f, 0.96f, 1.18f, 0.14f);
                break;

            case "crafting":
                StartLoopPulse(_dwarfActivityPulses, e.DwarfId, new Color(0.62f, 1f, 0.72f, 1f),
                    1.00f, 1.05f, -1f, -6f, 0.14f, 0.28f, 0.98f, 1.08f, 0.24f);
                if (e.EntityId >= 0)
                    StartLoopPulse(_buildingActivityPulses, e.EntityId, new Color(0.50f, 1f, 0.66f, 1f),
                        1.00f, 1.04f, 0f, -4f, 0.10f, 0.22f, 0.98f, 1.08f, 0.26f);
                StartLoopPulse(_tileActivityPulses, e.TargetPos, new Color(0.48f, 0.96f, 0.64f, 1f),
                    1f, 1f, 0f, 0f, 0.10f, 0.22f, 0.98f, 1.08f, 0.26f);
                break;
        }
    }

    private void StopWorkActivityAnimation(JobWorkStoppedEvent e)
    {
        switch (e.AnimationHint)
        {
            case "mining":
            case "wood_cutting":
                StopLoopPulse(_dwarfActivityPulses, e.DwarfId);
                StopLoopPulse(_tileActivityPulses, e.TargetPos);
                break;

            case "crafting":
                StopLoopPulse(_dwarfActivityPulses, e.DwarfId);
                if (e.EntityId >= 0)
                    StopLoopPulse(_buildingActivityPulses, e.EntityId);
                StopLoopPulse(_tileActivityPulses, e.TargetPos);
                break;
        }
    }

    private void TriggerDwarfActivityPulse(int dwarfId, Color color, float peakScale, float peakLift, float peakFlash)
        => TriggerPulse(_dwarfActivityPulses, dwarfId, color, peakScale, peakLift, peakFlash, 1.28f);

    private void TriggerBuildingActivityPulse(int buildingId, Color color, float peakScale, float peakLift, float peakFlash)
        => TriggerPulse(_buildingActivityPulses, buildingId, color, peakScale, peakLift, peakFlash, 1.20f);

    private void TriggerTileActivityPulse(Vec3i position, Color color, float peakRing, float peakFlash)
        => TriggerPulse(_tileActivityPulses, position, color, 1f, 0f, peakFlash, peakRing);

    private void TriggerTileBoingPulse(Vec3i position, Color color)
    {
        if (_tileActivityPulses.TryGetValue(position, out var existing))
            existing.Tween?.Kill();

        var pulse = new ActivityPulse(color);
        _tileActivityPulses[position] = pulse;

        var tween = _owner.CreateTween();
        pulse.Tween = tween;

        // Boing: spring up, elastic settle
        tween.TweenMethod(Callable.From<float>(v => pulse.Ring = v), 0.7f, 1.6f, 0.07f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenMethod(Callable.From<float>(v => pulse.Flash = v), 0f, 1.0f, 0.07f);
        tween.TweenMethod(Callable.From<float>(v => pulse.Ring = v), 1.6f, 1.0f, 0.30f)
            .SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenMethod(Callable.From<float>(v => pulse.Flash = v), 1.0f, 0f, 0.30f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(() => _tileActivityPulses.Remove(position)));
    }

    private void StartLoopPulse<TKey>(Dictionary<TKey, ActivityPulse> store, TKey key, Color color,
        float lowScale, float highScale,
        float lowLift, float highLift,
        float lowFlash, float highFlash,
        float lowRing, float highRing,
        float halfCycleSeconds) where TKey : notnull
    {
        StopLoopPulse(store, key);

        var pulse = new ActivityPulse(color)
        {
            Scale = lowScale,
            Lift = lowLift,
            Flash = lowFlash,
            Ring = lowRing,
        };
        store[key] = pulse;

        var tween = _owner.CreateTween().SetLoops();
        pulse.Tween = tween;

        tween.TweenMethod(Callable.From<float>(value => pulse.Scale = value), lowScale, highScale, halfCycleSeconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenMethod(Callable.From<float>(value => pulse.Lift = value), lowLift, highLift, halfCycleSeconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenMethod(Callable.From<float>(value => pulse.Flash = value), lowFlash, highFlash, halfCycleSeconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenMethod(Callable.From<float>(value => pulse.Ring = value), lowRing, highRing, halfCycleSeconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        tween.TweenMethod(Callable.From<float>(value => pulse.Scale = value), highScale, lowScale, halfCycleSeconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenMethod(Callable.From<float>(value => pulse.Lift = value), highLift, lowLift, halfCycleSeconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenMethod(Callable.From<float>(value => pulse.Flash = value), highFlash, lowFlash, halfCycleSeconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenMethod(Callable.From<float>(value => pulse.Ring = value), highRing, lowRing, halfCycleSeconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    private static void StopLoopPulse<TKey>(Dictionary<TKey, ActivityPulse> store, TKey key) where TKey : notnull
    {
        if (!store.Remove(key, out var pulse))
            return;

        pulse.Tween?.Kill();
    }

    private void TriggerPulse<TKey>(Dictionary<TKey, ActivityPulse> store, TKey key, Color color,
        float peakScale, float peakLift, float peakFlash, float peakRing) where TKey : notnull
    {
        if (store.TryGetValue(key, out var existing))
            existing.Tween?.Kill();

        var pulse = new ActivityPulse(color);
        store[key] = pulse;

        var tween = _owner.CreateTween();
        pulse.Tween = tween;

        tween.TweenMethod(Callable.From<float>(value => pulse.Scale = value), 1f, peakScale, 0.08f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenMethod(Callable.From<float>(value => pulse.Lift = value), 0f, peakLift, 0.08f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenMethod(Callable.From<float>(value => pulse.Flash = value), 0.08f, peakFlash, 0.06f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenMethod(Callable.From<float>(value => pulse.Ring = value), 0.86f, peakRing, 0.14f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        tween.TweenMethod(Callable.From<float>(value => pulse.Scale = value), peakScale, 1f, 0.18f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenMethod(Callable.From<float>(value => pulse.Lift = value), peakLift, 0f, 0.18f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenMethod(Callable.From<float>(value => pulse.Flash = value), peakFlash, 0f, 0.20f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        tween.Parallel().TweenMethod(Callable.From<float>(value => pulse.Ring = value), peakRing, 1.34f, 0.20f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() => store.Remove(key)));
    }

    private static Rect2 ScaleRect(Rect2 rect, float scale, Vector2? offset = null)
    {
        var nextSize = rect.Size * scale;
        var nextPosition = rect.GetCenter() - nextSize / 2f + (offset ?? Vector2.Zero);
        return new Rect2(nextPosition, nextSize);
    }

    private static string HumanizeId(string id)
        => string.IsNullOrWhiteSpace(id) ? string.Empty : id.Replace('_', ' ');

    private sealed class WorldFx
    {
        public string Text { get; }
        public Vec3i Position { get; }
        public Color Color { get; }
        public float TimeLeft { get; set; }
        public float Duration { get; }
        public int FollowEntityId { get; }

        public WorldFx(string text, Vec3i position, Color color, float timeLeft, float duration, int followEntityId)
        {
            Text = text;
            Position = position;
            Color = color;
            TimeLeft = timeLeft;
            Duration = duration;
            FollowEntityId = followEntityId;
        }
    }

    private sealed class ActivityPulse
    {
        public float Scale { get; set; } = 1f;
        public float Lift { get; set; }
        public float Flash { get; set; }
        public float Ring { get; set; } = 1f;
        public Color Color { get; }
        public Tween? Tween { get; set; }

        public ActivityPulse(Color color)
        {
            Color = color;
        }

        public Color WithAlpha(float alpha)
            => new(Color.R, Color.G, Color.B, alpha);
    }
}

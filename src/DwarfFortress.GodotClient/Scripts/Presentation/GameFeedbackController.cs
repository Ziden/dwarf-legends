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

namespace DwarfFortress.GodotClient.Presentation;

public sealed class GameFeedbackController
{
    private const float FxLifetimeSeconds = 0.55f;
    private const float CombatCueLifetimeSeconds = 0.26f;
    private const int MaxAreaSelectionPulseTiles = 32;
    private static readonly Color SelectionPulseColor = new(0.35f, 0.70f, 1f, 1f);

    private readonly Node _owner;
    private readonly List<WorldFx> _worldFx = new();
    private readonly List<CombatCue> _combatCues = new();
    private readonly Dictionary<int, ActivityPulse> _dwarfActivityPulses = new();
    private readonly Dictionary<int, ActivityPulse> _buildingActivityPulses = new();
    private readonly Dictionary<Vec3i, ActivityPulse> _tileActivityPulses = new();
    private int _nextWorldFxId = 1;
    private int _nextCombatCueId = 1;

    private WorldMap? _map;
    private WorldQuerySystem? _query;
    private bool _isBound;

    public GameFeedbackController(Node owner)
    {
        _owner = owner;
    }

    public readonly record struct ActivityPulseView(Color Color, float Scale, float Lift, float Flash, float Ring)
    {
        public Color WithAlpha(float alpha)
            => new(Color.R, Color.G, Color.B, alpha);
    }

    public readonly record struct TilePulseView(Vec3i Position, ActivityPulseView Pulse);

    public readonly record struct BuildingPulseView(int BuildingId, ActivityPulseView Pulse);

    public readonly record struct WorldFxView(int Id, string Text, Vec3i Position, Color Color, float TimeLeft, float Duration, int FollowEntityId);

    public readonly record struct CombatCueView(int Id, Vec3i Position, Color Color, float TimeLeft, float Duration, int DirectionX, int DirectionY, bool DidHit);

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

        bus.On<CombatHitEvent>(e => TriggerCombatFeedback(simulation.Context.Get<EntityRegistry>(), e.AttackerId, e.DefenderId, didHit: true));
        bus.On<CombatMissEvent>(e => TriggerCombatFeedback(simulation.Context.Get<EntityRegistry>(), e.AttackerId, e.DefenderId, didHit: false));

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

        for (int index = _combatCues.Count - 1; index >= 0; index--)
        {
            var cue = _combatCues[index];
            cue.TimeLeft -= delta;
            if (cue.TimeLeft <= 0f)
            {
                _combatCues.RemoveAt(index);
                continue;
            }

            _combatCues[index] = cue;
        }
    }

    public IReadOnlyList<TilePulseView> GetTilePulseViews(int currentZ)
        => _tileActivityPulses
            .Where(entry => entry.Key.Z == currentZ && entry.Value.Flash > 0.01f)
            .Select(entry => new TilePulseView(entry.Key, ToPulseView(entry.Value)))
            .ToArray();

    public IReadOnlyList<BuildingPulseView> GetBuildingPulseViews()
        => _buildingActivityPulses
            .Where(entry => entry.Value.Flash > 0.01f)
            .Select(entry => new BuildingPulseView(entry.Key, ToPulseView(entry.Value)))
            .ToArray();

    public IReadOnlyList<WorldFxView> GetWorldFxViews(int currentZ)
        => _worldFx
            .Where(fx => fx.Position.Z == currentZ)
            .Select(fx => new WorldFxView(fx.Id, fx.Text, fx.Position, fx.Color, fx.TimeLeft, fx.Duration, fx.FollowEntityId))
            .ToArray();

    public IReadOnlyList<CombatCueView> GetCombatCueViews(int currentZ)
        => _combatCues
            .Where(cue => cue.Position.Z == currentZ)
            .Select(cue => new CombatCueView(cue.Id, cue.Position, cue.Color, cue.TimeLeft, cue.Duration, cue.DirectionX, cue.DirectionY, cue.DidHit))
            .ToArray();

    public bool TryGetDwarfPulseView(int dwarfId, out ActivityPulseView pulse)
        => TryGetPulseView(_dwarfActivityPulses, dwarfId, out pulse);

    public bool TryGetBuildingPulseView(int buildingId, out ActivityPulseView pulse)
        => TryGetPulseView(_buildingActivityPulses, buildingId, out pulse);

    public void TriggerTileSelectionPulse(Vec3i position)
        => TriggerTileBoingPulse(position, SelectionPulseColor);

    public void TriggerAreaSelectionPulse(Vec3i from, Vec3i to)
    {
        var minX = Math.Min(from.X, to.X);
        var maxX = Math.Max(from.X, to.X);
        var minY = Math.Min(from.Y, to.Y);
        var maxY = Math.Max(from.Y, to.Y);
        var z = from.Z;

        if (minX == maxX && minY == maxY)
        {
            TriggerTileSelectionPulse(new Vec3i(minX, minY, z));
            return;
        }

        var borderTiles = new List<Vec3i>();
        for (var x = minX; x <= maxX; x++)
        {
            borderTiles.Add(new Vec3i(x, minY, z));
            if (maxY != minY)
                borderTiles.Add(new Vec3i(x, maxY, z));
        }

        for (var y = minY + 1; y < maxY; y++)
        {
            borderTiles.Add(new Vec3i(minX, y, z));
            if (maxX != minX)
                borderTiles.Add(new Vec3i(maxX, y, z));
        }

        var stride = Math.Max(1, (int)Math.Ceiling(borderTiles.Count / (double)MaxAreaSelectionPulseTiles));
        for (var index = 0; index < borderTiles.Count; index += stride)
            TriggerTileActivityPulse(borderTiles[index], SelectionPulseColor, 1.16f, 0.72f);
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
        => _worldFx.Add(new WorldFx(_nextWorldFxId++, text, position, color, FxLifetimeSeconds, FxLifetimeSeconds, followEntityId));

    private static bool TryGetPulseView<TKey>(Dictionary<TKey, ActivityPulse> store, TKey key, out ActivityPulseView pulse) where TKey : notnull
    {
        if (store.TryGetValue(key, out var activityPulse) && activityPulse.Flash > 0.01f)
        {
            pulse = ToPulseView(activityPulse);
            return true;
        }

        pulse = default;
        return false;
    }

    private static ActivityPulseView ToPulseView(ActivityPulse pulse)
        => new(pulse.Color, pulse.Scale, pulse.Lift, pulse.Flash, pulse.Ring);

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

    private void TriggerCombatFeedback(EntityRegistry registry, int attackerId, int defenderId, bool didHit)
    {
        var attackerPos = TryGetEntityPosition(registry, attackerId);
        var defenderPos = TryGetEntityPosition(registry, defenderId);
        if (!attackerPos.HasValue && !defenderPos.HasValue)
            return;

        var attackColor = didHit
            ? new Color(1f, 0.78f, 0.22f, 1f)
            : new Color(0.78f, 0.90f, 1f, 1f);
        var impactColor = didHit
            ? new Color(1f, 0.30f, 0.20f, 1f)
            : new Color(0.88f, 0.96f, 1f, 1f);

        if (attackerPos.HasValue)
            TriggerTileActivityPulse(attackerPos.Value, attackColor, didHit ? 1.18f : 1.10f, didHit ? 0.82f : 0.65f);
        if (defenderPos.HasValue)
            TriggerTileActivityPulse(defenderPos.Value, impactColor, didHit ? 1.36f : 1.12f, didHit ? 1.05f : 0.72f);

        TriggerCombatDwarfPulse(registry, attackerId, attackColor, didHit ? 1.16f : 1.08f, didHit ? -10f : -7f, didHit ? 0.96f : 0.62f);
        TriggerCombatDwarfPulse(registry, defenderId, impactColor, didHit ? 1.20f : 1.08f, didHit ? -12f : -7f, didHit ? 1.04f : 0.64f);

        var fxPosition = defenderPos ?? attackerPos;
        if (!fxPosition.HasValue)
            return;

        SpawnCombatCue(attackerPos, defenderPos, impactColor, didHit);
        SpawnWorldFx(didHit ? "Clash!" : "Miss!", fxPosition.Value, impactColor, defenderId);
    }

    private void SpawnCombatCue(Vec3i? attackerPos, Vec3i? defenderPos, Color color, bool didHit)
    {
        var impactPosition = defenderPos ?? attackerPos;
        if (!impactPosition.HasValue)
            return;

        var directionX = 0;
        var directionY = 0;
        if (attackerPos.HasValue && defenderPos.HasValue && attackerPos.Value.Z == defenderPos.Value.Z && attackerPos.Value != defenderPos.Value)
        {
            directionX = Math.Sign(defenderPos.Value.X - attackerPos.Value.X);
            directionY = Math.Sign(defenderPos.Value.Y - attackerPos.Value.Y);
        }

        _combatCues.Add(new CombatCue(
            _nextCombatCueId++,
            impactPosition.Value,
            color,
            CombatCueLifetimeSeconds,
            CombatCueLifetimeSeconds,
            directionX,
            directionY,
            didHit));
    }

    private void TriggerCombatDwarfPulse(EntityRegistry registry, int entityId, Color color, float peakScale, float peakLift, float peakFlash)
    {
        if (!registry.TryGetById<Dwarf>(entityId, out var dwarf) || dwarf is null)
            return;

        TriggerDwarfActivityPulse(dwarf.Id, color, peakScale, peakLift, peakFlash);
    }

    private static Vec3i? TryGetEntityPosition(EntityRegistry registry, int entityId)
    {
        var entity = registry.TryGetById(entityId);
        if (entity is null)
            return null;

        var position = entity.Components.TryGet<PositionComponent>();
        return position?.Position;
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
        public int Id { get; }
        public string Text { get; }
        public Vec3i Position { get; }
        public Color Color { get; }
        public float TimeLeft { get; set; }
        public float Duration { get; }
        public int FollowEntityId { get; }

        public WorldFx(int id, string text, Vec3i position, Color color, float timeLeft, float duration, int followEntityId)
        {
            Id = id;
            Text = text;
            Position = position;
            Color = color;
            TimeLeft = timeLeft;
            Duration = duration;
            FollowEntityId = followEntityId;
        }
    }

    private sealed class CombatCue
    {
        public CombatCue(int id, Vec3i position, Color color, float timeLeft, float duration, int directionX, int directionY, bool didHit)
        {
            Id = id;
            Position = position;
            Color = color;
            TimeLeft = timeLeft;
            Duration = duration;
            DirectionX = directionX;
            DirectionY = directionY;
            DidHit = didHit;
        }

        public int Id { get; }
        public Vec3i Position { get; }
        public Color Color { get; }
        public float TimeLeft { get; set; }
        public float Duration { get; }
        public int DirectionX { get; }
        public int DirectionY { get; }
        public bool DidHit { get; }
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

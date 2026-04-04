using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Systems;

/// <summary>
/// Maintains ambient need balloons and short-lived mood balloons.
/// Persistent need balloons sit underneath transient mood/sleep emotes.
/// </summary>
public sealed class EmoteFeedbackSystem : IGameSystem
{
    private const float MoodPulseDurationSeconds = 2.4f;
    private const float MoodDeltaThreshold = 0.04f;
    private const float MoodDeltaForMaxIntensity = 0.35f;

    private readonly Dictionary<int, float> _lastObservedHappiness = new();
    private readonly List<int> _staleEntityIds = new();

    private GameContext? _ctx;

    public string SystemId => SystemIds.EmoteFeedbackSystem;
    public int UpdateOrder => 18;
    public bool IsEnabled { get; set; } = true;

    public void Initialize(GameContext ctx)
        => _ctx = ctx;

    public void Tick(float delta)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        var sleepSystem = _ctx.TryGet<SleepSystem>();

        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            var isSleeping = sleepSystem?.IsSleeping(dwarf.Id) == true;
            SyncNeedEmote(dwarf.Needs, dwarf.Emotes, isSleeping);
            SyncMoodEmote(dwarf, isSleeping);
        }

        foreach (var creature in registry.GetAlive<Creature>())
        {
            var isSleeping = sleepSystem?.IsCreatureSleeping(creature.Id) == true;
            SyncNeedEmote(creature.Needs, creature.Emotes, isSleeping);
        }

        RemoveStaleTrackedEntities(registry);
    }

    public void OnSave(SaveWriter w) { }

    public void OnLoad(SaveReader r)
    {
        _lastObservedHappiness.Clear();
        _staleEntityIds.Clear();
    }

    private void SyncMoodEmote(Dwarf dwarf, bool isSleeping)
    {
        var currentHappiness = dwarf.Mood.Happiness;
        if (!_lastObservedHappiness.TryGetValue(dwarf.Id, out var previousHappiness))
        {
            _lastObservedHappiness[dwarf.Id] = currentHappiness;
            return;
        }

        _lastObservedHappiness[dwarf.Id] = currentHappiness;
        if (isSleeping)
            return;

        var delta = currentHappiness - previousHappiness;
        if (Math.Abs(delta) < MoodDeltaThreshold)
            return;

        dwarf.Emotes.SetEmote(
            delta > 0f ? EmoteIds.MoodUp : EmoteIds.MoodDown,
            MoodPulseDurationSeconds,
            ResolveMoodIntensity(delta),
            EmoteVisualStyle.Balloon,
            EmoteCategory.Mood);
    }

    private static void SyncNeedEmote(NeedsComponent needs, EmoteComponent emotes, bool isSleeping)
    {
        if (isSleeping)
        {
            ClearNeedEmote(emotes);
            return;
        }

        if (!TryResolveNeedBalloon(needs, out var emoteId, out var intensity))
        {
            ClearNeedEmote(emotes);
            return;
        }

        emotes.SetPersistentEmote(
            emoteId,
            intensity,
            EmoteVisualStyle.Balloon,
            EmoteCategory.Need);
    }

    private static bool TryResolveNeedBalloon(NeedsComponent needs, out string emoteId, out float intensity)
    {
        emoteId = string.Empty;
        intensity = 0f;

        var hunger = needs.Hunger;
        var thirst = needs.Thirst;
        if (!hunger.IsCritical && !thirst.IsCritical)
            return false;

        var selectedNeed = thirst.IsCritical && (!hunger.IsCritical || thirst.Level <= hunger.Level)
            ? thirst
            : hunger;

        emoteId = ReferenceEquals(selectedNeed, thirst)
            ? EmoteIds.NeedWater
            : EmoteIds.NeedFood;
        intensity = ResolveNeedIntensity(selectedNeed);
        return true;
    }

    private static void ClearNeedEmote(EmoteComponent emotes)
    {
        if (!IsNeedEmoteId(emotes.PersistentEmote?.Id))
            return;

        emotes.ClearPersistentEmote();
    }

    private void RemoveStaleTrackedEntities(EntityRegistry registry)
    {
        _staleEntityIds.Clear();
        foreach (var entry in _lastObservedHappiness)
        {
            var entity = registry.TryGetById(entry.Key);
            if (entity is null || !entity.IsAlive)
                _staleEntityIds.Add(entry.Key);
        }

        foreach (var entityId in _staleEntityIds)
            _lastObservedHappiness.Remove(entityId);
    }

    private static float ResolveNeedIntensity(Need need)
    {
        var criticalThreshold = Math.Max(0.01f, need.CriticalThreshold);
        var severity = 1f - (need.Level / criticalThreshold);
        return Math.Clamp(severity, 0.35f, 1f);
    }

    private static float ResolveMoodIntensity(float delta)
    {
        var normalizedDelta = Math.Abs(delta) / MoodDeltaForMaxIntensity;
        return Math.Clamp(normalizedDelta, 0.3f, 1f);
    }

    private static bool IsNeedEmoteId(string? emoteId)
        => string.Equals(emoteId, EmoteIds.NeedFood, StringComparison.Ordinal)
           || string.Equals(emoteId, EmoteIds.NeedWater, StringComparison.Ordinal);
}
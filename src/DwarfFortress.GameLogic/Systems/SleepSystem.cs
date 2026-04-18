using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

/// <summary>Emitted when an entity starts sleeping.</summary>
public record struct EntitySleepEvent(int EntityId, Vec3i Position, bool InBed);

/// <summary>Emitted when an entity wakes up.</summary>
public record struct EntityWakeEvent(int EntityId, Vec3i Position);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages sleep behavior for dwarves and creatures.
/// Handles sleep location scoring, attribute effects, and emergent sleep patterns.
/// Order 5 — runs after NeedsSystem (order 4) and before AttributeEffectSystem (order 7).
/// </summary>
public sealed class SleepSystem : IGameSystem
{
    private const float CreatureSleepNetRecoveryPerSecond = 0.003f;
    private const float DwarfBedSleepNetRecoveryPerSecond = 1f / 240f;
    private const float DwarfGroundSleepNetRecoveryPerSecond = 1f / 320f;
    public string SystemId    => SystemIds.SleepSystem;
    public int    UpdateOrder => 5;
    public bool   IsEnabled   { get; set; } = true;

    private const float CreatureSleepCheckInterval = 10f;
    private const int   CreatureSleepSearchRadius  = 12;
    private const int   SameSpeciesClusterRadius   = 5;
    private const int   DifferentSpeciesAvoidRadius = 2;
    private const float LazyWakeUpDelay            = 3f;  // seconds
    private const float GluttonyMidnightThreshold  = 0.7f; // hunger level
    private const float EmoteDuration              = 5f;   // seconds

    private GameContext? _ctx;
    private float _elapsed;

    // Track dwarves currently sleeping (for emote management)
    private readonly HashSet<int> _sleepingDwarves = new();
    private readonly HashSet<int> _sleepingCreatures = new();
    // Track lazy dwarves in wake-up delay
    private readonly Dictionary<int, float> _lazyWakeUpTimers = new();

    public void Initialize(GameContext ctx) => _ctx = ctx;

    public void Tick(float delta)
    {
        _elapsed += delta;
        var registry = _ctx!.Get<EntityRegistry>();
        var jobSystem = _ctx.TryGet<JobSystem>();

        // Tick dwarf sleep behavior
        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            dwarf.Emotes.Tick(delta);
            TickDwarfSleep(dwarf, delta, jobSystem);
        }

        foreach (var creature in registry.GetAlive<Creature>())
        {
            creature.Emotes.Tick(delta);
            if (_sleepingCreatures.Contains(creature.Id))
                EnsureSleepEmote(creature.Emotes);
        }

        // Tick creature sleep behavior
        TickCreatureSleep(delta, registry);

        // Process lazy wake-up delays
        ProcessLazyWakeUp(delta);
    }

    public void OnSave(SaveWriter w)
    {
        w.Write("sleepingDwarves", _sleepingDwarves.ToList());
        w.Write("sleepingCreatures", _sleepingCreatures.ToList());
    }

    public void OnLoad(SaveReader r)
    {
        _sleepingDwarves.Clear();
        _sleepingCreatures.Clear();
        _lazyWakeUpTimers.Clear();

        var dwarves = r.TryRead<List<int>>("sleepingDwarves");
        if (dwarves is not null)
            _sleepingDwarves.UnionWith(dwarves);

        var creatures = r.TryRead<List<int>>("sleepingCreatures");
        if (creatures is not null)
            _sleepingCreatures.UnionWith(creatures);
    }

    // ── Dwarf Sleep ────────────────────────────────────────────────────────

    private void TickDwarfSleep(Dwarf dwarf, float delta, JobSystem? jobSystem)
    {
        var currentJob = jobSystem?.GetAssignedJob(dwarf.Id);
        var currentStep = currentJob is null ? null : jobSystem?.GetCurrentStep(currentJob.Id);
        var isSleeping = IsActivelySleeping(currentJob, currentStep, dwarf.Position.Position);

        if (isSleeping)
        {
            var inBed = IsNearBed(dwarf);
            if (!_sleepingDwarves.Contains(dwarf.Id))
            {
                _sleepingDwarves.Add(dwarf.Id);
                _ctx!.EventBus.Emit(new EntitySleepEvent(dwarf.Id, dwarf.Position.Position, inBed));
            }

            EnsureSleepEmote(dwarf.Emotes);

            var sleepNeed = dwarf.Needs.Get(NeedIds.Sleep);
            var dataManager = _ctx!.TryGet<DataManager>();
            var recoveryPerSecond = GetDwarfSleepGrossRecoveryPerSecond(dwarf, inBed, dataManager);
            sleepNeed.Satisfy(recoveryPerSecond * delta);

            // Check low stamina: may wake up hungry (appetite >= 4)
            var appetiteLevel = dwarf.Attributes.GetLevel(AttributeIds.Appetite);
            if (appetiteLevel >= 4)
            {
                var hungerLevel = dwarf.Needs.Get(NeedIds.Hunger).Level;
                if (hungerLevel >= GluttonyMidnightThreshold)
                {
                    // Wake up hungry - interrupt sleep
                    WakeUpDwarf(dwarf);
                    dwarf.Emotes.SetEmote(EmoteIds.Hungry, EmoteDuration);
                    dwarf.Thoughts.AddThought(new Thought(
                        "woke_up_hungry",
                        "Woke up hungry in the middle of the night!",
                        -3f,
                        duration: 120f));
                    _ctx!.EventBus.Emit(new EntityActivityEvent(
                        dwarf.Id,
                        $"{dwarf.FirstName} woke up hungry!",
                        dwarf.Position.Position));
                }
            }
        }
        else
        {
            if (_sleepingDwarves.Contains(dwarf.Id))
            {
                WakeUpDwarf(dwarf);
            }
        }
    }

    private void WakeUpDwarf(Dwarf dwarf)
    {
        _sleepingDwarves.Remove(dwarf.Id);
        ClearSleepEmoteIfActive(dwarf.Emotes);
        _ctx!.EventBus.Emit(new EntityWakeEvent(dwarf.Id, dwarf.Position.Position));

        // Check if slept in bed or on ground
        var sleptInBed = IsNearBed(dwarf);
        if (sleptInBed)
        {
            dwarf.Thoughts.AddThought(new Thought(
                "slept_in_bed",
                "Had a good night's rest in a comfortable bed.",
                3f,
                duration: 3600f)); // 1 hour
        }
        else
        {
            dwarf.Thoughts.AddThought(new Thought(
                "slept_on_ground",
                "Slept on the cold, hard ground.",
                -5f,
                duration: 7200f)); // 2 hours
        }

        // Low focus/stamina: add wake-up delay (focus <= 2 or stamina <= 2)
        var focusLevel = dwarf.Attributes.GetLevel(AttributeIds.Focus);
        var staminaLevel = dwarf.Attributes.GetLevel(AttributeIds.Stamina);
        if (focusLevel <= 2 || staminaLevel <= 2)
        {
            _lazyWakeUpTimers[dwarf.Id] = LazyWakeUpDelay;
            dwarf.Emotes.SetEmote(EmoteIds.Sad, EmoteDuration);
            dwarf.Thoughts.AddThought(new Thought(
                "feeling_groggy",
                "Feeling groggy after waking up...",
                -1f,
                duration: 1800f)); // 30 min
        }
    }

    private bool IsNearBed(Dwarf dwarf)
    {
        var buildingSystem = _ctx!.TryGet<BuildingSystem>();
        if (buildingSystem is null) return false;

        var dwarfPos = dwarf.Position.Position;
        return buildingSystem.GetAll()
            .Any(b => b.IsComplete &&
                      b.BuildingDefId == BuildingDefIds.Bed &&
                      b.Origin.ManhattanDistanceTo(dwarfPos) <= 2);
    }

    // ── Lazy Wake-Up Delay ─────────────────────────────────────────────────

    private void ProcessLazyWakeUp(float delta)
    {
        var expiredIds = new List<int>();

        foreach (var kvp in _lazyWakeUpTimers)
        {
            var dwarfId = kvp.Key;
            var remaining = kvp.Value - delta;

            if (remaining <= 0f)
            {
                expiredIds.Add(dwarfId);
            }
            else
            {
                _lazyWakeUpTimers[dwarfId] = remaining;
            }
        }

        foreach (var id in expiredIds)
            _lazyWakeUpTimers.Remove(id);
    }

    // ── Creature Sleep AI ──────────────────────────────────────────────────

    private void TickCreatureSleep(float delta, EntityRegistry registry)
    {
        if (_elapsed % CreatureSleepCheckInterval > delta)
            return;

        foreach (var creature in registry.GetAlive<Creature>())
        {
            var sleepNeed = creature.Needs.Get(NeedIds.Sleep);

            if (_sleepingCreatures.Contains(creature.Id))
            {
                var recoveryPerSecond = CreatureSleepNetRecoveryPerSecond + sleepNeed.BaseDecayPerTick;
                sleepNeed.Satisfy(recoveryPerSecond * CreatureSleepCheckInterval);

                if (sleepNeed.IsSatisfied)
                {
                    _sleepingCreatures.Remove(creature.Id);
                    ClearSleepEmoteIfActive(creature.Emotes);
                    _ctx!.EventBus.Emit(new EntityWakeEvent(creature.Id, creature.Position.Position));
                }

                continue;
            }

            if (sleepNeed.IsCritical)
            {
                var sleepTarget = FindCreatureSleepLocation(creature, registry);
                if (sleepTarget.HasValue && sleepTarget.Value != creature.Position.Position)
                {
                    var (_, requiresSwimming) = CreatureTraversalProfile.Resolve(creature, _ctx!);
                    if (requiresSwimming)
                    {
                        RelocateCreature(creature, sleepTarget.Value);
                    }
                    else if (TryMoveCreatureTowardSleepLocation(creature, sleepTarget.Value))
                    {
                        continue;
                    }
                }

                StartCreatureSleeping(creature);
            }
        }
    }

    private void StartCreatureSleeping(Creature creature)
    {
        if (!_sleepingCreatures.Add(creature.Id))
            return;

        EnsureSleepEmote(creature.Emotes);
        _ctx!.EventBus.Emit(new EntitySleepEvent(creature.Id, creature.Position.Position, false));
    }

    private bool TryMoveCreatureTowardSleepLocation(Creature creature, Vec3i target)
    {
        var origin = creature.Position.Position;
        if (origin == target)
            return false;

        var map = _ctx!.Get<WorldMap>();
        var (canSwim, requiresSwimming) = CreatureTraversalProfile.Resolve(creature, _ctx!);
        var spatial = _ctx.TryGet<SpatialIndexSystem>();
        var path = Pathfinder.FindPath(
            map,
            origin,
            target,
            canSwim,
            requiresSwimming,
            pos => IsOccupiedByOtherEntity(spatial, pos, creature.Id));
        if (path.Count <= 1)
            return false;

        var next = path[1];
        if (origin.ManhattanDistanceTo(next) != 1)
            return false;

        return EntityMovement.TryMove(_ctx!, creature, next);
    }

    private void RelocateCreature(Creature creature, Vec3i newPos)
    {
        var oldPos = creature.Position.Position;
        if (oldPos == newPos)
            return;

        EntityMovement.TryMove(_ctx!, creature, newPos);
    }

    private Vec3i? FindCreatureSleepLocation(Creature creature, EntityRegistry registry)
    {
        var map = _ctx!.Get<WorldMap>();
        var center = creature.Position.Position;
        var (canSwim, requiresSwimming) = CreatureTraversalProfile.Resolve(creature, _ctx!);

        if (requiresSwimming)
            return FindAquaticSleepLocation(center, map);

        var bestPos = center;
        var bestScore = ScoreCreatureSleepSpot(center, creature, registry, map);

        // Search nearby tiles for better spots
        for (int dx = -CreatureSleepSearchRadius; dx <= CreatureSleepSearchRadius; dx++)
        {
            for (int dy = -CreatureSleepSearchRadius; dy <= CreatureSleepSearchRadius; dy++)
            {
                var pos = new Vec3i(center.X + dx, center.Y + dy, center.Z);
                if (!map.IsInBounds(pos) || !map.IsTraversable(pos, canSwim, requiresSwimming))
                    continue;

                var score = ScoreCreatureSleepSpot(pos, creature, registry, map);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPos = pos;
                }
            }
        }

        // Only move if the new spot is significantly better
        return bestScore > ScoreCreatureSleepSpot(center, creature, registry, map) + 5
            ? bestPos
            : null;
    }

    private static Vec3i? FindAquaticSleepLocation(Vec3i center, WorldMap map)
    {
        if (map.IsSwimmable(center))
            return center;

        Vec3i? nearest = null;
        var nearestDistance = int.MaxValue;
        byte bestFluidLevel = 0;
        for (var dx = -CreatureSleepSearchRadius; dx <= CreatureSleepSearchRadius; dx++)
        for (var dy = -CreatureSleepSearchRadius; dy <= CreatureSleepSearchRadius; dy++)
        {
            var pos = new Vec3i(center.X + dx, center.Y + dy, center.Z);
            if (!map.IsInBounds(pos) || !map.IsSwimmable(pos))
                continue;

            var fluidLevel = map.GetTile(pos).FluidLevel;
            var distance = center.ManhattanDistanceTo(pos);
            if (fluidLevel < bestFluidLevel)
                continue;
            if (fluidLevel == bestFluidLevel && distance >= nearestDistance)
                continue;

            nearest = pos;
            bestFluidLevel = fluidLevel;
            nearestDistance = distance;
        }

        return nearest;
    }

    private int ScoreCreatureSleepSpot(Vec3i pos, Creature creature, EntityRegistry registry, WorldMap map)
    {
        var score = 0;

        // Prefer near trees/plants
        foreach (var neighbor in pos.Neighbours4())
        {
            if (map.IsInBounds(neighbor))
            {
                var tile = map.GetTile(neighbor);
                if (tile.TileDefId == TileDefIds.Tree)
                    score += 10;
                if (!string.IsNullOrEmpty(tile.PlantDefId))
                    score += 5;
            }
        }

        // Prefer near quiet workshops (no animals nearby)
        var buildingSystem = _ctx!.TryGet<BuildingSystem>();
        if (buildingSystem is not null)
        {
            foreach (var building in buildingSystem.GetAll())
            {
                if (building.Origin.Z == pos.Z &&
                    building.IsComplete &&
                    building.IsWorkshop &&
                    building.Origin.ManhattanDistanceTo(pos) <= 3)
                {
                    score += 3;
                }
            }
        }

        // Avoid different species nearby
        foreach (var otherCreature in registry.GetAlive<Creature>())
        {
            if (otherCreature.Id == creature.Id) continue;
            var otherPos = otherCreature.Position.Position;
            if (otherPos.Z != pos.Z) continue;

            var dist = otherPos.ManhattanDistanceTo(pos);
            if (otherCreature.DefId == creature.DefId)
            {
                // Same species: prefer clustering
                if (dist <= SameSpeciesClusterRadius)
                    score += 8;
            }
            else
            {
                // Different species: avoid
                if (dist <= DifferentSpeciesAvoidRadius)
                    score -= 15;
            }
        }

        // Avoid dwarves (different species)
        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            var dwarfPos = dwarf.Position.Position;
            if (dwarfPos.Z != pos.Z) continue;
            if (dwarfPos.ManhattanDistanceTo(pos) <= DifferentSpeciesAvoidRadius)
                score -= 10;
        }

        // Avoid water
        var posTile = map.GetTile(pos);
        if (posTile.FluidType == FluidType.Water || posTile.TileDefId == World.TileDefIds.Water)
            score -= 20;

        return score;
    }

    private static bool IsActivelySleeping(Job? currentJob, ActionStep? currentStep, Vec3i currentPosition)
    {
        if (!string.Equals(currentJob?.JobDefId, JobDefIds.Sleep, StringComparison.OrdinalIgnoreCase))
            return false;

        return currentStep is WorkAtStep work
            && (!work.RequiredPosition.HasValue || work.RequiredPosition.Value == currentPosition);
    }

    private static void EnsureSleepEmote(EmoteComponent emotes)
    {
        var currentEmote = emotes.CurrentEmote;
        if (!string.Equals(currentEmote?.Id, EmoteIds.Sleep, StringComparison.Ordinal) || currentEmote?.TimeLeft <= 1f)
            emotes.SetEmote(EmoteIds.Sleep, EmoteDuration);
    }

    private static void ClearSleepEmoteIfActive(EmoteComponent emotes)
    {
        if (string.Equals(emotes.CurrentEmote?.Id, EmoteIds.Sleep, StringComparison.Ordinal))
            emotes.ClearEmote();
    }

    private static bool IsOccupiedByOtherEntity(SpatialIndexSystem? spatial, Vec3i position, int entityId)
    {
        if (spatial is null)
            return false;

        foreach (var dwarfId in spatial.GetDwarvesAt(position))
            if (dwarfId != entityId)
                return true;

        foreach (var creatureId in spatial.GetCreaturesAt(position))
            if (creatureId != entityId)
                return true;

        return false;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the given dwarf is currently sleeping.
    /// </summary>
    public bool IsSleeping(int dwarfId) => _sleepingDwarves.Contains(dwarfId);

    /// <summary>
    /// Returns true if the given creature is currently sleeping.
    /// </summary>
    public bool IsCreatureSleeping(int creatureId) => _sleepingCreatures.Contains(creatureId);

    /// <summary>
    /// Returns the configured sleep decay multiplier for the dwarf's stamina level.
    /// </summary>
    public static float GetSleepDecayMultiplier(Dwarf dwarf, DataManager? dataManager = null)
        => AttributeEffectSystem.GetConfiguredMultiplier(
            dwarf,
            dataManager,
            AttributeIds.Stamina,
            "sleep_need_decay_multiplier",
            1.0f);

    /// <summary>
    /// Returns the configured sleep recovery multiplier for the dwarf's stamina level.
    /// </summary>
    public static float GetSleepRecoveryMultiplier(Dwarf dwarf, DataManager? dataManager = null)
        => AttributeEffectSystem.GetConfiguredMultiplier(
            dwarf,
            dataManager,
            AttributeIds.Stamina,
            "sleep_recovery_multiplier",
            1.0f);

    /// <summary>
    /// Returns the net sleep recovery rate per second for a sleeping dwarf after normal need decay.
    /// </summary>
    public static float GetDwarfSleepNetRecoveryPerSecond(Dwarf dwarf, bool inBed, DataManager? dataManager = null)
    {
        var baseRecovery = inBed
            ? DwarfBedSleepNetRecoveryPerSecond
            : DwarfGroundSleepNetRecoveryPerSecond;

        return baseRecovery * GetSleepRecoveryMultiplier(dwarf, dataManager);
    }

    /// <summary>
    /// Returns the gross sleep recovery rate per second applied during sleep ticks.
    /// Combined with NeedsSystem decay this yields the net recovery returned by GetDwarfSleepNetRecoveryPerSecond.
    /// </summary>
    public static float GetDwarfSleepGrossRecoveryPerSecond(Dwarf dwarf, bool inBed, DataManager? dataManager = null)
    {
        var sleepNeed = dwarf.Needs.Get(NeedIds.Sleep);
        var sleepDecayPerSecond = sleepNeed.BaseDecayPerTick * GetSleepDecayMultiplier(dwarf, dataManager);
        return GetDwarfSleepNetRecoveryPerSecond(dwarf, inBed, dataManager) + sleepDecayPerSecond;
    }
}

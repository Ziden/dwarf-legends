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
/// Handles sleep location scoring, trait effects, and emergent sleep patterns.
/// Order 5 — runs after NeedsSystem (order 4) and before TraitSystem (order 7).
/// </summary>
public sealed class SleepSystem : IGameSystem
{
    private const float CreatureSleepRecoveryPerSecond = 0.18f;
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

        // Tick dwarf sleep behavior
        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            dwarf.Emotes.Tick(delta);
            TickDwarfSleep(dwarf, delta);
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

    private void TickDwarfSleep(Dwarf dwarf, float delta)
    {
        var jobSystem = _ctx!.TryGet<JobSystem>();
        var currentJob = jobSystem?.GetAssignedJob(dwarf.Id);
        var isSleeping = currentJob?.JobDefId == JobDefIds.Sleep;

        if (isSleeping)
        {
            if (!_sleepingDwarves.Contains(dwarf.Id))
            {
                _sleepingDwarves.Add(dwarf.Id);
                dwarf.Emotes.SetEmote(EmoteIds.Sleep, EmoteDuration);
                _ctx!.EventBus.Emit(new EntitySleepEvent(dwarf.Id, dwarf.Position.Position, IsNearBed(dwarf)));
            }

            // Apply Energetic trait: sleep recovers faster
            if (dwarf.Traits.HasTrait(TraitIds.Energetic))
            {
                var sleepNeed = dwarf.Needs.Get(NeedIds.Sleep);
                // Extra recovery per tick for energetic dwarves
                sleepNeed.Satisfy(0.02f * delta);
            }

            // Check Gluttony midnight snack
            if (dwarf.Traits.HasTrait(TraitIds.Gluttony))
            {
                var hungerLevel = dwarf.Needs.Get(NeedIds.Hunger).Level;
                if (hungerLevel >= GluttonyMidnightThreshold)
                {
                    // Wake up hungry - interrupt sleep
                    WakeUpDwarf(dwarf, jobSystem!);
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
                WakeUpDwarf(dwarf, jobSystem!);
            }
        }
    }

    private void WakeUpDwarf(Dwarf dwarf, JobSystem jobSystem)
    {
        _sleepingDwarves.Remove(dwarf.Id);
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

        // Lazy trait: add wake-up delay
        if (dwarf.Traits.HasTrait(TraitIds.Lazy))
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
            .Any(b => b.BuildingDefId == BuildingDefIds.Bed &&
                      b.Origin.ManhattanDistanceTo(dwarfPos) <= 2);
    }

    // ── Lazy Wake-Up Delay ─────────────────────────────────────────────────

    private void ProcessLazyWakeUp(float delta)
    {
        var jobSystem = _ctx!.TryGet<JobSystem>();
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
            creature.Emotes.Tick(delta);
            var sleepNeed = creature.Needs.Get(NeedIds.Sleep);

            if (_sleepingCreatures.Contains(creature.Id))
            {
                sleepNeed.Satisfy(CreatureSleepRecoveryPerSecond * CreatureSleepCheckInterval);

                if (!sleepNeed.IsCritical)
                {
                    _sleepingCreatures.Remove(creature.Id);
                    _ctx!.EventBus.Emit(new EntityWakeEvent(creature.Id, creature.Position.Position));
                }

                continue;
            }

            if (sleepNeed.IsCritical)
            {
                // Find a safe sleep location
                var sleepTarget = FindCreatureSleepLocation(creature, registry);
                _sleepingCreatures.Add(creature.Id);
                creature.Emotes.SetEmote(EmoteIds.Sleep, EmoteDuration);

                if (sleepTarget.HasValue)
                {
                    var oldPos = creature.Position.Position;
                    var newPos = sleepTarget.Value;
                    if (oldPos != newPos)
                    {
                        creature.Position.Position = newPos;
                        _ctx!.TryGet<ItemSystem>()?.UpdateCarriedItemsPosition(creature.Id, newPos);
                        _ctx.EventBus.Emit(new EntityMovedEvent(creature.Id, oldPos, newPos));
                    }
                }

                _ctx!.EventBus.Emit(new EntitySleepEvent(creature.Id, creature.Position.Position, false));
            }
        }
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
        for (var dx = -2; dx <= 2; dx++)
        for (var dy = -2; dy <= 2; dy++)
        {
            var pos = new Vec3i(center.X + dx, center.Y + dy, center.Z);
            if (!map.IsInBounds(pos) || !map.IsSwimmable(pos))
                continue;

            var distance = center.ManhattanDistanceTo(pos);
            if (distance >= nearestDistance)
                continue;

            nearest = pos;
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
    /// Returns the sleep decay multiplier based on traits.
    /// Sleepy = 2.0x (need increases faster).
    /// </summary>
    public static float GetSleepDecayMultiplier(Dwarf dwarf)
    {
        return dwarf.Traits.HasTrait(TraitIds.Sleepy) ? 2.0f : 1.0f;
    }

    /// <summary>
    /// Returns the sleep recovery multiplier based on traits.
    /// Energetic = 2.0x (sleep recovers faster).
    /// </summary>
    public static float GetSleepRecoveryMultiplier(Dwarf dwarf)
    {
        return dwarf.Traits.HasTrait(TraitIds.Energetic) ? 2.0f : 1.0f;
    }
}
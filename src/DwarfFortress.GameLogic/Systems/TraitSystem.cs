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

/// <summary>Emitted when a dwarf is assigned traits at spawn or migration.</summary>
public record struct TraitAssignedEvent(int DwarfId, string[] TraitIds);

/// <summary>Emitted when a dwarf refuses a job due to a trait (e.g. Fears Water).</summary>
public record struct JobRefusedEvent(int DwarfId, int JobId, string TraitId, string Reason);

/// <summary>Emitted when a fearful dwarf flees from an animal.</summary>
public record struct DwarfFledFromAnimalEvent(int DwarfId, Vec3i From, Vec3i To, int AnimalEntityId);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Applies trait effects to dwarves each tick.
/// Order 7 — runs after NutritionSystem (order 6) and NeedsSystem (order 4).
/// </summary>
public sealed class TraitSystem : IGameSystem
{
    public string SystemId    => SystemIds.TraitSystem;
    public int    UpdateOrder => 7;
    public bool   IsEnabled   { get; set; } = true;

    // Fearful trait: how often to check for nearby animals and flee radius
    private const float FearfulCheckInterval = 5f;
    private const int   FearfulAnimalRadius  = 4;
    private const float FearfulFleeChance    = 0.40f; // 40% chance to flee
    private const int   FearfulFleeDistance  = 5;

    // Shared random instance — avoids per-dwarf allocations and seed collisions
    private readonly Random _rng = new();

    private GameContext? _ctx;
    private float _elapsed;

    public void Initialize(GameContext ctx) => _ctx = ctx;

    public void Tick(float delta)
    {
        _elapsed += delta;
        var registry = _ctx!.Get<EntityRegistry>();
        var jobSystem = _ctx!.TryGet<Jobs.JobSystem>();

        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            var traits = dwarf.Traits;
            if (traits.Count == 0) continue;

            // Runner: permanent speed bonus
            if (traits.HasTrait(TraitIds.Runner))
                ApplyRunnerSpeed(dwarf);

            // Lazy/Motivated: dynamic speed modifiers based on job preferences
            ApplyMotivationSpeed(dwarf, delta);

            // Fearful: check for nearby animals periodically
            if (traits.HasTrait(TraitIds.Fearful))
                CheckFearfulFlee(dwarf, delta);

            // Body fat decay from working
            ApplyWorkFatLoss(dwarf, delta, jobSystem);
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    // ── Runner: +50% speed ─────────────────────────────────────────────────

    private void ApplyRunnerSpeed(Dwarf dwarf)
    {
        var speedStat = dwarf.Stats.Speed;
        if (!speedStat.Modifiers.Has("trait:runner"))
        {
            speedStat.Modifiers.Add(new Modifier(
                SourceId: "trait:runner",
                Type: ModType.PercentAdd,
                Value: 0.5f,
                Duration: -1f)); // permanent
        }
    }

    // ── Lazy / Motivated: speed modifiers based on job type ────────────────

    private void ApplyMotivationSpeed(Dwarf dwarf, float delta)
    {
        var jobSystem = _ctx!.TryGet<Jobs.JobSystem>();
        if (jobSystem is null) return;

        var currentJob = jobSystem.GetAssignedJob(dwarf.Id);
        if (currentJob is null)
        {
            // Remove any motivation modifiers when idle
            dwarf.Stats.Speed.Modifiers.Remove("trait:motivated");
            dwarf.Stats.Speed.Modifiers.Remove("trait:lazy");
            return;
        }

        var jobDefId = currentJob.JobDefId;
        var hasMotivated = dwarf.Traits.HasTrait(TraitIds.Motivated);
        var hasLazy = dwarf.Traits.HasTrait(TraitIds.Lazy);

        if (!hasMotivated && !hasLazy) return;

        // Determine if this is a "liked" or "disliked" task based on preferences
        var isLiked = IsJobLiked(dwarf, jobDefId);
        var isDisliked = IsJobDisliked(dwarf, jobDefId);

        // Motivated: +30% speed on liked tasks
        if (hasMotivated && isLiked)
        {
            if (!dwarf.Stats.Speed.Modifiers.Has("trait:motivated"))
            {
                dwarf.Stats.Speed.Modifiers.Add(new Modifier(
                    SourceId: "trait:motivated",
                    Type: ModType.PercentAdd,
                    Value: 0.3f,
                    Duration: -1f));
            }
        }
        else
        {
            dwarf.Stats.Speed.Modifiers.Remove("trait:motivated");
        }

        // Lazy: -30% speed on disliked tasks
        if (hasLazy && isDisliked)
        {
            if (!dwarf.Stats.Speed.Modifiers.Has("trait:lazy"))
            {
                dwarf.Stats.Speed.Modifiers.Add(new Modifier(
                    SourceId: "trait:lazy",
                    Type: ModType.PercentAdd,
                    Value: -0.3f,
                    Duration: -1f));
            }
        }
        else
        {
            dwarf.Stats.Speed.Modifiers.Remove("trait:lazy");
        }
    }

    private static bool IsJobLiked(Dwarf dwarf, string jobDefId)
    {
        // Map job types to preference tags
        // Mining → stone, WoodCutting → wood/plant, Farming → plant/food
        var prefs = dwarf.Preferences;
        if (prefs.LikedFoodId is null) return false;

        // For now, "liked" means the dwarf's liked food category matches the job's output
        // This will expand when we add labor preferences
        return jobDefId switch
        {
            JobDefIds.HarvestPlant => prefs.LikedFoodId.Contains("plant") || prefs.LikedFoodId.Contains("fruit"),
            JobDefIds.CutTree => prefs.LikedFoodId.Contains("wood") || prefs.LikedFoodId.Contains("nut"),
            _ => false,
        };
    }

    private static bool IsJobDisliked(Dwarf dwarf, string jobDefId)
    {
        var prefs = dwarf.Preferences;
        if (prefs.DislikedFoodId is null) return false;

        return jobDefId switch
        {
            JobDefIds.HarvestPlant => prefs.DislikedFoodId.Contains("plant") || prefs.DislikedFoodId.Contains("fruit"),
            JobDefIds.CutTree => prefs.DislikedFoodId.Contains("wood") || prefs.DislikedFoodId.Contains("nut"),
            _ => false,
        };
    }

    // ── Fearful: flee from animals within radius ───────────────────────────

    private void CheckFearfulFlee(Dwarf dwarf, float delta)
    {
        if (_elapsed % FearfulCheckInterval > delta) return;

        var registry = _ctx!.Get<EntityRegistry>();
        var map = _ctx!.Get<WorldMap>();
        var dwarfPos = dwarf.Position.Position;

        // Find nearest animal within radius
        Creature? nearestAnimal = null;
        float nearestDist = float.MaxValue;

        foreach (var creature in registry.GetAlive<Creature>())
        {
            var creaturePos = creature.Position.Position;
            if (creaturePos.Z != dwarfPos.Z) continue;

            var dist = MathF.Abs(creaturePos.X - dwarfPos.X) + MathF.Abs(creaturePos.Y - dwarfPos.Y);
            if (dist <= FearfulAnimalRadius && dist < nearestDist)
            {
                nearestDist = dist;
                nearestAnimal = creature;
            }
        }

        if (nearestAnimal is null) return;

        // Chance to flee when an animal is nearby
        if (_rng.NextDouble() > FearfulFleeChance) return;

        // Calculate flee direction (away from animal)
        var animalPos = nearestAnimal.Position.Position;
        var fleeDx = Math.Sign(dwarfPos.X - animalPos.X);
        var fleeDy = Math.Sign(dwarfPos.Y - animalPos.Y);
        var fleeTarget = new Vec3i(
            dwarfPos.X + fleeDx * FearfulFleeDistance,
            dwarfPos.Y + fleeDy * FearfulFleeDistance,
            dwarfPos.Z);

        // Clamp to map bounds
        fleeTarget = new Vec3i(
            Math.Clamp(fleeTarget.X, 0, map.Width - 1),
            Math.Clamp(fleeTarget.Y, 0, map.Height - 1),
            dwarfPos.Z);

        // Find a walkable position near the flee target
        var path = Pathfinder.FindPath(map, dwarfPos, fleeTarget);
        if (path.Count > 1)
        {
            var nextStep = path[1];
            var spatial = _ctx!.TryGet<SpatialIndexSystem>();
            if (spatial is not null && IsOccupiedByOtherEntity(spatial, nextStep, dwarf.Id))
                return;

            var oldPos = dwarfPos;
            dwarf.Position.Position = nextStep;
            _ctx!.TryGet<ItemSystem>()?.UpdateCarriedItemsPosition(dwarf.Id, dwarf.Position.Position);
            _ctx.EventBus.Emit(new DwarfFledFromAnimalEvent(dwarf.Id, oldPos, dwarf.Position.Position, nearestAnimal.Id));
        }
    }

    private static bool IsOccupiedByOtherEntity(SpatialIndexSystem spatial, Vec3i pos, int entityId)
    {
        foreach (var id in spatial.GetDwarvesAt(pos))
            if (id != entityId)
                return true;

        foreach (var id in spatial.GetCreaturesAt(pos))
            if (id != entityId)
                return true;

        return false;
    }

    // ── Public API: Get hunger satisfaction multiplier for a dwarf ─────────

    /// <summary>
    /// Returns the hunger satisfaction multiplier based on traits.
    /// Gluttony = 0.5x (needs twice as much food), Fit = 2.0x (needs half as much).
    /// </summary>
    public static float GetHungerSatisfactionMultiplier(Dwarf dwarf)
    {
        var hasGluttony = dwarf.Traits.HasTrait(TraitIds.Gluttony);
        var hasFit = dwarf.Traits.HasTrait(TraitIds.Fit);

        if (hasGluttony && hasFit) return 1.0f;  // cancel out
        if (hasGluttony) return 0.5f;
        if (hasFit) return 2.0f;
        return 1.0f;
    }

    /// <summary>
    /// Returns the nutrient credit multiplier based on traits.
    /// Fit = 2.0x nutrient absorption.
    /// </summary>
    public static float GetNutrientCreditMultiplier(Dwarf dwarf)
    {
        return dwarf.Traits.HasTrait(TraitIds.Fit) ? 2.0f : 1.0f;
    }

    /// <summary>
    /// Returns true if the dwarf has the Fears Water trait.
    /// </summary>
    public static bool FearsWater(Dwarf dwarf)
    {
        return dwarf.Traits.HasTrait(TraitIds.FearsWater);
    }

    // ── Work-based fat loss ────────────────────────────────────────────────

    private void ApplyWorkFatLoss(Dwarf dwarf, float delta, Jobs.JobSystem? jobSystem)
    {
        // Check if dwarf is currently working
        var isWorking = jobSystem?.GetAssignedJob(dwarf.Id) is not null;
        if (!isWorking) return;

        // Base fat loss from working
        var fatLoss = BodyFatComponent.FatLossPerWorkSecond * delta;

        // Strong dwarves burn more fat working (more muscle mass)
        if (dwarf.Traits.HasTrait(TraitIds.Strong))
            fatLoss *= 1.25f;

        // Lazy dwarves burn less fat (they slack off)
        if (dwarf.Traits.HasTrait(TraitIds.Lazy))
            fatLoss *= 0.75f;

        dwarf.BodyFat.LoseFat(fatLoss);
    }
}

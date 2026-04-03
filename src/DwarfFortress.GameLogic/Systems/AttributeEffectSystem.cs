using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

// ─────────────────────────────────────────────────────────────────────────────

public readonly record struct DwarfFledFromAnimalEvent(int DwarfId, int AnimalId, Vec3i From, Vec3i To);

/// <summary>
/// Applies dwarf attribute effects each tick.
/// Attributes are 1-5 scale where 3 is default/neutral.
/// Order 7 — runs after NutritionSystem (order 6) and NeedsSystem (order 4).
/// </summary>
public sealed class AttributeEffectSystem : IGameSystem
{
    public string SystemId    => "attribute_effects";
    public int    UpdateOrder => 7;
    public bool   IsEnabled   { get; set; } = true;

    // Flee behavior: how often to check for nearby animals
    private const float FleeCheckInterval = 5f;

    // Shared random instance — avoids per-dwarf allocations
    private readonly Random _rng = new();

    private GameContext? _ctx;
    private float _elapsed;
    private DataManager? _dataManager;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        _dataManager = ctx.TryGet<DataManager>();
    }

    public void Tick(float delta)
    {
        if (_dataManager is null) return;
        _elapsed += delta;
        var registry = _ctx!.Get<EntityRegistry>();
        var jobSystem = _ctx!.TryGet<Jobs.JobSystem>();
        var attrDefs = _dataManager.Attributes;

        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            var attrs = dwarf.Attributes;

            // Stamina → work speed, sleep decay, fat burn
            ApplyStaminaEffects(dwarf, attrs, attrDefs, delta, jobSystem);

            // Courage → flee behavior
            ApplyCourageEffects(dwarf, attrs, attrDefs, delta);

            // Focus → work speed modifier
            ApplyFocusSpeed(dwarf, attrs, attrDefs, jobSystem);
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    // ── Stamina: work speed, sleep decay, fat burn ──────────────────────────

    private void ApplyStaminaEffects(Dwarf dwarf, DwarfAttributeComponent attrs,
        Registry<DwarfAttributeDef> attrDefs, float delta, Jobs.JobSystem? jobSystem)
    {
        var staminaLevel = attrs.GetLevel(AttributeIds.Stamina);
        var def = attrDefs.GetOrNull(AttributeIds.Stamina);
        if (def is null) return;

        var curve = def.EffectCurves.GetValueOrDefault(staminaLevel.ToString());
        if (curve is null) return;

        var effects = curve.Effects;

        // Sleep need decay multiplier
        if (effects.TryGetValue("sleep_need_decay_multiplier", out var sleepMult))
            dwarf.Needs.Sleep.DecayMultiplier = sleepMult;

        // Work speed modifier → apply to Speed stat
        if (effects.TryGetValue("work_speed_multiplier", out var workSpeedMult))
        {
            var speedStat = dwarf.Stats.Speed;
            var sourceId = "attr:stamina_workspeed";
            var currentValue = workSpeedMult - 1f;

            if (Math.Abs(currentValue) < 0.001f)
            {
                speedStat.Modifiers.Remove(sourceId);
            }
            else if (!speedStat.Modifiers.Has(sourceId))
            {
                speedStat.Modifiers.Add(new Modifier(sourceId, ModType.PercentAdd, currentValue, -1f));
            }
            else
            {
                speedStat.Modifiers.UpdateValue(sourceId, currentValue);
            }
        }

        // Fat burn rate from working
        if (effects.TryGetValue("fat_gain_multiplier", out var fatMult) && jobSystem?.GetAssignedJob(dwarf.Id) is not null)
        {
            var fatLoss = BodyFatComponent.FatLossPerWorkSecond * delta;
            // fat_gain_multiplier: < 1 means less fat gain (burn more), > 1 means more fat gain
            fatLoss *= (2f - fatMult); // invert: 0.5 → 1.5x burn, 1.5 → 0.5x burn
            dwarf.BodyFat.LoseFat(fatLoss);
        }
    }

    // ── Courage: flee behavior ─────────────────────────────────────────────

    private void ApplyCourageEffects(Dwarf dwarf, DwarfAttributeComponent attrs,
        Registry<DwarfAttributeDef> attrDefs, float delta)
    {
        var courageLevel = attrs.GetLevel(AttributeIds.Courage);

        if (courageLevel >= 4) return; // brave dwarves don't flee

        var def = attrDefs.GetOrNull(AttributeIds.Courage);
        var curve = def?.EffectCurves.GetValueOrDefault(courageLevel.ToString());
        var effects = curve?.Effects;

        if (effects is null || !effects.TryGetValue("flee_chance", out var fleeChance) || fleeChance <= 0f)
            return;

        var fleeRadius = effects.TryGetValue("animal_flee_radius", out var configuredFleeRadius)
            ? configuredFleeRadius
            : 4f;

        // Check periodically
        if (_elapsed % FleeCheckInterval > delta) return;

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
            if (dist <= fleeRadius && dist < nearestDist)
            {
                nearestDist = dist;
                nearestAnimal = creature;
            }
        }

        if (nearestAnimal is null) return;

        // Chance to flee based on courage level
        if (_rng.NextDouble() > fleeChance) return;

        // Calculate flee direction (away from animal)
        var animalPos = nearestAnimal.Position.Position;
        var fleeDx = Math.Sign(dwarfPos.X - animalPos.X);
        var fleeDy = Math.Sign(dwarfPos.Y - animalPos.Y);
        var fleeDistance = courageLevel <= 1 ? 8 : courageLevel <= 2 ? 5 : 3;
        var fleeTarget = new Vec3i(
            dwarfPos.X + fleeDx * fleeDistance,
            dwarfPos.Y + fleeDy * fleeDistance,
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
            EntityMovement.TryMove(_ctx!, dwarf, nextStep);
            _ctx.EventBus.Emit(new DwarfFledFromAnimalEvent(dwarf.Id, nearestAnimal.Id, oldPos, nextStep));
        }
    }

    // ── Focus: work speed ──────────────────────────────────────────────────

    private void ApplyFocusSpeed(Dwarf dwarf, DwarfAttributeComponent attrs,
        Registry<DwarfAttributeDef> attrDefs, Jobs.JobSystem? jobSystem)
    {
        var focusLevel = attrs.GetLevel(AttributeIds.Focus);
        var def = attrDefs.GetOrNull(AttributeIds.Focus);
        if (def is null) return;

        var curve = def.EffectCurves.GetValueOrDefault(focusLevel.ToString());
        if (curve is null) return;

        var effects = curve.Effects;
        if (!effects.TryGetValue("work_speed_multiplier", out var workSpeedMult))
            return;

        var speedStat = dwarf.Stats.Speed;
        var sourceId = "attr:focus_workspeed";
        var currentValue = workSpeedMult - 1f;

        if (Math.Abs(currentValue) < 0.001f)
        {
            speedStat.Modifiers.Remove(sourceId);
        }
        else if (!speedStat.Modifiers.Has(sourceId))
        {
            speedStat.Modifiers.Add(new Modifier(sourceId, ModType.PercentAdd, currentValue, -1f));
        }
        else
        {
            speedStat.Modifiers.UpdateValue(sourceId, currentValue);
        }
    }

    // ── Helper ─────────────────────────────────────────────────────────────

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

    // ── Public API for other systems ───────────────────────────────────────

    /// <summary>
    /// Returns the hunger satisfaction multiplier configured for the dwarf's appetite level.
    /// </summary>
    public static float GetHungerSatisfactionMultiplier(Dwarf dwarf, DataManager? dataManager = null)
        => GetConfiguredMultiplier(dwarf, dataManager, AttributeIds.Appetite, "hunger_satisfaction_multiplier", 1.0f);

    /// <summary>
    /// Returns the nutrient credit multiplier configured for the dwarf's appetite level.
    /// </summary>
    public static float GetNutrientCreditMultiplier(Dwarf dwarf, DataManager? dataManager = null)
        => GetConfiguredMultiplier(dwarf, dataManager, AttributeIds.Appetite, "nutrient_credit_multiplier", 1.0f);

    public static float GetConfiguredEffectValue(
        Dwarf dwarf,
        DataManager? dataManager,
        string attributeId,
        string effectId,
        float defaultValue = 0f)
    {
        ArgumentNullException.ThrowIfNull(dwarf);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);

        var def = dataManager?.Attributes.GetOrNull(attributeId);
        if (def is null)
            return defaultValue;

        var levelKey = dwarf.Attributes.GetLevel(attributeId).ToString();
        if (!def.EffectCurves.TryGetValue(levelKey, out var curve) || curve?.Effects is null)
            return defaultValue;

        return curve.Effects.TryGetValue(effectId, out var value)
            ? value
            : defaultValue;
    }

    public static float GetConfiguredMultiplier(
        Dwarf dwarf,
        DataManager? dataManager,
        string attributeId,
        string effectId,
        float defaultValue = 1f)
        => GetConfiguredEffectValue(dwarf, dataManager, attributeId, effectId, defaultValue);

    /// <summary>
    /// Returns true when the dwarf's courage configuration marks them as afraid of water.
    /// </summary>
    public static bool FearsWater(Dwarf dwarf, DataManager? dataManager = null)
        => GetConfiguredEffectValue(dwarf, dataManager, AttributeIds.Courage, "water_fear", 0f) >= 0.5f;
}

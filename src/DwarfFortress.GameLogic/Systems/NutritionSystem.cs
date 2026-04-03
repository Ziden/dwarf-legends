using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct NutritionDeficiencyEvent(int DwarfId, string Nutrient);
public record struct NutritionCreditedEvent  (int DwarfId, float Carbs, float Protein, float Fat, float Vitamins);
public record struct MealPreferenceEvent     (int DwarfId, string ThoughtId);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Decays dietary nutrient stats on all dwarves each tick.
/// Emits NutritionDeficiencyEvent (every 60s while deficient) for ThoughtSystem to react.
/// Credit nutrients via CreditMeal() called from EatStrategy.OnComplete.
/// Order 6 — runs after NeedsSystem (order 4) so needs are decayed before nutrition checks.
/// </summary>
public sealed class NutritionSystem : IGameSystem
{
    public string SystemId    => SystemIds.NutritionSystem;
    public int    UpdateOrder => 6; // Changed from 5 to avoid conflict with NeedsSystem (now order 4)
    public bool   IsEnabled   { get; set; } = true;

    private const float DeficiencyFireInterval = 60f; // seconds between repeated deficiency events

    private readonly Dictionary<int, float> _lastDeficiencyFiredAt = new();
    private float _elapsed;

    private GameContext? _ctx;

    public void Initialize(GameContext ctx) => _ctx = ctx;

    public void Tick(float delta)
    {
        _elapsed += delta;
        var registry = _ctx!.Get<EntityRegistry>();

        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            var nutrition = dwarf.Components.Get<NutritionComponent>();
            nutrition.Decay(delta);

            if (!nutrition.AnyDeficiency)
            {
                _lastDeficiencyFiredAt.Remove(dwarf.Id);
                continue;
            }

            var alreadyFired = _lastDeficiencyFiredAt.TryGetValue(dwarf.Id, out var last);
            if (!alreadyFired || _elapsed - last >= DeficiencyFireInterval)
            {
                _lastDeficiencyFiredAt[dwarf.Id] = _elapsed;
                var nutrient = nutrition.GetWorstDeficiency()!;
                _ctx.EventBus.Emit(new NutritionDeficiencyEvent(dwarf.Id, nutrient));
            }
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { _lastDeficiencyFiredAt.Clear(); }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Credit the dwarf with nutrients derived from the consumed item's nutrition profile.
    /// Uses data-driven nutrition values from ItemDef when available, falls back to tag-based inference.
    /// Call this from EatStrategy.OnComplete after each meal.
    /// Also adds body fat based on calories consumed.
    /// </summary>
    public void CreditMeal(int dwarfId, ItemDef? itemDef)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        if (!registry.TryGetById<Dwarf>(dwarfId, out var dwarf) || dwarf is null)
            return;

        var dataManager = _ctx.TryGet<DataManager>();
        var (carbs, protein, fat, vitamins) = ResolveNutritionProfile(itemDef);

        // Apply attribute-based nutrient credit multiplier (stamina, appetite attributes)
        var nutrientMultiplier = AttributeEffectSystem.GetNutrientCreditMultiplier(dwarf, dataManager);
        carbs *= nutrientMultiplier;
        protein *= nutrientMultiplier;
        fat *= nutrientMultiplier;
        vitamins *= nutrientMultiplier;

        dwarf.Components.Get<NutritionComponent>().Credit(carbs, protein, fat, vitamins);
        _ctx.EventBus.Emit(new NutritionCreditedEvent(dwarfId, carbs, protein, fat, vitamins));

        // Add body fat based on meal nutrition (carbs + fat contribute most)
        var bodyFatGain = (carbs + fat) * BodyFatComponent.FatGainPerMeal;
        bodyFatGain *= AttributeEffectSystem.GetConfiguredMultiplier(
            dwarf,
            dataManager,
            AttributeIds.Appetite,
            "body_fat_gain_multiplier",
            1.0f);
        
        dwarf.BodyFat.GainFat(bodyFatGain);

        if (itemDef is not null)
        {
            var mod = dwarf.Preferences.GetFoodMoodMod(itemDef.Id);
            if (mod > 0f)
                _ctx.EventBus.Emit(new MealPreferenceEvent(dwarfId, ThoughtIds.AteLikedFood));
            else if (mod < 0f)
                _ctx.EventBus.Emit(new MealPreferenceEvent(dwarfId, ThoughtIds.AteDislikedFood));
        }
    }

    // ── Internal ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resolve nutrient profile from item definition.
    /// Uses explicit nutrition values from ItemDef if available, otherwise infers from tags.
    /// </summary>
    internal static (float Carbs, float Protein, float Fat, float Vitamins)
        ResolveNutritionProfile(ItemDef? itemDef)
    {
        if (itemDef is null)
            return (0.4f, 0.4f, 0.3f, 0.4f); // generic balanced

        // Use explicit nutrition values if defined
        if (itemDef.Nutrition is { } nutrition)
            return (nutrition.Carbs, nutrition.Protein, nutrition.Fat, nutrition.Vitamins);

        // Fall back to tag-based inference for backwards compatibility
        return ResolveNutritionFromTags(itemDef);
    }

    /// <summary>
    /// Infer nutrient profile from item tags (backwards compatibility for items without explicit nutrition).
    /// </summary>
    private static (float Carbs, float Protein, float Fat, float Vitamins)
        ResolveNutritionFromTags(ItemDef itemDef)
    {
        var tags = itemDef.Tags;

        if (tags.Contains("fruit"))
            return (0.5f, 0.1f, 0.2f, 0.7f);

        if (tags.Contains("meat"))
            return (0.1f, 0.7f, 0.6f, 0.2f);

        if (tags.Contains("grain"))
            return (0.8f, 0.3f, 0.2f, 0.2f);

        if (tags.Contains("meal"))    // cooked meal — most balanced
            return (0.6f, 0.5f, 0.4f, 0.4f);

        if (tags.Contains("plant") && !tags.Contains("seed"))
            return (0.5f, 0.2f, 0.1f, 0.6f);

        // Generic food
        return (0.4f, 0.4f, 0.3f, 0.4f);
    }
}

using System;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

/// <summary>Emitted when a dwarf becomes over-encumbered.</summary>
public record struct DwarfEncumberedEvent(int DwarfId, float CarryWeight, float MaxCapacity, float SpeedMultiplier);

/// <summary>Emitted when a dwarf's encumbrance state changes.</summary>
public record struct EncumbranceChangedEvent(int DwarfId, float OldRatio, float NewRatio);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages weight, carry capacity, and encumbrance for dwarves.
/// Order 8 — runs after TraitSystem (order 7) so trait modifiers are applied.
/// </summary>
public sealed class WeightSystem : IGameSystem
{
    public string SystemId    => SystemIds.WeightSystem;
    public int    UpdateOrder => 8;
    public bool   IsEnabled   { get; set; } = true;

    /// <summary>Base carry capacity in kg for an average dwarf (130cm, no traits).</summary>
    public const float BaseCarryCapacity = 50f;

    /// <summary>Maximum carry capacity when dwarf halves their speed (2x base).</summary>
    public const float MaxCarryCapacity = BaseCarryCapacity * 2f;

    /// <summary>Base weight from height (kg) - dwarves are dense!</summary>
    public const float BaseWeightPerCm = 0.8f;

    /// <summary>Speed penalty threshold - when carry ratio exceeds this, speed is halved.</summary>
    public const float SpeedPenaltyThreshold = 1.0f;

    /// <summary>Lazy trait threshold - lazy dwarves start slowing at 50% capacity.</summary>
    public const float LazySpeedPenaltyThreshold = 0.5f;

    private GameContext? _ctx;
    private const float EncumbranceEventInterval = 5f;
    private float _elapsed;

    public void Initialize(GameContext ctx) => _ctx = ctx;

    public void Tick(float delta)
    {
        _elapsed += delta;
        var registry = _ctx!.Get<EntityRegistry>();
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        var dm = _ctx!.TryGet<DataManager>();

        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            var carriedWeight = GetCarriedItemWeight(dwarf, itemSystem, dm);
            var maxCapacity = GetMaxCarryCapacity(dwarf);
            var carryRatio = carriedWeight / maxCapacity;

            // Apply speed modifier based on encumbrance
            ApplyEncumbranceSpeed(dwarf, carryRatio);

            // Emit encumbrance event periodically
            if (_elapsed % EncumbranceEventInterval < delta && carryRatio > 0.8f)
            {
                var speedMult = GetSpeedMultiplier(dwarf, carryRatio);
                _ctx.EventBus.Emit(new DwarfEncumberedEvent(dwarf.Id, carriedWeight, maxCapacity, speedMult));
            }
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    /// <summary>
    /// Gets the maximum carry capacity for a dwarf in kg.
    /// Base: 50kg, modified by height, Strong trait, and Fit trait.
    /// </summary>
    public static float GetMaxCarryCapacity(Dwarf dwarf)
    {
        var height = dwarf.Appearance.Height;
        var capacity = BaseCarryCapacity;

        // Height modifier: taller dwarves are stronger (+0.5kg per cm above 100cm)
        var heightBonus = Math.Max(0f, (height - 100f) * 0.5f);
        capacity += heightBonus;

        // Strong trait: +50% capacity
        if (dwarf.Traits.HasTrait(TraitIds.Strong))
            capacity *= 1.5f;

        // Fit trait: +25% capacity
        if (dwarf.Traits.HasTrait(TraitIds.Fit))
            capacity *= 1.25f;

        return capacity;
    }

    /// <summary>
    /// Gets the total weight of items currently carried by the dwarf.
    /// </summary>
    public static float GetCarriedItemWeight(Dwarf dwarf, ItemSystem? itemSystem = null, DataManager? dm = null)
    {
        if (itemSystem is null) return 0f;

        float totalWeight = 0f;
        
        foreach (var itemId in dwarf.Inventory.CarriedItemIds)
        {
            if (itemSystem.TryGetItem(itemId, out var item) && item is not null)
            {
                var itemDef = dm?.Items.GetOrNull(item.DefId);
                if (itemDef is not null)
                {
                    totalWeight += itemDef.Weight * item.StackSize;
                }
            }
        }
        return totalWeight;
    }

    /// <summary>
    /// Calculates the dwarf's total weight (body + carried items).
    /// Body weight = height-based base + fat weight
    /// </summary>
    public static float GetTotalWeight(Dwarf dwarf)
    {
        var bodyWeight = GetBodyWeight(dwarf);
        return bodyWeight;
    }

    /// <summary>
    /// Calculates the dwarf's body weight (without items).
    /// Formula: base weight from height + fat contribution
    /// </summary>
    public static float GetBodyWeight(Dwarf dwarf)
    {
        var height = dwarf.Appearance.Height;
        var bodyFat = dwarf.BodyFat.BodyFat;

        // Base weight from height (dwarves are dense and muscular)
        var baseWeight = height * BaseWeightPerCm;

        // Fat contribution (each % of body fat adds weight)
        var fatWeight = bodyFat * 0.3f; // 1% body fat = 0.3kg

        return baseWeight + fatWeight;
    }

    /// <summary>
    /// Gets the speed multiplier based on encumbrance.
    /// Returns 1.0 for no penalty, 0.5 for maximum encumbrance.
    /// </summary>
    public static float GetSpeedMultiplier(Dwarf dwarf, float carryRatio)
    {
        var threshold = dwarf.Traits.HasTrait(TraitIds.Lazy)
            ? LazySpeedPenaltyThreshold
            : SpeedPenaltyThreshold;

        if (carryRatio <= threshold)
            return 1.0f;

        // Linear interpolation from 1.0 to 0.5 as ratio goes from threshold to 2.0
        var penalty = Math.Min(1f, (carryRatio - threshold) / (2f - threshold));
        return 1.0f - (penalty * 0.5f);
    }

    /// <summary>
    /// Applies encumbrance speed modifier to the dwarf's speed stat.
    /// </summary>
    private void ApplyEncumbranceSpeed(Dwarf dwarf, float carryRatio)
    {
        var speedStat = dwarf.Stats.Speed;
        var multiplier = GetSpeedMultiplier(dwarf, carryRatio);

        // Remove existing encumbrance modifier
        speedStat.Modifiers.Remove("encumbrance");

        // Add new modifier if there's a penalty
        if (multiplier < 1.0f)
        {
            var penaltyValue = multiplier - 1.0f; // e.g., 0.5 -> -0.5
            speedStat.Modifiers.Add(new Modifier(
                SourceId: "encumbrance",
                Type: ModType.PercentAdd,
                Value: penaltyValue,
                Duration: -1f)); // permanent while encumbered
        }
    }

    /// <summary>
    /// Checks if a dwarf can pick up an item without exceeding max capacity.
    /// </summary>
    public bool CanPickUpItem(Dwarf dwarf, float itemWeight, ItemSystem? itemSystem = null)
    {
        var currentWeight = GetCarriedItemWeight(dwarf, itemSystem);
        var maxCapacity = GetMaxCarryCapacity(dwarf);
        return (currentWeight + itemWeight) <= maxCapacity;
    }

    /// <summary>
    /// Gets the encumbrance ratio (0-2+) for a dwarf.
    /// 0 = nothing carried, 1 = at capacity, 2 = double capacity (max penalty)
    /// </summary>
    public float GetEncumbranceRatio(Dwarf dwarf, ItemSystem? itemSystem = null)
    {
        var carriedWeight = GetCarriedItemWeight(dwarf, itemSystem);
        var maxCapacity = GetMaxCarryCapacity(dwarf);
        return carriedWeight / maxCapacity;
    }

    /// <summary>
    /// Gets a human-readable encumbrance status.
    /// </summary>
    public static string GetEncumbranceStatus(Dwarf dwarf, float carryRatio)
    {
        if (carryRatio <= 0f) return "Unencumbered";
        if (carryRatio <= 0.25f) return "Lightly Loaded";
        if (carryRatio <= 0.5f) return "Moderately Loaded";
        if (carryRatio <= 0.75f) return "Heavily Loaded";
        if (carryRatio <= 1.0f) return "Encumbered";
        return "Over-Encumbered";
    }
}
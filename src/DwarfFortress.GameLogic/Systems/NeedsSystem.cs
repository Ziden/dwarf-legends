using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct NeedCriticalEvent(int EntityId, string NeedId);
public record struct NeedSatisfiedEvent(int EntityId, string NeedId);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Ticks biological needs for all live entities with NeedsComponent.
/// Dwarves enqueue survival jobs (eat/drink/sleep); creatures rely on autonomous behaviors.
/// Order 4 — runs before NutritionSystem (order 6) to ensure needs are decayed first.
/// </summary>
public sealed class NeedsSystem : IGameSystem
{
    public string SystemId    => SystemIds.NeedsSystem;
    public int    UpdateOrder => 4; // Changed from 5 to avoid conflict with NutritionSystem
    public bool   IsEnabled   { get; set; } = true;

    // O(1) lookup: jobId → (entityId, jobDefId) for fast removal
    private readonly Dictionary<int, (int entityId, string jobDefId)> _jobIdToKey = new();
    // O(1) lookup: (entityId, jobDefId) → jobId for fast existence check
    private readonly Dictionary<(int entityId, string jobDefId), int> _activeJobIds = new();
    // Tracks when a need was last satisfied (in game seconds) to prevent job spam
    private readonly Dictionary<(int entityId, string needId), float> _lastSatisfiedAt = new();

    // Minimum time (in seconds) between satisfying a need and allowing another job for the same need
    private const float SatisfactionCooldown = 30f;

    private float _elapsedTime;
    private GameContext? _ctx;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;

        ctx.EventBus.On<Jobs.JobCompletedEvent>(e  => RemoveTracked(e.JobId));
        ctx.EventBus.On<Jobs.JobFailedEvent>   (e  => RemoveTracked(e.JobId));
        ctx.EventBus.On<Jobs.JobCancelledEvent>(e  => RemoveTracked(e.JobId));
        ctx.EventBus.On<NeedSatisfiedEvent>(e => OnNeedSatisfied(e));
    }

    public void Tick(float delta)
    {
        _elapsedTime += delta;

        var registry  = _ctx!.Get<EntityRegistry>();
        var jobSystem = _ctx!.TryGet<Jobs.JobSystem>();

        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            TickDwarfNeeds(dwarf, delta);

            CheckNeed(dwarf, dwarf.Needs.Hunger, Jobs.JobDefIds.Eat, jobSystem);
            CheckNeed(dwarf, dwarf.Needs.Thirst, Jobs.JobDefIds.Drink, jobSystem);
            CheckNeed(dwarf, dwarf.Needs.Sleep, Jobs.JobDefIds.Sleep, jobSystem);
        }

        foreach (var creature in registry.GetAlive<Creature>())
        {
            TickNeeds(creature.Needs, delta);
            EmitNeedCritical(creature, creature.Needs.Hunger);
            EmitNeedCritical(creature, creature.Needs.Thirst);
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { _activeJobIds.Clear(); _jobIdToKey.Clear(); }

    // ── Private ────────────────────────────────────────────────────────────

    private static void TickNeeds(NeedsComponent needs, float delta)
    {
        foreach (var need in needs.All)
            need.Decay(delta);
    }

    private static void TickDwarfNeeds(Dwarf dwarf, float delta)
    {
        var sleepDecayMultiplier = SleepSystem.GetSleepDecayMultiplier(dwarf);
        
        foreach (var need in dwarf.Needs.All)
        {
            if (need.Name == NeedIds.Sleep)
            {
                // Apply Sleepy trait: sleep need increases double as fast
                need.Decay(delta * sleepDecayMultiplier);
            }
            else
            {
                need.Decay(delta);
            }
        }
    }

    private void CheckNeed(Dwarf dwarf, Need need, string jobDefId, Jobs.JobSystem? jobSystem)
    {
        // Check if need was recently satisfied (cooldown to prevent job spam)
        var satisfactionKey = (dwarf.Id, need.Name);
        if (_lastSatisfiedAt.TryGetValue(satisfactionKey, out var lastSatisfied))
        {
            if (_elapsedTime - lastSatisfied < SatisfactionCooldown)
                return;  // Still in cooldown period
        }

        if (!need.IsCritical) return;

        // Don't queue eat/drink jobs if the dwarf is nauseous
        if ((jobDefId == Jobs.JobDefIds.Eat || jobDefId == Jobs.JobDefIds.Drink)
            && dwarf.Components.TryGet<Entities.Components.StatusEffectComponent>()
                   ?.Has(Entities.Components.StatusEffectIds.Nausea) == true)
            return;

        EmitNeedCritical(dwarf, need);

        if (jobSystem is null) return;

        var key = (dwarf.Id, jobDefId);
        if (_activeJobIds.ContainsKey(key)) return;  // job already pending or in-progress

        var targetPos = dwarf.Position.Position;
        if (string.Equals(jobDefId, Jobs.JobDefIds.Eat, System.StringComparison.OrdinalIgnoreCase))
        {
            var itemSystem = _ctx!.TryGet<ItemSystem>();
            if (itemSystem?.FindFoodItem() is null)
            {
                var map = _ctx.TryGet<WorldMap>();
                var data = _ctx.TryGet<DataManager>();
                if (map is not null && data is not null &&
                    PlantHarvesting.TryFindNearestHarvestablePlant(map, data, dwarf.Position.Position, searchRadius: 24, out var harvestTarget))
                {
                    targetPos = harvestTarget.PlantPos;
                }
                // If no world context or no harvestable plant, create Eat job at dwarf's position anyway
            }
        }

        var job = jobSystem.CreateJob(jobDefId, targetPos, priority: 100);
        _activeJobIds[key] = job.Id;
        _jobIdToKey[job.Id] = key;  // O(1) reverse mapping
    }

    private void RemoveTracked(int jobId)
    {
        // O(1) removal using reverse mapping
        if (_jobIdToKey.Remove(jobId, out var key))
            _activeJobIds.Remove(key);
    }

    private void OnNeedSatisfied(NeedSatisfiedEvent e)
    {
        var key = (e.EntityId, e.NeedId);
        _lastSatisfiedAt[key] = _elapsedTime;
    }

    private void EmitNeedCritical(Entity entity, Need need)
    {
        if (!need.IsCritical)
            return;

        _ctx!.EventBus.Emit(new NeedCriticalEvent(entity.Id, need.Name));
    }
}

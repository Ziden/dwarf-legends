using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Jobs.Strategies;
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
    // Tracks which needs are currently in their critical state so events fire on entry, not every tick.
    private readonly HashSet<(int entityId, string needId)> _activeCriticalNeeds = new();
    // Tracks when a need was last satisfied (in game seconds) to prevent job spam
    private readonly Dictionary<(int entityId, string needId), float> _lastSatisfiedAt = new();

    // Minimum time (in seconds) between satisfying a need and allowing another job for the same need
    private const float SatisfactionCooldown = 30f;
    private const int SleepSurvivalPriority = 100;
    private const int EatSurvivalPriority = 101;
    private const int DrinkSurvivalPriority = 102;
    private const int EatPlantSearchRadius = 24;
    private const int DrinkSearchRadius = 14;

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
            TickDwarfNeeds(dwarf, delta, _ctx!.TryGet<DataManager>());

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
    public void OnLoad(SaveReader r)
    {
        _activeJobIds.Clear();
        _jobIdToKey.Clear();
        _activeCriticalNeeds.Clear();
    }

    // ── Private ────────────────────────────────────────────────────────────

    private static void TickNeeds(NeedsComponent needs, float delta)
    {
        foreach (var need in needs.All)
            need.Decay(delta);
    }

    private static void TickDwarfNeeds(Dwarf dwarf, float delta, DataManager? dataManager)
    {
        var sleepDecayMultiplier = SleepSystem.GetSleepDecayMultiplier(dwarf, dataManager);
        
        foreach (var need in dwarf.Needs.All)
        {
            if (need.Name == NeedIds.Sleep)
            {
                // Sleep decay is driven by stamina rather than a separate trait system.
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
        var criticalKey = (dwarf.Id, need.Name);
        if (!need.IsCritical)
        {
            _activeCriticalNeeds.Remove(criticalKey);
            CancelTrackedPendingJob(dwarf.Id, jobDefId, jobSystem);
            return;
        }

        // Check if need was recently satisfied (cooldown to prevent job spam)
        var satisfactionKey = (dwarf.Id, need.Name);
        if (_lastSatisfiedAt.TryGetValue(satisfactionKey, out var lastSatisfied))
        {
            if (_elapsedTime - lastSatisfied < SatisfactionCooldown)
                return;  // Still in cooldown period
        }

        // Don't queue eat/drink jobs if the dwarf is nauseous
        if ((jobDefId == Jobs.JobDefIds.Eat || jobDefId == Jobs.JobDefIds.Drink)
            && dwarf.Components.TryGet<Entities.Components.StatusEffectComponent>()
                   ?.Has(Entities.Components.StatusEffectIds.Nausea) == true)
            return;

        EmitNeedCritical(dwarf, need);

        if (jobSystem is null) return;

        TryPreemptActiveJobForNeed(dwarf, jobDefId, jobSystem);

        var key = (dwarf.Id, jobDefId);
        if (_activeJobIds.ContainsKey(key)) return;  // job already pending or in-progress

        if (string.Equals(jobDefId, Jobs.JobDefIds.Eat, StringComparison.OrdinalIgnoreCase))
        {
            var eatJob = TryCreateEatJob(dwarf, jobSystem);
            if (eatJob is not null)
            {
                TrackJob(key, eatJob);
                return;
            }

            if (_ctx!.TryGet<ItemSystem>() is not null || _ctx.TryGet<WorldMap>() is not null)
                return;
        }

        if (string.Equals(jobDefId, Jobs.JobDefIds.Drink, StringComparison.OrdinalIgnoreCase))
        {
            var drinkJob = TryCreateDrinkJob(dwarf, jobSystem);
            if (drinkJob is not null)
            {
                TrackJob(key, drinkJob);
                return;
            }

            if (_ctx!.TryGet<ItemSystem>() is not null || _ctx.TryGet<WorldMap>() is not null)
                return;
        }

        var job = jobSystem.CreateJob(jobDefId, dwarf.Position.Position, priority: GetSurvivalPriority(jobDefId));
        job.AssignedDwarfId = dwarf.Id;
        TrackJob(key, job);
    }

    private Jobs.Job? TryCreateEatJob(Dwarf dwarf, Jobs.JobSystem jobSystem)
    {
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        if (itemSystem?.FindCarriedFoodItem(dwarf.Id) is { } carriedFood)
        {
            carriedFood.IsClaimed = true;
            var carriedJob = jobSystem.CreateJob(
                Jobs.JobDefIds.Eat,
                dwarf.Position.Position,
                priority: EatSurvivalPriority,
                entityId: carriedFood.Id);
            carriedJob.AssignedDwarfId = dwarf.Id;
            carriedJob.ReservedItemIds.Add(carriedFood.Id);
            return carriedJob;
        }

        var map = _ctx.TryGet<WorldMap>();
        var data = _ctx.TryGet<DataManager>();
        Vec3i? forageTarget = null;
        if (map is not null && data is not null
            && PlantHarvesting.TryFindNearestHarvestablePlant(map, data, dwarf.Position.Position, EatPlantSearchRadius, out var harvestTarget))
        {
            forageTarget = harvestTarget.PlantPos;
        }

        var inventory = dwarf.Components.TryGet<InventoryComponent>();
        if (inventory?.IsFull == true)
        {
            if (!forageTarget.HasValue)
                return null;

            var forageJob = jobSystem.CreateJob(Jobs.JobDefIds.Eat, forageTarget.Value, priority: EatSurvivalPriority);
            forageJob.AssignedDwarfId = dwarf.Id;
            return forageJob;
        }

        if (itemSystem?.FindFoodItem() is { } food)
        {
            food.IsClaimed = true;
            var foodJob = jobSystem.CreateJob(
                Jobs.JobDefIds.Eat,
                food.Position.Position,
                priority: EatSurvivalPriority,
                entityId: food.Id);
            foodJob.AssignedDwarfId = dwarf.Id;
            foodJob.ReservedItemIds.Add(food.Id);
            return foodJob;
        }

        if (!forageTarget.HasValue)
            return null;

        var harvestJob = jobSystem.CreateJob(Jobs.JobDefIds.Eat, forageTarget.Value, priority: EatSurvivalPriority);
        harvestJob.AssignedDwarfId = dwarf.Id;
        return harvestJob;
    }

    private Jobs.Job? TryCreateDrinkJob(Dwarf dwarf, Jobs.JobSystem jobSystem)
    {
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        var map = _ctx.TryGet<WorldMap>();
        var origin = dwarf.Position.Position;
        var reachableDrink = map is not null
            ? DrinkItemLocator.FindReachableDrinkItem(_ctx, map, origin)
            : itemSystem?.FindDrinkItem();
        if (reachableDrink is { } drink)
        {
            drink.IsClaimed = true;
            var itemJob = jobSystem.CreateJob(
                Jobs.JobDefIds.Drink,
                drink.Position.Position,
                priority: DrinkSurvivalPriority,
                entityId: drink.Id);
            itemJob.AssignedDwarfId = dwarf.Id;
            itemJob.ReservedItemIds.Add(drink.Id);
            return itemJob;
        }

        if (map is null)
            return null;

        Vec3i targetPos;
        if (DrinkSourceLocator.CanDrinkAt(map, origin))
        {
            targetPos = origin;
        }
        else if (TryResolveDrinkTileTarget(map, origin, out var drinkTile))
        {
            targetPos = drinkTile;
        }
        else
        {
            return null;
        }

        var waterJob = jobSystem.CreateJob(Jobs.JobDefIds.Drink, targetPos, priority: DrinkSurvivalPriority);
        waterJob.AssignedDwarfId = dwarf.Id;
        return waterJob;
    }

    private bool TryResolveDrinkTileTarget(WorldMap map, Vec3i origin, out Vec3i drinkTile)
    {
        if (DrinkSourceLocator.TryFindNearestDrinkableTile(map, origin, DrinkSearchRadius, out drinkTile))
            return true;

        var fortressLocations = _ctx!.TryGet<FortressLocationSystem>();
        if (fortressLocations is null || !fortressLocations.TryGetClosestDrinkLocation(out drinkTile))
            return false;

        return DrinkSourceLocator.IsDrinkableWaterTile(map, drinkTile);
    }

    private void TrackJob((int entityId, string jobDefId) key, Jobs.Job job)
    {
        _activeJobIds[key] = job.Id;
        _jobIdToKey[job.Id] = key;  // O(1) reverse mapping
    }

    private void CancelTrackedPendingJob(int entityId, string jobDefId, Jobs.JobSystem? jobSystem)
    {
        if (jobSystem is null)
            return;

        var key = (entityId, jobDefId);
        if (!_activeJobIds.TryGetValue(key, out var jobId))
            return;

        var job = jobSystem.GetJob(jobId);
        if (job is null)
        {
            RemoveTracked(jobId);
            return;
        }

        if (job.Status == Jobs.JobStatus.Pending)
            jobSystem.CancelJob(jobId);
    }

    private static void TryPreemptActiveJobForNeed(Dwarf dwarf, string requestedJobDefId, Jobs.JobSystem jobSystem)
    {
        var activeJob = jobSystem.GetAssignedJob(dwarf.Id);
        if (activeJob is null)
            return;

        if (string.Equals(activeJob.JobDefId, requestedJobDefId, StringComparison.OrdinalIgnoreCase))
            return;

        if (IsSurvivalJob(activeJob.JobDefId)
            && GetSurvivalPriority(requestedJobDefId) <= GetSurvivalPriority(activeJob.JobDefId))
            return;

        jobSystem.CancelJob(activeJob.Id);
    }

    private static int GetSurvivalPriority(string jobDefId)
        => string.Equals(jobDefId, Jobs.JobDefIds.Drink, StringComparison.OrdinalIgnoreCase)
            ? DrinkSurvivalPriority
            : string.Equals(jobDefId, Jobs.JobDefIds.Eat, StringComparison.OrdinalIgnoreCase)
                ? EatSurvivalPriority
                : SleepSurvivalPriority;

    private static bool IsSurvivalJob(string jobDefId)
        => string.Equals(jobDefId, Jobs.JobDefIds.Eat, StringComparison.OrdinalIgnoreCase)
           || string.Equals(jobDefId, Jobs.JobDefIds.Drink, StringComparison.OrdinalIgnoreCase)
           || string.Equals(jobDefId, Jobs.JobDefIds.Sleep, StringComparison.OrdinalIgnoreCase);

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
        var key = (entity.Id, need.Name);
        if (!need.IsCritical)
        {
            _activeCriticalNeeds.Remove(key);
            return;
        }

        if (!_activeCriticalNeeds.Add(key))
            return;

        _ctx!.EventBus.Emit(new NeedCriticalEvent(entity.Id, need.Name));
    }
}

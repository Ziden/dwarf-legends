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
    // Tracks failed hunger/thirst searches so unreachable resources do not trigger a full rescan every tick.
    private readonly Dictionary<(int entityId, string needId), float> _lastSearchAttemptAt = new();

    // Minimum time (in seconds) between satisfying a need and allowing another job for the same need
    private const float SatisfactionCooldown = 30f;
    private const float FailedSearchRetrySeconds = 1f;
    private const int SleepSurvivalPriority = 100;
    private const int EatSurvivalPriority = 101;
    private const int DrinkSurvivalPriority = 102;
    private const int EatPlantSearchRadius = 24;
    private const int DrinkSearchRadius = 14;
    private const float StaggeredNeedIntervalSeconds = 1f;
    private const string NeedsTickSpan = "needs_tick";
    private const string NeedsDwarvesSpan = "needs_dwarves";
    private const string NeedsCreaturesSpan = "needs_creatures";
    private const string CheckHungerSpan = "check_hunger";
    private const string CheckThirstSpan = "check_thirst";
    private const string SearchEatJobSpan = "search_eat_job";
    private const string SearchDrinkJobSpan = "search_drink_job";
    private const string EatCarriedFoodSpan = "eat_carried_food";
    private const string EatForageSearchSpan = "eat_forage_search";
    private const string EatItemLookupSpan = "eat_item_lookup";
    private const string DrinkItemSearchSpan = "drink_item_search";
    private const string DrinkTileSearchSpan = "drink_tile_search";
    private const string DrinkFortressFallbackSpan = "drink_fortress_fallback";

    private float _elapsedTime;
    private GameContext? _ctx;
    private SimulationProfiler? _profiler;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        _profiler = ctx.Profiler;

        ctx.EventBus.On<Jobs.JobCompletedEvent>(e  => RemoveTracked(e.JobId));
        ctx.EventBus.On<Jobs.JobFailedEvent>   (OnTrackedJobFailed);
        ctx.EventBus.On<Jobs.JobCancelledEvent>(e  => RemoveTracked(e.JobId));
        ctx.EventBus.On<NeedSatisfiedEvent>(e => OnNeedSatisfied(e));
    }

    public void Tick(float delta)
    {
        var elapsedBeforeTick = _elapsedTime;
        _elapsedTime += delta;

        var registry  = _ctx!.Get<EntityRegistry>();
        var jobSystem = _ctx!.TryGet<Jobs.JobSystem>();
        var data = _ctx!.TryGet<DataManager>();

        using var needsScope = _profiler?.Measure(NeedsTickSpan) ?? default;

        using (var dwarfScope = _profiler?.Measure(NeedsDwarvesSpan) ?? default)
        {
            foreach (var dwarf in registry.GetAlive<Dwarf>())
            {
                TickDwarfNeeds(dwarf, delta, elapsedBeforeTick, data);

                CheckNeed(dwarf, dwarf.Needs.Hunger, Jobs.JobDefIds.Eat, jobSystem);
                CheckNeed(dwarf, dwarf.Needs.Thirst, Jobs.JobDefIds.Drink, jobSystem);
                CheckNeed(dwarf, dwarf.Needs.Sleep, Jobs.JobDefIds.Sleep, jobSystem);
            }
        }

        using (var creatureScope = _profiler?.Measure(NeedsCreaturesSpan) ?? default)
        {
            foreach (var creature in registry.GetAlive<Creature>())
            {
                TickNeeds(creature.Id, creature.Needs, delta, elapsedBeforeTick);
                EmitNeedCritical(creature, creature.Needs.Hunger);
                EmitNeedCritical(creature, creature.Needs.Thirst);
            }
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r)
    {
        _activeJobIds.Clear();
        _jobIdToKey.Clear();
        _activeCriticalNeeds.Clear();
        _lastSearchAttemptAt.Clear();
    }

    // ── Private ────────────────────────────────────────────────────────────

    private static void TickNeeds(int entityId, NeedsComponent needs, float delta, float elapsedBeforeTick)
    {
        TickNeed(entityId, needs.Hunger, delta, elapsedBeforeTick);
        TickNeed(entityId, needs.Thirst, delta, elapsedBeforeTick);
        needs.Sleep.Decay(delta);
        needs.Social.Decay(delta);
        needs.Recreation.Decay(delta);
    }

    private static void TickDwarfNeeds(Dwarf dwarf, float delta, float elapsedBeforeTick, DataManager? dataManager)
    {
        var sleepDecayMultiplier = SleepSystem.GetSleepDecayMultiplier(dwarf, dataManager);

        TickNeed(dwarf.Id, dwarf.Needs.Hunger, delta, elapsedBeforeTick);
        TickNeed(dwarf.Id, dwarf.Needs.Thirst, delta, elapsedBeforeTick);
        // Sleep decay is driven by stamina rather than a separate trait system.
        dwarf.Needs.Sleep.Decay(delta * sleepDecayMultiplier);
        dwarf.Needs.Social.Decay(delta);
        dwarf.Needs.Recreation.Decay(delta);
    }

    private static void TickNeed(int entityId, Need need, float delta, float elapsedBeforeTick)
    {
        if (!RequiresStaggeredDecay(need.Name))
        {
            need.Decay(delta);
            return;
        }

        var pulses = CountStaggeredNeedPulses(entityId, need.Name, elapsedBeforeTick, delta, StaggeredNeedIntervalSeconds);
        for (var pulse = 0; pulse < pulses; pulse++)
            need.Decay(StaggeredNeedIntervalSeconds);
    }

    private static bool RequiresStaggeredDecay(string needId)
        => string.Equals(needId, NeedIds.Hunger, StringComparison.OrdinalIgnoreCase)
            || string.Equals(needId, NeedIds.Thirst, StringComparison.OrdinalIgnoreCase);

    private static int CountStaggeredNeedPulses(int entityId, string needId, float elapsedBeforeTick, float delta, float intervalSeconds)
    {
        if (delta <= 0f)
            return 0;

        var phaseOffset = ResolveNeedPhaseOffset(entityId, needId, intervalSeconds);
        var previous = elapsedBeforeTick + phaseOffset;
        var current = previous + delta;
        return Math.Max(0, (int)MathF.Floor(current / intervalSeconds) - (int)MathF.Floor(previous / intervalSeconds));
    }

    private static float ResolveNeedPhaseOffset(int entityId, string needId, float intervalSeconds)
    {
        var hash = ((uint)entityId * 747796405u) ^ StableOrdinalHash(needId);
        var normalized = (hash & 0xFFFF) / 65536f;
        return normalized * intervalSeconds;
    }

    private static uint StableOrdinalHash(string value)
    {
        const uint offset = 2166136261u;
        const uint prime = 16777619u;
        var hash = offset;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }

        return hash;
    }

    private void CheckNeed(Dwarf dwarf, Need need, string jobDefId, Jobs.JobSystem? jobSystem)
    {
        var criticalKey = (dwarf.Id, need.Name);
        if (!need.IsCritical)
        {
            _activeCriticalNeeds.Remove(criticalKey);
            _lastSearchAttemptAt.Remove(criticalKey);
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
        if (ShouldDelayFailedSearch(dwarf.Id, need.Name)) return;

        if (string.Equals(jobDefId, Jobs.JobDefIds.Eat, StringComparison.OrdinalIgnoreCase))
        {
            using var eatScope = _profiler?.Measure(CheckHungerSpan) ?? default;
            var eatJob = TryCreateEatJob(dwarf, jobSystem);
            if (eatJob is not null)
            {
                TrackJob(key, eatJob);
                return;
            }

            if (_ctx!.TryGet<ItemSystem>() is not null || _ctx.TryGet<WorldMap>() is not null)
            {
                MarkFailedSearchAttempt(dwarf.Id, need.Name);
                return;
            }
        }

        if (string.Equals(jobDefId, Jobs.JobDefIds.Drink, StringComparison.OrdinalIgnoreCase))
        {
            using var drinkScope = _profiler?.Measure(CheckThirstSpan) ?? default;
            var drinkJob = TryCreateDrinkJob(dwarf, jobSystem);
            if (drinkJob is not null)
            {
                TrackJob(key, drinkJob);
                return;
            }

            if (_ctx!.TryGet<ItemSystem>() is not null || _ctx.TryGet<WorldMap>() is not null)
            {
                MarkFailedSearchAttempt(dwarf.Id, need.Name);
                return;
            }
        }

        var job = jobSystem.CreateJob(jobDefId, dwarf.Position.Position, priority: GetSurvivalPriority(jobDefId));
        job.AssignedDwarfId = dwarf.Id;
        TrackJob(key, job);
    }

    private Jobs.Job? TryCreateEatJob(Dwarf dwarf, Jobs.JobSystem jobSystem)
    {
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        using var searchScope = _profiler?.Measure(SearchEatJobSpan) ?? default;

        using (var carriedFoodScope = _profiler?.Measure(EatCarriedFoodSpan) ?? default)
        {
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
        }

        var map = _ctx.TryGet<WorldMap>();
        var data = _ctx.TryGet<DataManager>();
        Vec3i? forageTarget = null;
        using (var forageScope = _profiler?.Measure(EatForageSearchSpan) ?? default)
        {
            if (map is not null && data is not null
                && PlantHarvesting.TryFindNearestHarvestablePlant(map, data, dwarf.Position.Position, EatPlantSearchRadius, out var harvestTarget))
            {
                forageTarget = harvestTarget.PlantPos;
            }
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

        using (var itemLookupScope = _profiler?.Measure(EatItemLookupSpan) ?? default)
        {
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
        using var searchScope = _profiler?.Measure(SearchDrinkJobSpan) ?? default;

        Item? reachableDrink;
        using (var itemLookupScope = _profiler?.Measure(DrinkItemSearchSpan) ?? default)
        {
            reachableDrink = map is not null
                ? DrinkItemLocator.FindReachableDrinkItem(_ctx, map, origin)
                : itemSystem?.FindDrinkItem();
        }

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
        using (var tileSearchScope = _profiler?.Measure(DrinkTileSearchSpan) ?? default)
        {
            if (DrinkSourceLocator.CanDrinkAt(map, origin))
            {
                targetPos = origin;
            }
            else if (!TryResolveDrinkTileTarget(map, origin, out targetPos))
            {
                return null;
            }
        }

        var waterJob = jobSystem.CreateJob(Jobs.JobDefIds.Drink, targetPos, priority: DrinkSurvivalPriority);
        waterJob.AssignedDwarfId = dwarf.Id;
        return waterJob;
    }

    private bool TryResolveDrinkTileTarget(WorldMap map, Vec3i origin, out Vec3i drinkTile)
    {
        if (DrinkSourceLocator.TryFindNearestDrinkableTile(map, origin, DrinkSearchRadius, out drinkTile))
            return true;

        using var fallbackScope = _profiler?.Measure(DrinkFortressFallbackSpan) ?? default;
        var fortressLocations = _ctx!.TryGet<FortressLocationSystem>();
        if (fortressLocations is null || !fortressLocations.TryGetClosestDrinkLocation(out var fallbackDrinkTile))
        {
            drinkTile = origin;
            return false;
        }

        drinkTile = fallbackDrinkTile;
        return DrinkSourceLocator.TryFindReachableDrinkStandPosition(map, origin, drinkTile, out _);
    }

    private void TrackJob((int entityId, string jobDefId) key, Jobs.Job job)
    {
        _activeJobIds[key] = job.Id;
        _jobIdToKey[job.Id] = key;  // O(1) reverse mapping
        if (TryResolveNeedIdForJob(key.jobDefId, out var needId))
            _lastSearchAttemptAt.Remove((key.entityId, needId));
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

    private void OnTrackedJobFailed(Jobs.JobFailedEvent e)
    {
        if (_jobIdToKey.TryGetValue(e.JobId, out var key)
            && TryResolveNeedIdForJob(key.jobDefId, out var needId)
            && ShouldRetryFailedSearch(needId))
        {
            _lastSearchAttemptAt[(key.entityId, needId)] = _elapsedTime;
        }

        RemoveTracked(e.JobId);
    }

    private void OnNeedSatisfied(NeedSatisfiedEvent e)
    {
        var key = (e.EntityId, e.NeedId);
        _lastSatisfiedAt[key] = _elapsedTime;
        _lastSearchAttemptAt.Remove(key);
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

    private bool ShouldDelayFailedSearch(int entityId, string needId)
    {
        if (!ShouldRetryFailedSearch(needId))
            return false;

        return _lastSearchAttemptAt.TryGetValue((entityId, needId), out var lastAttemptAt)
            && (_elapsedTime - lastAttemptAt) < FailedSearchRetrySeconds;
    }

    private void MarkFailedSearchAttempt(int entityId, string needId)
    {
        if (!ShouldRetryFailedSearch(needId))
            return;

        _lastSearchAttemptAt[(entityId, needId)] = _elapsedTime;
    }

    private static bool ShouldRetryFailedSearch(string needId)
        => string.Equals(needId, NeedIds.Hunger, StringComparison.OrdinalIgnoreCase)
            || string.Equals(needId, NeedIds.Thirst, StringComparison.OrdinalIgnoreCase);

    private static bool TryResolveNeedIdForJob(string jobDefId, out string needId)
    {
        if (string.Equals(jobDefId, Jobs.JobDefIds.Eat, StringComparison.OrdinalIgnoreCase))
        {
            needId = NeedIds.Hunger;
            return true;
        }

        if (string.Equals(jobDefId, Jobs.JobDefIds.Drink, StringComparison.OrdinalIgnoreCase))
        {
            needId = NeedIds.Thirst;
            return true;
        }

        if (string.Equals(jobDefId, Jobs.JobDefIds.Sleep, StringComparison.OrdinalIgnoreCase))
        {
            needId = NeedIds.Sleep;
            return true;
        }

        needId = string.Empty;
        return false;
    }
}

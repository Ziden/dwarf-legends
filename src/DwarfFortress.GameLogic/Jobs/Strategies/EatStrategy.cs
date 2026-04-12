using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

/// <summary>
/// Dwarf finds a food item, hauls it to a table/floor, and eats it.
/// </summary>
public sealed class EatStrategy : IJobStrategy
{
    public string JobDefId => JobDefIds.Eat;

    // How much of the hunger need is satisfied by eating one meal.
    private const float HungerSatisfaction = 0.8f;
    private const float FloorMealDuration = 2f;
    private const float TableMealDuration = 1.5f;
    private const float SeatedMealDuration = 1f;
    private const float ForageDuration = 2.5f;
    private const int PlantSearchRadius = 24;

    // A dwarf eating when hunger is above this is overeating → nausea
    private const float OvereatThreshold = 0.5f;
    // Duration of nausea in simulation seconds
    private const float NauseaDuration = 60f;

    public bool CanExecute(Job job, int dwarfId, GameContext ctx)
    {
        // Refuse if nauseous
        var registry = ctx.Get<Entities.EntityRegistry>();
        if (!registry.TryGetById<Entities.Dwarf>(dwarfId, out var dwarf) || dwarf is null)
            return false;

        if (dwarf.Components.TryGet<StatusEffectComponent>()?.Has(StatusEffectIds.Nausea) == true)
                return false;

        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        var reservedFood = ResolveReservedFood(job, itemSystem);
        if (reservedFood is not null)
        {
            if (reservedFood.CarriedByEntityId == dwarfId)
                return true;

            if (reservedFood.CarriedByEntityId >= 0)
                return false;

            var reservedInventory = dwarf.Components.TryGet<InventoryComponent>();
            return reservedInventory is null || !reservedInventory.IsFull;
        }

        var harvestTarget = ResolveHarvestTarget(job, dwarfId, ctx);

        if (itemSystem?.FindCarriedFoodItem(dwarfId) is not null)
            return true;

        // Harvest path — no inventory needed
        if (itemSystem?.FindFoodItem() is null)
            return harvestTarget is not null;

        // Pickup path — check inventory capacity
        var inventory = dwarf.Components.TryGet<InventoryComponent>();
        if (inventory is not null && inventory.IsFull)
            return harvestTarget is not null;

        return true;
    }

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        var reservedFood = ResolveReservedFood(job, itemSystem);
        if (reservedFood is not null)
        {
            ReserveFood(job, reservedFood);
            if (reservedFood.CarriedByEntityId == dwarfId)
                return BuildCarriedFoodSteps(dwarfId, ctx);

            if (reservedFood.CarriedByEntityId >= 0)
                return Array.Empty<ActionStep>();

            return BuildPickupFoodSteps(dwarfId, ctx, reservedFood);
        }

        var carriedFood = itemSystem?.FindCarriedFoodItem(dwarfId);
        if (carriedFood is not null)
        {
            ReserveFood(job, carriedFood);
            return BuildCarriedFoodSteps(dwarfId, ctx);
        }

        var entityRegistry = ctx.Get<Entities.EntityRegistry>();
        var inventory = entityRegistry.TryGetById<Entities.Dwarf>(dwarfId, out var actor) && actor is not null
            ? actor.Components.TryGet<InventoryComponent>()
            : null;
        var harvestTarget = ResolveHarvestTarget(job, dwarfId, ctx);
        var food       = itemSystem?.FindFoodItem();
        if ((inventory?.IsFull == true && harvestTarget is not null) || food is null)
        {
            if (harvestTarget is null)
                return Array.Empty<ActionStep>();

            return
            [
                new MoveToStep(harvestTarget.Value.StandPos),
                new WorkAtStep(Duration: ForageDuration, AnimationHint: "gather_plants", RequiredPosition: harvestTarget.Value.StandPos),
            ];
        }

        ReserveFood(job, food);
        return BuildPickupFoodSteps(dwarfId, ctx, food);
    }

    public void OnInterrupt(Job job, int dwarfId, GameContext ctx)
    {
        ReleaseReserved(job, ctx);
    }

    public void OnComplete(Job job, int dwarfId, GameContext ctx)
    {
        var entityRegistry = ctx.Get<Entities.EntityRegistry>();
        if (!entityRegistry.TryGetById<Entities.Dwarf>(dwarfId, out var dwarf) || dwarf is null) return;

        if (job.ReservedItemIds.Count > 0)
        {
            var hungerBefore = dwarf.Needs.Get(NeedIds.Hunger).Level;
            var dataManager = ctx.TryGet<DataManager>();
            var hungerMultiplier = AttributeEffectSystem.GetHungerSatisfactionMultiplier(dwarf, dataManager);
            dwarf.Needs.Get(NeedIds.Hunger).Satisfy(HungerSatisfaction * hungerMultiplier);

            // Emit satisfaction event to trigger cooldown in NeedsSystem
            ctx.EventBus.Emit(new Systems.NeedSatisfiedEvent(dwarfId, NeedIds.Hunger));

            // Overeating: was already well-fed before this meal → nausea
            if (hungerBefore >= OvereatThreshold)
            {
                var fx = dwarf.Components.TryGet<StatusEffectComponent>();
                fx?.Apply(StatusEffectIds.Nausea, NauseaDuration);
                ctx.EventBus.Emit(new EntityActivityEvent(dwarf.Id, "Ate too much and feels sick!", dwarf.Position.Position));
            }

            var itemSystem = ctx.TryGet<Systems.ItemSystem>();
            var consumedItemName = ResolveConsumedItemName(job, itemSystem, ctx);
            var consumedItemDef  = ResolveConsumedItemDef(job, itemSystem, ctx);
            foreach (var id in job.ReservedItemIds)
                itemSystem?.DestroyItem(id);
            job.ReservedItemIds.Clear();

            ctx.TryGet<Systems.NutritionSystem>()?.CreditMeal(dwarf.Id, consumedItemDef);
            ctx.EventBus.Emit(new EntityActivityEvent(dwarf.Id, $"Ate {consumedItemName}", dwarf.Position.Position));
            return;
        }

        var harvestPos = ResolveCompletionHarvestPos(job, dwarf, ctx);
        if (!harvestPos.HasValue || !PlantHarvesting.TryHarvestPlant(ctx, harvestPos.Value, dropHarvestItem: false, dropSeedItem: false, out var result))
            return;

        dwarf.Needs.Get(NeedIds.Hunger).Satisfy(HungerSatisfaction);

        // Emit satisfaction event to trigger cooldown in NeedsSystem
        ctx.EventBus.Emit(new Systems.NeedSatisfiedEvent(dwarfId, NeedIds.Hunger));

        var harvestItemDef = ctx.TryGet<DataManager>()?.Items.GetOrNull(result.HarvestItemDefId);
        ctx.TryGet<Systems.NutritionSystem>()?.CreditMeal(dwarf.Id, harvestItemDef);
        ctx.EventBus.Emit(new EntityActivityEvent(dwarf.Id, $"Foraged {result.HarvestDisplayName}", dwarf.Position.Position));
    }

    private static HarvestablePlantTarget? ResolveHarvestTarget(Job job, int dwarfId, GameContext ctx)
    {
        var map = ctx.TryGet<WorldMap>();
        var data = ctx.TryGet<DataManager>();
        if (map is null || data is null)
            return null;

        if (PlantHarvesting.TryGetHarvestablePlant(map, data, job.TargetPos, out var plantDef))
        {
            var standPos = PlantHarvesting.ResolveHarvestStandPosition(map, job.TargetPos);
            if (standPos.HasValue)
                return new HarvestablePlantTarget(job.TargetPos, standPos.Value, plantDef);
        }

        var entityRegistry = ctx.Get<Entities.EntityRegistry>();
        if (!entityRegistry.TryGetById<Entities.Dwarf>(dwarfId, out var dwarf) || dwarf is null)
            return null;

        return PlantHarvesting.TryFindNearestHarvestablePlant(map, data, dwarf.Position.Position, PlantSearchRadius, out var harvestTarget)
            ? harvestTarget
            : null;
    }

    private static Vec3i? ResolveCompletionHarvestPos(Job job, Entities.Dwarf dwarf, GameContext ctx)
    {
        var map = ctx.TryGet<WorldMap>();
        var data = ctx.TryGet<DataManager>();
        if (map is null || data is null)
            return null;

        if (PlantHarvesting.TryGetHarvestablePlant(map, data, job.TargetPos, out _))
            return job.TargetPos;

        return PlantHarvesting.TryFindNearestHarvestablePlant(map, data, dwarf.Position.Position, searchRadius: 2, out var harvestTarget)
            ? harvestTarget.PlantPos
            : null;
    }

    private static void ReleaseReserved(Job job, GameContext ctx)
    {
        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        if (itemSystem is null) return;
        var registry = ctx.Get<Entities.EntityRegistry>();
        var dropPos = registry.TryGetById<Entities.Dwarf>(job.AssignedDwarfId, out var dwarf) && dwarf is not null
            ? dwarf.Position.Position
            : job.TargetPos;
        foreach (var id in job.ReservedItemIds)
            if (itemSystem.TryGetItem(id, out var item) && item is not null)
            {
                if (item.CarriedByEntityId == job.AssignedDwarfId)
                    itemSystem.ReleaseCarriedItem(id, dropPos);
                item.IsClaimed = false;
            }
        job.ReservedItemIds.Clear();
    }

    // Maximum distance to search for dining spots — dwarves won't walk across the map for a table
    private const int DiningSearchRadius = 20;

    /// <summary>
    /// Finds the best dining spot for a dwarf, prioritizing seated meals at tables with chairs.
    /// Uses bounded search to avoid O(T×C) cross-join scans when many buildings exist.
    /// </summary>
    private static DiningSpot? FindDiningSpot(int dwarfId, GameContext ctx)
    {
        var entityRegistry = ctx.Get<Entities.EntityRegistry>();
        var buildingSystem = ctx.TryGet<BuildingSystem>();
        if (buildingSystem is null) return null;
        if (!entityRegistry.TryGetById<Entities.Dwarf>(dwarfId, out var dwarf) || dwarf is null) return null;

        var dwarfPos = dwarf.Position.Position;

        // First pass: find tables within search radius, prefer the nearest one
        var buildings = buildingSystem.GetAll();
        PlacedBuildingData? nearestTableInRadius = null;
        var nearestTableDist = int.MaxValue;

        foreach (var b in buildings)
        {
            if (b.BuildingDefId != BuildingDefIds.Table || b.Origin.Z != dwarfPos.Z)
                continue;

            var dist = b.Origin.ManhattanDistanceTo(dwarfPos);
            if (dist > DiningSearchRadius)
                continue;

            if (dist < nearestTableDist)
            {
                nearestTableDist = dist;
                nearestTableInRadius = b;
            }
        }

        // If we found a table in radius, check if there's an adjacent chair
        if (nearestTableInRadius is not null)
        {
            foreach (var b in buildings)
            {
                if (b.BuildingDefId != BuildingDefIds.Chair || b.Origin.Z != dwarfPos.Z)
                    continue;

                if (nearestTableInRadius.Origin.ManhattanDistanceTo(b.Origin) == 1)
                    return new DiningSpot(b.Origin, SeatedMealDuration);
            }

            // Table without chair — dwarf can still eat at the table
            return new DiningSpot(nearestTableInRadius.Origin, TableMealDuration);
        }

        // No table in radius — dwarf eats on the floor
        return null;
    }

    private sealed record DiningSpot(Vec3i Target, float Duration);

    private static IReadOnlyList<ActionStep> BuildCarriedFoodSteps(int dwarfId, GameContext ctx)
    {
        var registry = ctx.Get<Entities.EntityRegistry>();
        if (!registry.TryGetById<Entities.Dwarf>(dwarfId, out var dwarf) || dwarf is null)
            return Array.Empty<ActionStep>();

        var carriedDiningSpot = FindDiningSpot(dwarfId, ctx);
        var carriedSteps = new List<ActionStep>();
        if (carriedDiningSpot is not null)
            carriedSteps.Add(new MoveToStep(carriedDiningSpot.Target));

        carriedSteps.Add(new WorkAtStep(
            Duration: carriedDiningSpot?.Duration ?? FloorMealDuration,
            RequiredPosition: carriedDiningSpot?.Target ?? dwarf.Position.Position));
        return carriedSteps;
    }

    private static IReadOnlyList<ActionStep> BuildPickupFoodSteps(int dwarfId, GameContext ctx, Item food)
    {
        var steps = new List<ActionStep>
        {
            ItemPickupHelper.CreatePickupMoveStep(food),
            new PickUpItemStep(food.Id),
        };

        var diningSpot = FindDiningSpot(dwarfId, ctx);
        if (diningSpot is not null)
            steps.Add(new MoveToStep(diningSpot.Target));

        steps.Add(new WorkAtStep(
            Duration: diningSpot?.Duration ?? FloorMealDuration,
            RequiredPosition: diningSpot?.Target ?? ItemPickupHelper.ResolveConsumeWorkPosition(food)));

        return steps;
    }

    private static Item? ResolveReservedFood(Job job, Systems.ItemSystem? itemSystem)
    {
        if (itemSystem is null)
            return null;

        foreach (var itemId in job.ReservedItemIds)
            if (itemSystem.TryGetItem(itemId, out var reservedItem) && reservedItem is not null)
                return reservedItem;

        if (job.EntityId >= 0 && itemSystem.TryGetItem(job.EntityId, out var entityItem) && entityItem is not null)
            return entityItem;

        return null;
    }

    private static void ReserveFood(Job job, Item food)
    {
        food.IsClaimed = true;
        if (!job.ReservedItemIds.Contains(food.Id))
            job.ReservedItemIds.Add(food.Id);
    }

    private static string ResolveConsumedItemName(Job job, Systems.ItemSystem? itemSystem, GameContext ctx)
    {
        var reservedFood = ResolveReservedFood(job, itemSystem);
        if (reservedFood is not null)
            return ctx.TryGet<DataManager>()?.Items.GetOrNull(reservedFood.DefId)?.DisplayName
                   ?? reservedFood.DefId.Replace('_', ' ');

        return "food";
    }

    private static ItemDef? ResolveConsumedItemDef(Job job, Systems.ItemSystem? itemSystem, GameContext ctx)
    {
        var reservedFood = ResolveReservedFood(job, itemSystem);
        return reservedFood is null ? null : ctx.TryGet<DataManager>()?.Items.GetOrNull(reservedFood.DefId);
    }
}
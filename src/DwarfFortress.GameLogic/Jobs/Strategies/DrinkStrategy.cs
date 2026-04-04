using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

/// <summary>
/// Dwarf finds a drink source and satisfies thirst.
/// Prefers drink items; falls back to nearby natural water.
/// </summary>
public sealed class DrinkStrategy : IJobStrategy
{
    public string JobDefId => JobDefIds.Drink;

    // How much of the thirst need is satisfied by drinking once.
    private const float ThirstSatisfaction = 0.9f;
    private const int WaterSearchRadius = 14;

    // A dwarf drinking when thirst is above this is over-drinking → nausea
    private const float OverdrinkThreshold = 0.5f;
    private const float NauseaDuration = 60f;

    public bool CanExecute(Job job, int dwarfId, GameContext ctx)
    {
        var registry = ctx.Get<EntityRegistry>();
        if (!registry.TryGetById<Dwarf>(dwarfId, out var dwarf) || dwarf is null)
            return false;

        if (!dwarf.Needs.Thirst.IsCritical)
            return false;

        // Refuse if nauseous
        if (dwarf.Components.TryGet<StatusEffectComponent>()?.Has(StatusEffectIds.Nausea) == true)
            return false;

        var itemSystem = ctx.TryGet<ItemSystem>();
        if (itemSystem?.FindDrinkItem() is not null)
            return true;

        var map = ctx.Get<WorldMap>();
        return DrinkSourceLocator.CanDrinkAt(map, dwarf.Position.Position)
            || TryResolveDrinkTileTarget(ctx, map, dwarf.Position.Position, out _);
    }

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        if (!TryGetDwarfAndMap(dwarfId, ctx, out var dwarf, out var map))
            return Array.Empty<ActionStep>();

        var itemSystem = ctx.TryGet<ItemSystem>();
        var drink = itemSystem?.FindDrinkItem();
        if (drink is not null)
        {
            drink.IsClaimed = true;
            job.ReservedItemIds.Add(drink.Id);

            return new ActionStep[]
            {
                ItemPickupHelper.CreatePickupMoveStep(drink),
                new PickUpItemStep(drink.Id),
                new WorkAtStep(Duration: 1f, RequiredPosition: ItemPickupHelper.ResolveConsumeWorkPosition(drink)),
            };
        }

        var origin = dwarf.Position.Position;
        if (DrinkSourceLocator.CanDrinkAt(map, origin))
        {
            return new ActionStep[]
            {
                new WorkAtStep(Duration: 1f, RequiredPosition: origin),
            };
        }

        if (!TryResolveDrinkTileTarget(ctx, map, origin, out var drinkTile))
            return Array.Empty<ActionStep>();

        return new ActionStep[]
        {
            new MoveToStep(drinkTile, AcceptableDistance: 1, PreferAdjacent: true),
            new WorkAtStep(Duration: 1f),
        };
    }

    public void OnInterrupt(Job job, int dwarfId, GameContext ctx)
    {
        ReleaseReserved(job, ctx);
    }

    public void OnComplete(Job job, int dwarfId, GameContext ctx)
    {
        var entityRegistry = ctx.Get<EntityRegistry>();
        if (!entityRegistry.TryGetById<Dwarf>(dwarfId, out var dwarf) || dwarf is null)
            return;

        dwarf.Needs.Get(NeedIds.Thirst).Satisfy(ThirstSatisfaction);

        // Emit satisfaction event to trigger cooldown in NeedsSystem
        ctx.EventBus.Emit(new NeedSatisfiedEvent(dwarfId, NeedIds.Thirst));

        var itemSystem = ctx.TryGet<ItemSystem>();
        var consumedItemName = ResolveConsumedItemName(job, itemSystem, ctx);
        foreach (var id in job.ReservedItemIds)
            itemSystem?.DestroyItem(id);
        job.ReservedItemIds.Clear();

        ctx.EventBus.Emit(new EntityActivityEvent(dwarf.Id, $"Drank {consumedItemName}", dwarf.Position.Position));
    }

    private static bool TryGetDwarfAndMap(
        int dwarfId,
        GameContext ctx,
        out Dwarf dwarf,
        out WorldMap map)
    {
        dwarf = null!;
        map = null!;

        var entityRegistry = ctx.Get<EntityRegistry>();
        if (!entityRegistry.TryGetById<Dwarf>(dwarfId, out var found) || found is null)
            return false;

        dwarf = found;
        map = ctx.Get<WorldMap>();
        return true;
    }

    private static bool TryResolveDrinkTileTarget(GameContext ctx, WorldMap map, Vec3i origin, out Vec3i drinkTile)
    {
        if (DrinkSourceLocator.TryFindNearestDrinkableTile(map, origin, WaterSearchRadius, out drinkTile))
            return true;

        var fortressLocations = ctx.TryGet<FortressLocationSystem>();
        if (fortressLocations is null || !fortressLocations.TryGetClosestDrinkLocation(out drinkTile))
            return false;

        return DrinkSourceLocator.IsDrinkableWaterTile(map, drinkTile);
    }

    private static void ReleaseReserved(Job job, GameContext ctx)
    {
        var itemSystem = ctx.TryGet<ItemSystem>();
        if (itemSystem is null) return;
        var registry = ctx.Get<EntityRegistry>();
        var dropPos = registry.TryGetById<Dwarf>(job.AssignedDwarfId, out var dwarf) && dwarf is not null
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

    private static string ResolveConsumedItemName(Job job, ItemSystem? itemSystem, GameContext ctx)
    {
        if (itemSystem is not null)
        {
            foreach (var itemId in job.ReservedItemIds)
                if (itemSystem.TryGetItem(itemId, out var item) && item is not null)
                    return ctx.TryGet<DataManager>()?.Items.GetOrNull(item.DefId)?.DisplayName
                           ?? item.DefId.Replace('_', ' ');
        }

        return CanJobUseNaturalWater(job) ? "water" : "drink";
    }

    private static bool CanJobUseNaturalWater(Job job)
        => job.ReservedItemIds.Count == 0;
}

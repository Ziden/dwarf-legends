using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

/// <summary>
/// Fallback job when no higher-priority work exists.
/// If the dwarf is carrying storable items, idle time is used to unload them into stockpiles.
/// </summary>
public sealed class IdleStrategy : IJobStrategy
{
    public string JobDefId => JobDefIds.Idle;

    public bool CanExecute(Job job, int dwarfId, GameContext ctx) => true;

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        var itemSystem = ctx.TryGet<ItemSystem>();
        var stockpileManager = ctx.TryGet<StockpileManager>();
        if (itemSystem is not null && stockpileManager is not null)
        {
            var steps = new List<ActionStep>();
            foreach (var item in itemSystem.GetItemsCarriedBy(dwarfId).OrderBy(item => item.Id))
            {
                if (item.IsClaimed)
                    continue;
                if (!stockpileManager.TryReserveSlot(item, out var stockpileId, out var reservedSlot))
                    continue;

                item.IsClaimed = true;
                job.ReservedItemIds.Add(item.Id);
                job.ReservedStockpilePlacements.Add(new ReservedStockpilePlacement(item.Id, stockpileId, reservedSlot));
                steps.Add(new MoveToStep(reservedSlot));
                steps.Add(new PlaceItemStep(item.Id, reservedSlot));
            }

            if (steps.Count > 0)
                return steps;
        }

        // Check if inventory is full — shorter wait to force faster unload retry
        var registry = ctx.TryGet<EntityRegistry>();
        InventoryComponent? inventory = null;
        if (registry?.TryGetById<Dwarf>(dwarfId, out var dwarf) == true)
            inventory = dwarf?.Components.TryGet<InventoryComponent>();
        var waitDuration = inventory?.IsFull == true ? 0.5f : 2f;
        return new ActionStep[] { new WaitStep(Duration: waitDuration) };
    }

    public void OnInterrupt(Job job, int dwarfId, GameContext ctx)
    {
        ReleaseReservation(job, ctx);
    }

    public void OnComplete(Job job, int dwarfId, GameContext ctx)
    {
        var itemSystem = ctx.TryGet<ItemSystem>();
        var stockpileManager = ctx.TryGet<StockpileManager>();

        if (itemSystem is not null)
            foreach (var placement in job.ReservedStockpilePlacements)
                if (itemSystem.TryGetItem(placement.ItemEntityId, out var item) && item is not null)
                {
                    item.IsClaimed = false;
                    item.StockpileId = placement.StockpileId;
                }

        if (stockpileManager is not null)
            foreach (var placement in job.ReservedStockpilePlacements)
                stockpileManager.ConfirmStoredItem(placement.ItemEntityId, placement.StockpileId, placement.Slot);

        job.ReservedStockpileId = -1;
        job.ReservedSlot = null;
        job.ReservedItemIds.Clear();
        job.ReservedStockpilePlacements.Clear();
    }

    private static void ReleaseReservation(Job job, GameContext ctx)
    {
        var itemSystem = ctx.TryGet<ItemSystem>();
        if (itemSystem is not null)
            foreach (var id in job.ReservedItemIds)
                if (itemSystem.TryGetItem(id, out var item) && item is not null)
                    item.IsClaimed = false;

        var stockpileManager = ctx.TryGet<StockpileManager>();
        if (stockpileManager is not null)
            foreach (var placement in job.ReservedStockpilePlacements)
                stockpileManager.ReleaseSlotReservation(placement.StockpileId, placement.Slot);

        job.ReservedStockpileId = -1;
        job.ReservedSlot = null;
        job.ReservedItemIds.Clear();
        job.ReservedStockpilePlacements.Clear();
    }
}

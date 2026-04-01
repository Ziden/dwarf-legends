using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

/// <summary>
/// Hauls an unclaimed item to an appropriate stockpile.
/// </summary>
public sealed class HaulItemStrategy : IJobStrategy
{
    public string JobDefId => JobDefIds.HaulItem;

    public bool CanExecute(Job job, int dwarfId, GameContext ctx)
    {
        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        if (itemSystem is null) return false;
        if (job.EntityId >= 0)
            return itemSystem.TryGetItem(job.EntityId, out var item) && item is not null && !item.IsClaimed;

        return itemSystem.TryGetItemAt(job.TargetPos, out var fallbackItem) && !fallbackItem!.IsClaimed;
    }

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        if (itemSystem is null) return System.Array.Empty<ActionStep>();

        Item? item;
        if (job.EntityId >= 0)
        {
            if (!itemSystem.TryGetItem(job.EntityId, out item) || item is null)
                return System.Array.Empty<ActionStep>();
        }
        else if (!itemSystem.TryGetItemAt(job.TargetPos, out item) || item is null)
        {
            return System.Array.Empty<ActionStep>();
        }

        item.IsClaimed = true;
        job.ReservedItemIds.Add(item.Id);

        var stockpileManager = ctx.TryGet<Systems.StockpileManager>();
        var destPos = job.TargetPos;
        if (stockpileManager is not null && stockpileManager.TryReserveSlot(item, out var stockpileId, out var reservedSlot))
        {
            job.ReservedStockpileId = stockpileId;
            job.ReservedSlot        = reservedSlot;
            destPos                 = reservedSlot;
        }

        return new ActionStep[]
        {
            new MoveToStep(item.Position.Position),
            new PickUpItemStep(item.Id),
            new MoveToStep(destPos),
            new PlaceItemStep(item.Id, destPos),
        };
    }

    public void OnInterrupt(Job job, int dwarfId, GameContext ctx)
    {
        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        if (itemSystem is null) return;
        var registry = ctx.Get<Entities.EntityRegistry>();
        var dropPos = registry.TryGetById<Entities.Dwarf>(dwarfId, out var dwarf) && dwarf is not null
            ? dwarf.Position.Position
            : job.TargetPos;
        foreach (var id in job.ReservedItemIds)
            if (itemSystem.TryGetItem(id, out var item) && item is not null)
            {
                if (item.CarriedByEntityId == dwarfId)
                    itemSystem.ReleaseCarriedItem(id, dropPos);
                item.IsClaimed = false;
            }

        var stockpileManager = ctx.TryGet<Systems.StockpileManager>();
        if (stockpileManager is not null && job.ReservedStockpileId >= 0 && job.ReservedSlot.HasValue)
            stockpileManager.ReleaseSlotReservation(job.ReservedStockpileId, job.ReservedSlot.Value);

        job.ReservedStockpileId = -1;
        job.ReservedSlot        = null;
        job.ReservedItemIds.Clear();
    }

    public void OnComplete(Job job, int dwarfId, GameContext ctx)
    {
        var itemSystem       = ctx.TryGet<Systems.ItemSystem>();
        var stockpileManager = ctx.TryGet<Systems.StockpileManager>();

        if (itemSystem is not null)
            foreach (var id in job.ReservedItemIds)
                if (itemSystem.TryGetItem(id, out var item) && item is not null)
                {
                    item.IsClaimed = false;
                    item.ContainerBuildingId = -1;
                    item.CarriedByEntityId = -1;
                    item.StockpileId = job.ReservedStockpileId;
                }

        if (stockpileManager is not null && job.ReservedStockpileId >= 0 && job.ReservedSlot.HasValue)
            foreach (var id in job.ReservedItemIds)
                stockpileManager.ConfirmStoredItem(id, job.ReservedStockpileId, job.ReservedSlot.Value);

        job.ReservedStockpileId = -1;
        job.ReservedSlot        = null;
        job.ReservedItemIds.Clear();
    }
}

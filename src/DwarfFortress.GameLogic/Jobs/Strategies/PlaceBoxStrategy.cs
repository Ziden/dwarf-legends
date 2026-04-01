using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Items;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

/// <summary>
/// A dwarf picks up a loose box item, carries it to an empty stockpile slot,
/// and "places" it there. On completion the item is consumed and a <see cref="Box"/>
/// entity is created in its place, providing <see cref="Box.DefaultCapacity"/> item slots.
/// </summary>
public sealed class PlaceBoxStrategy : IJobStrategy
{
    public string JobDefId => JobDefIds.PlaceBox;

    public bool CanExecute(Job job, int dwarfId, GameContext ctx)
    {
        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        if (itemSystem is null) return false;
        if (job.EntityId < 0) return false;
        return itemSystem.TryGetItem(job.EntityId, out var item) && item is not null && !item.IsClaimed;
    }

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        if (itemSystem is null) return System.Array.Empty<ActionStep>();

        if (!itemSystem.TryGetItem(job.EntityId, out var item) || item is null)
            return System.Array.Empty<ActionStep>();

        item.IsClaimed = true;
        job.ReservedItemIds.Add(item.Id);

        return new ActionStep[]
        {
            new MoveToStep(item.Position.Position),
            new PickUpItemStep(item.Id),
            new MoveToStep(job.TargetPos),
            new PlaceItemStep(item.Id, job.TargetPos),
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

        job.ReservedItemIds.Clear();
    }

    public void OnComplete(Job job, int dwarfId, GameContext ctx)
    {
        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        if (itemSystem is null) return;
        var registry = ctx.Get<Entities.EntityRegistry>();

        foreach (var id in job.ReservedItemIds)
        {
            if (!itemSystem.TryGetItem(id, out var item) || item is null) continue;

            var pos = item.Position.Position;
            item.IsClaimed = false;
            item.CarriedByEntityId = -1;

            // Consume the box item and spawn a Box entity at the same tile
            itemSystem.DestroyItem(id);
            registry.Register(new Box(registry.NextId(), pos));
        }

        job.ReservedItemIds.Clear();
    }
}


using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct ProductionOrderCreatedEvent(int WorkshopId, string RecipeId, int Quantity);
public record struct ProductionOrderCancelledEvent(int WorkshopId, string RecipeId);
public record struct RecipeCraftedEvent(int WorkshopId, int DwarfId, string RecipeId, int[] OutputItemIds);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Holds the queue of production orders for one workshop entity.</summary>
public sealed class ProductionQueue
{
    public record Order(string RecipeId, int Remaining);

    private readonly Queue<Order> _orders = new();

    public void Enqueue(string recipeId, int qty) => _orders.Enqueue(new Order(recipeId, qty));
    public Order? Peek() => _orders.Count > 0 ? _orders.Peek() : null;
    public void Decrement()
    {
        if (_orders.Count == 0) return;
        var top = _orders.Dequeue();
        if (top.Remaining > 1) _orders.Enqueue(top with { Remaining = top.Remaining - 1 });
    }

    /// <summary>Remove a single order by zero-based index. No-op if index is out of range.</summary>
    public string? RemoveAt(int index)
    {
        if (index < 0 || index >= _orders.Count) return null;
        var list = _orders.ToList();
        var removed = list[index];
        list.RemoveAt(index);
        _orders.Clear();
        foreach (var o in list) _orders.Enqueue(o);
        return removed.RecipeId;
    }

    public IEnumerable<Order> All => _orders;
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Handles workshop production queues. Assigns craft jobs, consumes inputs,
/// and spawns output items on completion.
/// Order 12.
/// </summary>
public sealed class RecipeSystem : IGameSystem
{
    public string SystemId    => SystemIds.RecipeSystem;
    public int    UpdateOrder => 12;
    public bool   IsEnabled   { get; set; } = true;

    private readonly Dictionary<int, ProductionQueue> _queues = new();

    private GameContext? _ctx;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        ctx.Commands.Register<SetProductionOrderCommand>(OnSetProductionOrder);
        ctx.Commands.Register<CancelProductionOrderCommand>(OnCancelProductionOrder);
        ctx.EventBus.On<Jobs.JobCompletedEvent>(OnJobCompleted);
    }

    public void Tick(float delta)
    {
        // For each workshop with a queued order, ensure a craft job exists
        var jobSystem  = _ctx!.TryGet<Jobs.JobSystem>();
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        if (jobSystem is null || itemSystem is null) return;

        foreach (var (workshopId, queue) in _queues)
        {
            var order = queue.Peek();
            if (order is null) continue;

            var craftJobs = jobSystem.GetAllJobs()
                .Where(j => j.JobDefId == Jobs.JobDefIds.Craft && j.EntityId == workshopId)
                .ToList();

            if (craftJobs.Count > 0)
                continue;

            if (!CanStartOrder(order, itemSystem))
                continue;

            if (craftJobs.Count == 0)
            {
                var buildingSystem = _ctx!.TryGet<BuildingSystem>();
                var origin = buildingSystem?.GetById(workshopId)?.Origin;
                if (origin is null)
                {
                    _ctx.Logger?.Warn($"[RecipeSystem] Workshop entity {workshopId} not found — skipping craft job.");
                    continue;
                }
                jobSystem.CreateJob(Jobs.JobDefIds.Craft, origin.Value, priority: 3, entityId: workshopId);
            }
        }
    }

    public void OnSave(SaveWriter w)
    {
        var saved = _queues.Select(kv => new QueueDto
        {
            WorkshopId = kv.Key,
            Orders     = kv.Value.All.Select(o => new OrderDto
            {
                RecipeId  = o.RecipeId,
                Remaining = o.Remaining,
            }).ToList(),
        }).ToList();
        w.Write("productionQueues", saved);
    }

    public void OnLoad(SaveReader r)
    {
        var saved = r.TryRead<System.Collections.Generic.List<QueueDto>>("productionQueues");
        if (saved is null) return;

        _queues.Clear();
        foreach (var dto in saved)
        {
            var q = GetOrCreateQueue(dto.WorkshopId);
            foreach (var o in dto.Orders)
                q.Enqueue(o.RecipeId, o.Remaining);
        }
    }

    // ── Save model ─────────────────────────────────────────────────────────────

    private sealed class QueueDto
    {
        public int  WorkshopId { get; set; }
        public System.Collections.Generic.List<OrderDto> Orders { get; set; } = new();
    }

    private sealed class OrderDto
    {
        public string RecipeId  { get; set; } = "";
        public int    Remaining { get; set; } = 1;
    }

    // ── Queries ────────────────────────────────────────────────────────────

    public ProductionQueue GetOrCreateQueue(int workshopId)
    {
        if (!_queues.TryGetValue(workshopId, out var q))
            _queues[workshopId] = q = new ProductionQueue();
        return q;
    }

    // ── Private ────────────────────────────────────────────────────────────

    private void OnSetProductionOrder(SetProductionOrderCommand cmd)
    {
        GetOrCreateQueue(cmd.WorkshopEntityId).Enqueue(cmd.RecipeDefId, cmd.Quantity);
        _ctx!.EventBus.Emit(new ProductionOrderCreatedEvent(cmd.WorkshopEntityId, cmd.RecipeDefId, cmd.Quantity));
    }

    private void OnCancelProductionOrder(CancelProductionOrderCommand cmd)
    {
        if (!_queues.TryGetValue(cmd.WorkshopEntityId, out var queue)) return;
        var removedRecipeId = queue.RemoveAt(cmd.OrderIndex);
        if (removedRecipeId is not null)
            _ctx!.EventBus.Emit(new ProductionOrderCancelledEvent(cmd.WorkshopEntityId, removedRecipeId));
    }

    private void OnJobCompleted(Jobs.JobCompletedEvent e)
    {
        if (e.JobDefId != Jobs.JobDefIds.Craft) return;

        int workshopId = e.EntityId;
        if (workshopId < 0) return; // no valid workshop — stale or invalid job

        CraftInQueue(workshopId, e.DwarfId, e.ReservedItemIds ?? []);
    }

    private void CraftInQueue(int workshopId, int dwarfId, IReadOnlyList<int> reservedItemIds)
    {
        if (!_queues.TryGetValue(workshopId, out var queue)) return;
        var order = queue.Peek();
        if (order is null) return;
        TryCraft(workshopId, order, queue, dwarfId, reservedItemIds);
    }

    private bool TryCraft(int workshopId, ProductionQueue.Order order, ProductionQueue queue, int dwarfId, IReadOnlyList<int> reservedItemIds)
    {
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        var dm         = _ctx!.Get<DataManager>();
        var registry   = _ctx!.Get<EntityRegistry>();

        if (!registry.TryGetById<Dwarf>(dwarfId, out var dwarf) || dwarf is null) return false;
        if (!dm.Recipes.Contains(order.RecipeId)) return false;

        var recipe = dm.Recipes.Get(order.RecipeId);
        if (!ConsumeInputs(recipe, workshopId, itemSystem, reservedItemIds)) return false;

        // Spawn outputs at the workshop's world position
        var buildingSystem  = _ctx!.TryGet<BuildingSystem>();
        var workshopOrigin  = buildingSystem?.GetById(workshopId)?.Origin
                              ?? dwarf.Components.Get<PositionComponent>().Position;
        var outputIds = new List<int>();
        foreach (var output in recipe.Outputs)
            for (int i = 0; i < output.Quantity; i++)
            {
                var item = itemSystem?.CreateItem(output.ItemDefId, "unknown", workshopOrigin);
                if (item is not null) outputIds.Add(item.Id);
            }

        queue.Decrement();
        _ctx.EventBus.Emit(new RecipeCraftedEvent(workshopId, dwarfId, order.RecipeId, outputIds.ToArray()));
        return true;
    }

    private bool ConsumeInputs(RecipeDef recipe, int workshopId, ItemSystem? itemSystem, IReadOnlyList<int> reservedItemIds)
    {
        if (itemSystem is null) return false;
        if (reservedItemIds.Count > 0)
            return itemSystem.TryConsumeReservedInputs(recipe.Inputs, reservedItemIds);
        return itemSystem.TryConsumeInputs(recipe.Inputs);
    }

    private bool CanStartOrder(ProductionQueue.Order order, ItemSystem? itemSystem)
    {
        if (itemSystem is null) return false;

        var dm = _ctx!.Get<DataManager>();
        if (!dm.Recipes.Contains(order.RecipeId))
            return false;

        var recipe = dm.Recipes.Get(order.RecipeId);
        return itemSystem.CanFulfillInputs(recipe.Inputs);
    }
}

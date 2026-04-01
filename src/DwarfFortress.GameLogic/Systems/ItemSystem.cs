using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct ItemCreatedEvent  (int ItemId, string ItemDefId, Vec3i Position);
public record struct ItemDestroyedEvent(int ItemId, string ItemDefId);
public record struct ItemMovedEvent    (int ItemId, Vec3i OldPos, Vec3i NewPos);
public record struct ItemPickedUpEvent (int ItemId, string ItemDefId, int CarrierEntityId, Vec3i Position);
public record struct ItemDroppedEvent  (int ItemId, string ItemDefId, int CarrierEntityId, Vec3i Position, int ContainerBuildingId = -1);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Creates, destroys, and locates Item entities.
/// Order 3 — after EntityRegistry.
/// </summary>
public sealed class ItemSystem : IGameSystem
{
    public string SystemId    => SystemIds.ItemSystem;
    public int    UpdateOrder => 3;
    public bool   IsEnabled   { get; set; } = true;

    // Spatial lookup: pos → list of item IDs at that position
    private readonly Dictionary<Vec3i, List<int>> _byPos   = new();
    private readonly Dictionary<int, Item>        _items   = new();

    private GameContext? _ctx;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
    }

    public void Tick(float delta) { /* items don't self-tick */ }

    public void OnSave(SaveWriter w)
    {
        var saved = new System.Collections.Generic.List<SavedItem>();
        foreach (var item in _items.Values)
        {
            var pos = item.Components.Get<PositionComponent>().Position;
            var corpse = item.Components.TryGet<CorpseComponent>();
            var rot = item.Components.TryGet<RotComponent>();
            saved.Add(new SavedItem
            {
                Id          = item.Id,
                DefId       = item.DefId,
                MaterialId  = item.MaterialId,
                X           = pos.X,
                Y           = pos.Y,
                Z           = pos.Z,
                StackSize   = item.StackSize,
                Quality     = (int)item.Quality,
                StockpileId = item.StockpileId,
                ContainerBuildingId = item.ContainerBuildingId,
                ContainerItemId = item.ContainerItemId,
                CarriedByEntityId = item.CarriedByEntityId,
                CorpseFormerEntityId = corpse?.FormerEntityId ?? -1,
                CorpseFormerDefId = corpse?.FormerDefId,
                CorpseDisplayName = corpse?.DisplayName,
                CorpseDeathCause = corpse?.DeathCause,
                RotProgress = rot?.Progress,
            });
        }
        w.Write("items", saved);
    }

    public void OnLoad(SaveReader r)
    {
        var saved = r.TryRead<System.Collections.Generic.List<SavedItem>>("items");
        if (saved is null) return;

        _items.Clear();
        _byPos.Clear();

        var restoredBySavedId = new Dictionary<int, Item>();
        foreach (var s in saved)
        {
            var pos  = new Vec3i(s.X, s.Y, s.Z);
            var item = CreateItem(s.DefId, s.MaterialId ?? string.Empty, pos, s.StackSize);
            item.Quality     = (Entities.ItemQuality)s.Quality;
            item.StockpileId = s.StockpileId;
            item.ContainerBuildingId = s.ContainerBuildingId;
            if (s.CorpseFormerEntityId >= 0)
                item.Components.Add(new CorpseComponent(
                    s.CorpseFormerEntityId,
                    s.CorpseFormerDefId ?? string.Empty,
                    s.CorpseDisplayName ?? string.Empty,
                    s.CorpseDeathCause ?? string.Empty));
            if (s.RotProgress is float rotProgress)
            {
                var rot = new RotComponent();
                rot.Restore(rotProgress);
                item.Components.Add(rot);
            }

            restoredBySavedId[s.Id] = item;
        }

        foreach (var s in saved)
        {
            if (!restoredBySavedId.TryGetValue(s.Id, out var item))
                continue;

            if (s.CarriedByEntityId >= 0)
            {
                SetCarriedState(item, s.CarriedByEntityId, isCarried: true);
                continue;
            }

            if (s.ContainerItemId >= 0 && restoredBySavedId.TryGetValue(s.ContainerItemId, out var container))
                StoreItemInItem(item.Id, container.Id, item.Position.Position);
        }
    }

    // ── Save model ─────────────────────────────────────────────────────────

    private sealed class SavedItem
    {
        public int     Id           { get; set; }
        public string  DefId       { get; set; } = "";
        public string? MaterialId  { get; set; }
        public int     X           { get; set; }
        public int     Y           { get; set; }
        public int     Z           { get; set; }
        public int     StackSize   { get; set; } = 1;
        public int     Quality     { get; set; }
        public int     StockpileId { get; set; } = -1;
        public int     ContainerBuildingId { get; set; } = -1;
        public int     ContainerItemId { get; set; } = -1;
        public int     CarriedByEntityId { get; set; } = -1;
        public int     CorpseFormerEntityId { get; set; } = -1;
        public string? CorpseFormerDefId { get; set; }
        public string? CorpseDisplayName { get; set; }
        public string? CorpseDeathCause { get; set; }
        public float?  RotProgress { get; set; }
    }

    // ── Factory ────────────────────────────────────────────────────────────

    /// <summary>Creates a new item entity and registers it spatially.</summary>
    public Item CreateItem(string itemDefId, string materialId, Vec3i position, int stackSize = 1)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        var item     = new Item(registry.NextId(), itemDefId, position)
        {
            MaterialId = materialId,
            StackSize  = stackSize,
        };

        registry.Register(item);
        _items[item.Id] = item;
        AddToPos(item.Id, position);

        _ctx.EventBus.Emit(new ItemCreatedEvent(item.Id, itemDefId, position));
        return item;
    }

    /// <summary>Removes an item from the world entirely.</summary>
    public void DestroyItem(int itemId)
    {
        if (!_items.TryGetValue(itemId, out var item)) return;

        var pos = item.Components.Get<PositionComponent>().Position;
        foreach (var child in GetItemsInItem(itemId).ToArray())
            MoveItem(child.Id, pos);

        RemoveFromCarrierInventory(item);
        if (item.CarriedByEntityId < 0 && item.ContainerItemId < 0)
            RemoveFromPos(itemId, pos);
        _items.Remove(itemId);

        var defId = item.DefId;
        _ctx!.Get<EntityRegistry>().Kill(itemId, "destroyed");
        _ctx.EventBus.Emit(new ItemDestroyedEvent(itemId, defId));
    }

    /// <summary>Moves an item to a new position, updating spatial index.</summary>
    public void MoveItem(int itemId, Vec3i newPos, int containerBuildingId = -1)
    {
        if (!_items.TryGetValue(itemId, out var item)) return;

        var pos = item.Components.Get<PositionComponent>();
        var old = pos.Position;
        var previousCarrierEntityId = item.CarriedByEntityId;
        RemoveFromCarrierInventory(item);
        if (item.CarriedByEntityId < 0 && item.ContainerItemId < 0)
            RemoveFromPos(itemId, old);
        pos.Position = newPos;
        item.StockpileId = -1;
        item.ContainerBuildingId = -1;
        item.ContainerItemId = -1;
        item.CarriedByEntityId = -1;
        AddToPos(itemId, newPos);

        _ctx!.EventBus.Emit(new ItemMovedEvent(itemId, old, newPos));
        if (previousCarrierEntityId >= 0)
            _ctx.EventBus.Emit(new ItemDroppedEvent(item.Id, item.DefId, previousCarrierEntityId, newPos, containerBuildingId));
    }

    public void StoreItemInBuilding(int itemId, int buildingId, Vec3i newPos)
    {
        MoveItem(itemId, newPos, buildingId);
        if (_items.TryGetValue(itemId, out var item))
            item.ContainerBuildingId = buildingId;
    }

    public void StoreItemInItem(int itemId, int containerItemId, Vec3i newPos)
    {
        if (!_items.TryGetValue(itemId, out var item) || !_items.ContainsKey(containerItemId) || itemId == containerItemId)
            return;

        var pos = item.Components.Get<PositionComponent>();
        RemoveFromCarrierInventory(item);
        if (item.CarriedByEntityId < 0 && item.ContainerItemId < 0)
            RemoveFromPos(itemId, pos.Position);

        pos.Position = newPos;
        item.StockpileId = -1;
        item.ContainerBuildingId = -1;
        item.CarriedByEntityId = -1;
        item.ContainerItemId = containerItemId;
    }

    public void PickUpItem(int itemId, int carrierEntityId, Vec3i carrierPos)
    {
        if (!_items.TryGetValue(itemId, out var item)) return;

        // Remove from box container if the item was stored inside one
        if (item.ContainerItemId >= 0)
        {
            var registry = _ctx?.TryGet<EntityRegistry>();
            if (registry is not null && registry.TryGetById<Box>(item.ContainerItemId, out var containerBox))
                containerBox?.Container.Remove(itemId);
        }

        var pos = item.Components.Get<PositionComponent>();
        RemoveFromCarrierInventory(item);
        if (item.CarriedByEntityId < 0)
            RemoveFromPos(itemId, pos.Position);
        pos.Position = carrierPos;
        item.StockpileId = -1;
        item.ContainerBuildingId = -1;
        item.ContainerItemId = -1;
        item.CarriedByEntityId = carrierEntityId;
        AddToCarrierInventory(item);

        _ctx!.EventBus.Emit(new ItemPickedUpEvent(item.Id, item.DefId, carrierEntityId, carrierPos));
    }

    public void UpdateCarriedItemsPosition(int carrierEntityId, Vec3i carrierPos)
    {
        foreach (var item in GetItemsCarriedBy(carrierEntityId))
            item.Components.Get<PositionComponent>().Position = carrierPos;
    }

    public IEnumerable<Item> GetItemsCarriedBy(int carrierEntityId)
    {
        var registry = _ctx?.Get<EntityRegistry>();
        var inventory = registry?.TryGetById(carrierEntityId)?.Components.TryGet<InventoryComponent>();
        if (inventory is null)
            return _items.Values.Where(item => item.CarriedByEntityId == carrierEntityId);

        return inventory.CarriedItemIds
            .Select(itemId => _items.TryGetValue(itemId, out var item) ? item : null)
            .Where(item => item is not null)
            .Select(item => item!);
    }

    public void ReleaseCarriedItem(int itemId, Vec3i dropPos)
    {
        if (!_items.TryGetValue(itemId, out var item)) return;

        var carrierEntityId = item.CarriedByEntityId;
        RemoveFromCarrierInventory(item);
        item.Components.Get<PositionComponent>().Position = dropPos;
        item.CarriedByEntityId = -1;
        item.ContainerBuildingId = -1;
        item.ContainerItemId = -1;
        item.StockpileId = -1;
        AddToPos(itemId, dropPos);

        _ctx!.EventBus.Emit(new ItemDroppedEvent(item.Id, item.DefId, carrierEntityId, dropPos));
    }

    // ── Queries ────────────────────────────────────────────────────────────

    public bool TryGetItem(int itemId, out Item? item)
        => _items.TryGetValue(itemId, out item);

    public bool TryGetItemAt(Vec3i pos, out Item? item)
    {
        item = null;
        if (!_byPos.TryGetValue(pos, out var list) || list.Count == 0) return false;
        _items.TryGetValue(list[0], out item);
        return item is not null;
    }

    public IEnumerable<Item> GetItemsAt(Vec3i pos)
    {
        if (!_byPos.TryGetValue(pos, out var list)) yield break;
        foreach (var id in list)
            if (_items.TryGetValue(id, out var it))
                yield return it;
    }

    public IEnumerable<Item> GetAllItems() => _items.Values;

    public IEnumerable<Item> GetItemsInItem(int containerItemId)
        => _items.Values.Where(item => item.ContainerItemId == containerItemId);

    /// <summary>Returns items stored inside a Box entity.</summary>
    public IEnumerable<Item> GetItemsInBox(int boxEntityId)
        => _items.Values.Where(item => item.ContainerItemId == boxEntityId);

    /// <summary>
    /// Places an item inside a Box entity, setting its position to the box tile.
    /// The box's ContainerComponent is updated.
    /// </summary>
    public void StoreItemInBox(int itemId, Box box, int stockpileId = -1)
    {
        if (!_items.TryGetValue(itemId, out var item)) return;
        var pos = item.Components.Get<PositionComponent>();
        RemoveFromCarrierInventory(item);
        if (item.CarriedByEntityId < 0 && item.ContainerItemId < 0)
            RemoveFromPos(itemId, pos.Position);
        pos.Position = box.Position.Position;
        item.StockpileId = stockpileId;
        item.ContainerBuildingId = -1;
        item.CarriedByEntityId = -1;
        item.ContainerItemId = box.Id;
        box.Container.TryAdd(itemId);
    }

    public IEnumerable<Item> GetItemsInBuilding(int buildingId)
        => _items.Values.Where(item => item.ContainerBuildingId == buildingId);

    public IEnumerable<Item> GetUsableItems() =>
        _items.Values.Where(IsUsableItem);

    public IEnumerable<Item> GetUnstoredUsableItems() =>
        GetUsableItems().Where(item => item.StockpileId < 0);

    public IEnumerable<Item> GetUnclaimedItems() => GetUnstoredUsableItems();

    public bool CanFulfillInputs(IReadOnlyList<RecipeInput> inputs)
    {
        return TryMatchInputIds(inputs, GetUsableItems().ToList(), out _);
    }

    public bool TryReserveInputs(IReadOnlyList<RecipeInput> inputs, List<int> reservedIds)
    {
        if (!TryMatchInputIds(inputs, GetUsableItems().ToList(), out var matchedIds))
            return false;

        foreach (var itemId in matchedIds)
            if (_items.TryGetValue(itemId, out var item))
                item.IsClaimed = true;

        reservedIds.AddRange(matchedIds);
        return true;
    }

    public bool TryConsumeReservedInputs(IReadOnlyList<RecipeInput> inputs, IReadOnlyList<int> reservedIds)
    {
        var reservedItems = reservedIds
            .Select(id => _items.TryGetValue(id, out var item) ? item : null)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();

        if (!TryMatchInputIds(inputs, reservedItems, out var matchedIds))
            return false;

        foreach (var itemId in matchedIds)
            DestroyItem(itemId);

        return true;
    }

    public bool TryConsumeInputs(IReadOnlyList<RecipeInput> inputs)
    {
        if (!TryMatchInputIds(inputs, GetUsableItems().ToList(), out var consumedIds))
            return false;

        foreach (var itemId in consumedIds)
            DestroyItem(itemId);

        return true;
    }

    /// <summary>Returns a usable food item anywhere in the fortress, including inside boxes.</summary>
    public Item? FindFoodItem() =>
        GetUsableItems().FirstOrDefault(i => HasTag(i, TagIds.Food))
        ?? FindItemInBoxes(TagIds.Food);

    /// <summary>Returns a usable drink item anywhere in the fortress, including inside boxes.</summary>
    public Item? FindDrinkItem() =>
        GetUsableItems().FirstOrDefault(i => HasTag(i, TagIds.Drink))
        ?? FindItemInBoxes(TagIds.Drink);

    private Item? FindItemInBoxes(string tag)
    {
        var registry = _ctx?.TryGet<EntityRegistry>();
        if (registry is null) return null;
        foreach (var box in registry.GetAlive<Box>())
            foreach (var itemId in box.Container.StoredItemIds)
                if (_items.TryGetValue(itemId, out var item) && !item.IsClaimed && HasTag(item, tag))
                    return item;
        return null;
    }

    // ── Private ────────────────────────────────────────────────────────────

    private void AddToPos(int itemId, Vec3i pos)
    {
        if (!_byPos.TryGetValue(pos, out var list))
            _byPos[pos] = list = new List<int>();
        if (!list.Contains(itemId))
            list.Add(itemId);
    }

    private void RemoveFromPos(int itemId, Vec3i pos)
    {
        if (_byPos.TryGetValue(pos, out var list))
        {
            list.Remove(itemId);
            if (list.Count == 0)
                _byPos.Remove(pos);
        }
    }

    private void SetCarriedState(Item item, int carrierEntityId, bool isCarried)
    {
        if (isCarried)
        {
            RemoveFromCarrierInventory(item);
            if (item.ContainerItemId < 0)
                RemoveFromPos(item.Id, item.Position.Position);
            item.CarriedByEntityId = carrierEntityId;
            item.StockpileId = -1;
            item.ContainerBuildingId = -1;
            item.ContainerItemId = -1;
            AddToCarrierInventory(item);
            return;
        }

        RemoveFromCarrierInventory(item);
        item.CarriedByEntityId = -1;
    }

    private void AddToCarrierInventory(Item item)
    {
        if (item.CarriedByEntityId < 0 || _ctx is null)
            return;

        var carrier = _ctx.Get<EntityRegistry>().TryGetById(item.CarriedByEntityId);
        carrier?.Components.TryGet<InventoryComponent>()?.AddCarriedItem(item.Id);
    }

    private void RemoveFromCarrierInventory(Item item)
    {
        if (item.CarriedByEntityId < 0 || _ctx is null)
            return;

        var carrier = _ctx.Get<EntityRegistry>().TryGetById(item.CarriedByEntityId);
        carrier?.Components.TryGet<InventoryComponent>()?.RemoveCarriedItem(item.Id);
    }

    private bool HasTag(Item item, string tag)
    {
        var dm = _ctx?.TryGet<Data.DataManager>();
        if (dm is null) return false;
        var def = dm.Items.GetOrNull(item.DefId);
        return def?.Tags.Contains(tag) ?? false;
    }

    private static bool IsUsableItem(Item item)
        => !item.IsClaimed && item.CarriedByEntityId < 0 && item.ContainerItemId < 0;

    private bool MatchesInput(Item item, TagSet requiredTags)
    {
        var dm = _ctx?.TryGet<Data.DataManager>();
        var def = dm?.Items.GetOrNull(item.DefId);
        return def?.Tags.HasAll(requiredTags.All.ToArray()) ?? false;
    }

    private bool TryMatchInputs(
        IReadOnlyList<TagSet> requiredTags,
        int index,
        IReadOnlyList<Item> availableItems,
        HashSet<int> matchedIds,
        List<int> consumedIds)
    {
        if (index >= requiredTags.Count)
            return true;

        var tags = requiredTags[index];
        var candidates = availableItems
            .Where(item => !matchedIds.Contains(item.Id) && MatchesInput(item, tags))
            .OrderBy(item => CountMatchingRequirements(item, requiredTags, index + 1))
            .ToList();

        foreach (var candidate in candidates)
        {
            matchedIds.Add(candidate.Id);
            consumedIds.Add(candidate.Id);

            if (TryMatchInputs(requiredTags, index + 1, availableItems, matchedIds, consumedIds))
                return true;

            consumedIds.RemoveAt(consumedIds.Count - 1);
            matchedIds.Remove(candidate.Id);
        }

        return false;
    }

    private bool TryMatchInputIds(IReadOnlyList<RecipeInput> inputs, IReadOnlyList<Item> availableItems, out List<int> matchedIds)
    {
        var requiredTags = inputs
            .SelectMany(input => Enumerable.Repeat(input.RequiredTags, input.Quantity))
            .OrderByDescending(tags => tags.Count)
            .ToList();

        matchedIds = new List<int>();
        return TryMatchInputs(requiredTags, 0, availableItems, new HashSet<int>(), matchedIds);
    }

    private int CountMatchingRequirements(Item item, IReadOnlyList<TagSet> requiredTags, int startIndex)
    {
        int count = 0;
        for (int index = startIndex; index < requiredTags.Count; index++)
            if (MatchesInput(item, requiredTags[index]))
                count++;
        return count;
    }
}

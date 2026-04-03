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
                CarryMode = item.CarryMode,
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
                SetCarriedState(
                    item,
                    s.CarriedByEntityId,
                    s.CarryMode == ItemCarryMode.None ? ItemCarryMode.Inventory : s.CarryMode);
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
        public ItemCarryMode CarryMode { get; set; } = ItemCarryMode.None;
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
        if (!_items.TryGetValue(itemId, out var item))
            return;

        var pos = item.Components.Get<PositionComponent>().Position;
        foreach (var child in GetItemsInItem(itemId).ToArray())
            MoveItem(child.Id, pos);

        DetachFromCurrentLocation(item, pos);
        _items.Remove(itemId);

        var defId = item.DefId;
        _ctx!.Get<EntityRegistry>().Kill(itemId, "destroyed");
        _ctx.EventBus.Emit(new ItemDestroyedEvent(itemId, defId));
    }

    /// <summary>Moves an item to a new position, updating spatial index.</summary>
    public void MoveItem(int itemId, Vec3i newPos, int containerBuildingId = -1)
    {
        if (!_items.TryGetValue(itemId, out var item))
            return;

        var pos = item.Components.Get<PositionComponent>();
        var old = pos.Position;
        var previousCarrierEntityId = item.CarriedByEntityId;
        DetachFromCurrentLocation(item, old);
        pos.Position = newPos;
        item.StockpileId = -1;
        item.ContainerBuildingId = -1;
        item.ContainerItemId = -1;
        item.CarriedByEntityId = -1;
        item.CarryMode = ItemCarryMode.None;
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
        DetachFromCurrentLocation(item, pos.Position);

        pos.Position = newPos;
        item.StockpileId = -1;
        item.ContainerBuildingId = -1;
        item.CarriedByEntityId = -1;
        item.CarryMode = ItemCarryMode.None;
        item.ContainerItemId = containerItemId;
    }

    public bool PickUpItem(int itemId, int carrierEntityId, Vec3i carrierPos, ItemCarryMode carryMode = ItemCarryMode.Inventory)
    {
        if (!_items.TryGetValue(itemId, out var item) || carryMode == ItemCarryMode.None)
            return false;

        if (carryMode == ItemCarryMode.Inventory &&
            _ctx?.TryGet<EntityRegistry>() is { } entityRegistry &&
            entityRegistry.TryGetById<Dwarf>(carrierEntityId, out var dwarf) && dwarf is not null)
        {
            var dm = _ctx.TryGet<DataManager>();
            var itemWeight = WeightSystem.GetItemWeight(item, dm);
            if (itemWeight > 0f && !(_ctx.TryGet<WeightSystem>()?.CanPickUpItem(dwarf, itemWeight, this) ?? true))
                return false;
        }

        var pos = item.Components.Get<PositionComponent>();
        var previousPosition = pos.Position;
        var previousStockpileId = item.StockpileId;
        var previousContainerBuildingId = item.ContainerBuildingId;
        var previousContainerItemId = item.ContainerItemId;
        var previousCarrierEntityId = item.CarriedByEntityId;
        var previousCarryMode = item.CarryMode;
        var wasIndexedInWorld = IsIndexedInWorld(item);
        var previousBoxContainer = ResolveBoxContainer(item.ContainerItemId);

        DetachFromCurrentLocation(item, previousPosition);

        item.CarriedByEntityId = carrierEntityId;
        item.CarryMode = carryMode;
        item.StockpileId = -1;
        item.ContainerBuildingId = -1;
        item.ContainerItemId = -1;

        // Check inventory capacity
        if (!AddToCarrier(item))
        {
            // Inventory full — revert carrier assignment
            item.CarriedByEntityId = previousCarrierEntityId;
            item.CarryMode = previousCarryMode;
            item.StockpileId = previousStockpileId;
            item.ContainerBuildingId = previousContainerBuildingId;
            item.ContainerItemId = previousContainerItemId;
            pos.Position = previousPosition;

            if (previousCarrierEntityId >= 0 && previousCarryMode != ItemCarryMode.None)
                AddToCarrier(item);
            else if (wasIndexedInWorld)
                AddToPos(itemId, previousPosition);

            previousBoxContainer?.Container.TryAdd(itemId);
            return false;
        }

        pos.Position = carrierPos;

        _ctx!.EventBus.Emit(new ItemPickedUpEvent(item.Id, item.DefId, carrierEntityId, carrierPos));
        return true;
    }

    public void UpdateCarriedItemsPosition(int carrierEntityId, Vec3i carrierPos)
    {
        foreach (var item in GetItemsCarriedBy(carrierEntityId))
            item.Components.Get<PositionComponent>().Position = carrierPos;
    }

    public IEnumerable<Item> GetItemsCarriedBy(int carrierEntityId)
    {
        var carrier = _ctx?.TryGet<EntityRegistry>()?.TryGetById(carrierEntityId);
        var yieldedIds = new HashSet<int>();

        if (carrier?.Components.TryGet<InventoryComponent>() is { } inventory)
        {
            foreach (var itemId in inventory.CarriedItemIds)
            {
                if (!_items.TryGetValue(itemId, out var item))
                    continue;

                yieldedIds.Add(item.Id);
                yield return item;
            }
        }

        if (carrier?.Components.TryGet<HaulingComponent>() is { IsHauling: true } hauling
            && _items.TryGetValue(hauling.HauledItemId, out var hauledItem))
        {
            yieldedIds.Add(hauledItem.Id);
            yield return hauledItem;
        }

        foreach (var item in _items.Values)
            if (item.CarriedByEntityId == carrierEntityId && yieldedIds.Add(item.Id))
                yield return item;
    }

    public bool TryGetHauledItem(int carrierEntityId, out Item? item)
    {
        item = null;
        var hauling = _ctx?.TryGet<EntityRegistry>()?
            .TryGetById(carrierEntityId)?
            .Components
            .TryGet<HaulingComponent>();
        if (hauling is not null && hauling.IsHauling)
            return _items.TryGetValue(hauling.HauledItemId, out item);

        foreach (var candidate in _items.Values)
        {
            if (candidate.CarriedByEntityId != carrierEntityId || candidate.CarryMode != ItemCarryMode.Hauling)
                continue;

            item = candidate;
            return true;
        }

        return false;
    }

    public void ReleaseCarriedItem(int itemId, Vec3i dropPos)
    {
        if (!_items.TryGetValue(itemId, out var item)) return;

        var carrierEntityId = item.CarriedByEntityId;
        DetachFromCurrentLocation(item, item.Components.Get<PositionComponent>().Position);
        item.Components.Get<PositionComponent>().Position = dropPos;
        item.CarriedByEntityId = -1;
        item.CarryMode = ItemCarryMode.None;
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

    public IEnumerable<Item> GetLooseItemsAt(Vec3i pos)
    {
        foreach (var item in GetItemsAt(pos))
            if (IsLooseWorldItem(item))
                yield return item;
    }

    public void CollectLooseItemsInBounds(int z, int minX, int minY, int maxX, int maxY, List<int> results)
    {
        results.Clear();
        if (maxX < minX || maxY < minY)
            return;

        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            if (!_byPos.TryGetValue(new Vec3i(x, y, z), out var itemIds))
                continue;

            foreach (var itemId in itemIds)
            {
                if (_items.TryGetValue(itemId, out var item) && IsLooseWorldItem(item))
                    results.Add(itemId);
            }
        }
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
        DetachFromCurrentLocation(item, pos.Position);
        pos.Position = box.Position.Position;
        item.StockpileId = stockpileId;
        item.ContainerBuildingId = -1;
        item.CarriedByEntityId = -1;
        item.CarryMode = ItemCarryMode.None;
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
        var data = _ctx?.TryGet<DataManager>();
        return data is not null &&
               RecipeResolver.TryMatchInputs(data, inputs, GetUsableItems().ToList(), out _);
    }

    public bool CanFulfillRecipe(RecipeDef recipe)
    {
        var data = _ctx?.TryGet<DataManager>();
        if (data is null)
            return false;

        return RecipeResolver.TryMatchRecipe(data, recipe, GetUsableItems().ToList(), out _, out _);
    }

    public bool TryReserveInputs(IReadOnlyList<RecipeInput> inputs, List<int> reservedIds)
    {
        var data = _ctx?.TryGet<DataManager>();
        if (data is null ||
            !RecipeResolver.TryMatchInputs(data, inputs, GetUsableItems().ToList(), out var matchedItems))
            return false;

        return ClaimMatchedItems(matchedItems, reservedIds);
    }

    public bool TryReserveRecipeInputs(RecipeDef recipe, List<int> reservedIds)
    {
        var data = _ctx?.TryGet<DataManager>();
        if (data is null ||
            !RecipeResolver.TryMatchRecipe(data, recipe, GetUsableItems().ToList(), out var matchedItems, out _))
        {
            return false;
        }

        return ClaimMatchedItems(matchedItems, reservedIds);
    }

    public bool TryConsumeReservedInputs(IReadOnlyList<RecipeInput> inputs, IReadOnlyList<int> reservedIds)
    {
        var data = _ctx?.TryGet<DataManager>();
        if (data is null ||
            !TryResolveReservedInputItems(data, inputs, reservedIds, out var matchedItems))
            return false;

        DestroyMatchedItems(matchedItems);
        return true;
    }

    public bool TryConsumeInputs(IReadOnlyList<RecipeInput> inputs)
        => TryConsumeInputs(inputs, out _);

    public bool TryConsumeInputs(IReadOnlyList<RecipeInput> inputs, out List<Item> consumedItems)
    {
        consumedItems = new List<Item>();
        var data = _ctx?.TryGet<DataManager>();
        if (data is null ||
            !RecipeResolver.TryMatchInputs(data, inputs, GetUsableItems().ToList(), out var matchedItems))
        {
            return false;
        }

        consumedItems = matchedItems.ToList();
        DestroyMatchedItems(matchedItems);
        return true;
    }

    /// <summary>Returns a usable food item anywhere in the fortress, including inside boxes.</summary>
    public Item? FindFoodItem() =>
        GetUsableItems().FirstOrDefault(i => HasTag(i, TagIds.Food))
        ?? FindItemInBoxes(TagIds.Food);

    /// <summary>Returns an unclaimed food item currently carried by the specified entity.</summary>
    public Item? FindCarriedFoodItem(int carrierEntityId)
        => GetItemsCarriedBy(carrierEntityId).FirstOrDefault(item => !item.IsClaimed && HasTag(item, TagIds.Food));

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

    private void DetachFromCurrentLocation(Item item, Vec3i pos)
    {
        ResolveBoxContainer(item.ContainerItemId)?.Container.Remove(item.Id);
        RemoveFromCarrier(item);
        if (IsIndexedInWorld(item))
            RemoveFromPos(item.Id, pos);
    }

    private Box? ResolveBoxContainer(int containerItemId)
    {
        if (containerItemId < 0 || _ctx?.TryGet<EntityRegistry>() is not { } registry)
            return null;

        return registry.TryGetById<Box>(containerItemId, out var box) ? box : null;
    }

    private static bool IsIndexedInWorld(Item item)
        => item.CarriedByEntityId < 0 && item.ContainerItemId < 0;

    private void SetCarriedState(Item item, int carrierEntityId, ItemCarryMode carryMode)
    {
        DetachFromCurrentLocation(item, item.Position.Position);
        item.CarriedByEntityId = carrierEntityId;
        item.CarryMode = carryMode == ItemCarryMode.None ? ItemCarryMode.Inventory : carryMode;
        item.StockpileId = -1;
        item.ContainerBuildingId = -1;
        item.ContainerItemId = -1;
        AddToCarrier(item);
    }

    private bool AddToCarrier(Item item)
    {
        if (item.CarriedByEntityId < 0 || item.CarryMode == ItemCarryMode.None || _ctx is null)
            return false;

        var carrier = _ctx.Get<EntityRegistry>().TryGetById(item.CarriedByEntityId);
        if (carrier is null)
            return false;

        return item.CarryMode switch
        {
            ItemCarryMode.Inventory => carrier.Components.TryGet<InventoryComponent>()?.TryAddCarriedItem(item.Id) == true,
            ItemCarryMode.Hauling => carrier.Components.TryGet<HaulingComponent>()?.TryStartHauling(item.Id) == true,
            _ => false,
        };
    }

    private void RemoveFromCarrier(Item item)
    {
        if (item.CarriedByEntityId < 0 || _ctx is null)
            return;

        var carrier = _ctx.Get<EntityRegistry>().TryGetById(item.CarriedByEntityId);
        carrier?.Components.TryGet<InventoryComponent>()?.RemoveCarriedItem(item.Id);
        carrier?.Components.TryGet<HaulingComponent>()?.StopHauling(item.Id);
    }

    private bool HasTag(Item item, string tag)
    {
        var dm = _ctx?.TryGet<Data.DataManager>();
        if (dm is null) return false;
        var def = dm.Items.GetOrNull(item.DefId);
        return def?.Tags.Contains(tag) ?? false;
    }

    public static bool IsLooseWorldItem(Item item)
        => item.CarriedByEntityId < 0 && item.ContainerItemId < 0 && item.ContainerBuildingId < 0;

    private static bool IsUsableItem(Item item)
        => !item.IsClaimed && item.CarriedByEntityId < 0 && item.ContainerItemId < 0;

    private bool ClaimMatchedItems(IReadOnlyList<Item> matchedItems, List<int> reservedIds)
    {
        foreach (var matchedItem in matchedItems)
        {
            if (!_items.TryGetValue(matchedItem.Id, out var item))
                return false;
        }

        foreach (var matchedItem in matchedItems)
        {
            var item = _items[matchedItem.Id];
            item.IsClaimed = true;
            reservedIds.Add(item.Id);
        }

        return true;
    }

    private void DestroyMatchedItems(IReadOnlyList<Item> matchedItems)
    {
        foreach (var matchedItem in matchedItems)
            DestroyItem(matchedItem.Id);
    }

    private bool TryResolveReservedInputItems(
        DataManager data,
        IReadOnlyList<RecipeInput> inputs,
        IReadOnlyList<int> reservedIds,
        out List<Item> matchedItems)
    {
        var reservedItems = reservedIds
            .Select(id => _items.TryGetValue(id, out var item) ? item : null)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();

        return RecipeResolver.TryMatchInputs(data, inputs, reservedItems, out matchedItems);
    }
}

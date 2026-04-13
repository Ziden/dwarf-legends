using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct StockpileCreatedEvent (int StockpileId, Vec3i From, Vec3i To, int OwnerBuildingId = -1);
public record struct StockpileRemovedEvent (int StockpileId);
public record struct ItemStoredEvent       (int ItemId, int StockpileId, Vec3i SlotPos);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Represents a stockpile zone accepting specific item categories.</summary>
public sealed class StockpileData
{
    public int        Id             { get; init; }
    public Vec3i      From           { get; init; }
    public Vec3i      To             { get; init; }
    public string[]   AcceptedTags   { get; set; } = System.Array.Empty<string>();
    public int        OwnerBuildingId { get; set; } = -1;
    public HashSet<Vec3i> OccupiedSlots { get; } = new();

    public IEnumerable<Vec3i> AllSlots()
    {
        for (int x = System.Math.Min(From.X, To.X); x <= System.Math.Max(From.X, To.X); x++)
        for (int y = System.Math.Min(From.Y, To.Y); y <= System.Math.Max(From.Y, To.Y); y++)
        for (int z = System.Math.Min(From.Z, To.Z); z <= System.Math.Max(From.Z, To.Z); z++)
            yield return new Vec3i(x, y, z);
    }

    public bool Accepts(string itemTag) =>
        AcceptedTags.Length == 0 || AcceptedTags.Any(t => t == itemTag);

    public Vec3i? FindOpenSlot()
    {
        foreach (var slot in AllSlots())
            if (!OccupiedSlots.Contains(slot))
                return slot;

        return null;
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages stockpile zones and triggers haul jobs for unstored items.
/// Order 4 — after ItemSystem.
/// </summary>
public sealed class StockpileManager : IGameSystem
{
    public string SystemId    => SystemIds.StockpileManager;
    public int    UpdateOrder => 4;
    public bool   IsEnabled   { get; set; } = true;

    private readonly Dictionary<int, StockpileData> _stockpiles = new();
    private int _nextStockpileId = 1;

    private float      _haulingTimer = 0f;
    private const float HaulingInterval = 2f;   // seconds between haul-scan passes

    private GameContext? _ctx;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        ctx.Commands.Register<CreateStockpileCommand>(OnCreateStockpile);
        ctx.Commands.Register<RemoveStockpileCommand>(OnRemoveStockpile);
    }

    public void Tick(float delta)
    {
        _haulingTimer += delta;
        if (_haulingTimer < HaulingInterval) return;
        _haulingTimer = 0f;

        EnqueuePlaceBoxJobs();
        EnqueueHaulJobsForUnstored();
    }

    public void OnSave(SaveWriter w)
    {
        w.Write("nextStockpileId", _nextStockpileId);

        var saved = _stockpiles.Values.Select(sp => new StockpileDto
        {
            Id           = sp.Id,
            FromX        = sp.From.X, FromY = sp.From.Y, FromZ = sp.From.Z,
            ToX          = sp.To.X,   ToY   = sp.To.Y,   ToZ   = sp.To.Z,
            AcceptedTags = sp.AcceptedTags.ToList(),
            OwnerBuildingId = sp.OwnerBuildingId,
            OccupiedSlots = sp.OccupiedSlots.Select(s => new SlotDto { X = s.X, Y = s.Y, Z = s.Z }).ToList(),
        }).ToList();

        w.Write("stockpiles", saved);
    }

    public void OnLoad(SaveReader r)
    {
        _nextStockpileId = r.TryRead<int>("nextStockpileId");

        var saved = r.TryRead<System.Collections.Generic.List<StockpileDto>>("stockpiles");
        if (saved is null) return;

        _stockpiles.Clear();
        foreach (var dto in saved)
        {
            var sp = new StockpileData
            {
                Id           = dto.Id,
                From         = new Vec3i(dto.FromX, dto.FromY, dto.FromZ),
                To           = new Vec3i(dto.ToX,   dto.ToY,   dto.ToZ),
                AcceptedTags = dto.AcceptedTags.ToArray(),
                OwnerBuildingId = dto.OwnerBuildingId,
            };
            foreach (var slot in dto.OccupiedSlots)
                sp.OccupiedSlots.Add(new Vec3i(slot.X, slot.Y, slot.Z));
            _stockpiles[sp.Id] = sp;
        }
    }

    // ── Save model ─────────────────────────────────────────────────────────────

    private sealed class StockpileDto
    {
        public int                 Id           { get; set; }
        public int FromX { get; set; } public int FromY { get; set; } public int FromZ { get; set; }
        public int ToX   { get; set; } public int ToY   { get; set; } public int ToZ   { get; set; }
        public System.Collections.Generic.List<string> AcceptedTags { get; set; } = new();
        public int OwnerBuildingId { get; set; } = -1;
        public System.Collections.Generic.List<SlotDto> OccupiedSlots { get; set; } = new();
    }

    private sealed class SlotDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }

    // ── Stockpile queries ──────────────────────────────────────────────────

    public IEnumerable<StockpileData> GetAll() => _stockpiles.Values;

    public StockpileData? GetById(int id) =>
        _stockpiles.TryGetValue(id, out var s) ? s : null;

    public StockpileData? GetByOwnerBuilding(int buildingId)
        => _stockpiles.Values.FirstOrDefault(stockpile => stockpile.OwnerBuildingId == buildingId);

    public StockpileData? GetContaining(Vec3i position)
        => _stockpiles.Values.FirstOrDefault(stockpile =>
            position.X >= System.Math.Min(stockpile.From.X, stockpile.To.X) && position.X <= System.Math.Max(stockpile.From.X, stockpile.To.X) &&
            position.Y >= System.Math.Min(stockpile.From.Y, stockpile.To.Y) && position.Y <= System.Math.Max(stockpile.From.Y, stockpile.To.Y) &&
            position.Z >= System.Math.Min(stockpile.From.Z, stockpile.To.Z) && position.Z <= System.Math.Max(stockpile.From.Z, stockpile.To.Z));

    public int CreateStockpile(Vec3i from, Vec3i to, string[] acceptedTags, int ownerBuildingId = -1)
    {
        var stockpile = new StockpileData
        {
            Id = _nextStockpileId++,
            From = from,
            To = to,
            AcceptedTags = acceptedTags,
            OwnerBuildingId = ownerBuildingId,
        };

        _stockpiles[stockpile.Id] = stockpile;
        _ctx!.EventBus.Emit(new StockpileCreatedEvent(stockpile.Id, stockpile.From, stockpile.To, stockpile.OwnerBuildingId));
        return stockpile.Id;
    }

    public bool RemoveStockpile(int stockpileId)
    {
        if (!_stockpiles.Remove(stockpileId))
            return false;

        var itemSystem = _ctx?.TryGet<ItemSystem>();
        if (itemSystem is not null)
        {
            foreach (var item in itemSystem.GetAllItems().Where(i => i.StockpileId == stockpileId))
                item.StockpileId = -1;
        }

        _ctx!.EventBus.Emit(new StockpileRemovedEvent(stockpileId));
        return true;
    }

    /// <summary>Finds a free slot in any stockpile that accepts the item.
    /// Slots occupied by a non-full Box entity are considered open.</summary>
    public Vec3i? FindOpenSlot(Item item)
    {
        var dm = _ctx?.TryGet<Data.DataManager>();
        var registry = _ctx?.TryGet<Entities.EntityRegistry>();
        foreach (var sp in GetCandidateStockpiles(item, dm))
        {
            if (dm is not null && sp.AcceptedTags.Length > 0)
            {
                var def = dm.Items.GetOrNull(item.DefId);
                if (def is not null && !def.Tags.HasAny(sp.AcceptedTags)) continue;
            }
            var slot = FindOpenSlotInStockpile(sp, registry);
            if (slot.HasValue) return slot;
        }
        return null;
    }

    public bool TryReserveSlot(Item item, out int stockpileId, out Vec3i slot)
    {
        var dm = _ctx?.TryGet<Data.DataManager>();
        var registry = _ctx?.TryGet<Entities.EntityRegistry>();
        foreach (var sp in GetCandidateStockpiles(item, dm))
        {
            if (dm is not null && sp.AcceptedTags.Length > 0)
            {
                var def = dm.Items.GetOrNull(item.DefId);
                if (def is not null && !def.Tags.HasAny(sp.AcceptedTags)) continue;
            }

            var open = FindOpenSlotInStockpile(sp, registry);
            if (!open.HasValue) continue;

            // Only mark occupied if the slot has no box (boxes manage their own capacity)
            var boxAtSlot = GetBoxAt(open.Value, registry);
            if (boxAtSlot is null)
                sp.OccupiedSlots.Add(open.Value);

            stockpileId = sp.Id;
            slot = open.Value;
            return true;
        }

        stockpileId = -1;
        slot = default;
        return false;
    }

    private static Vec3i? FindOpenSlotInStockpile(StockpileData sp, Entities.EntityRegistry? registry)
    {
        foreach (var slot in sp.AllSlots())
        {
            if (!sp.OccupiedSlots.Contains(slot))
                return slot;

            // A box entity at this slot with free capacity acts as an open slot
            var box = GetBoxAt(slot, registry);
            if (box is not null && !box.Container.IsFull)
                return slot;
        }
        return null;
    }

    private static Entities.Box? GetBoxAt(Vec3i pos, Entities.EntityRegistry? registry)
        => registry?.GetAlive<Entities.Box>().FirstOrDefault(b => b.Position.Position == pos);

    private IEnumerable<StockpileData> GetCandidateStockpiles(Item item, Data.DataManager? dm)
    {
        var def = dm?.Items.GetOrNull(item.DefId);
        if (def is null)
            return _stockpiles.Values;

        var ordered = new List<StockpileData>();
        var preferredTags = GetPreferredAcceptedTags(item, def);

        AddMatchingStockpiles(ordered, sp => AcceptsAny(sp, preferredTags));
        AddMatchingStockpiles(ordered, sp => sp.AcceptedTags.Length > 0 && def.Tags.HasAny(sp.AcceptedTags));
        AddMatchingStockpiles(ordered, sp => sp.AcceptedTags.Length == 0);

        return ordered.Count > 0 ? ordered : _stockpiles.Values;

        void AddMatchingStockpiles(List<StockpileData> destination, System.Func<StockpileData, bool> predicate)
        {
            foreach (var stockpile in _stockpiles.Values)
            {
                if (destination.Contains(stockpile) || !predicate(stockpile))
                    continue;

                destination.Add(stockpile);
            }
        }
    }

    private static bool AcceptsAny(StockpileData stockpile, IReadOnlyList<string> tags)
    {
        if (stockpile.AcceptedTags.Length == 0 || tags.Count == 0)
            return false;

        foreach (var acceptedTag in stockpile.AcceptedTags)
            foreach (var preferredTag in tags)
                if (string.Equals(acceptedTag, preferredTag, System.StringComparison.OrdinalIgnoreCase))
                    return true;

        return false;
    }

    private static IReadOnlyList<string> GetPreferredAcceptedTags(Item item, ItemDef def)
    {
        if (def.Tags.HasAny(TagIds.Corpse, TagIds.Refuse) || item.Components.TryGet<CorpseComponent>() is not null)
            return [TagIds.Corpse, TagIds.Refuse];

        if (def.Tags.Contains(TagIds.Seed))
            return [TagIds.Seed];

        if (def.Tags.Contains(TagIds.Food))
            return [TagIds.Food];

        return System.Array.Empty<string>();
    }

    public void ReleaseSlotReservation(int stockpileId, Vec3i slot)
    {
        if (_stockpiles.TryGetValue(stockpileId, out var sp))
            sp.OccupiedSlots.Remove(slot);
    }

    public void ConfirmStoredItem(int itemId, int stockpileId, Vec3i slot)
    {
        if (!_stockpiles.TryGetValue(stockpileId, out var sp)) return;

        var registry = _ctx?.TryGet<Entities.EntityRegistry>();
        var box = GetBoxAt(slot, registry);
        if (box is not null)
        {
            // Store item inside the box container; mark slot occupied only when full
            box.Container.TryAdd(itemId);
            if (box.Container.IsFull)
                sp.OccupiedSlots.Add(slot);
            // Track the item's container
            var itemSystem = _ctx?.TryGet<ItemSystem>();
            if (itemSystem is not null && itemSystem.TryGetItem(itemId, out var boxItem) && boxItem is not null)
                boxItem.ContainerItemId = box.Id;
        }
        else
        {
            sp.OccupiedSlots.Add(slot);
        }

        _ctx!.EventBus.Emit(new ItemStoredEvent(itemId, stockpileId, slot));
    }

    // ── Private ────────────────────────────────────────────────────────────

    /// <summary>
    /// Scan stockpiles for slots that have no Box entity and enqueue PlaceBox jobs
    /// for any unclaimed loose box items found in the world.
    /// </summary>
    private void EnqueuePlaceBoxJobs()
    {
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        var jobSystem  = _ctx!.TryGet<Jobs.JobSystem>();
        var registry   = _ctx!.TryGet<Entities.EntityRegistry>();
        if (itemSystem is null || jobSystem is null || registry is null) return;

        // Collect loose unplaced box items
        var looseBoxes = itemSystem.GetUnstoredUsableItems()
            .Where(i => i.DefId == Items.ItemDefIds.Box && !i.IsClaimed)
            .ToList();
        if (looseBoxes.Count == 0) return;

        // Find stockpile slots that don't yet have a Box entity
        foreach (var sp in _stockpiles.Values)
        {
            foreach (var slot in sp.AllSlots())
            {
                if (GetBoxAt(slot, registry) is not null) continue;
                if (sp.OccupiedSlots.Contains(slot)) continue;

                // Pick the nearest unclaimed box item
                var boxItem = looseBoxes.FirstOrDefault(b => !b.IsClaimed);
                if (boxItem is null) return;

                // Don't double-enqueue
                bool alreadyQueued = jobSystem.GetAllJobs()
                    .Any(j => j.JobDefId == Jobs.JobDefIds.PlaceBox && j.EntityId == boxItem.Id);
                if (alreadyQueued) continue;

                jobSystem.CreateJob(Jobs.JobDefIds.PlaceBox,
                    slot,
                    priority: 3,
                    entityId: boxItem.Id);
                boxItem.IsClaimed = true;  // Prevent double-assignment before job starts
                break;
            }
        }
    }

    private void EnqueueHaulJobsForUnstored()
    {
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        var jobSystem  = _ctx!.TryGet<Jobs.JobSystem>();
        if (itemSystem is null || jobSystem is null) return;

        foreach (var item in itemSystem.GetUnstoredUsableItems())
        {
            if (item.ContainerBuildingId >= 0) continue;

            // Check a stockpile slot exists for this item
            var dest = FindOpenSlot(item);
            if (dest is null) continue;

            // Don't double-enqueue jobs for same item position
            bool alreadyQueued = jobSystem.GetAllJobs()
                .Any(j => j.JobDefId == Jobs.JobDefIds.HaulItem &&
                          j.EntityId == item.Id);
            if (alreadyQueued) continue;

            jobSystem.CreateJob(Jobs.JobDefIds.HaulItem,
                item.Components.Get<PositionComponent>().Position,
                priority: 2,
                entityId: item.Id);
        }
    }

    // ── Command handlers ───────────────────────────────────────────────────

    private void OnCreateStockpile(CreateStockpileCommand cmd)
    {
        CreateStockpile(cmd.From, cmd.To, cmd.AcceptedTags, cmd.OwnerBuildingId);
    }

    private void OnRemoveStockpile(RemoveStockpileCommand cmd)
    {
        RemoveStockpile(cmd.StockpileId);
    }
}

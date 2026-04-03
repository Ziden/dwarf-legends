using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;

namespace DwarfFortress.GameLogic.Systems;

/// <summary>
/// Incremental spatial index for live gameplay queries. This is distinct from SaveGameSystem:
/// it is updated by world events and movement events instead of being rebuilt every frame.
///
/// Optimization: Uses HashSet instead of List for O(1) contains/add/remove operations.
/// </summary>
public sealed class SpatialIndexSystem : IGameSystem
{
    private static readonly int[] NoIds = [];

    private readonly Dictionary<Vec3i, HashSet<int>> _dwarvesByTile = new();
    private readonly Dictionary<Vec3i, HashSet<int>> _creaturesByTile = new();
    private readonly Dictionary<Vec3i, HashSet<int>> _containersByTile = new();
    private readonly Dictionary<Vec3i, HashSet<int>> _itemsByTile = new();
    private readonly Dictionary<Vec3i, int> _buildingByTile = new();

    private readonly Dictionary<int, Vec3i> _dwarfPositions = new();
    private readonly Dictionary<int, Vec3i> _creaturePositions = new();
    private readonly Dictionary<int, Vec3i> _containerPositions = new();
    private readonly Dictionary<int, Vec3i> _itemPositions = new();

    private GameContext? _ctx;

    public string SystemId => SystemIds.SpatialIndexSystem;
    public int UpdateOrder => 4;
    public bool IsEnabled { get; set; } = true;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;

        ctx.EventBus.On<EntitySpawnedEvent>(OnEntitySpawned);
        ctx.EventBus.On<EntityKilledEvent>(OnEntityKilled);
        ctx.EventBus.On<EntityMovedEvent>(OnEntityMoved);

        ctx.EventBus.On<ItemCreatedEvent>(OnItemCreated);
        ctx.EventBus.On<ItemDestroyedEvent>(OnItemDestroyed);
        ctx.EventBus.On<ItemMovedEvent>(OnItemMoved);

        ctx.EventBus.On<BuildingPlacedEvent>(OnBuildingPlaced);
        ctx.EventBus.On<BuildingRemovedEvent>(OnBuildingRemoved);

        Rebuild();
    }

    public void Tick(float delta) { }
    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) => Rebuild();

    public IReadOnlyCollection<int> GetDwarvesAt(Vec3i pos)
        => _dwarvesByTile.TryGetValue(pos, out var ids) ? ids : System.Array.Empty<int>();

    public IReadOnlyCollection<int> GetCreaturesAt(Vec3i pos)
        => _creaturesByTile.TryGetValue(pos, out var ids) ? ids : System.Array.Empty<int>();

    public IReadOnlyCollection<int> GetContainersAt(Vec3i pos)
        => _containersByTile.TryGetValue(pos, out var ids) ? ids : System.Array.Empty<int>();

    public IReadOnlyCollection<int> GetItemsAt(Vec3i pos)
        => _itemsByTile.TryGetValue(pos, out var ids) ? ids : System.Array.Empty<int>();

    public int? GetBuildingAt(Vec3i pos)
        => _buildingByTile.TryGetValue(pos, out var buildingId) ? buildingId : null;

    public void CollectDwarvesInBounds(int z, int minX, int minY, int maxX, int maxY, List<int> results)
        => CollectIdsInBounds(_dwarvesByTile, z, minX, minY, maxX, maxY, results);

    public void CollectCreaturesInBounds(int z, int minX, int minY, int maxX, int maxY, List<int> results)
        => CollectIdsInBounds(_creaturesByTile, z, minX, minY, maxX, maxY, results);

    public void CollectContainersInBounds(int z, int minX, int minY, int maxX, int maxY, List<int> results)
        => CollectIdsInBounds(_containersByTile, z, minX, minY, maxX, maxY, results);

    public void CollectItemsInBounds(int z, int minX, int minY, int maxX, int maxY, List<int> results)
        => CollectIdsInBounds(_itemsByTile, z, minX, minY, maxX, maxY, results);

    private void Rebuild()
    {
        _dwarvesByTile.Clear();
        _creaturesByTile.Clear();
        _containersByTile.Clear();
        _itemsByTile.Clear();
        _buildingByTile.Clear();
        _dwarfPositions.Clear();
        _creaturePositions.Clear();
        _containerPositions.Clear();
        _itemPositions.Clear();

        if (_ctx is null) return;

        var registry = _ctx.Get<EntityRegistry>();
        foreach (var dwarf in registry.GetAlive<Dwarf>())
            AddEntity(_dwarvesByTile, _dwarfPositions, dwarf.Id, dwarf.Position.Position);

        foreach (var creature in registry.GetAlive<Creature>())
            AddEntity(_creaturesByTile, _creaturePositions, creature.Id, creature.Position.Position);

        foreach (var entity in registry.GetAlive<Entity>())
        {
            if (entity is Item)
                continue;

            var position = entity.Components.TryGet<Entities.Components.PositionComponent>();
            var container = entity.Components.TryGet<Entities.Components.ContainerComponent>();
            if (position is null || container is null)
                continue;

            AddEntity(_containersByTile, _containerPositions, entity.Id, position.Position);
        }

        var itemSystem = _ctx.TryGet<ItemSystem>();
        if (itemSystem is not null)
            foreach (var item in itemSystem.GetAllItems())
                AddEntity(_itemsByTile, _itemPositions, item.Id, item.Position.Position);

        var buildingSystem = _ctx.TryGet<BuildingSystem>();
        if (buildingSystem is not null)
            foreach (var building in buildingSystem.GetAll())
                AddBuilding(building.Id, building.BuildingDefId, building.Origin);
    }

    private void OnEntitySpawned(EntitySpawnedEvent e)
    {
        if (_ctx is null) return;
        var entity = _ctx.Get<EntityRegistry>().TryGetById(e.EntityId);
        switch (entity)
        {
            case Dwarf dwarf:
                AddEntity(_dwarvesByTile, _dwarfPositions, dwarf.Id, dwarf.Position.Position);
                break;
            case Creature creature:
                AddEntity(_creaturesByTile, _creaturePositions, creature.Id, creature.Position.Position);
                break;
            case { } occupant when occupant is not Item:
                var position = occupant.Components.TryGet<Entities.Components.PositionComponent>();
                var container = occupant.Components.TryGet<Entities.Components.ContainerComponent>();
                if (position is not null && container is not null)
                    AddEntity(_containersByTile, _containerPositions, occupant.Id, position.Position);
                break;
        }
    }

    private void OnEntityKilled(EntityKilledEvent e)
    {
        if (_dwarfPositions.TryGetValue(e.EntityId, out var dwarfPos))
            RemoveEntity(_dwarvesByTile, _dwarfPositions, e.EntityId, dwarfPos);

        if (_creaturePositions.TryGetValue(e.EntityId, out var creaturePos))
            RemoveEntity(_creaturesByTile, _creaturePositions, e.EntityId, creaturePos);

        if (_containerPositions.TryGetValue(e.EntityId, out var containerPos))
            RemoveEntity(_containersByTile, _containerPositions, e.EntityId, containerPos);
    }

    private void OnEntityMoved(EntityMovedEvent e)
    {
        if (_dwarfPositions.ContainsKey(e.EntityId))
        {
            MoveEntity(_dwarvesByTile, _dwarfPositions, e.EntityId, e.OldPos, e.NewPos);
            return;
        }

        if (_creaturePositions.ContainsKey(e.EntityId))
        {
            MoveEntity(_creaturesByTile, _creaturePositions, e.EntityId, e.OldPos, e.NewPos);
            return;
        }

        if (_containerPositions.ContainsKey(e.EntityId))
            MoveEntity(_containersByTile, _containerPositions, e.EntityId, e.OldPos, e.NewPos);
    }

    private void OnItemCreated(ItemCreatedEvent e)
        => AddEntity(_itemsByTile, _itemPositions, e.ItemId, e.Position);

    private void OnItemDestroyed(ItemDestroyedEvent e)
    {
        if (_itemPositions.TryGetValue(e.ItemId, out var pos))
            RemoveEntity(_itemsByTile, _itemPositions, e.ItemId, pos);
    }

    private void OnItemMoved(ItemMovedEvent e)
        => MoveEntity(_itemsByTile, _itemPositions, e.ItemId, e.OldPos, e.NewPos);

    private void OnBuildingPlaced(BuildingPlacedEvent e)
        => AddBuilding(e.BuildingId, e.BuildingDefId, e.Origin);

    private void OnBuildingRemoved(BuildingRemovedEvent e)
        => RemoveBuilding(e.BuildingId, e.BuildingDefId, e.Origin);

    private void AddBuilding(int buildingId, string buildingDefId, Vec3i origin)
    {
        if (_ctx is null) return;

        var def = _ctx.Get<DataManager>().Buildings.GetOrNull(buildingDefId);
        if (def is null)
        {
            _buildingByTile[origin] = buildingId;
            return;
        }

        foreach (var tile in def.Footprint)
            _buildingByTile[new Vec3i(origin.X + tile.Offset.X, origin.Y + tile.Offset.Y, origin.Z)] = buildingId;
    }

    private void RemoveBuilding(int buildingId, string buildingDefId, Vec3i origin)
    {
        if (_ctx is null) return;

        var def = _ctx.Get<DataManager>().Buildings.GetOrNull(buildingDefId);
        if (def is null)
        {
            if (_buildingByTile.TryGetValue(origin, out var existingId) && existingId == buildingId)
                _buildingByTile.Remove(origin);
            return;
        }

        foreach (var tile in def.Footprint)
        {
            var pos = new Vec3i(origin.X + tile.Offset.X, origin.Y + tile.Offset.Y, origin.Z);
            if (_buildingByTile.TryGetValue(pos, out var existingId) && existingId == buildingId)
                _buildingByTile.Remove(pos);
        }
    }

    private static void AddEntity(Dictionary<Vec3i, HashSet<int>> byTile, Dictionary<int, Vec3i> positions, int id, Vec3i pos)
    {
        if (!byTile.TryGetValue(pos, out var ids))
            byTile[pos] = ids = new HashSet<int>();

        ids.Add(id); // HashSet.Add() is no-op if already present — O(1) instead of List's O(n)

        positions[id] = pos;
    }

    private static void RemoveEntity(Dictionary<Vec3i, HashSet<int>> byTile, Dictionary<int, Vec3i> positions, int id, Vec3i pos)
    {
        if (byTile.TryGetValue(pos, out var ids))
        {
            ids.Remove(id);
            if (ids.Count == 0)
                byTile.Remove(pos);
        }

        positions.Remove(id);
    }

    private static void MoveEntity(Dictionary<Vec3i, HashSet<int>> byTile, Dictionary<int, Vec3i> positions, int id, Vec3i oldPos, Vec3i newPos)
    {
        if (oldPos == newPos) return;
        RemoveEntity(byTile, positions, id, oldPos);
        AddEntity(byTile, positions, id, newPos);
    }

    private static void CollectIdsInBounds(
        Dictionary<Vec3i, HashSet<int>> byTile,
        int z,
        int minX,
        int minY,
        int maxX,
        int maxY,
        List<int> results)
    {
        results.Clear();
        if (maxX < minX || maxY < minY)
            return;

        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            if (!byTile.TryGetValue(new Vec3i(x, y, z), out var ids))
                continue;

            results.AddRange(ids);
        }
    }
}

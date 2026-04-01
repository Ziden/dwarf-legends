using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

public record struct BuildingPlacedEvent(int BuildingId, string BuildingDefId, Vec3i Origin, bool IsWorkshop);
public record struct BuildingRemovedEvent(int BuildingId, string BuildingDefId, Vec3i Origin);
public record struct BuildingPlacementRejectedEvent(string BuildingDefId, Vec3i Origin, string Reason);

public sealed class PlacedBuildingData
{
    public int Id { get; init; }
    public string BuildingDefId { get; init; } = "";
    public Vec3i Origin { get; init; }
    public bool IsWorkshop { get; init; }
}

/// <summary>
/// Minimal runtime building registry for workshop placement and client queries.
/// Buildings are currently placed immediately; job-based construction can build on this later.
/// </summary>
public sealed class BuildingSystem : IGameSystem
{
    public string SystemId => SystemIds.BuildingSystem;
    public int UpdateOrder => 2;
    public bool IsEnabled { get; set; } = true;

    private readonly Dictionary<int, PlacedBuildingData>  _buildings   = new();
    private readonly Dictionary<Vec3i, int>               _byOrigin    = new();
    private int _nextBuildingId = 1;
    private GameContext? _ctx;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        ctx.Commands.Register<PlaceBuildingCommand>(OnPlaceBuilding);
        ctx.Commands.Register<DeconstructBuildingCommand>(OnRemoveBuilding);
    }

    public void Tick(float delta) { }

    public void OnSave(SaveWriter w)
    {
        w.Write("nextBuildingId", _nextBuildingId);
        w.Write("buildings", _buildings.Values.Select(b => new BuildingDto
        {
            Id = b.Id,
            BuildingDefId = b.BuildingDefId,
            X = b.Origin.X,
            Y = b.Origin.Y,
            Z = b.Origin.Z,
            IsWorkshop = b.IsWorkshop,
        }).ToList());
    }

    public void OnLoad(SaveReader r)
    {
        _nextBuildingId = r.TryRead<int>("nextBuildingId");
        if (_nextBuildingId <= 0) _nextBuildingId = 1;

        _buildings.Clear();
        _byOrigin.Clear();

        var saved = r.TryRead<List<BuildingDto>>("buildings");
        if (saved is null) return;

        foreach (var dto in saved)
        {
            var building = new PlacedBuildingData
            {
                Id = dto.Id,
                BuildingDefId = dto.BuildingDefId,
                Origin = new Vec3i(dto.X, dto.Y, dto.Z),
                IsWorkshop = dto.IsWorkshop,
            };

            _buildings[building.Id] = building;
            _byOrigin[building.Origin] = building.Id;
            ApplyFootprint(building);
        }
    }

    public IEnumerable<PlacedBuildingData> GetAll() => _buildings.Values;

    public PlacedBuildingData? GetById(int id)
        => _buildings.TryGetValue(id, out var building) ? building : null;

    public PlacedBuildingData? GetByOrigin(Vec3i origin)
        => _byOrigin.TryGetValue(origin, out var id) ? _buildings.GetValueOrDefault(id) : null;

    private void OnPlaceBuilding(PlaceBuildingCommand cmd)
    {
        var dm = _ctx!.Get<DataManager>();
        var def = dm.Buildings.GetOrNull(cmd.BuildingDefId);
        if (def is null)
        {
            _ctx.Logger?.Warn($"BuildingSystem: building def '{cmd.BuildingDefId}' not found.");
            _ctx.EventBus.Emit(new BuildingPlacementRejectedEvent(cmd.BuildingDefId, cmd.Origin, "Unknown building."));
            return;
        }

        if (FootprintConflicts(cmd.Origin, def))
        {
            _ctx.Logger?.Warn($"BuildingSystem: footprint for '{cmd.BuildingDefId}' conflicts with an existing building.");
            _ctx.EventBus.Emit(new BuildingPlacementRejectedEvent(cmd.BuildingDefId, cmd.Origin, "Footprint is blocked."));
            return;
        }

        var itemSystem = _ctx.TryGet<ItemSystem>();
        if (itemSystem is null || !itemSystem.TryConsumeInputs(def.ConstructionInputs))
        {
            _ctx.Logger?.Warn($"BuildingSystem: missing construction inputs for '{cmd.BuildingDefId}'.");
            _ctx.EventBus.Emit(new BuildingPlacementRejectedEvent(cmd.BuildingDefId, cmd.Origin, "Missing construction materials."));
            return;
        }

        var building = new PlacedBuildingData
        {
            Id = _nextBuildingId++,
            BuildingDefId = def.Id,
            Origin = cmd.Origin,
            IsWorkshop = def.IsWorkshop,
        };

        _buildings[building.Id] = building;
        _byOrigin[building.Origin] = building.Id;
        ApplyFootprint(building);
        _ctx.EventBus.Emit(new BuildingPlacedEvent(building.Id, building.BuildingDefId, building.Origin, building.IsWorkshop));
    }

    private void OnRemoveBuilding(DeconstructBuildingCommand cmd)
    {
        var building = _buildings.Values.FirstOrDefault(b => FootprintCells(b).Contains(cmd.Origin));
        if (building is null) return;

        ClearFootprint(building);
        _byOrigin.Remove(building.Origin);
        _buildings.Remove(building.Id);
        _ctx!.EventBus.Emit(new BuildingRemovedEvent(building.Id, building.BuildingDefId, building.Origin));
    }

    private bool FootprintConflicts(Vec3i origin, BuildingDef def)
    {
        var wanted = def.Footprint
            .Select(t => new Vec3i(origin.X + t.Offset.X, origin.Y + t.Offset.Y, origin.Z))
            .ToHashSet();
        return _buildings.Values.SelectMany(FootprintCells).Any(wanted.Contains);
    }

    private IEnumerable<Vec3i> FootprintCells(PlacedBuildingData building)
    {
        var dm = _ctx!.Get<DataManager>();
        var def = dm.Buildings.Get(building.BuildingDefId);
        foreach (var tile in def.Footprint)
            yield return new Vec3i(building.Origin.X + tile.Offset.X, building.Origin.Y + tile.Offset.Y, building.Origin.Z);
    }

    private void ApplyFootprint(PlacedBuildingData building)
    {
        var dm = _ctx!.Get<DataManager>();
        var map = _ctx.Get<WorldMap>();
        var def = dm.Buildings.Get(building.BuildingDefId);

        foreach (var footprintTile in def.Footprint)
        {
            var pos = new Vec3i(building.Origin.X + footprintTile.Offset.X, building.Origin.Y + footprintTile.Offset.Y, building.Origin.Z);
            var existing = map.GetTile(pos);
            var tileDef = dm.Tiles.Get(footprintTile.TileDefId);

            map.SetTile(pos, new TileData
            {
                TileDefId = tileDef.Id,
                MaterialId = InferMaterial(def, tileDef, existing.MaterialId),
                IsPassable = tileDef.IsPassable,
                IsUnderConstruction = false,
                FluidLevel = existing.FluidLevel,
                FluidMaterialId = existing.FluidMaterialId,
                CoatingAmount = existing.CoatingAmount,
                CoatingMaterialId = existing.CoatingMaterialId,
            });
        }
    }

    private void ClearFootprint(PlacedBuildingData building)
    {
        var map = _ctx!.Get<WorldMap>();
        foreach (var pos in FootprintCells(building))
        {
            map.SetTile(pos, new TileData
            {
                TileDefId = TileDefIds.StoneFloor,
                MaterialId = MaterialIds.Granite,
                IsPassable = true,
            });
        }
    }

    private static string InferMaterial(BuildingDef def, TileDef tileDef, string? fallback)
    {
        if (def.Tags.Contains(TagIds.Wooden) || tileDef.Id == TileDefIds.WoodFloor)
            return MaterialIds.Wood;

        if (def.Tags.Contains(TagIds.Stone) || tileDef.Id == TileDefIds.StoneFloor || tileDef.Id == TileDefIds.StoneBrick)
            return fallback ?? MaterialIds.Granite;

        return fallback ?? MaterialIds.Granite;
    }

    private sealed class BuildingDto
    {
        public int Id { get; set; }
        public string BuildingDefId { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public bool IsWorkshop { get; set; }
    }
}

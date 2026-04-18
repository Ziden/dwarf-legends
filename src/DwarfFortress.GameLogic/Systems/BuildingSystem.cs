using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

public record struct BuildingPlacedEvent(int BuildingId, string BuildingDefId, Vec3i Origin, bool IsWorkshop, BuildingRotation Rotation);
public record struct BuildingRemovedEvent(int BuildingId, string BuildingDefId, Vec3i Origin, BuildingRotation Rotation);
public record struct BuildingPlacementRejectedEvent(string BuildingDefId, Vec3i Origin, string Reason);
public record struct BuildingConstructionStartedEvent(int BuildingId, string BuildingDefId, Vec3i Origin, BuildingRotation Rotation, int JobId);
public record struct BuildingConstructionCompletedEvent(int BuildingId, string BuildingDefId, Vec3i Origin, BuildingRotation Rotation);

public sealed class CapturedFootprintTileData
{
    public Vec3i Position { get; init; }
    public TileData Tile { get; init; }
}

public sealed class PlacedBuildingData
{
    public int Id { get; init; }
    public string BuildingDefId { get; init; } = "";
    public Vec3i Origin { get; init; }
    public bool IsWorkshop { get; init; }
    public BuildingRotation Rotation { get; set; }
    public string? MaterialId { get; set; }
    public bool IsComplete { get; set; }
    public int ConstructionJobId { get; set; } = -1;
    public int LinkedStockpileId { get; set; } = -1;
    public List<CapturedFootprintTileData> UnderlyingTiles { get; init; } = new();
}

/// <summary>
/// Runtime building registry for reserved construction sites, completed buildings, and client queries.
/// </summary>
public sealed class BuildingSystem : IGameSystem
{
    public string SystemId => SystemIds.BuildingSystem;
    public int UpdateOrder => 2;
    public bool IsEnabled { get; set; } = true;

    private readonly Dictionary<int, PlacedBuildingData>  _buildings   = new();
    private readonly Dictionary<Vec3i, int>               _byOrigin    = new();
    private readonly Dictionary<Vec3i, int>               _byFootprintTile = new();
    private int _nextBuildingId = 1;
    private GameContext? _ctx;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        ctx.Commands.Register<PlaceBuildingCommand>(OnPlaceBuilding);
        ctx.Commands.Register<DeconstructBuildingCommand>(OnRemoveBuilding);
        ctx.Get<WorldMap>().RegisterTraversalConstraint(CanTraverseBuildingBoundary);
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
            MaterialId = b.MaterialId,
            Rotation = (int)b.Rotation,
            IsComplete = b.IsComplete,
            ConstructionJobId = b.ConstructionJobId,
            LinkedStockpileId = b.LinkedStockpileId,
            UnderlyingTiles = b.UnderlyingTiles.Select(tile => new FootprintTileDto
            {
                X = tile.Position.X,
                Y = tile.Position.Y,
                Z = tile.Position.Z,
                Tile = TileDataSnapshot.FromTile(tile.Tile),
            }).ToList(),
        }).ToList());
    }

    public void OnLoad(SaveReader r)
    {
        _nextBuildingId = r.TryRead<int>("nextBuildingId");
        if (_nextBuildingId <= 0) _nextBuildingId = 1;

        _buildings.Clear();
        _byOrigin.Clear();
        _byFootprintTile.Clear();

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
                MaterialId = dto.MaterialId,
                Rotation = Enum.IsDefined(typeof(BuildingRotation), dto.Rotation)
                    ? (BuildingRotation)dto.Rotation
                    : BuildingRotation.None,
                IsComplete = dto.IsComplete,
                ConstructionJobId = dto.ConstructionJobId,
                LinkedStockpileId = dto.LinkedStockpileId,
                UnderlyingTiles = dto.UnderlyingTiles?.Select(ToCapturedFootprintTileData).ToList() ?? new List<CapturedFootprintTileData>(),
            };

            _buildings[building.Id] = building;
            _byOrigin[building.Origin] = building.Id;
            IndexFootprint(building);

            if (string.IsNullOrWhiteSpace(building.MaterialId))
                building.MaterialId = ResolveAppliedFootprintMaterialId(building);
        }
    }

    public IEnumerable<PlacedBuildingData> GetAll() => _buildings.Values;

    public PlacedBuildingData? GetById(int id)
        => _buildings.TryGetValue(id, out var building) ? building : null;

    public PlacedBuildingData? GetByOrigin(Vec3i origin)
        => _byOrigin.TryGetValue(origin, out var id) ? _buildings.GetValueOrDefault(id) : null;

    public PlacedBuildingData? GetByFootprintTile(Vec3i position)
        => _byFootprintTile.TryGetValue(position, out var id) ? _buildings.GetValueOrDefault(id) : null;

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

        var discovery = _ctx.TryGet<DiscoverySystem>();
        if (discovery is not null && discovery.GetBuildingState(def.Id) < DiscoveryKnowledgeState.Unlocked)
        {
            _ctx.Logger?.Warn($"BuildingSystem: placement for '{cmd.BuildingDefId}' rejected: building is not discovered.");
            _ctx.EventBus.Emit(new BuildingPlacementRejectedEvent(cmd.BuildingDefId, cmd.Origin, "Building not discovered yet."));
            return;
        }

        if (!TryValidateFootprint(cmd.Origin, def, cmd.Rotation, out var rejectionReason))
        {
            _ctx.Logger?.Warn($"BuildingSystem: placement for '{cmd.BuildingDefId}' rejected: {rejectionReason}");
            _ctx.EventBus.Emit(new BuildingPlacementRejectedEvent(cmd.BuildingDefId, cmd.Origin, rejectionReason));
            return;
        }

        var itemSystem = _ctx.TryGet<ItemSystem>();
        if (itemSystem is null || !itemSystem.TryConsumeInputs(def.ConstructionInputs, out var consumedInputs))
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
            Rotation = cmd.Rotation,
            MaterialId = ResolveConstructionMaterialId(dm, def, consumedInputs),
            IsComplete = false,
            UnderlyingTiles = CaptureUnderlyingTiles(cmd.Origin, def, cmd.Rotation),
        };

        _buildings[building.Id] = building;
        _byOrigin[building.Origin] = building.Id;
        ApplyFootprint(building);
        IndexFootprint(building);
        StartConstructionJob(building);
        _ctx.EventBus.Emit(new BuildingPlacedEvent(building.Id, building.BuildingDefId, building.Origin, building.IsWorkshop, building.Rotation));
        _ctx.EventBus.Emit(new BuildingConstructionStartedEvent(
            building.Id,
            building.BuildingDefId,
            building.Origin,
            building.Rotation,
            building.ConstructionJobId));
    }

    private void OnRemoveBuilding(DeconstructBuildingCommand cmd)
    {
        var building = GetByFootprintTile(cmd.Origin);
        if (building is null) return;

        CancelConstructionJobIfNeeded(building);
        RemoveOwnedStockpile(building);
        RestoreUnderlyingTiles(building);
        RemoveFootprintIndex(building);
        _byOrigin.Remove(building.Origin);
        _buildings.Remove(building.Id);
        _ctx!.EventBus.Emit(new BuildingRemovedEvent(building.Id, building.BuildingDefId, building.Origin, building.Rotation));
    }

    public bool CompleteConstruction(int buildingId, int completedByJobId = -1)
    {
        if (!_buildings.TryGetValue(buildingId, out var building))
            return false;

        var dm = _ctx!.Get<DataManager>();
        var def = dm.Buildings.GetOrNull(building.BuildingDefId);
        if (def is null)
            return false;

        if (building.IsComplete)
            return true;

        CancelConstructionJobIfNeeded(building, completedByJobId);
        building.IsComplete = true;
        building.ConstructionJobId = -1;

        ApplyFootprint(building);
        CreateOwnedStockpileIfNeeded(building, def);
        _ctx.EventBus.Emit(new BuildingConstructionCompletedEvent(
            building.Id,
            building.BuildingDefId,
            building.Origin,
            building.Rotation));
        return true;
    }

    private bool TryValidateFootprint(Vec3i origin, BuildingDef def, BuildingRotation rotation, out string reason)
    {
        var map = _ctx!.Get<WorldMap>();
        foreach (var position in BuildingPlacementGeometry.EnumerateWorldFootprint(def, origin, rotation))
        {
            if (!map.IsInBounds(position))
            {
                reason = "Footprint is out of bounds.";
                return false;
            }

            if (_byFootprintTile.ContainsKey(position))
            {
                reason = "Footprint is blocked.";
                return false;
            }

            var tile = map.GetTile(position);
            if (tile.TileDefId == TileDefIds.Empty || !tile.IsPassable || tile.FluidLevel > 0)
            {
                reason = "Footprint is blocked.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private IEnumerable<Vec3i> FootprintCells(PlacedBuildingData building)
    {
        var dm = _ctx!.Get<DataManager>();
        var def = dm.Buildings.Get(building.BuildingDefId);
        foreach (var position in BuildingPlacementGeometry.EnumerateWorldFootprint(def, building.Origin, building.Rotation))
            yield return position;
    }

    private void ApplyFootprint(PlacedBuildingData building)
    {
        var dm = _ctx!.Get<DataManager>();
        var map = _ctx.Get<WorldMap>();
        var def = dm.Buildings.Get(building.BuildingDefId);

        foreach (var footprintTile in BuildingPlacementGeometry.EnumerateRotatedTiles(def, building.Rotation))
        {
            var pos = new Vec3i(building.Origin.X + footprintTile.Offset.X, building.Origin.Y + footprintTile.Offset.Y, building.Origin.Z);
            var existing = map.GetTile(pos);
            var tileDef = dm.Tiles.Get(footprintTile.Tile.TileDefId);

            map.SetTile(pos, new TileData
            {
                TileDefId = tileDef.Id,
                MaterialId = ResolveFootprintMaterialId(dm, def, tileDef, building.MaterialId, existing.MaterialId),
                IsPassable = tileDef.IsPassable,
                IsUnderConstruction = !building.IsComplete,
                FluidLevel = existing.FluidLevel,
                FluidMaterialId = existing.FluidMaterialId,
                CoatingAmount = existing.CoatingAmount,
                CoatingMaterialId = existing.CoatingMaterialId,
            });
        }
    }

    private void RestoreUnderlyingTiles(PlacedBuildingData building)
    {
        var map = _ctx!.Get<WorldMap>();
        if (building.UnderlyingTiles.Count == 0)
        {
            foreach (var pos in FootprintCells(building))
            {
                map.SetTile(pos, new TileData
                {
                    TileDefId = TileDefIds.StoneFloor,
                    MaterialId = MaterialIds.Granite,
                    IsPassable = true,
                });
            }

            return;
        }

        foreach (var tile in building.UnderlyingTiles)
            map.SetTile(tile.Position, tile.Tile);
    }

    private string? ResolveAppliedFootprintMaterialId(PlacedBuildingData building)
    {
        var map = _ctx!.Get<WorldMap>();
        return FootprintCells(building)
            .Select(pos => map.GetTile(pos).MaterialId)
            .FirstOrDefault(materialId => !string.IsNullOrWhiteSpace(materialId));
    }

    private static string InferMaterial(BuildingDef def, TileDef tileDef, string? fallback)
    {
        if (def.Tags.Contains(TagIds.Wooden) || tileDef.Id == TileDefIds.WoodFloor)
            return MaterialIds.Wood;

        if (def.Tags.Contains(TagIds.Stone) || tileDef.Id == TileDefIds.StoneFloor || tileDef.Id == TileDefIds.StoneBrick)
            return fallback ?? MaterialIds.Granite;

        return fallback ?? MaterialIds.Granite;
    }

    private static string ResolveFootprintMaterialId(
        DataManager data,
        BuildingDef def,
        TileDef tileDef,
        string? buildingMaterialId,
        string? existingMaterialId)
    {
        if (!string.IsNullOrWhiteSpace(buildingMaterialId))
            return buildingMaterialId!;

        if (MatchesBuildingMaterialFamily(data, def, tileDef, existingMaterialId))
            return existingMaterialId!;

        return InferMaterial(def, tileDef, existingMaterialId);
    }

    private static string? ResolveConstructionMaterialId(
        DataManager data,
        BuildingDef def,
        IReadOnlyList<Item> consumedInputs)
    {
        var materialIds = consumedInputs
            .Select(item => item.MaterialId)
            .Where(materialId => !string.IsNullOrWhiteSpace(materialId))
            .Cast<string>()
            .ToArray();

        if (materialIds.Length == 0)
            return null;

        var preferredMaterials = materialIds
            .Where(materialId => MatchesBuildingMaterialFamily(data, def, null, materialId))
            .ToArray();

        return SelectDominantMaterialId(preferredMaterials.Length > 0 ? preferredMaterials : materialIds);
    }

    private static string SelectDominantMaterialId(IReadOnlyList<string> materialIds)
    {
        return materialIds
            .GroupBy(materialId => materialId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                MaterialId = group.First(),
                Count = group.Count(),
            })
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.MaterialId, StringComparer.OrdinalIgnoreCase)
            .First()
            .MaterialId;
    }

    private static bool MatchesBuildingMaterialFamily(
        DataManager data,
        BuildingDef def,
        TileDef? tileDef,
        string? materialId)
    {
        if (string.IsNullOrWhiteSpace(materialId))
            return false;

        if (def.Tags.Contains(TagIds.Stone) ||
            tileDef?.Id == TileDefIds.StoneFloor ||
            tileDef?.Id == TileDefIds.StoneBrick)
        {
            return data.Materials.GetOrNull(materialId!)?.Tags.Contains(TagIds.Stone) == true;
        }

        if (def.Tags.Contains(TagIds.Wooden) || tileDef?.Id == TileDefIds.WoodFloor)
            return IsWoodLikeMaterial(data, materialId!);

        return true;
    }

    private void StartConstructionJob(PlacedBuildingData building)
    {
        var jobSystem = _ctx!.TryGet<JobSystem>();
        if (jobSystem is null)
            return;

        var job = jobSystem.CreateJob(JobDefIds.Construct, building.Origin, priority: 5, entityId: building.Id);
        building.ConstructionJobId = job.Id;
    }

    private void CancelConstructionJobIfNeeded(PlacedBuildingData building, int preservingJobId = -1)
    {
        var jobId = building.ConstructionJobId;
        if (jobId < 0 || jobId == preservingJobId)
            return;

        var jobSystem = _ctx!.TryGet<JobSystem>();
        var job = jobSystem?.GetJob(jobId);
        if (job is { Status: JobStatus.Pending or JobStatus.InProgress })
            jobSystem!.CancelJob(jobId);

        building.ConstructionJobId = -1;
    }

    private static bool IsWoodLikeMaterial(DataManager data, string materialId)
    {
        if (string.Equals(materialId, MaterialIds.Wood, StringComparison.OrdinalIgnoreCase) ||
            materialId.EndsWith("_wood", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var material = data.Materials.GetOrNull(materialId);
        return material?.Tags.Contains(TagIds.Wood) == true ||
               !string.IsNullOrWhiteSpace(data.ContentQueries?.ResolveLogItemDefId(materialId));
    }

    private List<CapturedFootprintTileData> CaptureUnderlyingTiles(Vec3i origin, BuildingDef def, BuildingRotation rotation)
    {
        var map = _ctx!.Get<WorldMap>();
        var captured = new List<CapturedFootprintTileData>();
        foreach (var position in BuildingPlacementGeometry.EnumerateWorldFootprint(def, origin, rotation))
        {
            captured.Add(new CapturedFootprintTileData
            {
                Position = position,
                Tile = map.GetTile(position),
            });
        }

        return captured;
    }

    private void IndexFootprint(PlacedBuildingData building)
    {
        foreach (var position in FootprintCells(building))
            _byFootprintTile[position] = building.Id;
    }

    private void RemoveFootprintIndex(PlacedBuildingData building)
    {
        foreach (var position in FootprintCells(building))
        {
            if (_byFootprintTile.TryGetValue(position, out var buildingId) && buildingId == building.Id)
                _byFootprintTile.Remove(position);
        }
    }

    private void CreateOwnedStockpileIfNeeded(PlacedBuildingData building, BuildingDef def)
    {
        if (def.AutoStockpileAcceptedTags.Count == 0)
            return;

        var stockpileManager = _ctx!.TryGet<StockpileManager>();
        if (stockpileManager is null)
            return;

        var stockpileCells = BuildingPlacementGeometry.GetAutoStockpileCells(def, building.Origin, building.Rotation);
        if (stockpileCells.Count == 0)
            return;

        var minX = stockpileCells.Min(cell => cell.X);
        var maxX = stockpileCells.Max(cell => cell.X);
        var minY = stockpileCells.Min(cell => cell.Y);
        var maxY = stockpileCells.Max(cell => cell.Y);
        var minZ = stockpileCells.Min(cell => cell.Z);
        var maxZ = stockpileCells.Max(cell => cell.Z);

        building.LinkedStockpileId = stockpileManager.CreateStockpile(
            new Vec3i(minX, minY, minZ),
            new Vec3i(maxX, maxY, maxZ),
            def.AutoStockpileAcceptedTags.ToArray(),
            building.Id);
    }

    private void RemoveOwnedStockpile(PlacedBuildingData building)
    {
        if (building.LinkedStockpileId < 0)
            return;

        _ctx!.TryGet<StockpileManager>()?.RemoveStockpile(building.LinkedStockpileId);
        building.LinkedStockpileId = -1;
    }

    private bool CanTraverseBuildingBoundary(Vec3i from, Vec3i to)
    {
        if (from.Z != to.Z)
            return true;

        var tested = new HashSet<int>();
        if (_byFootprintTile.TryGetValue(from, out var fromBuildingId))
            tested.Add(fromBuildingId);
        if (_byFootprintTile.TryGetValue(to, out var toBuildingId))
            tested.Add(toBuildingId);

        if (tested.Count == 0)
            return true;

        var dataManager = _ctx!.Get<DataManager>();
        foreach (var buildingId in tested)
        {
            if (!_buildings.TryGetValue(buildingId, out var building))
                continue;

            var definition = dataManager.Buildings.GetOrNull(building.BuildingDefId);
            if (definition is null || !building.IsComplete || definition.Entries.Count == 0)
                continue;

            if (!BuildingPlacementGeometry.CanTraverseBoundary(definition, building.Origin, building.Rotation, from, to))
                return false;
        }

        return true;
    }

    private static CapturedFootprintTileData ToCapturedFootprintTileData(FootprintTileDto dto)
        => new()
        {
            Position = new Vec3i(dto.X, dto.Y, dto.Z),
            Tile = dto.Tile?.ToTileData() ?? TileData.Empty,
        };

    private sealed class BuildingDto
    {
        public int Id { get; set; }
        public string BuildingDefId { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public bool IsWorkshop { get; set; }
        public string? MaterialId { get; set; }
        public int Rotation { get; set; }
        public bool IsComplete { get; set; }
        public int ConstructionJobId { get; set; } = -1;
        public int LinkedStockpileId { get; set; } = -1;
        public List<FootprintTileDto>? UnderlyingTiles { get; set; }
    }

    private sealed class FootprintTileDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public TileDataSnapshot? Tile { get; set; }
    }
}

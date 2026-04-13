using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Godot;

using WorldTileData = DwarfFortress.GameLogic.World.TileData;

namespace DwarfFortress.GodotClient.Rendering;

public partial class WorldRender3D : Node3D
{
    public const float TileWorldSize = 1f;
    public const float VerticalSliceSpacing = 0.65f;
    private const string ChunkArrayShaderPath = "res://Graphics/Shaders/WorldChunkTopArray.gdshader";

    private const float OverlaySurfaceOffset = 0.014f;
    private const float OverlayThickness = 0.035f;
    private const int OverlayRenderPriority = 0;
    private const int BillboardRenderPriority = 1;
    private const int BillboardOutlineRenderPriority = 2;
    private const float GroundSpriteHeight = 0.18f;
    private const float TreeSpriteOverlayHeight = 0.46f;
    private const float StockpileRailInset = 0.05f;
    private const float StockpileRailWidth = 0.11f;
    private const float StockpileTrimInset = 0.10f;
    private const float StockpileTrimWidth = 0.04f;
    private const float StockpilePostSize = 0.17f;
    private const int MaxChunkBuildsPerSync = 2;
    private const float ResourceDesignationOutlineScale = 1.08f;
    private const float ResourceDesignationOutlineDepthOffset = -0.0035f;
    private const float ResourceHoverOutlineScale = 1.14f;
    private const float ResourceHoverOutlineDepthOffset = -0.0025f;
    private static readonly Color ResourceDesignationOutlineTint = new(0.28f, 0.96f, 0.20f, 0.82f);
    private static readonly Color ResourceHoverOutlineTint = new(0.76f, 1f, 0.62f, 0.9f);

    private readonly Dictionary<Vec3i, WorldChunkRenderSnapshot> _chunkSnapshots = new();
    private readonly Dictionary<Vec3i, ChunkMeshState> _chunkMeshes = new();
    private readonly Dictionary<int, StructureMeshState> _structureMeshes = new();
    private readonly Dictionary<Vec3i, BillboardState> _treeBillboards = new();
    private readonly Dictionary<Vec3i, BillboardState> _plantBillboards = new();
    private readonly List<Chunk> _activeChunks = new();
    private readonly HashSet<Vec3i> _activeChunkOrigins = new();
    private readonly Queue<Vec3i> _pendingChunkBuildOrigins = new();
    private readonly HashSet<Vec3i> _pendingChunkBuildOriginSet = new();
    private readonly List<Vec3i> _pendingChunkScratch = new();
    private readonly Dictionary<Texture2D, StandardMaterial3D> _billboardMaterials = new();
    private readonly Dictionary<Texture2D, StandardMaterial3D> _designationOutlineBillboardMaterials = new();
    private readonly Dictionary<Texture2D, StandardMaterial3D> _outlineBillboardMaterials = new();
    private readonly WorldChunkSliceMesher _sliceMesher = new();
    private readonly WorldStructureMesher _structureMesher = new();

    private WorldActorPresentation3D? _actorPresentation;
    private ShaderMaterial? _chunkTopMaterial;
    private ShaderMaterial? _chunkDetailMaterial;
    private StandardMaterial3D? _chunkMaterial;
    private StandardMaterial3D? _overlayMaterial;
    private MeshInstance3D? _designationOverlayMesh;
    private MeshInstance3D? _stockpileOverlayMesh;
    private MeshInstance3D? _dynamicOverlayMesh;
    private BillboardState? _hoveredResourceBillboard;
    private bool _isActive;
    private bool _tileSpriteBillboardsDirty = true;
    private int? _tileSpriteBillboardViewHash;
    private int? _dynamicOverlayStateHash;
    private int _debugVisibleCombatCueCount;
    private int _debugCombatCuePlateCount;
    private int _debugMaxVisibleCombatCueId;
    private int _debugVisibleResourceBurstCount;
    private int _debugResourceBurstPlateCount;
    private int _debugMaxVisibleResourceBurstId;

    public override void _Ready()
    {
        EnsureChunkTopMaterial();
        EnsureChunkDetailMaterial();
        EnsureChunkMaterial();
        EnsureOverlayMaterial();
        EnsureActorPresentation();
        SetActive(false);
    }

    public bool HasPendingChunkBuilds => _pendingChunkBuildOrigins.Count > 0;

    public void Reset()
    {
        foreach (var state in _chunkMeshes.Values)
            ReleaseChunkMeshState(state);

        foreach (var state in _structureMeshes.Values)
            ReleaseStructureMeshState(state);

        ClearOverlayMesh(ref _designationOverlayMesh);
        ClearOverlayMesh(ref _stockpileOverlayMesh);
        ClearOverlayMesh(ref _dynamicOverlayMesh);
        _actorPresentation?.Reset();
        SetHoveredResourceBillboard(null);
        ClearBillboards(_treeBillboards);
        ClearBillboards(_plantBillboards);
        DisposeMaterials(_billboardMaterials);
        DisposeMaterials(_designationOutlineBillboardMaterials);
        DisposeMaterials(_outlineBillboardMaterials);

        _chunkMeshes.Clear();
        _structureMeshes.Clear();
        _chunkSnapshots.Clear();
        _activeChunks.Clear();
        _activeChunkOrigins.Clear();
        _pendingChunkBuildOrigins.Clear();
        _pendingChunkBuildOriginSet.Clear();
        _pendingChunkScratch.Clear();
        _tileSpriteBillboardsDirty = true;
        _tileSpriteBillboardViewHash = null;
        _dynamicOverlayStateHash = null;
        _debugVisibleCombatCueCount = 0;
        _debugCombatCuePlateCount = 0;
        _debugMaxVisibleCombatCueId = 0;
        _debugVisibleResourceBurstCount = 0;
        _debugResourceBurstPlateCount = 0;
        _debugMaxVisibleResourceBurstId = 0;
    }

    public void SetActive(bool active)
    {
        _isActive = active;

        foreach (var state in _chunkMeshes.Values)
            state.Mesh.Visible = active && state.IsVisible;

        foreach (var state in _structureMeshes.Values)
            ApplyStructureMeshVisibility(state, hovered: false);

        if (_designationOverlayMesh is not null)
            _designationOverlayMesh.Visible = active;

        if (_stockpileOverlayMesh is not null)
            _stockpileOverlayMesh.Visible = active;

        if (_dynamicOverlayMesh is not null)
            _dynamicOverlayMesh.Visible = active;

        _actorPresentation?.SetActive(active);

        foreach (var state in _treeBillboards.Values)
        {
            state.Root.Visible = active;
            ApplyResourceBillboardHighlight(state);
        }

        foreach (var state in _plantBillboards.Values)
        {
            state.Root.Visible = active;
            ApplyResourceBillboardHighlight(state);
        }
    }

    public void SyncSlice(WorldMap? map, BuildingSystem? buildings, StockpileManager? stockpiles, DataManager? data, int currentZ, Rect2I visibleTileBounds, SimulationProfiler? profiler = null)
    {
        if (map is null)
            return;

        EnsureChunkMaterial();

        using (profiler?.Measure("active_chunks") ?? default)
            CollectActiveChunks(map, currentZ, visibleTileBounds);

        using (profiler?.Measure("prune_inactive_chunks") ?? default)
            UpdateChunkResidency(currentZ);

        using (profiler?.Measure("refresh_dirty_snapshots") ?? default)
            RefreshDirtyChunkSnapshots(_activeChunks);

        using (profiler?.Measure("sync_chunk_meshes") ?? default)
        {
            foreach (var chunk in _activeChunks)
            {
                var snapshot = GetOrCaptureSnapshot(chunk);
                QueueChunkMeshSync(snapshot, currentZ);
                SetChunkMeshVisibility(snapshot.Origin, currentZ, isVisible: true);
            }

            ProcessChunkBuildQueue(map, data, currentZ);
        }

        using (profiler?.Measure("sync_structures") ?? default)
            SyncStructureMeshes(buildings, data, currentZ, visibleTileBounds);

        using (profiler?.Measure("stockpile_overlay") ?? default)
            SyncStockpileOverlay(map, stockpiles, currentZ, visibleTileBounds);

        using (profiler?.Measure("designation_overlay") ?? default)
            SyncDesignationOverlay(currentZ, visibleTileBounds);
    }

    public void SyncDynamicState(

        Camera3D? camera,

        WorldMap? map,

        WorldQuerySystem? query,

        EntityRegistry? registry,

        ItemSystem? items,

        SpatialIndexSystem? spatial,

        MovementPresentationSystem? movementPresentation,

        DataManager? data,

        InputController? input,

        RenderCache renderCache,

        GameFeedbackController? feedback,

        int currentZ,

        Rect2I visibleTileBounds,

        Vec3i? focusedLogTile,

        double presentationTimeSeconds,

        SimulationProfiler? profiler = null)

    {

        EnsureOverlayMaterial();



        using (profiler?.Measure("dynamic_overlay") ?? default)

            SyncDynamicOverlay(map, query, data, input, feedback, currentZ, visibleTileBounds, focusedLogTile);



        using (profiler?.Measure("tile_sprites") ?? default)
        {
            if (camera is null)
            {
                SyncTileSpriteBillboards(null, currentZ, visibleTileBounds);
                _tileSpriteBillboardsDirty = true;
                _tileSpriteBillboardViewHash = null;
            }
            else
            {
                var viewHash = HashCode.Combine(currentZ, visibleTileBounds);
                if (_tileSpriteBillboardsDirty || _tileSpriteBillboardViewHash != viewHash)
                {
                    SyncTileSpriteBillboards(camera, currentZ, visibleTileBounds);
                    _tileSpriteBillboardsDirty = false;
                    _tileSpriteBillboardViewHash = viewHash;
                }
            }

            SyncResourceBillboardHighlights(input);
        }

        UpdateStructureHoverState(input, spatial, currentZ);

        EnsureActorPresentation();
        _actorPresentation?.Sync(camera, map, registry, items, spatial, movementPresentation, data, renderCache, feedback, currentZ, visibleTileBounds, presentationTimeSeconds, profiler);

    }

    public (int Dwarves, int Creatures, int Items, int Trees, int Plants) GetDebugSpriteCounts()
    {
        var actorCounts = _actorPresentation?.GetDebugSpriteCounts() ?? (0, 0, 0);
        return (actorCounts.Item1, actorCounts.Item2, actorCounts.Item3, _treeBillboards.Count, _plantBillboards.Count);
    }

    public bool TryResolveHoveredBillboardTarget(Camera3D? camera, Viewport viewport, out Vector2I tile, out HoverSelectionMode selectionMode)
    {
        return TryResolveHoveredBillboardTarget(camera, viewport, viewport.GetMousePosition(), out tile, out selectionMode);
    }

    public bool TryResolveHoveredBillboardTarget(Camera3D? camera, Viewport viewport, Vector2 screenPosition, out Vector2I tile, out HoverSelectionMode selectionMode)
    {
        EnsureActorPresentation();
        tile = default;
        selectionMode = HoverSelectionMode.QueryTile;

        if (_actorPresentation?.TryResolveHoveredBillboardTile(camera, viewport, screenPosition, out tile) == true)
        {
            SetHoveredResourceBillboard(null);
            return true;
        }

        if (TryResolveHoveredResourceBillboardTile(camera, viewport, screenPosition, out tile))
        {
            selectionMode = HoverSelectionMode.RawTile;
            return true;
        }

        return false;
    }

    public bool TryResolveHoveredBillboardTile(Camera3D? camera, Viewport viewport, out Vector2I tile)
        => TryResolveHoveredBillboardTarget(camera, viewport, out tile, out _);

    public bool TryResolveHoveredBillboardTile(Camera3D? camera, Viewport viewport, Vector2 screenPosition, out Vector2I tile)
        => TryResolveHoveredBillboardTarget(camera, viewport, screenPosition, out tile, out _);

    public bool TryGetDebugResourceBillboardProbe(Camera3D? camera, Viewport viewport, out Vector2 screenPosition, out Vector2I tile)
    {
        screenPosition = default;
        tile = default;
        if (camera is null)
            return false;

        return TryGetDebugResourceBillboardProbe(camera, viewport, _plantBillboards.Values, out screenPosition, out tile)
            || TryGetDebugResourceBillboardProbe(camera, viewport, _treeBillboards.Values, out screenPosition, out tile);
    }

    public bool TryGetDebugBillboardProbe(Camera3D? camera, Viewport viewport, out Vector2 screenPosition, out Vector2I tile)
    {
        EnsureActorPresentation();
        screenPosition = default;
        tile = default;
        return _actorPresentation?.TryGetDebugBillboardProbe(camera, viewport, out screenPosition, out tile) == true;
    }

    public bool HasDebugHoveredBillboardOutline()
        => _actorPresentation?.HasDebugHoveredBillboardOutline() == true || _hoveredResourceBillboard?.Outline.Visible == true;

    public bool HasDebugHoveredResourceBillboardOutline()
        => _hoveredResourceBillboard?.Outline.Visible == true;

    public bool HasDebugDesignatedResourceBillboardOutline(Vector2I tile)
        => HasDebugDesignatedResourceBillboardOutline(_treeBillboards.Values, tile)
            || HasDebugDesignatedResourceBillboardOutline(_plantBillboards.Values, tile);

    public int GetDebugEmphasizedResourceBillboardCount()
        => CountEmphasizedResourceBillboards(_treeBillboards.Values) + CountEmphasizedResourceBillboards(_plantBillboards.Values);

    public int GetDebugItemBillboardRenderPriority()
    {
        EnsureActorPresentation();
        return _actorPresentation?.GetDebugItemBillboardRenderPriority() ?? 0;
    }

    public int GetDebugItemPreviewCount(int itemLikeId)
    {
        EnsureActorPresentation();
        return _actorPresentation?.GetDebugItemPreviewCount(itemLikeId) ?? 0;
    }

    public int GetDebugOverlayRenderPriority()
        => _overlayMaterial?.RenderPriority ?? 0;

    public bool TryGetDebugBillboardWorldPosition(int entityId, out Vector3 worldPosition)
    {
        EnsureActorPresentation();
        worldPosition = default;
        return _actorPresentation?.TryGetDebugBillboardWorldPosition(entityId, out worldPosition) == true;
    }

    public bool TryGetDebugBillboardRenderPriority(int entityId, out int renderPriority)
    {
        EnsureActorPresentation();
        renderPriority = default;
        return _actorPresentation?.TryGetDebugBillboardRenderPriority(entityId, out renderPriority) == true;
    }

    public bool TryGetDebugTreeBillboardTexture(Vec3i tilePosition, out Texture2D? texture)
    {
        if (_treeBillboards.TryGetValue(tilePosition, out var state))
        {
            texture = state.Texture;
            return true;
        }

        texture = null;
        return false;
    }

    public int GetDebugChunkMeshCount()
        => _chunkMeshes.Count;

    public bool HasDebugStockpileOverlay()
        => _stockpileOverlayMesh?.Mesh is not null;

    public int GetDebugVisibleCombatCueCount()
        => _debugVisibleCombatCueCount;

    public int GetDebugCombatCuePlateCount()
        => _debugCombatCuePlateCount;

    public int GetDebugMaxVisibleCombatCueId()
        => _debugMaxVisibleCombatCueId;

    public int GetDebugVisibleInventoryPickupCueCount()
        => _actorPresentation?.GetDebugVisibleInventoryPickupCueCount() ?? 0;

    public int GetDebugMaxVisibleInventoryPickupCueId()
        => _actorPresentation?.GetDebugMaxVisibleInventoryPickupCueId() ?? 0;

    public bool HasDebugInventoryPickupCue(int cueId)
        => _actorPresentation?.HasDebugInventoryPickupCue(cueId) == true;

    public int GetDebugVisibleResourceBurstCount()
        => _debugVisibleResourceBurstCount;

    public int GetDebugResourceBurstPlateCount()
        => _debugResourceBurstPlateCount;

    public int GetDebugMaxVisibleResourceBurstId()
        => _debugMaxVisibleResourceBurstId;

    private void CollectActiveChunks(WorldMap map, int currentZ, Rect2I visibleTileBounds)
    {
        _activeChunks.Clear();
        _activeChunkOrigins.Clear();
        if (visibleTileBounds.Size.X <= 0 || visibleTileBounds.Size.Y <= 0)
            return;

        var chunkZ = (currentZ / Chunk.Depth) * Chunk.Depth;
        var maxVisibleX = visibleTileBounds.Position.X + visibleTileBounds.Size.X - 1;
        var maxVisibleY = visibleTileBounds.Position.Y + visibleTileBounds.Size.Y - 1;
        var minChunkX = (visibleTileBounds.Position.X / Chunk.Width) * Chunk.Width;
        var minChunkY = (visibleTileBounds.Position.Y / Chunk.Height) * Chunk.Height;
        var maxChunkX = (maxVisibleX / Chunk.Width) * Chunk.Width;
        var maxChunkY = (maxVisibleY / Chunk.Height) * Chunk.Height;

        for (var originX = minChunkX; originX <= maxChunkX; originX += Chunk.Width)
        for (var originY = minChunkY; originY <= maxChunkY; originY += Chunk.Height)
        {
            var origin = new Vec3i(originX, originY, chunkZ);
            if (!map.TryGetChunk(origin, out var chunk) || chunk is null)
                continue;

            _activeChunks.Add(chunk);
            _activeChunkOrigins.Add(origin);
        }
    }

    private void RefreshDirtyChunkSnapshots(IEnumerable<Chunk> activeChunks)
    {
        var updatedSnapshot = false;
        foreach (var chunk in activeChunks)
        {
            if (!chunk.IsDirty)
                continue;

            _chunkSnapshots[chunk.Origin] = WorldChunkRenderSnapshot.Capture(chunk);
            chunk.ClearDirty();
            updatedSnapshot = true;
        }

        if (updatedSnapshot)
        {
            _tileSpriteBillboardsDirty = true;
            _dynamicOverlayStateHash = null;
        }
    }

    private WorldChunkRenderSnapshot GetOrCaptureSnapshot(Chunk chunk)
    {
        if (_chunkSnapshots.TryGetValue(chunk.Origin, out var snapshot))
            return snapshot;

        snapshot = WorldChunkRenderSnapshot.Capture(chunk);
        _chunkSnapshots[chunk.Origin] = snapshot;
        if (chunk.IsDirty)
            chunk.ClearDirty();

        return snapshot;
    }

    private void QueueChunkMeshSync(WorldChunkRenderSnapshot snapshot, int currentZ)
    {
        if (!RequiresChunkMeshRebuild(snapshot, currentZ))
            return;

        if (_pendingChunkBuildOriginSet.Add(snapshot.Origin))
            _pendingChunkBuildOrigins.Enqueue(snapshot.Origin);
    }

    private bool RequiresChunkMeshRebuild(WorldChunkRenderSnapshot snapshot, int currentZ)
    {
        return !_chunkMeshes.TryGetValue(snapshot.Origin, out var state)
            || state.SnapshotVersion != snapshot.Version
            || state.SliceZ != currentZ;
    }

    private void ProcessChunkBuildQueue(WorldMap map, DataManager? data, int currentZ)
    {
        var buildsProcessed = 0;
        while (buildsProcessed < MaxChunkBuildsPerSync && _pendingChunkBuildOrigins.Count > 0)
        {
            var origin = _pendingChunkBuildOrigins.Dequeue();
            _pendingChunkBuildOriginSet.Remove(origin);

            if (!_activeChunkOrigins.Contains(origin) || !_chunkSnapshots.TryGetValue(origin, out var snapshot))
                continue;

            if (!RequiresChunkMeshRebuild(snapshot, currentZ))
            {
                SetChunkMeshVisibility(origin, currentZ, isVisible: true);
                continue;
            }

            SyncChunkMesh(snapshot, map, data, currentZ);
            buildsProcessed++;
        }
    }

    private void SetChunkMeshVisibility(Vec3i origin, int currentZ, bool isVisible)
    {
        if (!_chunkMeshes.TryGetValue(origin, out var state))
            return;

        var shouldShow = isVisible && state.SliceZ == currentZ;
        state.IsVisible = shouldShow;
        state.Mesh.Position = ResolveChunkMeshPosition(origin, currentZ);
        state.Mesh.Visible = _isActive && shouldShow;
    }

    private void SyncChunkMesh(WorldChunkRenderSnapshot snapshot, WorldMap map, DataManager? data, int currentZ)
    {
        EnsureChunkTopMaterial();
        EnsureChunkDetailMaterial();
        if (_chunkMaterial is null || _chunkTopMaterial is null)
            return;

        var build = _sliceMesher.BuildSliceMesh(
            snapshot,
            map,
            currentZ,
            (x, y, z) => TryGetSnapshotTileRenderData(snapshot, map, x, y, z),
            materialId => ResolveGroundTileDefIdFromMaterial(data, materialId));
        if (build is not WorldChunkSliceMesher.ChunkSliceMeshBuild resolvedBuild)
        {
            RemoveChunkMesh(snapshot.Origin);
            return;
        }

        RefreshChunkTopMaterialTextureArray();
        RefreshChunkDetailMaterialTextureArray();

        if (!_chunkMeshes.TryGetValue(snapshot.Origin, out var state))
        {
            var meshInstance = CreateChunkMeshInstance(snapshot.Origin);
            state = new ChunkMeshState(meshInstance, snapshot.Version, currentZ, isVisible: false);
            _chunkMeshes[snapshot.Origin] = state;
        }

        ReplaceOwnedMesh(state.Mesh, resolvedBuild.Mesh);
        state.SnapshotVersion = snapshot.Version;
        state.SliceZ = currentZ;
        state.Mesh.MaterialOverride = null;
        state.Mesh.SetSurfaceOverrideMaterial(resolvedBuild.TopSurfaceIndex, _chunkTopMaterial);
        if (resolvedBuild.SideSurfaceIndex.HasValue)
            state.Mesh.SetSurfaceOverrideMaterial(resolvedBuild.SideSurfaceIndex.Value, _chunkMaterial);
        if (resolvedBuild.DetailSurfaceIndex.HasValue && _chunkDetailMaterial is not null)
            state.Mesh.SetSurfaceOverrideMaterial(resolvedBuild.DetailSurfaceIndex.Value, _chunkDetailMaterial);
        state.Mesh.Position = ResolveChunkMeshPosition(snapshot.Origin, currentZ);
        state.Mesh.Visible = false;

        SetChunkMeshVisibility(snapshot.Origin, currentZ, isVisible: true);
    }

    private void UpdateChunkResidency(int currentZ)
    {
        if (_chunkMeshes.Count > 0)
        {
            var staleMeshOrigins = _chunkMeshes.Keys.Where(origin => !_activeChunkOrigins.Contains(origin)).ToArray();
            foreach (var origin in staleMeshOrigins)
                RemoveChunkMesh(origin);
        }

        foreach (var chunkMesh in _chunkMeshes)
        {
            var isVisible = _activeChunkOrigins.Contains(chunkMesh.Key) && chunkMesh.Value.SliceZ == currentZ;
            chunkMesh.Value.IsVisible = isVisible;
            chunkMesh.Value.Mesh.Visible = _isActive && isVisible;
        }

        if (_chunkSnapshots.Count > 0)
        {
            var staleSnapshotOrigins = _chunkSnapshots.Keys.Where(origin => !_activeChunkOrigins.Contains(origin)).ToArray();
            foreach (var origin in staleSnapshotOrigins)
                _chunkSnapshots.Remove(origin);
        }

        PrunePendingChunkBuilds();
    }

    private void PrunePendingChunkBuilds()
    {
        if (_pendingChunkBuildOrigins.Count == 0)
            return;

        _pendingChunkScratch.Clear();
        while (_pendingChunkBuildOrigins.Count > 0)
        {
            var origin = _pendingChunkBuildOrigins.Dequeue();
            if (_activeChunkOrigins.Contains(origin))
            {
                _pendingChunkScratch.Add(origin);
                continue;
            }

            _pendingChunkBuildOriginSet.Remove(origin);
        }

        foreach (var origin in _pendingChunkScratch)
            _pendingChunkBuildOrigins.Enqueue(origin);
    }

    private void RemoveChunkMesh(Vec3i origin)
    {
        if (!_chunkMeshes.TryGetValue(origin, out var state))
            return;

        ReleaseChunkMeshState(state);
        _chunkMeshes.Remove(origin);
    }

    private MeshInstance3D CreateChunkMeshInstance(Vec3i origin)
    {
        var meshInstance = new MeshInstance3D
        {
            Name = $"Chunk_{origin.X}_{origin.Y}_{origin.Z}",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        AddChild(meshInstance);
        return meshInstance;
    }

    private void SyncStructureMeshes(BuildingSystem? buildings, DataManager? data, int currentZ, Rect2I visibleTileBounds)
    {
        if (buildings is null || data is null)
        {
            ClearStructureMeshes();
            return;
        }

        var visibleIds = new HashSet<int>();
        foreach (var building in buildings.GetAll())
        {
            if (building.Origin.Z != currentZ)
                continue;

            var definition = data.Buildings.GetOrNull(building.BuildingDefId);
            if (definition is null || !ShouldRenderStructure(definition, building) || !FootprintIntersectsVisibleTileBounds(building.Origin, definition, building.Rotation, visibleTileBounds))
                continue;

            visibleIds.Add(building.Id);
            SyncStructureMesh(building, definition, currentZ);
        }

        var staleIds = _structureMeshes.Keys.Where(id => !visibleIds.Contains(id)).ToArray();
        foreach (var staleId in staleIds)
            RemoveStructureMesh(staleId);
    }

    private void SyncStructureMesh(PlacedBuildingData building, BuildingDef definition, int currentZ)
    {
        var needsRebuild = !_structureMeshes.TryGetValue(building.Id, out var state)
            || state.BuildingDefId != building.BuildingDefId
            || state.Origin != building.Origin
            || state.Rotation != building.Rotation;

        if (!needsRebuild && state is not null)
        {
            state.Root.Position = ResolveChunkMeshPosition(building.Origin, currentZ);
            ApplyStructureMeshVisibility(state, hovered: false);
            return;
        }

        var meshParts = _structureMesher.BuildStructureMeshes(definition, building);
        if (meshParts.BodyMesh is null && meshParts.RoofMesh is null)
        {
            RemoveStructureMesh(building.Id);
            return;
        }

        state ??= CreateStructureMeshState(building.Id);
        ReplaceOwnedMesh(state.BodyMesh, meshParts.BodyMesh);
        ReplaceOwnedMesh(state.RoofMesh, meshParts.RoofMesh);
        state.BodyMesh.MaterialOverride = _chunkMaterial;
        state.RoofMesh.MaterialOverride = _chunkMaterial;
        state.Root.Position = ResolveChunkMeshPosition(building.Origin, currentZ);
        state.BuildingDefId = building.BuildingDefId;
        state.Origin = building.Origin;
        state.Rotation = building.Rotation;
        state.HideRoofOnHover = meshParts.HideRoofOnHover;
        ApplyStructureMeshVisibility(state, hovered: false);

        _structureMeshes[building.Id] = state;
    }

    private StructureMeshState CreateStructureMeshState(int buildingId)
    {
        var root = new Node3D
        {
            Name = $"Structure_{buildingId}",
        };

        var bodyMesh = new MeshInstance3D
        {
            Name = "Body",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        var roofMesh = new MeshInstance3D
        {
            Name = "Roof",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        root.AddChild(bodyMesh);
        root.AddChild(roofMesh);
        AddChild(root);
        return new StructureMeshState(root, bodyMesh, roofMesh);
    }

    private void ClearStructureMeshes()
    {
        foreach (var state in _structureMeshes.Values)
            ReleaseStructureMeshState(state);

        _structureMeshes.Clear();
    }

    private void RemoveStructureMesh(int buildingId)
    {
        if (!_structureMeshes.TryGetValue(buildingId, out var state))
            return;

        ReleaseStructureMeshState(state);
        _structureMeshes.Remove(buildingId);
    }

    private void UpdateStructureHoverState(InputController? input, SpatialIndexSystem? spatial, int currentZ)
    {
        var hoveredBuildingId = -1;
        if (input is not null && spatial is not null)
            hoveredBuildingId = spatial.GetBuildingAt(new Vec3i(input.HoveredTile.X, input.HoveredTile.Y, currentZ)) ?? -1;

        foreach (var pair in _structureMeshes)
            ApplyStructureMeshVisibility(pair.Value, hoveredBuildingId == pair.Key);
    }

    private void ApplyStructureMeshVisibility(StructureMeshState state, bool hovered)
    {
        state.Root.Visible = _isActive;
        state.BodyMesh.Visible = _isActive && state.BodyMesh.Mesh is not null;
        state.RoofMesh.Visible = _isActive
            && state.RoofMesh.Mesh is not null
            && (!state.HideRoofOnHover || !hovered);
    }

    private static bool ShouldRenderStructure(BuildingDef definition, PlacedBuildingData building)
        => building.IsWorkshop || !string.IsNullOrWhiteSpace(definition.StructureVisualId);

    private void SyncDesignationOverlay(int currentZ, Rect2I visibleTileBounds)
    {
        var plates = new List<WorldOverlayMesher.OverlayPlate>();

        foreach (var snapshot in _chunkSnapshots.Values)
        {
            if (!snapshot.ContainsWorldZ(currentZ) || !ChunkIntersectsVisibleTileBounds(snapshot.Origin, visibleTileBounds))
                continue;

            var localZ = currentZ - snapshot.Origin.Z;
            for (var localY = 0; localY < Chunk.Height; localY++)
            for (var localX = 0; localX < Chunk.Width; localX++)
            {
                if (!snapshot.TryGetLocalTile(localX, localY, localZ, out var tile) || !tile.IsDesignated)
                    continue;

                AddTilePlate(
                    plates,
                    snapshot.Origin.X + localX,
                    snapshot.Origin.Y + localY,
                    visibleTileBounds,
                    ResolveOverlayBaseY(currentZ, tile),
                    new Color(1f, 0.78f, 0.05f, 0.34f),
                    0.16f);
            }
        }

        SyncOverlayMesh(ref _designationOverlayMesh, "DesignationOverlay", WorldOverlayMesher.Build(plates));
    }

    private void SyncStockpileOverlay(WorldMap map, StockpileManager? stockpiles, int currentZ, Rect2I visibleTileBounds)
    {
        if (stockpiles is null)
        {
            ClearOverlayMesh(ref _stockpileOverlayMesh);
            return;
        }

        var plates = new List<WorldOverlayMesher.OverlayPlate>();
        foreach (var stockpile in stockpiles.GetAll())
        {
            if (stockpile.From.Z > currentZ || stockpile.To.Z < currentZ)
                continue;

            var style = StockpileVisualResolver.Resolve(stockpile.AcceptedTags);
            var slots = stockpile.AllSlots()
                .Where(slot => slot.Z == currentZ)
                .ToArray();
            var slotSet = slots.ToHashSet();

            foreach (var slot in slots)
            {
                var overlayBaseY = ResolveOverlayBaseY(map, slot.X, slot.Y, currentZ) - 0.005f;
                AddStockpilePerimeter(plates, slot, visibleTileBounds, overlayBaseY, style, slotSet);
            }
        }

        SyncOverlayMesh(ref _stockpileOverlayMesh, "StockpileOverlay", WorldOverlayMesher.Build(plates));
    }

    private void SyncDynamicOverlay(
        WorldMap? map,
        WorldQuerySystem? query,
        DataManager? data,
        InputController? input,
        GameFeedbackController? feedback,
        int currentZ,
        Rect2I visibleTileBounds,
        Vec3i? focusedLogTile)
    {
        if (input is null && feedback is null)
        {
            ClearOverlayMesh(ref _dynamicOverlayMesh);
            _dynamicOverlayStateHash = null;
            _debugVisibleCombatCueCount = 0;
            _debugCombatCuePlateCount = 0;
            _debugMaxVisibleCombatCueId = 0;
            _debugVisibleResourceBurstCount = 0;
            _debugResourceBurstPlateCount = 0;
            _debugMaxVisibleResourceBurstId = 0;
            return;
        }

        var selectedDwarf = input?.SelectedDwarfId is int dwarfId
            ? query?.GetDwarfView(dwarfId)
            : null;
        var selectedCreature = input?.SelectedCreatureId is int creatureId
            ? query?.GetCreatureView(creatureId)
            : null;
        var selectedItem = input?.SelectedItemId is int itemId
            ? query?.GetItemView(itemId)
            : null;
        var selectedBuilding = input?.SelectedBuildingId is int buildingId
            ? query?.GetBuildingView(buildingId)
            : null;
        var selectionRect = input?.GetSelectionRect();
        var currentMode = input?.CurrentMode ?? InputMode.Select;
        var visibleTilePulses = new List<GameFeedbackController.TilePulseView>();
        var visibleBuildingPulses = new List<(BuildingView Building, GameFeedbackController.BuildingPulseView Pulse)>();
        var visibleCombatCues = new List<GameFeedbackController.CombatCueView>();
        var visibleResourceBursts = new List<GameFeedbackController.ResourceBurstCueView>();

        if (feedback is not null)
        {
            foreach (var tilePulse in feedback.GetTilePulseViews(currentZ))
            {
                if (TileBoundsContains(visibleTileBounds, tilePulse.Position.X, tilePulse.Position.Y))
                    visibleTilePulses.Add(tilePulse);
            }

            foreach (var combatCue in feedback.GetCombatCueViews(currentZ))
            {
                if (TileBoundsContains(visibleTileBounds, combatCue.Position.X, combatCue.Position.Y))
                    visibleCombatCues.Add(combatCue);
            }

            foreach (var resourceBurst in feedback.GetResourceBurstCueViews(currentZ))
            {
                if (TileBoundsContains(visibleTileBounds, resourceBurst.Position.X, resourceBurst.Position.Y))
                    visibleResourceBursts.Add(resourceBurst);
            }

            if (query is not null)
            {
                foreach (var buildingPulse in feedback.GetBuildingPulseViews())
                {
                    var buildingView = query.GetBuildingView(buildingPulse.BuildingId);
                    if (buildingView is null || buildingView.Origin.Z != currentZ)
                        continue;

                    visibleBuildingPulses.Add((buildingView, buildingPulse));
                }
            }
        }

        _debugVisibleCombatCueCount = visibleCombatCues.Count;
        _debugMaxVisibleCombatCueId = visibleCombatCues.Count > 0
            ? visibleCombatCues.Max(combatCue => combatCue.Id)
            : 0;
        _debugVisibleResourceBurstCount = visibleResourceBursts.Count;
        _debugMaxVisibleResourceBurstId = visibleResourceBursts.Count > 0
            ? visibleResourceBursts.Max(resourceBurst => resourceBurst.Id)
            : 0;

        var overlayState = new HashCode();
        overlayState.Add(currentZ);
        overlayState.Add(visibleTileBounds);
        overlayState.Add(currentMode);
        overlayState.Add(input?.HoveredTile ?? new Vector2I(-1, -1));
        overlayState.Add(input?.SelectedTile.HasValue ?? false);
        if (input?.SelectedTile is Vector2I selectedTileState)
            overlayState.Add(selectedTileState);
        overlayState.Add(input?.GetSelectedAreaRect().HasValue ?? false);
        if (input?.GetSelectedAreaRect() is { } selectedAreaState)
        {
            overlayState.Add(selectedAreaState.from);
            overlayState.Add(selectedAreaState.to);
        }
        overlayState.Add(focusedLogTile.HasValue);
        if (focusedLogTile is Vec3i focusedTileState)
            overlayState.Add(focusedTileState);
        overlayState.Add(selectionRect.HasValue);
        if (selectionRect.HasValue)
        {
            overlayState.Add(selectionRect.Value.from);
            overlayState.Add(selectionRect.Value.to);
        }

        overlayState.Add(selectedDwarf is not null);
        if (selectedDwarf is not null)
            overlayState.Add(selectedDwarf.Position);

        overlayState.Add(selectedCreature is not null);
        if (selectedCreature is not null)
            overlayState.Add(selectedCreature.Position);

        overlayState.Add(selectedItem is not null);
        if (selectedItem is not null)
            overlayState.Add(selectedItem.Position);

        overlayState.Add(selectedBuilding is not null);
        if (selectedBuilding is not null)
        {
            overlayState.Add(selectedBuilding.Origin);
            overlayState.Add(selectedBuilding.BuildingDefId);
        }

        overlayState.Add(visibleTilePulses.Count);
        foreach (var tilePulse in visibleTilePulses)
        {
            overlayState.Add(tilePulse.Position);
            overlayState.Add(tilePulse.Pulse.Color);
            overlayState.Add(tilePulse.Pulse.Scale);
            overlayState.Add(tilePulse.Pulse.Lift);
            overlayState.Add(tilePulse.Pulse.Flash);
            overlayState.Add(tilePulse.Pulse.Ring);
        }

        overlayState.Add(visibleCombatCues.Count);
        foreach (var combatCue in visibleCombatCues)
        {
            overlayState.Add(combatCue.Id);
            overlayState.Add(combatCue.Position);
            overlayState.Add(combatCue.Color);
            overlayState.Add(combatCue.TimeLeft);
            overlayState.Add(combatCue.Duration);
            overlayState.Add(combatCue.DirectionX);
            overlayState.Add(combatCue.DirectionY);
            overlayState.Add(combatCue.DidHit);
        }

        overlayState.Add(visibleResourceBursts.Count);
        foreach (var resourceBurst in visibleResourceBursts)
        {
            overlayState.Add(resourceBurst.Id);
            overlayState.Add(resourceBurst.Position);
            overlayState.Add(resourceBurst.Color);
            overlayState.Add(resourceBurst.TimeLeft);
            overlayState.Add(resourceBurst.Duration);
            overlayState.Add(resourceBurst.Scale);
        }

        overlayState.Add(visibleBuildingPulses.Count);
        foreach (var (building, buildingPulse) in visibleBuildingPulses)
        {
            overlayState.Add(building.Id);
            overlayState.Add(building.Origin);
            overlayState.Add(building.BuildingDefId);
            overlayState.Add(buildingPulse.Pulse.Color);
            overlayState.Add(buildingPulse.Pulse.Scale);
            overlayState.Add(buildingPulse.Pulse.Lift);
            overlayState.Add(buildingPulse.Pulse.Flash);
            overlayState.Add(buildingPulse.Pulse.Ring);
        }

        var hasPreview = currentMode == InputMode.BuildingPreview && input?.PendingBuildingDefId is not null;
        overlayState.Add(hasPreview);
        if (hasPreview)
        {
            overlayState.Add(input!.PendingBuildingDefId);
            overlayState.Add(input.PendingBuildingRotation);
            overlayState.Add(new Vec3i(input.HoveredTile.X, input.HoveredTile.Y, currentZ));
        }

        var overlayHash = overlayState.ToHashCode();
        if (_dynamicOverlayStateHash == overlayHash)
            return;

        _dynamicOverlayStateHash = overlayHash;
        var plates = new List<WorldOverlayMesher.OverlayPlate>();
        var combatCuePlateCount = 0;
        var resourceBurstPlateCount = 0;

        foreach (var tilePulse in visibleTilePulses)
        {
            AddTilePulsePlate(
                plates,
                tilePulse,
                visibleTileBounds,
                ResolveOverlayBaseY(map, tilePulse.Position.X, tilePulse.Position.Y, currentZ));
        }

        foreach (var resourceBurst in visibleResourceBursts)
        {
            var plateCountBefore = plates.Count;
            AddResourceBurstCuePlate(
                plates,
                resourceBurst,
                visibleTileBounds,
                ResolveOverlayBaseY(map, resourceBurst.Position.X, resourceBurst.Position.Y, currentZ));
            resourceBurstPlateCount += plates.Count - plateCountBefore;
        }

        foreach (var combatCue in visibleCombatCues)
        {
            var plateCountBefore = plates.Count;
            AddCombatCuePlate(
                plates,
                combatCue,
                visibleTileBounds,
                ResolveOverlayBaseY(map, combatCue.Position.X, combatCue.Position.Y, currentZ));
            combatCuePlateCount += plates.Count - plateCountBefore;
        }

        _debugCombatCuePlateCount = combatCuePlateCount;
        _debugResourceBurstPlateCount = resourceBurstPlateCount;

        foreach (var (building, buildingPulse) in visibleBuildingPulses)
        {
            AddBuildingFootprint(
                plates,
                map,
                data,
                building.Origin,
                building.BuildingDefId,
                building.Rotation,
                currentZ,
                visibleTileBounds,
                buildingPulse.Pulse.WithAlpha(0.08f + (buildingPulse.Pulse.Flash * 0.16f)),
                ResolvePulseInset(0.10f, buildingPulse.Pulse.Ring, 0.08f));
        }

        if (input is not null)
            AddTilePlate(plates, map, input.HoveredTile.X, input.HoveredTile.Y, currentZ, visibleTileBounds, new Color(1f, 1f, 1f, 0.18f), 0.10f);

        if (focusedLogTile is Vec3i focusedTile && focusedTile.Z == currentZ)
            AddTilePlate(plates, map, focusedTile.X, focusedTile.Y, currentZ, visibleTileBounds, new Color(0.58f, 0.95f, 1f, 0.30f), 0.06f);

        if (input?.SelectedTile is Vector2I selectedTile)
            AddTilePlate(plates, map, selectedTile.X, selectedTile.Y, currentZ, visibleTileBounds, new Color(0.35f, 0.70f, 1f, 0.34f), 0.06f);

        if (input?.GetSelectedAreaRect() is { } selectedArea)
        {
            var minX = Mathf.Max(selectedArea.from.X, visibleTileBounds.Position.X);
            var maxX = Mathf.Min(selectedArea.to.X, visibleTileBounds.Position.X + visibleTileBounds.Size.X - 1);
            var minY = Mathf.Max(selectedArea.from.Y, visibleTileBounds.Position.Y);
            var maxY = Mathf.Min(selectedArea.to.Y, visibleTileBounds.Position.Y + visibleTileBounds.Size.Y - 1);

            for (var x = minX; x <= maxX; x++)
            for (var y = minY; y <= maxY; y++)
                AddTilePlate(plates, map, x, y, currentZ, visibleTileBounds, new Color(0.35f, 0.70f, 1f, 0.22f), 0.06f);
        }

        if (selectionRect.HasValue)
        {
            var (from, to) = selectionRect.Value;
            var fill = ResolveSelectionOverlayColor(currentMode);
            var minX = Mathf.Max(from.X, visibleTileBounds.Position.X);
            var maxX = Mathf.Min(to.X, visibleTileBounds.Position.X + visibleTileBounds.Size.X - 1);
            var minY = Mathf.Max(from.Y, visibleTileBounds.Position.Y);
            var maxY = Mathf.Min(to.Y, visibleTileBounds.Position.Y + visibleTileBounds.Size.Y - 1);

            for (var x = minX; x <= maxX; x++)
            for (var y = minY; y <= maxY; y++)
                AddTilePlate(plates, map, x, y, currentZ, visibleTileBounds, fill, 0.03f);
        }

        if (selectedDwarf is not null && selectedDwarf.Position.Z == currentZ)
            AddTilePlate(plates, map, selectedDwarf.Position.X, selectedDwarf.Position.Y, currentZ, visibleTileBounds, new Color(1f, 1f, 0f, 0.32f), 0.06f);

        if (selectedCreature is not null && selectedCreature.Position.Z == currentZ)
            AddTilePlate(plates, map, selectedCreature.Position.X, selectedCreature.Position.Y, currentZ, visibleTileBounds, new Color(1f, 0.45f, 0.15f, 0.30f), 0.06f);

        if (selectedItem is not null && selectedItem.Position.Z == currentZ)
            AddTilePlate(plates, map, selectedItem.Position.X, selectedItem.Position.Y, currentZ, visibleTileBounds, new Color(0.90f, 0.72f, 1f, 0.28f), 0.08f);

        if (selectedBuilding is not null && selectedBuilding.Origin.Z == currentZ)
            AddBuildingFootprint(plates, map, data, selectedBuilding.Origin, selectedBuilding.BuildingDefId, selectedBuilding.Rotation, currentZ, visibleTileBounds, new Color(0.40f, 0.85f, 1f, 0.30f), 0.05f);

        if (hasPreview)
        {
            var previewOrigin = new Vec3i(input!.HoveredTile.X, input.HoveredTile.Y, currentZ);
            AddBuildingFootprint(
                plates,
                map,
                data,
                previewOrigin,
                input.PendingBuildingDefId!,
                input.PendingBuildingRotation,
                currentZ,
                visibleTileBounds,
                new Color(0.35f, 0.70f, 1f, 0.30f),
                0.06f);
        }

        SyncOverlayMesh(ref _dynamicOverlayMesh, "DynamicOverlay", WorldOverlayMesher.Build(plates));
    }

    private void SyncTileSpriteBillboards(Camera3D? camera, int currentZ, Rect2I visibleTileBounds)
    {
        if (camera is null)
        {
            ClearBillboards(_treeBillboards);
            ClearBillboards(_plantBillboards);
            return;
        }

        var visibleTreeIds = new HashSet<Vec3i>();
        var visiblePlantIds = new HashSet<Vec3i>();

        foreach (var snapshot in _chunkSnapshots.Values)
        {
            if (!snapshot.ContainsWorldZ(currentZ) || !ChunkIntersectsVisibleTileBounds(snapshot.Origin, visibleTileBounds))
                continue;

            var localZ = currentZ - snapshot.Origin.Z;
            for (var localY = 0; localY < Chunk.Height; localY++)
            for (var localX = 0; localX < Chunk.Width; localX++)
            {
                if (!snapshot.TryGetLocalTile(localX, localY, localZ, out var tile) || tile.TileDefId == TileDefIds.Empty)
                    continue;

                var position = new Vec3i(snapshot.Origin.X + localX, snapshot.Origin.Y + localY, currentZ);
                if (!TileBoundsContains(visibleTileBounds, position.X, position.Y))
                    continue;

                var compositeTreeVisual = default(WorldSpriteVisual);
                var hasTreeCanopyOverlay = tile.TileDefId == TileDefIds.Tree
                    && WorldSpriteVisuals.TryTreeWithPlantOverlay(
                        tile.TreeSpeciesId,
                        tile.PlantDefId,
                        tile.PlantGrowthStage,
                        tile.PlantYieldLevel,
                        tile.PlantSeedLevel,
                        out compositeTreeVisual);

                if (tile.TileDefId == TileDefIds.Tree)
                {
                    var treeVisual = hasTreeCanopyOverlay
                        ? compositeTreeVisual
                        : WorldSpriteVisuals.Tree(tile.TreeSpeciesId);
                    visibleTreeIds.Add(position);
                    SyncBillboard(
                        _treeBillboards,
                        position,
                        position,
                        ResourceBillboardKind.Tree,
                        tile.IsDesignated,
                        $"Tree_{position.X}_{position.Y}_{position.Z}",
                        treeVisual.Texture,
                        ResolveBillboardPosition(position, GroundSpriteHeight),
                        treeVisual.WorldSize);
                }

                if (hasTreeCanopyOverlay)
                    continue;

                if (!WorldSpriteVisuals.TryPlantOverlay(tile.PlantDefId, tile.PlantGrowthStage, tile.PlantYieldLevel, tile.PlantSeedLevel, out var plantVisual))
                    continue;

                visiblePlantIds.Add(position);
                var plantHeight = tile.TileDefId == TileDefIds.Tree ? TreeSpriteOverlayHeight : GroundSpriteHeight;
                SyncBillboard(
                    _plantBillboards,
                    position,
                    position,
                    ResourceBillboardKind.Plant,
                    isDesignated: false,
                    $"Plant_{position.X}_{position.Y}_{position.Z}",
                    plantVisual.Texture,
                    ResolveBillboardPosition(position, plantHeight),
                    plantVisual.WorldSize);
            }
        }

        RemoveStaleBillboards(_treeBillboards, visibleTreeIds);
        RemoveStaleBillboards(_plantBillboards, visiblePlantIds);
    }

    private void SyncResourceBillboardHighlights(InputController? input)
    {
        UpdateResourceBillboardHighlights(_treeBillboards.Values, input);
        UpdateResourceBillboardHighlights(_plantBillboards.Values, input);
    }

    private void UpdateResourceBillboardHighlights(IEnumerable<BillboardState> states, InputController? input)
    {
        foreach (var state in states)
        {
            state.IsAreaSelected = IsTileWithinSelection(state.TilePosition, input);
            ApplyResourceBillboardHighlight(state);
        }
    }

    private bool IsTileWithinSelection(Vec3i tilePosition, InputController? input)
    {
        return SelectionContains(input?.GetSelectionRect(), tilePosition)
            || SelectionContains(input?.GetSelectedAreaRect(), tilePosition);
    }

    private static bool SelectionContains((Vector2I from, Vector2I to)? selection, Vec3i tilePosition)
    {
        if (selection is not { } rect)
            return false;

        return tilePosition.X >= rect.from.X
            && tilePosition.X <= rect.to.X
            && tilePosition.Y >= rect.from.Y
            && tilePosition.Y <= rect.to.Y;
    }

    private void ApplyResourceBillboardHighlight(BillboardState state)
    {
        state.DesignationOutline.Visible = _isActive && state.IsDesignated;
        state.Outline.Visible = _isActive
            && (state.IsAreaSelected || ReferenceEquals(state, _hoveredResourceBillboard));
    }

    private void SyncBillboard<TKey>(
        Dictionary<TKey, BillboardState> states,
        TKey entityId,
        string name,
        Texture2D texture,
        Vector3 position,
        Vector2 size)
        where TKey : notnull
    {
        if (!states.TryGetValue(entityId, out var state))
        {
            state = CreateBillboardState(name, texture, size);
            states[entityId] = state;
        }
        else if (state.Texture != texture || state.Size != size)
        {
            ApplyBillboardVisual(state, texture, size);
        }

        state.Root.Position = position;
        state.Root.Visible = _isActive;
        ApplyResourceBillboardHighlight(state);
    }

    private void SyncBillboard<TKey>(
        Dictionary<TKey, BillboardState> states,
        TKey entityId,
        Vec3i tilePosition,
        ResourceBillboardKind kind,
        bool isDesignated,
        string name,
        Texture2D texture,
        Vector3 position,
        Vector2 size)
        where TKey : notnull
    {
        SyncBillboard(states, entityId, name, texture, position, size);
        states[entityId].TilePosition = tilePosition;
        states[entityId].Kind = kind;
        states[entityId].IsDesignated = isDesignated;
        ApplyResourceBillboardHighlight(states[entityId]);
    }

    private BillboardState CreateBillboardState(string name, Texture2D texture, Vector2 size)
    {
        var root = new Node3D { Name = name };
        var designationOutline = new MeshInstance3D
        {
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        var outline = new MeshInstance3D
        {
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        var mesh = new MeshInstance3D
        {
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        root.AddChild(designationOutline);
        root.AddChild(outline);
        root.AddChild(mesh);
        AddChild(root);

        var state = new BillboardState(root, mesh, designationOutline, outline, texture, size);
        ApplyBillboardVisual(state, texture, size);
        return state;
    }

    private void ApplyBillboardVisual(BillboardState state, Texture2D texture, Vector2 size)
    {
        state.Texture = texture;
        state.Size = size;
        var quadMesh = state.Mesh.Mesh as QuadMesh ?? new QuadMesh();
        quadMesh.Size = size;
        state.Mesh.Mesh = quadMesh;
        state.Mesh.MaterialOverride = GetBillboardMaterial(texture);
        state.Mesh.Position = new Vector3(0f, size.Y * 0.5f, 0f);

        var designationOutlineMesh = state.DesignationOutline.Mesh as QuadMesh ?? new QuadMesh();
        designationOutlineMesh.Size = size * ResourceDesignationOutlineScale;
        state.DesignationOutline.Mesh = designationOutlineMesh;
        state.DesignationOutline.MaterialOverride = GetDesignationOutlineBillboardMaterial(texture);
        state.DesignationOutline.Position = new Vector3(0f, size.Y * 0.5f, ResourceDesignationOutlineDepthOffset);

        var outlineMesh = state.Outline.Mesh as QuadMesh ?? new QuadMesh();
        outlineMesh.Size = size * ResourceHoverOutlineScale;
        state.Outline.Mesh = outlineMesh;
        state.Outline.MaterialOverride = GetOutlineBillboardMaterial(texture);
        state.Outline.Position = new Vector3(0f, size.Y * 0.5f, ResourceHoverOutlineDepthOffset);
        ApplyResourceBillboardHighlight(state);
    }

    private StandardMaterial3D GetBillboardMaterial(Texture2D texture)
    {
        if (_billboardMaterials.TryGetValue(texture, out var material))
            return material;

        material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoTexture = texture,
            RenderPriority = BillboardRenderPriority,
        };

        _billboardMaterials[texture] = material;
        return material;
    }

    private StandardMaterial3D GetDesignationOutlineBillboardMaterial(Texture2D texture)
    {
        if (_designationOutlineBillboardMaterials.TryGetValue(texture, out var material))
            return material;

        material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoTexture = texture,
            AlbedoColor = ResourceDesignationOutlineTint,
            RenderPriority = BillboardOutlineRenderPriority,
        };

        _designationOutlineBillboardMaterials[texture] = material;
        return material;
    }

    private StandardMaterial3D GetOutlineBillboardMaterial(Texture2D texture)
    {
        if (_outlineBillboardMaterials.TryGetValue(texture, out var material))
            return material;

        material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoTexture = texture,
            AlbedoColor = ResourceHoverOutlineTint,
            RenderPriority = BillboardOutlineRenderPriority,
        };

        _outlineBillboardMaterials[texture] = material;
        return material;
    }

    private bool TryResolveHoveredResourceBillboardTile(Camera3D? camera, Viewport viewport, Vector2 screenPosition, out Vector2I tile)
    {
        tile = default;
        if (camera is null)
        {
            SetHoveredResourceBillboard(null);
            return false;
        }

        ResourceBillboardPickCandidate? best = null;
        TryPickResourceBillboards(camera, viewport, screenPosition, _plantBillboards.Values, ref best);
        TryPickResourceBillboards(camera, viewport, screenPosition, _treeBillboards.Values, ref best);

        SetHoveredResourceBillboard(best?.State);
        if (best is null)
            return false;

        tile = new Vector2I(best.Value.TilePosition.X, best.Value.TilePosition.Y);
        return true;
    }

    private void TryPickResourceBillboards(Camera3D camera, Viewport viewport, Vector2 screenPosition, IEnumerable<BillboardState> states, ref ResourceBillboardPickCandidate? best)
    {
        foreach (var state in states)
        {
            if (!TryProjectBillboard(camera, viewport, state, out var screenCenter, out var screenRect, out var depthSquared))
                continue;

            if (!screenRect.HasPoint(screenPosition))
                continue;

            var screenDistanceSquared = screenCenter.DistanceSquaredTo(screenPosition);
            if (best is null
                || depthSquared < best.Value.DepthSquared
                || (Mathf.IsEqualApprox(depthSquared, best.Value.DepthSquared) && screenDistanceSquared < best.Value.ScreenDistanceSquared))
            {
                best = new ResourceBillboardPickCandidate(state, state.TilePosition, state.Kind, depthSquared, screenDistanceSquared);
            }
        }
    }

    private bool TryGetDebugResourceBillboardProbe(Camera3D camera, Viewport viewport, IEnumerable<BillboardState> states, out Vector2 screenPosition, out Vector2I tile)
    {
        screenPosition = default;
        tile = default;

        foreach (var state in states)
        {
            if (!TryProjectBillboard(camera, viewport, state, out var projectedCenter, out _, out _))
                continue;

            screenPosition = projectedCenter;
            tile = new Vector2I(state.TilePosition.X, state.TilePosition.Y);
            return true;
        }

        return false;
    }

    private static bool TryProjectBillboard(Camera3D camera, Viewport viewport, BillboardState state, out Vector2 screenCenter, out Rect2 screenRect, out float depthSquared)
    {
        screenCenter = default;
        screenRect = default;
        depthSquared = 0f;

        if (!state.Root.Visible)
            return false;

        var rightAxis = camera.GlobalTransform.Basis.X.Normalized();
        var upAxis = camera.GlobalTransform.Basis.Y.Normalized();
        var footWorld = state.Root.GlobalTransform.Origin;
        var centerWorld = footWorld + (upAxis * (state.Size.Y * 0.5f));
        var cameraOrigin = camera.GlobalTransform.Origin;
        var forwardAxis = -camera.GlobalTransform.Basis.Z.Normalized();
        var toCenter = centerWorld - cameraOrigin;
        if (toCenter.Dot(forwardAxis) <= 0f)
            return false;

        var rightWorld = centerWorld + (rightAxis * (state.Size.X * 0.5f));
        var topWorld = centerWorld + (upAxis * (state.Size.Y * 0.5f));
        screenCenter = camera.UnprojectPosition(centerWorld);
        var rightScreen = camera.UnprojectPosition(rightWorld);
        var topScreen = camera.UnprojectPosition(topWorld);
        var halfWidth = Mathf.Max(3f, Mathf.Abs(rightScreen.X - screenCenter.X) + 2f);
        var halfHeight = Mathf.Max(5f, Mathf.Abs(topScreen.Y - screenCenter.Y) + 2f);
        screenRect = new Rect2(
            screenCenter - new Vector2(halfWidth, halfHeight),
            new Vector2(halfWidth * 2f, halfHeight * 2f));

        var viewportRect = viewport.GetVisibleRect();
        if (!viewportRect.Intersects(screenRect))
            return false;

        depthSquared = toCenter.LengthSquared();
        return true;
    }

    private void SetHoveredResourceBillboard(BillboardState? next)
    {
        var previous = _hoveredResourceBillboard;
        if (ReferenceEquals(previous, next))
        {
            if (previous is not null)
                ApplyResourceBillboardHighlight(previous);

            return;
        }

        _hoveredResourceBillboard = next;
        if (previous is not null)
            ApplyResourceBillboardHighlight(previous);

        if (next is not null)
            ApplyResourceBillboardHighlight(next);
    }

    private static int CountEmphasizedResourceBillboards(IEnumerable<BillboardState> states)
        => states.Count(state => state.Outline.Visible);

    private static bool HasDebugDesignatedResourceBillboardOutline(IEnumerable<BillboardState> states, Vector2I tile)
        => states.Any(state => state.IsDesignated
            && state.DesignationOutline.Visible
            && state.TilePosition.X == tile.X
            && state.TilePosition.Y == tile.Y);

    private static Vector3 ResolveBillboardPosition(Vec3i position, float localFeetHeight)
        => new(position.X + 0.5f, (position.Z * VerticalSliceSpacing) + localFeetHeight, position.Y + 0.5f);

    private void AddBuildingFootprint(
        List<WorldOverlayMesher.OverlayPlate> plates,
        WorldMap? map,
        DataManager? data,
        Vec3i origin,
        string buildingDefId,
        BuildingRotation rotation,
        int currentZ,
        Rect2I visibleTileBounds,
        Color color,
        float inset)
    {
        var definition = data?.Buildings.GetOrNull(buildingDefId);
        if (definition is null || definition.Footprint.Count == 0)
        {
            AddTilePlate(plates, map, origin.X, origin.Y, currentZ, visibleTileBounds, color, inset);
            return;
        }

        foreach (var footprintPosition in BuildingPlacementGeometry.EnumerateWorldFootprint(definition, origin, rotation))
            AddTilePlate(plates, map, footprintPosition.X, footprintPosition.Y, currentZ, visibleTileBounds, color, inset);
    }

    private static void AddTilePlate(
        List<WorldOverlayMesher.OverlayPlate> plates,
        int x,
        int y,
        Rect2I visibleTileBounds,
        float overlayBaseY,
        Color color,
        float inset)
    {
        if (!TileBoundsContains(visibleTileBounds, x, y))
            return;

        plates.Add(new WorldOverlayMesher.OverlayPlate(
            x + inset,
            x + 1f - inset,
            y + inset,
            y + 1f - inset,
            overlayBaseY,
            overlayBaseY + OverlayThickness,
            color,
            color.Darkened(0.18f)));
    }

    private static void AddTilePlate(
        List<WorldOverlayMesher.OverlayPlate> plates,
        WorldMap? map,
        int x,
        int y,
        int currentZ,
        Rect2I visibleTileBounds,
        Color color,
        float inset)
    {
        if (!TileBoundsContains(visibleTileBounds, x, y))
            return;

        var overlayBaseY = ResolveOverlayBaseY(map, x, y, currentZ);
        AddTilePlate(plates, x, y, visibleTileBounds, overlayBaseY, color, inset);
    }

    private static void AddTilePulsePlate(List<WorldOverlayMesher.OverlayPlate> plates, GameFeedbackController.TilePulseView tilePulse, Rect2I visibleTileBounds, float overlayBaseY)
    {
        var color = tilePulse.Pulse.WithAlpha(0.08f + (tilePulse.Pulse.Flash * 0.18f));
        AddTilePlate(
            plates,
            tilePulse.Position.X,
            tilePulse.Position.Y,
            visibleTileBounds,
            overlayBaseY,
            color,
            ResolvePulseInset(0.12f, tilePulse.Pulse.Ring, 0.10f));
    }

    private static void AddResourceBurstCuePlate(List<WorldOverlayMesher.OverlayPlate> plates, GameFeedbackController.ResourceBurstCueView resourceBurst, Rect2I visibleTileBounds, float overlayBaseY)
    {
        var fade = Mathf.Clamp(resourceBurst.TimeLeft / resourceBurst.Duration, 0f, 1f);
        var progress = 1f - fade;
        var scale = Mathf.Clamp(resourceBurst.Scale, 0.20f, 1.25f);
        var dustColor = resourceBurst.Color.Lightened(0.24f) with { A = 0.08f + (fade * 0.14f) };
        var chipColor = resourceBurst.Color.Darkened(0.12f) with { A = 0.10f + (fade * 0.24f) };
        var splinterColor = resourceBurst.Color.Lightened(0.08f) with { A = 0.10f + (fade * 0.22f) };
        var centerInset = Mathf.Clamp(0.34f - (progress * 0.08f), 0.18f, 0.34f);
        var drift = Mathf.Lerp(0.08f, 0.24f, progress) * scale;
        var chipHalfWidth = Mathf.Lerp(0.07f, 0.04f, progress) * scale;
        var chipHalfLength = Mathf.Lerp(0.10f, 0.06f, progress) * scale;
        var diagonalDrift = Mathf.Lerp(0.05f, 0.18f, progress) * scale;
        var (dustMin, dustMax) = ScaleLocalRange(centerInset, 1f - centerInset, scale);
        var (westMinX, westMaxX) = ScaleLocalRange(0.50f - drift - chipHalfWidth, 0.50f - drift + chipHalfWidth, scale);
        var (chipMin, chipMax) = ScaleLocalRange(0.50f - chipHalfLength, 0.50f + chipHalfLength, scale);
        var (eastMinX, eastMaxX) = ScaleLocalRange(0.50f + drift - chipHalfWidth, 0.50f + drift + chipHalfWidth, scale);
        var (northMinY, northMaxY) = ScaleLocalRange(0.50f - drift - chipHalfWidth, 0.50f - drift + chipHalfWidth, scale);
        var (southMinY, southMaxY) = ScaleLocalRange(0.50f + drift - chipHalfWidth, 0.50f + drift + chipHalfWidth, scale);
        var (splinterMinX, splinterMaxX) = ScaleLocalRange(0.50f + diagonalDrift - 0.05f, 0.50f + diagonalDrift + 0.05f, scale);
        var (splinterMinY, splinterMaxY) = ScaleLocalRange(0.50f - diagonalDrift - 0.05f, 0.50f - diagonalDrift + 0.05f, scale);

        AddTileLocalPlate(
            plates,
            resourceBurst.Position.X,
            resourceBurst.Position.Y,
            visibleTileBounds,
            overlayBaseY,
            dustMin,
            dustMax,
            dustMin,
            dustMax,
            dustColor,
            0.68f);

        AddTileLocalPlate(
            plates,
            resourceBurst.Position.X,
            resourceBurst.Position.Y,
            visibleTileBounds,
            overlayBaseY,
            westMinX,
            westMaxX,
            chipMin,
            chipMax,
            chipColor,
            0.82f);
        AddTileLocalPlate(
            plates,
            resourceBurst.Position.X,
            resourceBurst.Position.Y,
            visibleTileBounds,
            overlayBaseY,
            eastMinX,
            eastMaxX,
            chipMin,
            chipMax,
            chipColor,
            0.82f);
        AddTileLocalPlate(
            plates,
            resourceBurst.Position.X,
            resourceBurst.Position.Y,
            visibleTileBounds,
            overlayBaseY,
            chipMin,
            chipMax,
            northMinY,
            northMaxY,
            chipColor,
            0.82f);
        AddTileLocalPlate(
            plates,
            resourceBurst.Position.X,
            resourceBurst.Position.Y,
            visibleTileBounds,
            overlayBaseY,
            chipMin,
            chipMax,
            southMinY,
            southMaxY,
            chipColor,
            0.82f);
        AddTileLocalPlate(
            plates,
            resourceBurst.Position.X,
            resourceBurst.Position.Y,
            visibleTileBounds,
            overlayBaseY,
            splinterMinX,
            splinterMaxX,
            splinterMinY,
            splinterMaxY,
            splinterColor,
            0.76f);
    }

    private static (float Min, float Max) ScaleLocalRange(float min, float max, float scale)
    {
        var center = (min + max) * 0.5f;
        var halfExtent = (max - min) * 0.5f * scale;
        return (center - halfExtent, center + halfExtent);
    }

    private static void AddCombatCuePlate(List<WorldOverlayMesher.OverlayPlate> plates, GameFeedbackController.CombatCueView combatCue, Rect2I visibleTileBounds, float overlayBaseY)
    {
        var fade = Mathf.Clamp(combatCue.TimeLeft / combatCue.Duration, 0f, 1f);
        var progress = 1f - fade;
        var outerColor = combatCue.Color with { A = (combatCue.DidHit ? 0.10f : 0.08f) + (fade * (combatCue.DidHit ? 0.24f : 0.18f)) };
        var coreColor = combatCue.Color.Lightened(combatCue.DidHit ? 0.18f : 0.08f) with { A = (combatCue.DidHit ? 0.16f : 0.12f) + (fade * (combatCue.DidHit ? 0.34f : 0.22f)) };
        var outerInset = Mathf.Clamp((combatCue.DidHit ? 0.28f : 0.32f) - (progress * 0.10f), 0.14f, 0.34f);
        var coreInset = Mathf.Clamp((combatCue.DidHit ? 0.38f : 0.40f) - (progress * 0.06f), 0.24f, 0.42f);

        AddTileLocalPlate(
            plates,
            combatCue.Position.X,
            combatCue.Position.Y,
            visibleTileBounds,
            overlayBaseY,
            outerInset,
            1f - outerInset,
            outerInset,
            1f - outerInset,
            outerColor,
            0.85f);
        AddTileLocalPlate(
            plates,
            combatCue.Position.X,
            combatCue.Position.Y,
            visibleTileBounds,
            overlayBaseY,
            coreInset,
            1f - coreInset,
            coreInset,
            1f - coreInset,
            coreColor,
            1.15f);

        if (combatCue.DirectionX == 0 && combatCue.DirectionY == 0)
            return;

        var streakColor = combatCue.Color.Darkened(combatCue.DidHit ? 0.08f : 0.02f) with { A = (combatCue.DidHit ? 0.12f : 0.09f) + (fade * (combatCue.DidHit ? 0.22f : 0.14f)) };
        AddDirectionalCombatStreak(plates, combatCue, visibleTileBounds, overlayBaseY, streakColor, progress);
    }

    private static void AddDirectionalCombatStreak(
        List<WorldOverlayMesher.OverlayPlate> plates,
        GameFeedbackController.CombatCueView combatCue,
        Rect2I visibleTileBounds,
        float overlayBaseY,
        Color color,
        float progress)
    {
        const float laneMin = 0.36f;
        const float laneMax = 0.64f;
        var reach = Mathf.Lerp(0.30f, 0.58f, progress);

        if (combatCue.DirectionX > 0)
        {
            AddTileLocalPlate(plates, combatCue.Position.X, combatCue.Position.Y, visibleTileBounds, overlayBaseY, 0.04f, reach, laneMin, laneMax, color, 0.72f);
            return;
        }

        if (combatCue.DirectionX < 0)
        {
            AddTileLocalPlate(plates, combatCue.Position.X, combatCue.Position.Y, visibleTileBounds, overlayBaseY, 1f - reach, 0.96f, laneMin, laneMax, color, 0.72f);
            return;
        }

        if (combatCue.DirectionY > 0)
        {
            AddTileLocalPlate(plates, combatCue.Position.X, combatCue.Position.Y, visibleTileBounds, overlayBaseY, laneMin, laneMax, 0.04f, reach, color, 0.72f);
            return;
        }

        AddTileLocalPlate(plates, combatCue.Position.X, combatCue.Position.Y, visibleTileBounds, overlayBaseY, laneMin, laneMax, 1f - reach, 0.96f, color, 0.72f);
    }

    private static void AddTileLocalPlate(
        List<WorldOverlayMesher.OverlayPlate> plates,
        int x,
        int y,
        Rect2I visibleTileBounds,
        float overlayBaseY,
        float minXNorm,
        float maxXNorm,
        float minYNorm,
        float maxYNorm,
        Color color,
        float heightScale)
    {
        if (!TileBoundsContains(visibleTileBounds, x, y))
            return;

        plates.Add(new WorldOverlayMesher.OverlayPlate(
            x + Mathf.Clamp(minXNorm, 0f, 1f),
            x + Mathf.Clamp(maxXNorm, 0f, 1f),
            y + Mathf.Clamp(minYNorm, 0f, 1f),
            y + Mathf.Clamp(maxYNorm, 0f, 1f),
            overlayBaseY,
            overlayBaseY + (OverlayThickness * heightScale),
            color,
            color.Darkened(0.18f)));
    }

    private static void AddStockpilePerimeter(
        List<WorldOverlayMesher.OverlayPlate> plates,
        Vec3i slot,
        Rect2I visibleTileBounds,
        float overlayBaseY,
        StockpileVisualStyle style,
        HashSet<Vec3i> slotSet)
    {
        var hasNorth = slotSet.Contains(slot + Vec3i.North);
        var hasSouth = slotSet.Contains(slot + Vec3i.South);
        var hasEast = slotSet.Contains(slot + Vec3i.East);
        var hasWest = slotSet.Contains(slot + Vec3i.West);

        if (!hasNorth)
            AddStockpileEdge(plates, slot, visibleTileBounds, overlayBaseY, style, StockpileRailInset, 1f - StockpileRailInset, StockpileRailInset, StockpileRailInset + StockpileRailWidth, StockpileTrimInset, 1f - StockpileTrimInset, StockpileTrimInset, StockpileTrimInset + StockpileTrimWidth);

        if (!hasSouth)
            AddStockpileEdge(plates, slot, visibleTileBounds, overlayBaseY, style, StockpileRailInset, 1f - StockpileRailInset, 1f - StockpileRailInset - StockpileRailWidth, 1f - StockpileRailInset, StockpileTrimInset, 1f - StockpileTrimInset, 1f - StockpileTrimInset - StockpileTrimWidth, 1f - StockpileTrimInset);

        if (!hasWest)
            AddStockpileEdge(plates, slot, visibleTileBounds, overlayBaseY, style, StockpileRailInset, StockpileRailInset + StockpileRailWidth, StockpileRailInset, 1f - StockpileRailInset, StockpileTrimInset, StockpileTrimInset + StockpileTrimWidth, StockpileTrimInset, 1f - StockpileTrimInset);

        if (!hasEast)
            AddStockpileEdge(plates, slot, visibleTileBounds, overlayBaseY, style, 1f - StockpileRailInset - StockpileRailWidth, 1f - StockpileRailInset, StockpileRailInset, 1f - StockpileRailInset, 1f - StockpileTrimInset - StockpileTrimWidth, 1f - StockpileTrimInset, StockpileTrimInset, 1f - StockpileTrimInset);

        if (!hasNorth && !hasWest)
            AddStockpilePost(plates, slot, visibleTileBounds, overlayBaseY, style.InnerBorderColor, StockpileRailInset, StockpileRailInset, StockpilePostSize);

        if (!hasNorth && !hasEast)
            AddStockpilePost(plates, slot, visibleTileBounds, overlayBaseY, style.InnerBorderColor, 1f - StockpileRailInset - StockpilePostSize, StockpileRailInset, StockpilePostSize);

        if (!hasSouth && !hasWest)
            AddStockpilePost(plates, slot, visibleTileBounds, overlayBaseY, style.InnerBorderColor, StockpileRailInset, 1f - StockpileRailInset - StockpilePostSize, StockpilePostSize);

        if (!hasSouth && !hasEast)
            AddStockpilePost(plates, slot, visibleTileBounds, overlayBaseY, style.InnerBorderColor, 1f - StockpileRailInset - StockpilePostSize, 1f - StockpileRailInset - StockpilePostSize, StockpilePostSize);
    }

    private static void AddStockpileEdge(
        List<WorldOverlayMesher.OverlayPlate> plates,
        Vec3i slot,
        Rect2I visibleTileBounds,
        float overlayBaseY,
        StockpileVisualStyle style,
        float minX,
        float maxX,
        float minY,
        float maxY,
        float trimMinX,
        float trimMaxX,
        float trimMinY,
        float trimMaxY)
    {
        AddTileLocalPlate(plates, slot.X, slot.Y, visibleTileBounds, overlayBaseY, minX, maxX, minY, maxY, style.BorderColor, 1.10f);
        AddTileLocalPlate(plates, slot.X, slot.Y, visibleTileBounds, overlayBaseY, trimMinX, trimMaxX, trimMinY, trimMaxY, style.InnerBorderColor, 1.55f);
    }

    private static void AddStockpilePost(
        List<WorldOverlayMesher.OverlayPlate> plates,
        Vec3i slot,
        Rect2I visibleTileBounds,
        float overlayBaseY,
        Color color,
        float minX,
        float minY,
        float size)
    {
        AddTileLocalPlate(
            plates,
            slot.X,
            slot.Y,
            visibleTileBounds,
            overlayBaseY,
            minX,
            minX + size,
            minY,
            minY + size,
            color,
            1.85f);
    }

    private static float ResolvePulseInset(float baseInset, float ring, float spread)
        => Mathf.Clamp(baseInset - ((ring - 1f) * spread), 0.01f, 0.22f);

    private static Color ResolveSelectionOverlayColor(InputMode mode)
    {
        return mode switch
        {
            InputMode.DesignateClear => new Color(0.95f, 0.72f, 0.12f, 0.32f),
            InputMode.DesignateMine => new Color(1f, 0.78f, 0.05f, 0.32f),
            InputMode.DesignateCutTrees => new Color(0.25f, 0.92f, 0.15f, 0.32f),
            InputMode.DesignateCancel => new Color(0.95f, 0.2f, 0.10f, 0.32f),
            InputMode.StockpileZone => new Color(0.95f, 0.88f, 0.10f, 0.28f),
            _ => new Color(0.35f, 0.70f, 1f, 0.28f),
        };
    }

    private void SyncOverlayMesh(ref MeshInstance3D? meshInstance, string name, ArrayMesh? mesh)
    {
        if (mesh is null)
        {
            ClearOverlayMesh(ref meshInstance);
            return;
        }

        EnsureOverlayMaterial();
        var instance = meshInstance ?? CreateOverlayMeshInstance(name);
        ReplaceOwnedMesh(instance, mesh);
        instance.MaterialOverride = _overlayMaterial;
        instance.Visible = _isActive;
        meshInstance = instance;
    }

    private MeshInstance3D CreateOverlayMeshInstance(string name)
    {
        var meshInstance = new MeshInstance3D
        {
            Name = name,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        AddChild(meshInstance);
        return meshInstance;
    }

    private static void ClearOverlayMesh(ref MeshInstance3D? meshInstance)
    {
        if (meshInstance is null)
            return;

        ReplaceOwnedMesh(meshInstance, null);
        meshInstance.Visible = false;
    }

    private static void ClearBillboards<TKey>(Dictionary<TKey, BillboardState> states)
        where TKey : notnull
    {
        foreach (var state in states.Values)
            ReleaseBillboardState(state);

        states.Clear();
    }

    private static void RemoveStaleBillboards<TKey>(Dictionary<TKey, BillboardState> states, HashSet<TKey> visibleIds)
        where TKey : notnull
    {
        if (states.Count == 0)
            return;

        var staleIds = states.Keys.Where(id => !visibleIds.Contains(id)).ToArray();
        foreach (var staleId in staleIds)
        {
            ReleaseBillboardState(states[staleId]);
            states.Remove(staleId);
        }
    }

    private static void ReleaseChunkMeshState(ChunkMeshState state)
    {
        state.Mesh.MaterialOverride = null;
        ReplaceOwnedMesh(state.Mesh, null);
        state.Mesh.QueueFree();
    }

    private static void ReleaseStructureMeshState(StructureMeshState state)
    {
        state.BodyMesh.MaterialOverride = null;
        ReplaceOwnedMesh(state.BodyMesh, null);
        state.RoofMesh.MaterialOverride = null;
        ReplaceOwnedMesh(state.RoofMesh, null);
        state.Root.QueueFree();
    }

    private static void ReleaseBillboardState(BillboardState state)
    {
        ReplaceOwnedMesh(state.Mesh, null);
        state.Mesh.MaterialOverride = null;
        ReplaceOwnedMesh(state.Outline, null);
        state.Outline.MaterialOverride = null;
        state.Root.QueueFree();
    }

    private static void ReplaceOwnedMesh(MeshInstance3D meshInstance, Mesh? nextMesh)
    {
        var previousMesh = meshInstance.Mesh;
        meshInstance.Mesh = nextMesh;
        if (!ReferenceEquals(previousMesh, nextMesh))
            DisposeResource(previousMesh);
    }

    private static void DisposeMaterials(Dictionary<Texture2D, StandardMaterial3D> materials)
    {
        foreach (var material in materials.Values)
            DisposeResource(material);

        materials.Clear();
    }

    private static void DisposeResource(IDisposable? resource)
        => resource?.Dispose();

    private static bool TileBoundsContains(Rect2I visibleTileBounds, int x, int y)
    {
        if (visibleTileBounds.Size.X <= 0 || visibleTileBounds.Size.Y <= 0)
            return false;

        return x >= visibleTileBounds.Position.X
            && x < visibleTileBounds.Position.X + visibleTileBounds.Size.X
            && y >= visibleTileBounds.Position.Y
            && y < visibleTileBounds.Position.Y + visibleTileBounds.Size.Y;
    }

    private static float ResolveOverlayBaseY(int currentZ, WorldTileData tile)
        => WorldTileHeightResolver3D.ResolveSurfaceY(currentZ, tile, OverlaySurfaceOffset);

    private static float ResolveOverlayBaseY(WorldMap? map, int x, int y, int currentZ)
    {
        if (map is null || x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            return WorldTileHeightResolver3D.ResolveSliceY(currentZ, OverlaySurfaceOffset);

        var tile = map.GetTile(new Vec3i(x, y, currentZ));
        return tile.TileDefId == TileDefIds.Empty
            ? WorldTileHeightResolver3D.ResolveSliceY(currentZ, OverlaySurfaceOffset)
            : ResolveOverlayBaseY(currentZ, tile);
    }

    private static bool ChunkContainsWorldZ(Vec3i origin, int currentZ)
        => currentZ >= origin.Z && currentZ < origin.Z + Chunk.Depth;

    private static bool ChunkIntersectsVisibleTileBounds(Vec3i origin, Rect2I visibleTileBounds)
    {
        if (visibleTileBounds.Size.X <= 0 || visibleTileBounds.Size.Y <= 0)
            return false;

        var chunkMaxX = origin.X + Chunk.Width - 1;
        var chunkMaxY = origin.Y + Chunk.Height - 1;
        var visibleMaxX = visibleTileBounds.Position.X + visibleTileBounds.Size.X - 1;
        var visibleMaxY = visibleTileBounds.Position.Y + visibleTileBounds.Size.Y - 1;

        return chunkMaxX >= visibleTileBounds.Position.X
            && origin.X <= visibleMaxX
            && chunkMaxY >= visibleTileBounds.Position.Y
            && origin.Y <= visibleMaxY;
    }

    private static bool FootprintIntersectsVisibleTileBounds(Vec3i origin, BuildingDef definition, BuildingRotation rotation, Rect2I visibleTileBounds)
    {
        if (visibleTileBounds.Size.X <= 0 || visibleTileBounds.Size.Y <= 0)
            return false;

        var bounds = BuildingPlacementGeometry.GetRotatedBounds(definition, rotation);
        var minX = origin.X + bounds.MinX;
        var maxX = origin.X + bounds.MaxX;
        var minY = origin.Y + bounds.MinY;
        var maxY = origin.Y + bounds.MaxY;
        var visibleMaxX = visibleTileBounds.Position.X + visibleTileBounds.Size.X - 1;
        var visibleMaxY = visibleTileBounds.Position.Y + visibleTileBounds.Size.Y - 1;

        return maxX >= visibleTileBounds.Position.X
            && minX <= visibleMaxX
            && maxY >= visibleTileBounds.Position.Y
            && minY <= visibleMaxY;
    }

    private static Vector3 ResolveChunkMeshPosition(Vec3i origin, int currentZ)
        => new(origin.X * TileWorldSize, currentZ * VerticalSliceSpacing, origin.Y * TileWorldSize);

    private static TileRenderData? TryGetSnapshotTileRenderData(WorldChunkRenderSnapshot snapshot, WorldMap map, int x, int y, int z)
    {
        var position = new Vec3i(x, y, z);
        if (!map.IsInBounds(position))
            return null;

        WorldTileData tile;
        var localX = x - snapshot.Origin.X;
        var localY = y - snapshot.Origin.Y;
        var localZ = z - snapshot.Origin.Z;
        if (snapshot.TryGetLocalTile(localX, localY, localZ, out var snapshotTile))
            tile = snapshotTile;
        else
            tile = map.GetTile(position);

        return tile.TileDefId == TileDefIds.Empty
            ? null
            : new TileRenderData(tile.TileDefId, tile.MaterialId, tile.FluidType, tile.FluidLevel, tile.FluidMaterialId, tile.OreItemDefId, tile.PlantDefId, tile.PlantGrowthStage, tile.PlantYieldLevel, tile.PlantSeedLevel);
    }

    private static string? ResolveGroundTileDefIdFromMaterial(DataManager? data, string? materialId)
    {
        if (string.IsNullOrWhiteSpace(materialId))
            return null;

        var material = data?.Materials.GetOrNull(materialId);
        return GroundMaterialResolver.ResolveGroundTileDefId(materialId, material?.Tags);
    }

    private void EnsureChunkTopMaterial()
    {
        if (_chunkTopMaterial is not null)
        {
            RefreshChunkTopMaterialTextureArray();
            return;
        }

        _chunkTopMaterial = new ShaderMaterial
        {
            Shader = LoadChunkArrayShader(),
        };
        RefreshChunkTopMaterialTextureArray();
    }

    private void RefreshChunkTopMaterialTextureArray()
    {
        if (_chunkTopMaterial is null)
            return;

        var textureArray = TileSurfaceLibrary.GetTextureArray();
        if (textureArray is not null)
            _chunkTopMaterial.SetShaderParameter("tile_array", textureArray);
    }

    private void EnsureChunkDetailMaterial()
    {
        if (_chunkDetailMaterial is not null)
        {
            RefreshChunkDetailMaterialTextureArray();
            return;
        }

        _chunkDetailMaterial = new ShaderMaterial
        {
            Shader = LoadChunkArrayShader(),
        };
        RefreshChunkDetailMaterialTextureArray();
    }

    private void RefreshChunkDetailMaterialTextureArray()
    {
        if (_chunkDetailMaterial is null)
            return;

        var textureArray = TerrainDetailOverlayLibrary.GetTextureArray();
        if (textureArray is not null)
            _chunkDetailMaterial.SetShaderParameter("tile_array", textureArray);
    }

    private static Shader LoadChunkArrayShader()
    {
        var shader = GD.Load<Shader>(ChunkArrayShaderPath);
        if (shader is null)
            throw new InvalidOperationException($"Missing terrain texture array shader at {ChunkArrayShaderPath}.");

        return shader;
    }

    private void EnsureChunkMaterial()
    {
        if (_chunkMaterial is not null)
            return;

        _chunkMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    private void EnsureOverlayMaterial()
    {
        if (_overlayMaterial is not null)
            return;

        _overlayMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = OverlayRenderPriority,
        };
    }

    private void EnsureActorPresentation()
    {
        if (_actorPresentation is not null)
            return;

        _actorPresentation = new WorldActorPresentation3D
        {
            Name = "ActorPresentation3D",
        };
        AddChild(_actorPresentation);
        _actorPresentation.SetActive(_isActive);
    }

    private sealed class ChunkMeshState
    {
        public ChunkMeshState(MeshInstance3D mesh, int snapshotVersion, int sliceZ, bool isVisible)
        {
            Mesh = mesh;
            SnapshotVersion = snapshotVersion;
            SliceZ = sliceZ;
            IsVisible = isVisible;
        }

        public MeshInstance3D Mesh { get; }

        public int SnapshotVersion { get; set; }

        public int SliceZ { get; set; }

        public bool IsVisible { get; set; }
    }

    private sealed class StructureMeshState
    {
        public StructureMeshState(Node3D root, MeshInstance3D bodyMesh, MeshInstance3D roofMesh)
        {
            Root = root;
            BodyMesh = bodyMesh;
            RoofMesh = roofMesh;
        }

        public Node3D Root { get; }

        public MeshInstance3D BodyMesh { get; }

        public MeshInstance3D RoofMesh { get; }

        public string BuildingDefId { get; set; } = string.Empty;

        public Vec3i Origin { get; set; }

        public BuildingRotation Rotation { get; set; }

        public bool HideRoofOnHover { get; set; }
    }

    private sealed class BillboardState
    {
        public BillboardState(
            Node3D root,
            MeshInstance3D mesh,
            MeshInstance3D designationOutline,
            MeshInstance3D outline,
            Texture2D texture,
            Vector2 size)
        {
            Root = root;
            Mesh = mesh;
            DesignationOutline = designationOutline;
            Outline = outline;
            Texture = texture;
            Size = size;
        }

        public Node3D Root { get; }

        public MeshInstance3D Mesh { get; }

    public MeshInstance3D DesignationOutline { get; }

        public MeshInstance3D Outline { get; }

        public Texture2D Texture { get; set; }

        public Vector2 Size { get; set; }

        public Vec3i TilePosition { get; set; }

        public ResourceBillboardKind Kind { get; set; }

        public bool IsDesignated { get; set; }

        public bool IsAreaSelected { get; set; }
    }

    private enum ResourceBillboardKind
    {
        Tree,
        Plant,
    }

    private readonly record struct ResourceBillboardPickCandidate(
        BillboardState State,
        Vec3i TilePosition,
        ResourceBillboardKind Kind,
        float DepthSquared,
        float ScreenDistanceSquared);
}

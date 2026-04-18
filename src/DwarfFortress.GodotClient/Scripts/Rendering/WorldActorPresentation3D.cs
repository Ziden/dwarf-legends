using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Godot;

using WorldTileData = DwarfFortress.GameLogic.World.TileData;

namespace DwarfFortress.GodotClient.Rendering;

public readonly record struct ActorBillboardHoverPresentation(
    Vec3i TilePosition,
    Vector3 WorldPosition,
    Texture2D Texture,
    Vector2 Size,
    bool UsesCarriedVariant);

public sealed partial class WorldActorPresentation3D : Node3D
{
    private const float EntityFeetHeight = 0.18f;
    private const float ItemFeetHeight = 0.12f;
    private const float CarriedItemFeetHeight = 0.70f;
    private const float CarriedItemScale = 0.78f;
    private const float CarriedItemGripFraction = 0.46f;
    private const float CarriedItemForwardOffset = 0.05f;
    private const float ContainerPreviewScale = 0.54f;
    private const float ContainerPreviewDepthOffset = 0.016f;
    private const float WaterSurfaceOffset = 0.012f;
    private const int BillboardRenderPriority = 1;
    private const int CarriedBillboardRenderPriority = 3;
    private const int MaxVisibleCreatureBillboards = 96;
    private const int MaxVisibleItemLikeBillboards = 160;
    private const int WorldFxLabelFontSize = 24;
    private const float WorldFxLabelPixelSize = 0.0030f;
    private const int EmoteBubbleRenderPriority = 10;
    private const int EmoteIconRenderPriority = 11;
    private static readonly Vector2 EmoteBubbleSize = new(0.94f, 0.68f);
    private static readonly Vector2 EmoteTailSize = new(0.26f, 0.22f);
    private static readonly Vector2 EmoteBubbleIconSize = new(0.58f, 0.58f);
    private static readonly Vector2 EmoteSymbolIconSize = new(0.62f, 0.62f);

    private readonly Dictionary<int, BillboardState> _dwarfBillboards = new();
    private readonly Dictionary<int, BillboardState> _creatureBillboards = new();
    private readonly Dictionary<int, BillboardState> _itemBillboards = new();
    private readonly Dictionary<int, BillboardState> _carriedItemBillboards = new();
    private readonly Dictionary<int, BillboardState> _inventoryPickupCueBillboards = new();
    private readonly Dictionary<int, MovementInterpolationState> _movementInterpolations = new();
    private readonly Dictionary<int, DwarfSpriteFacing> _dwarfFacingById = new();
    private readonly Dictionary<int, CreatureSpriteFacing> _creatureFacingById = new();
    private readonly Dictionary<int, EmoteBubbleState> _emoteBubbles = new();
    private readonly Dictionary<int, Label3D> _worldFxLabels = new();
    private readonly Dictionary<Texture2D, StandardMaterial3D> _billboardMaterials = new();
    private readonly Dictionary<Texture2D, StandardMaterial3D> _carriedBillboardMaterials = new();
    private readonly List<int> _visibleDwarfIds = new();
    private readonly List<int> _visibleCreatureIds = new();
    private readonly List<int> _visibleItemIds = new();
    private readonly List<int> _visibleContainerIds = new();
    private readonly List<ItemLikeSceneEntry> _visibleItemEntries = new();
    private readonly HashSet<int> _visibleDwarfIdSet = new();
    private readonly HashSet<int> _visibleCreatureIdSet = new();
    private readonly HashSet<int> _visibleItemIdSet = new();
    private readonly HashSet<int> _visibleCarriedItemIdSet = new();
    private readonly HashSet<int> _visibleInventoryPickupCueIds = new();
    private readonly HashSet<int> _visibleInterpolatedIds = new();
    private readonly HashSet<int> _visibleEmoteBubbleIds = new();

    private MeshInstance3D? _waterEffectMesh;
    private StandardMaterial3D? _waterEffectMaterial;
    private bool _isActive;

    public override void _Ready()
    {
        EnsureWaterEffectMaterial();
        SetActive(false);
    }

    public (int Dwarves, int Creatures, int Items) GetDebugSpriteCounts()
        => (_dwarfBillboards.Count, _creatureBillboards.Count, _itemBillboards.Count + _carriedItemBillboards.Count);

    public int GetDebugItemBillboardRenderPriority()
    {
        var loosePriority = _itemBillboards.Values
            .Select(state => state.Mesh.MaterialOverride as StandardMaterial3D)
            .FirstOrDefault(material => material is not null)
            ?.RenderPriority ?? 0;
        var carriedPriority = _carriedItemBillboards.Values
            .Select(state => state.Mesh.MaterialOverride as StandardMaterial3D)
            .FirstOrDefault(material => material is not null)
            ?.RenderPriority ?? 0;
        return Math.Max(loosePriority, carriedPriority);
    }

    public int GetDebugItemPreviewCount(int itemLikeId)
    {
        if (!_itemBillboards.TryGetValue(itemLikeId, out var state))
            return _carriedItemBillboards.TryGetValue(itemLikeId, out var carriedState)
                ? carriedState.PreviewMeshes.Count(mesh => mesh.Visible)
                : 0;

        return state.PreviewMeshes.Count(mesh => mesh.Visible);
    }

    public int GetDebugVisibleInventoryPickupCueCount()
        => _inventoryPickupCueBillboards.Count;

    public int GetDebugMaxVisibleInventoryPickupCueId()
        => _inventoryPickupCueBillboards.Count > 0 ? _inventoryPickupCueBillboards.Keys.Max() : 0;

    public bool HasDebugInventoryPickupCue(int cueId)
        => _inventoryPickupCueBillboards.ContainsKey(cueId);

    public bool TryResolveHoveredBillboardTile(Camera3D? camera, Viewport viewport, Vector2 screenPosition, out Vector2I tile)
    {
        tile = default;
        if (camera is null)
            return false;

        BillboardPickCandidate? best = null;
        TryPickBillboards(camera, viewport, screenPosition, _dwarfBillboards.Values, ref best);
        TryPickBillboards(camera, viewport, screenPosition, _creatureBillboards.Values, ref best);
        TryPickBillboards(camera, viewport, screenPosition, _itemBillboards.Values, ref best);
        TryPickBillboards(camera, viewport, screenPosition, _carriedItemBillboards.Values, ref best);

        if (best is null)
            return false;

        tile = new Vector2I(best.Value.TilePosition.X, best.Value.TilePosition.Y);
        return true;
    }

    public bool TryGetDebugBillboardProbe(Camera3D? camera, Viewport viewport, out Vector2 screenPosition, out Vector2I tile)
    {
        screenPosition = default;
        tile = default;
        if (camera is null)
            return false;

        return TryGetDebugBillboardProbe(camera, viewport, _dwarfBillboards.Values, out screenPosition, out tile)
            || TryGetDebugBillboardProbe(camera, viewport, _creatureBillboards.Values, out screenPosition, out tile)
            || TryGetDebugBillboardProbe(camera, viewport, _itemBillboards.Values, out screenPosition, out tile)
            || TryGetDebugBillboardProbe(camera, viewport, _carriedItemBillboards.Values, out screenPosition, out tile);
    }

    public bool TryGetDebugBillboardWorldPosition(int entityId, out Vector3 worldPosition)
    {
        worldPosition = default;
        if (_dwarfBillboards.TryGetValue(entityId, out var dwarfBillboard))
        {
            worldPosition = dwarfBillboard.Root.Position;
            return true;
        }

        if (_creatureBillboards.TryGetValue(entityId, out var creatureBillboard))
        {
            worldPosition = creatureBillboard.Root.Position;
            return true;
        }

        if (_itemBillboards.TryGetValue(entityId, out var itemBillboard))
        {
            worldPosition = itemBillboard.Root.Position;
            return true;
        }

        if (_carriedItemBillboards.TryGetValue(entityId, out var carriedItemBillboard))
        {
            worldPosition = carriedItemBillboard.Root.Position;
            return true;
        }

        return false;
    }

    public bool TryGetDebugBillboardRenderPriority(int entityId, out int renderPriority)
    {
        renderPriority = default;
        if (TryGetBillboardState(entityId, out var billboardState)
            && billboardState.Mesh.MaterialOverride is StandardMaterial3D material)
        {
            renderPriority = material.RenderPriority;
            return true;
        }

        return false;
    }

    public bool TryGetHoverPresentation(int entityId, out ActorBillboardHoverPresentation snapshot)
    {
        snapshot = default;
        if (!TryGetBillboardState(entityId, out var billboardState))
            return false;

        snapshot = new ActorBillboardHoverPresentation(
            billboardState.TilePosition,
            billboardState.Root.GlobalTransform.Origin,
            billboardState.Texture,
            billboardState.Size,
            _carriedItemBillboards.ContainsKey(entityId));
        return true;
    }

    public bool TryGetDebugBillboardAlbedoColor(int entityId, out Color albedoColor)
    {
        albedoColor = default;
        if (TryGetBillboardState(entityId, out var billboardState)
            && billboardState.Mesh.MaterialOverride is StandardMaterial3D material)
        {
            albedoColor = material.AlbedoColor;
            return true;
        }

        return false;
    }

    public bool TryGetDebugBillboardProbeForEntity(int entityId, Camera3D? camera, Viewport viewport, out Vector2 screenPosition, out Vector2I tile)
    {
        screenPosition = default;
        tile = default;
        return camera is not null
            && TryGetBillboardState(entityId, out var billboardState)
            && TryGetDebugBillboardProbe(camera, viewport, new[] { billboardState }, out screenPosition, out tile);
    }

    public void Reset()
    {
        ClearBillboards(_dwarfBillboards);
        ClearBillboards(_creatureBillboards);
        ClearBillboards(_itemBillboards);
        ClearBillboards(_carriedItemBillboards);
        ClearBillboards(_inventoryPickupCueBillboards);
        _movementInterpolations.Clear();
        ClearEmoteBubbles(_emoteBubbles);
        ClearLabels(_worldFxLabels);
        ClearOverlayMesh(ref _waterEffectMesh);
        DisposeMaterials(_billboardMaterials);
        DisposeMaterials(_carriedBillboardMaterials);
        _billboardMaterials.Clear();
        _carriedBillboardMaterials.Clear();
        _dwarfFacingById.Clear();
        _creatureFacingById.Clear();
        ClearVisibleEntityIds();
    }

    private bool TryGetBillboardState(int entityId, out BillboardState billboardState)
    {
        if (_dwarfBillboards.TryGetValue(entityId, out var dwarfBillboard))
        {
            billboardState = dwarfBillboard;
            return true;
        }

        if (_creatureBillboards.TryGetValue(entityId, out var creatureBillboard))
        {
            billboardState = creatureBillboard;
            return true;
        }

        if (_itemBillboards.TryGetValue(entityId, out var itemBillboard))
        {
            billboardState = itemBillboard;
            return true;
        }

        if (_carriedItemBillboards.TryGetValue(entityId, out var carriedItemBillboard))
        {
            billboardState = carriedItemBillboard;
            return true;
        }

        billboardState = null!;
        return false;
    }

    public void SetActive(bool active)
    {
        _isActive = active;

        foreach (var state in _dwarfBillboards.Values)
            state.Root.Visible = active;

        foreach (var state in _creatureBillboards.Values)
            state.Root.Visible = active;

        foreach (var state in _itemBillboards.Values)
            state.Root.Visible = active;

        foreach (var state in _carriedItemBillboards.Values)
            state.Root.Visible = active;

        foreach (var state in _inventoryPickupCueBillboards.Values)
            state.Root.Visible = active;

        foreach (var bubble in _emoteBubbles.Values)
            bubble.Root.Visible = active;

        foreach (var label in _worldFxLabels.Values)
            label.Visible = active;

        if (_waterEffectMesh is not null)
            _waterEffectMesh.Visible = active;
    }

    public void Sync(
        Camera3D? camera,
        WorldMap? map,
        EntityRegistry? registry,
        ItemSystem? items,
        SpatialIndexSystem? spatial,
        MovementPresentationSystem? movementPresentation,
        DataManager? data,
        RenderCache renderCache,
        GameFeedbackController? feedback,
        int currentZ,
        Rect2I visibleTileBounds,
        double presentationTimeSeconds,
        SimulationProfiler? profiler = null)
    {
        if (camera is null || registry is null || spatial is null || !TryResolveVisibleTileRange(visibleTileBounds, out var minX, out var minY, out var maxX, out var maxY))
        {
            ClearVisibleEntityIds();
            _movementInterpolations.Clear();
            _dwarfFacingById.Clear();
            _creatureFacingById.Clear();
            ClearBillboards(_dwarfBillboards);
            ClearBillboards(_creatureBillboards);
            ClearBillboards(_itemBillboards);
            ClearBillboards(_carriedItemBillboards);
            ClearEmoteBubbles(_emoteBubbles);
            ClearLabels(_worldFxLabels);
            ClearOverlayMesh(ref _waterEffectMesh);
            return;
        }

        var nowSeconds = presentationTimeSeconds;

        using (profiler?.Measure("billboards") ?? default)
            SyncBillboards(camera, registry, items, spatial, movementPresentation, feedback, currentZ, minX, minY, maxX, maxY, nowSeconds);

        using (profiler?.Measure("item_pickup_cues") ?? default)
            SyncInventoryPickupCueBillboards(feedback, currentZ);

        using (profiler?.Measure("water_effects") ?? default)
            SyncWaterEffects(map, data, registry, renderCache, currentZ);

        using (profiler?.Measure("emote_labels") ?? default)
            SyncEmoteLabels(registry, renderCache, currentZ);

        using (profiler?.Measure("world_fx_labels") ?? default)
            SyncWorldFxLabels(renderCache, feedback, currentZ, visibleTileBounds);
    }

    private void SyncBillboards(Camera3D camera, EntityRegistry registry, ItemSystem? items, SpatialIndexSystem spatial, MovementPresentationSystem? movementPresentation, GameFeedbackController? feedback, int currentZ, int minX, int minY, int maxX, int maxY, double nowSeconds)
    {
        _visibleInterpolatedIds.Clear();
        SyncDwarfBillboards(camera, registry, spatial, movementPresentation, feedback, currentZ, minX, minY, maxX, maxY, nowSeconds);
        SyncCreatureBillboards(camera, registry, spatial, movementPresentation, currentZ, minX, minY, maxX, maxY, nowSeconds);

        if (items is null)
        {
            _visibleItemIds.Clear();
            _visibleItemIdSet.Clear();
            _visibleCarriedItemIdSet.Clear();
            ClearBillboards(_itemBillboards);
            ClearBillboards(_carriedItemBillboards);
            RemoveStaleInterpolations();
            return;
        }

        SyncItemBillboards(registry, items, spatial, movementPresentation, currentZ, minX, minY, maxX, maxY, nowSeconds);
        RemoveStaleInterpolations();
    }

    private void SyncInventoryPickupCueBillboards(GameFeedbackController? feedback, int currentZ)
    {
        _visibleInventoryPickupCueIds.Clear();
        if (feedback is null)
        {
            ClearBillboards(_inventoryPickupCueBillboards);
            return;
        }

        foreach (var cue in feedback.GetInventoryPickupCueViews(currentZ))
        {
            _visibleInventoryPickupCueIds.Add(cue.Id);
            var visual = WorldSpriteVisuals.Item(cue.ItemDefId);
            var progress = cue.Duration <= 0f
                ? 1f
                : Mathf.Clamp(1f - (cue.TimeLeft / cue.Duration), 0f, 1f);
            var start = ResolveBillboardPosition(cue.SourcePosition, ItemFeetHeight);
            var end = TryResolveCarrierAnchorPosition(cue.CarrierEntityId, out var carrierPosition)
                ? carrierPosition
                : ResolveBillboardPosition(cue.TargetPosition, CarriedItemFeetHeight);
            var position = start.Lerp(end, progress);
            position.Y += Mathf.Sin(progress * Mathf.Pi) * 0.34f;

            SyncBillboard(
                _inventoryPickupCueBillboards,
                cue.Id,
                $"InventoryPickupCue_{cue.Id}",
                visual.Texture,
                position,
                visual.WorldSize * CarriedItemScale);
        }

        RemoveStaleBillboards(_inventoryPickupCueBillboards, _visibleInventoryPickupCueIds);
    }

    private void SyncDwarfBillboards(Camera3D camera, EntityRegistry registry, SpatialIndexSystem spatial, MovementPresentationSystem? movementPresentation, GameFeedbackController? feedback, int currentZ, int minX, int minY, int maxX, int maxY, double nowSeconds)
    {
        _visibleDwarfIds.Clear();
        _visibleDwarfIdSet.Clear();
        spatial.CollectDwarvesInBounds(currentZ, minX, minY, maxX, maxY, _visibleDwarfIds);

        foreach (var dwarfId in _visibleDwarfIds)
        {
            if (!registry.TryGetById<Dwarf>(dwarfId, out var dwarf) || dwarf is null)
                continue;

            var position = dwarf.Position.Position;
            _visibleDwarfIdSet.Add(dwarf.Id);
            _visibleInterpolatedIds.Add(dwarf.Id);
            var segment = movementPresentation?.TryGetEntitySegment(dwarf.Id, out var movementSegment) == true ? movementSegment : (MovementPresentationSegment?)null;
            var movementView = ResolveInterpolatedMovementView(dwarf.Id, position, segment, nowSeconds);
            var workAnimation = feedback?.TryGetDwarfWorkAnimation(dwarf.Id, out var activeWorkAnimation) == true
                ? activeWorkAnimation
                : (GameFeedbackController.DwarfWorkAnimationView?)null;
            var facing = ResolveDwarfFacing(camera, dwarf.Id, position, movementView, workAnimation);
            var pose = ResolveDwarfPose(facing, movementView, workAnimation);
            var visual = WorldSpriteVisuals.Dwarf(dwarf.Appearance, pose);
            var billboardPosition = ResolveBillboardPosition(movementView.TilePosition, EntityFeetHeight);
            var billboardSize = visual.WorldSize;
            if (feedback?.TryGetDwarfPulseView(dwarf.Id, out var pulse) == true)
            {
                billboardSize *= pulse.Scale;
                billboardPosition.Y += ResolvePulseLiftWorldUnits(pulse.Lift);
            }

            SyncBillboard(
                _dwarfBillboards,
                dwarf.Id,
                position,
                $"Dwarf_{dwarf.Id}",
                visual.Texture,
                billboardPosition,
                billboardSize);
            _dwarfBillboards[dwarf.Id].Root.RotationDegrees = Vector3.Zero;
            _dwarfBillboards[dwarf.Id].HasCarryAnchor = true;
            _dwarfBillboards[dwarf.Id].CarryAnchorOffset = ResolveDwarfCarryAnchorOffset(camera, pose, billboardSize);
        }

        RemoveStaleBillboards(_dwarfBillboards, _visibleDwarfIdSet);
        RemoveStaleDwarfFacingStates();
    }

    private DwarfSpriteFacing ResolveDwarfFacing(Camera3D camera, int dwarfId, Vec3i logicalPosition, InterpolatedMovementView movementView, GameFeedbackController.DwarfWorkAnimationView? workAnimation)
    {
        if (movementView.ActiveSegment is MovementPresentationSegment movementSegment
            && TryResolveDwarfFacing(camera, movementSegment.OldPos, movementSegment.NewPos, out var movementFacing))
        {
            _dwarfFacingById[dwarfId] = movementFacing;
            return movementFacing;
        }

        if (workAnimation is GameFeedbackController.DwarfWorkAnimationView activeWorkAnimation
            && TryResolveDwarfFacing(camera, logicalPosition, activeWorkAnimation.TargetPos, out var workFacing))
        {
            _dwarfFacingById[dwarfId] = workFacing;
            return workFacing;
        }

        if (_dwarfFacingById.TryGetValue(dwarfId, out var cachedFacing))
            return cachedFacing;

        _dwarfFacingById[dwarfId] = DwarfSpriteFacing.Right;
        return DwarfSpriteFacing.Right;
    }

    private static bool TryResolveDwarfFacing(Camera3D camera, Vec3i from, Vec3i to, out DwarfSpriteFacing facing)
    {
        var delta = new Vector2(to.X - from.X, to.Y - from.Y);
        if (delta.LengthSquared() <= Mathf.Epsilon)
        {
            facing = default;
            return false;
        }

        var cameraRight = new Vector2(camera.GlobalTransform.Basis.X.X, camera.GlobalTransform.Basis.X.Z);
        if (cameraRight.LengthSquared() <= Mathf.Epsilon)
            cameraRight = Vector2.Right;

        facing = delta.Normalized().Dot(cameraRight.Normalized()) >= 0f
            ? DwarfSpriteFacing.Right
            : DwarfSpriteFacing.Left;
        return true;
    }

    private static DwarfSpritePose ResolveDwarfPose(DwarfSpriteFacing facing, InterpolatedMovementView movementView, GameFeedbackController.DwarfWorkAnimationView? workAnimation)
    {
        if (movementView.ActiveSegment is not null && movementView.Progress < 1f)
        {
            var walkFrame = movementView.Progress < 0.5f ? 0 : 1;
            return new DwarfSpritePose(facing, DwarfSpriteActionKind.Walk, walkFrame);
        }

        if (workAnimation is GameFeedbackController.DwarfWorkAnimationView activeWorkAnimation)
        {
            var action = ResolveDwarfWorkAction(activeWorkAnimation.AnimationHint);
            if (action != DwarfSpriteActionKind.Idle)
            {
                var frameCount = DwarfSpriteComposer.GetFrameCount(action);
                var frameDuration = DwarfSpriteComposer.GetFrameDurationSeconds(action);
                var frame = frameCount <= 1 || frameDuration <= 0f
                    ? 0
                    : (int)Math.Floor(activeWorkAnimation.ElapsedSeconds / frameDuration) % frameCount;
                return new DwarfSpritePose(facing, action, frame);
            }
        }

        return DwarfSpritePose.Idle(facing);
    }

    private static DwarfSpriteActionKind ResolveDwarfWorkAction(string animationHint)
        => animationHint switch
        {
            "mining" => DwarfSpriteActionKind.Mine,
            "wood_cutting" => DwarfSpriteActionKind.Chop,
            "crafting" => DwarfSpriteActionKind.Craft,
            "gather_plants" => DwarfSpriteActionKind.Gather,
            "construction" => DwarfSpriteActionKind.Build,
            "combat" => DwarfSpriteActionKind.Combat,
            _ => DwarfSpriteActionKind.Idle,
        };

    private static Vector3 ResolveDwarfCarryAnchorOffset(Camera3D camera, DwarfSpritePose pose, Vector2 spriteWorldSize)
    {
        var localOffset = DwarfSpriteComposer.ResolveCarryAnchorOffset(pose, spriteWorldSize);
        var rightAxis = camera.GlobalTransform.Basis.X.Normalized();
        var upAxis = camera.GlobalTransform.Basis.Y.Normalized();
           var viewerAxis = camera.GlobalTransform.Basis.Z.Normalized();
        return (rightAxis * localOffset.X)
             + (upAxis * (localOffset.Y - (CarriedItemScale * CarriedItemGripFraction)))
               + (viewerAxis * CarriedItemForwardOffset);
    }

    private void RemoveStaleDwarfFacingStates()
    {
        if (_dwarfFacingById.Count == 0)
            return;

        var staleIds = _dwarfFacingById.Keys.Where(id => !_visibleDwarfIdSet.Contains(id)).ToArray();
        foreach (var staleId in staleIds)
            _dwarfFacingById.Remove(staleId);
    }

    private void SyncCreatureBillboards(Camera3D camera, EntityRegistry registry, SpatialIndexSystem spatial, MovementPresentationSystem? movementPresentation, int currentZ, int minX, int minY, int maxX, int maxY, double nowSeconds)
    {
        _visibleCreatureIds.Clear();
        _visibleCreatureIdSet.Clear();
        spatial.CollectCreaturesInBounds(currentZ, minX, minY, maxX, maxY, _visibleCreatureIds);
        TrimVisibleIds(_visibleCreatureIds, MaxVisibleCreatureBillboards);

        foreach (var creatureId in _visibleCreatureIds)
        {
            if (!registry.TryGetById<Creature>(creatureId, out var creature) || creature is null)
                continue;

            var position = creature.Position.Position;
            _visibleCreatureIdSet.Add(creature.Id);
            _visibleInterpolatedIds.Add(creature.Id);
            var segment = movementPresentation?.TryGetEntitySegment(creature.Id, out var movementSegment) == true ? movementSegment : (MovementPresentationSegment?)null;
            var movementView = ResolveInterpolatedMovementView(creature.Id, position, segment, nowSeconds);
            var facing = ResolveCreatureFacing(camera, creature.Id, movementView);
            var pose = ResolveCreaturePose(facing, movementView);
            var visual = WorldSpriteVisuals.Creature(creature.DefId, pose);
            SyncBillboard(
                _creatureBillboards,
                creature.Id,
                position,
                $"Creature_{creature.Id}",
                visual.Texture,
                ResolveBillboardPosition(movementView.TilePosition, EntityFeetHeight),
                visual.WorldSize);
        }

        RemoveStaleBillboards(_creatureBillboards, _visibleCreatureIdSet);
        RemoveStaleCreatureFacingStates();
    }

    private CreatureSpriteFacing ResolveCreatureFacing(Camera3D camera, int creatureId, InterpolatedMovementView movementView)
    {
        if (movementView.ActiveSegment is MovementPresentationSegment movementSegment
            && TryResolveCreatureFacing(camera, movementSegment.OldPos, movementSegment.NewPos, out var movementFacing))
        {
            _creatureFacingById[creatureId] = movementFacing;
            return movementFacing;
        }

        if (_creatureFacingById.TryGetValue(creatureId, out var cachedFacing))
            return cachedFacing;

        _creatureFacingById[creatureId] = CreatureSpriteFacing.Right;
        return CreatureSpriteFacing.Right;
    }

    private static bool TryResolveCreatureFacing(Camera3D camera, Vec3i from, Vec3i to, out CreatureSpriteFacing facing)
    {
        var delta = new Vector2(to.X - from.X, to.Y - from.Y);
        if (delta.LengthSquared() <= Mathf.Epsilon)
        {
            facing = default;
            return false;
        }

        var cameraRight = new Vector2(camera.GlobalTransform.Basis.X.X, camera.GlobalTransform.Basis.X.Z);
        if (cameraRight.LengthSquared() <= Mathf.Epsilon)
            cameraRight = Vector2.Right;

        facing = delta.Normalized().Dot(cameraRight.Normalized()) >= 0f
            ? CreatureSpriteFacing.Right
            : CreatureSpriteFacing.Left;
        return true;
    }

    private static CreatureSpritePose ResolveCreaturePose(CreatureSpriteFacing facing, InterpolatedMovementView movementView)
    {
        if (movementView.ActiveSegment is not null && movementView.Progress < 1f)
        {
            var walkFrame = movementView.Progress < 0.5f ? 0 : 1;
            return new CreatureSpritePose(facing, CreatureSpriteActionKind.Walk, walkFrame);
        }

        return CreatureSpritePose.Idle(facing);
    }

    private void RemoveStaleCreatureFacingStates()
    {
        if (_creatureFacingById.Count == 0)
            return;

        var staleIds = _creatureFacingById.Keys.Where(id => !_visibleCreatureIdSet.Contains(id)).ToArray();
        foreach (var staleId in staleIds)
            _creatureFacingById.Remove(staleId);
    }

    private static void TrimVisibleIds(List<int> visibleIds, int maxCount)
    {
        if (maxCount <= 0)
        {
            visibleIds.Clear();
            return;
        }

        if (visibleIds.Count <= maxCount)
            return;

        visibleIds.Sort();
        visibleIds.RemoveRange(maxCount, visibleIds.Count - maxCount);
    }

    private void SyncItemBillboards(EntityRegistry registry, ItemSystem items, SpatialIndexSystem spatial, MovementPresentationSystem? movementPresentation, int currentZ, int minX, int minY, int maxX, int maxY, double nowSeconds)
    {
        _visibleItemIdSet.Clear();
        _visibleCarriedItemIdSet.Clear();
        ItemLikeSceneResolver.CollectVisibleEntries(
            registry,
            items,
            spatial,
            movementPresentation,
            currentZ,
            minX,
            minY,
            maxX,
            maxY,
            _visibleDwarfIds,
            _visibleCreatureIds,
            _visibleItemIds,
            _visibleContainerIds,
            _visibleItemEntries,
            MaxVisibleItemLikeBillboards);

        foreach (var entry in _visibleItemEntries)
        {
            var targetStates = entry.CarryMode == ItemCarryMode.Hauling
                ? _carriedItemBillboards
                : _itemBillboards;
            var targetVisibleIds = entry.CarryMode == ItemCarryMode.Hauling
                ? _visibleCarriedItemIdSet
                : _visibleItemIdSet;

            targetVisibleIds.Add(entry.RuntimeId);
            _visibleInterpolatedIds.Add(entry.RuntimeId);
            var billboardSize = entry.Descriptor.Visual.WorldSize;
            if (entry.CarryMode == ItemCarryMode.Hauling)
                billboardSize *= CarriedItemScale;

            SyncBillboard(
                targetStates,
                entry.RuntimeId,
                entry.TilePosition,
                entry.NodeName,
                entry.Descriptor.Visual.Texture,
                ResolveItemBillboardPosition(entry, nowSeconds),
                billboardSize,
                useCarriedMaterial: entry.CarryMode == ItemCarryMode.Hauling);
            ApplyItemLikeDecorators(targetStates[entry.RuntimeId], entry.Descriptor);
        }

        RemoveStaleBillboards(_itemBillboards, _visibleItemIdSet);
        RemoveStaleBillboards(_carriedItemBillboards, _visibleCarriedItemIdSet);
    }

    private void ApplyItemLikeDecorators(BillboardState state, ItemLikeVisualDescriptor descriptor)
    {
        var visiblePreviewCount = Math.Min(descriptor.PreviewItems.Length, state.PreviewMeshes.Length);
        for (var index = 0; index < state.PreviewMeshes.Length; index++)
        {
            var previewMesh = state.PreviewMeshes[index];
            if (index >= visiblePreviewCount)
            {
                ReplaceOwnedMesh(previewMesh, null);
                previewMesh.MaterialOverride = null;
                previewMesh.Visible = false;
                continue;
            }

            var previewVisual = descriptor.PreviewItems[index];
            var previewSize = previewVisual.WorldSize * ContainerPreviewScale;
            var quadMesh = previewMesh.Mesh as QuadMesh ?? new QuadMesh();
            quadMesh.Size = previewSize;
            previewMesh.Mesh = quadMesh;
            previewMesh.MaterialOverride = GetBillboardMaterial(previewVisual.Texture);
            previewMesh.Position = ResolveContainerPreviewPosition(state.Size, visiblePreviewCount, index);
            previewMesh.Visible = _isActive;
        }
    }

    private static Vector3 ResolveContainerPreviewPosition(Vector2 containerSize, int previewCount, int index)
    {
        float[] xOffsets = previewCount switch
        {
            1 => new[] { 0f },
            2 => new[] { -0.18f, 0.18f },
            _ => new[] { -0.24f, 0f, 0.24f },
        };
        float[] yOffsets = previewCount switch
        {
            1 => new[] { 0.84f },
            2 => new[] { 0.78f, 0.88f },
            _ => new[] { 0.76f, 0.90f, 0.80f },
        };

        return new Vector3(
            containerSize.X * xOffsets[index],
            containerSize.Y * yOffsets[index],
            ContainerPreviewDepthOffset + (index * 0.0025f));
    }

    private Vector3 ResolveItemBillboardPosition(ItemLikeSceneEntry entry, double nowSeconds)
    {
        var interpolation = ResolveInterpolatedMovementView(entry.RuntimeId, entry.TilePosition, entry.MovementSegment, nowSeconds);
        if (TryResolveAnimatedItemPosition(interpolation, out var animatedPosition))
            return animatedPosition;

        if (entry.CarryMode == ItemCarryMode.Hauling &&
            entry.CarrierEntityId >= 0 &&
            TryResolveCarrierAnchorPosition(entry.CarrierEntityId, out var carriedPosition))
        {
            return carriedPosition;
        }

        var feetHeight = entry.CarryMode == ItemCarryMode.Hauling
            ? CarriedItemFeetHeight
            : ItemFeetHeight;
        return ResolveBillboardPosition(interpolation.TilePosition, feetHeight);
    }

    private bool TryResolveAnimatedItemPosition(InterpolatedMovementView interpolation, out Vector3 position)
    {
        if (interpolation.ActiveSegment is not MovementPresentationSegment segment)
        {
            position = default;
            return false;
        }

        var start = ResolvePresentationAnchorPosition(segment.StartAnchor, segment.OldPos, segment.StartAnchorEntityId);
        var end = ResolvePresentationAnchorPosition(segment.EndAnchor, segment.NewPos, segment.EndAnchorEntityId);
        position = start.Lerp(end, interpolation.Progress);
        if (segment.MotionKind == MovementPresentationMotionKind.Jump && segment.ArcHeight > 0f)
            position.Y += Mathf.Sin(interpolation.Progress * Mathf.Pi) * segment.ArcHeight;

        return true;
    }

    private Vector3 ResolvePresentationAnchorPosition(MovementPresentationAnchorKind anchorKind, Vec3i tilePosition, int anchorEntityId)
    {
        if (anchorKind == MovementPresentationAnchorKind.Carrier &&
            TryResolveCarrierAnchorPosition(anchorEntityId, out var carrierPosition))
        {
            return carrierPosition;
        }

        var feetHeight = anchorKind == MovementPresentationAnchorKind.Carrier
            ? CarriedItemFeetHeight
            : ItemFeetHeight;
        return ResolveBillboardPosition(new Vector3(tilePosition.X, tilePosition.Y, tilePosition.Z), feetHeight);
    }

    private bool TryResolveCarrierAnchorPosition(int carrierEntityId, out Vector3 position)
    {
        if (_dwarfBillboards.TryGetValue(carrierEntityId, out var dwarfBillboard))
        {
            position = dwarfBillboard.Root.Position + (dwarfBillboard.HasCarryAnchor
                ? dwarfBillboard.CarryAnchorOffset
                : new Vector3(0f, CarriedItemFeetHeight - EntityFeetHeight, 0f));
            return true;
        }

        if (_creatureBillboards.TryGetValue(carrierEntityId, out var creatureBillboard))
        {
            position = creatureBillboard.Root.Position + new Vector3(0f, CarriedItemFeetHeight - EntityFeetHeight, 0f);
            return true;
        }

        position = default;
        return false;
    }

    private Vector3 ResolveInterpolatedBillboardPosition(
        int entityId,
        Vec3i logicalPosition,
        float localFeetHeight,
        MovementPresentationSegment? segment,
        double nowSeconds)
        => ResolveBillboardPosition(ResolveInterpolatedMovementView(entityId, logicalPosition, segment, nowSeconds).TilePosition, localFeetHeight);

    private InterpolatedMovementView ResolveInterpolatedMovementView(
        int entityId,
        Vec3i logicalPosition,
        MovementPresentationSegment? segment,
        double nowSeconds)
    {
        var targetTilePosition = new Vector3(logicalPosition.X, logicalPosition.Y, logicalPosition.Z);
        if (!_movementInterpolations.TryGetValue(entityId, out var state))
        {
            if (segment is MovementPresentationSegment initialSegment &&
                initialSegment.NewPos == logicalPosition)
            {
                var initialTilePosition = new Vector3(initialSegment.OldPos.X, initialSegment.OldPos.Y, initialSegment.OldPos.Z);
                state = new MovementInterpolationState(initialTilePosition, nowSeconds)
                {
                    LastLogicalTilePosition = targetTilePosition,
                    StartTilePosition = initialTilePosition,
                    TargetTilePosition = targetTilePosition,
                    SegmentStartTimeSeconds = nowSeconds,
                    SegmentDurationSeconds = initialSegment.DurationSeconds,
                    LastAppliedSequence = initialSegment.Sequence,
                };
                _movementInterpolations[entityId] = state;
                return new InterpolatedMovementView(initialTilePosition, 0f, initialSegment.DurationSeconds > 0f ? initialSegment : null);
            }

            _movementInterpolations[entityId] = new MovementInterpolationState(targetTilePosition, nowSeconds);
            return new InterpolatedMovementView(targetTilePosition, 1f, null);
        }

        if (segment is MovementPresentationSegment movementSegment
            && movementSegment.Sequence != state.LastAppliedSequence
            && movementSegment.NewPos == logicalPosition)
        {
            var currentTilePosition = EvaluateInterpolatedTilePosition(state, nowSeconds);
            state.StartTilePosition = currentTilePosition;
            state.TargetTilePosition = targetTilePosition;
            state.LastLogicalTilePosition = targetTilePosition;
            state.SegmentStartTimeSeconds = nowSeconds;
            state.SegmentDurationSeconds = movementSegment.DurationSeconds;
            state.LastAppliedSequence = movementSegment.Sequence;
        }
        else if (state.LastLogicalTilePosition != targetTilePosition)
        {
            state.LastLogicalTilePosition = targetTilePosition;
            state.StartTilePosition = targetTilePosition;
            state.TargetTilePosition = targetTilePosition;
            state.SegmentStartTimeSeconds = nowSeconds;
            state.SegmentDurationSeconds = 0f;
        }

        var progress = EvaluateInterpolationProgress(state, nowSeconds);
        MovementPresentationSegment? activeSegment = null;
        if (segment is MovementPresentationSegment activeMovementSegment &&
            activeMovementSegment.Sequence == state.LastAppliedSequence &&
            state.SegmentDurationSeconds > 0f &&
            progress < 1f)
        {
            activeSegment = activeMovementSegment;
        }

        return new InterpolatedMovementView(EvaluateInterpolatedTilePosition(state, nowSeconds), progress, activeSegment);
    }

    private static Vector3 EvaluateInterpolatedTilePosition(MovementInterpolationState state, double nowSeconds)
    {
        if (state.SegmentDurationSeconds <= 0f)
            return state.TargetTilePosition;

        var elapsedSeconds = nowSeconds - state.SegmentStartTimeSeconds;
        if (elapsedSeconds <= 0d)
            return state.StartTilePosition;

        var progress = Mathf.Clamp((float)(elapsedSeconds / state.SegmentDurationSeconds), 0f, 1f);
        return state.StartTilePosition.Lerp(state.TargetTilePosition, progress);
    }

    private static float EvaluateInterpolationProgress(MovementInterpolationState state, double nowSeconds)
    {
        if (state.SegmentDurationSeconds <= 0f)
            return 1f;

        var elapsedSeconds = nowSeconds - state.SegmentStartTimeSeconds;
        if (elapsedSeconds <= 0d)
            return 0f;

        return Mathf.Clamp((float)(elapsedSeconds / state.SegmentDurationSeconds), 0f, 1f);
    }

    private void RemoveStaleInterpolations()
    {
        if (_movementInterpolations.Count == 0)
            return;

        var staleIds = _movementInterpolations.Keys.Where(id => !_visibleInterpolatedIds.Contains(id)).ToArray();
        foreach (var staleId in staleIds)
            _movementInterpolations.Remove(staleId);
    }

    private void SyncWaterEffects(WorldMap? map, DataManager? data, EntityRegistry registry, RenderCache renderCache, int currentZ)
    {
        if (map is null)
        {
            ClearOverlayMesh(ref _waterEffectMesh);
            HideBillboardWaterTints(_dwarfBillboards);
            HideBillboardWaterTints(_creatureBillboards);
            return;
        }

        var quads = new List<WorldWaterEffectMesher.WaterQuad>();

        foreach (var dwarfId in _visibleDwarfIds)
        {
            if (!registry.TryGetById<Dwarf>(dwarfId, out var dwarf) || dwarf is null)
                continue;

            if (!_dwarfBillboards.TryGetValue(dwarf.Id, out var state))
                continue;

            SyncEntityWaterEffect(
                quads,
                map,
                renderCache.DwarfPositions,
                renderCache.DwarfPreviousPositions,
                dwarf.Id,
                dwarf.Position.Position,
                8f,
                WorldWaterEffectProfiles.DwarfStyle,
                state);
        }

        foreach (var creatureId in _visibleCreatureIds)
        {
            if (!registry.TryGetById<Creature>(creatureId, out var creature) || creature is null)
                continue;

            if (!_creatureBillboards.TryGetValue(creature.Id, out var state))
                continue;

            SyncEntityWaterEffect(
                quads,
                map,
                renderCache.CreaturePositions,
                renderCache.CreaturePreviousPositions,
                creature.Id,
                creature.Position.Position,
                10f,
                WorldWaterEffectProfiles.ResolveCreatureStyle(creature, data, renderCache.CreatureWaterEffectStyles),
                state);
        }

        HideMissingBillboardWaterTints(_dwarfBillboards, _visibleDwarfIdSet);
        HideMissingBillboardWaterTints(_creatureBillboards, _visibleCreatureIdSet);
        SyncWaterEffectMesh(WorldWaterEffectMesher.Build(quads));
    }

    private void SyncEntityWaterEffect(
        List<WorldWaterEffectMesher.WaterQuad> quads,
        WorldMap map,
        Dictionary<int, Vector2> currentPositions,
        Dictionary<int, Vector2> previousPositions,
        int entityId,
        Vec3i position,
        float yOffsetPixels,
        RenderCache.WaterEffectStyle style,
        BillboardState state)
    {
        if (!TryGetWaterTile(map, position, out var tile))
        {
            HideBillboardWaterTint(state);
            return;
        }

        var currentScreenPos = ResolveCurrentScreenPosition(currentPositions, entityId, position, yOffsetPixels);
        var motion = RenderCache.ResolveEntityMotionVector(previousPositions, entityId, currentScreenPos);
        AddWaterEffectQuads(
            quads,
            new Vector3(state.Root.Position.X, ResolveWaterSurfaceY(position.Z, tile), state.Root.Position.Z),
            tile,
            entityId,
            motion,
            style);
        ApplyBillboardWaterTint(state, tile, style);
    }

    private void AddWaterEffectQuads(List<WorldWaterEffectMesher.WaterQuad> quads, Vector3 center, WorldTileData tile, int phaseSeed, Vector2 motion, RenderCache.WaterEffectStyle style)
    {
        var depthNorm = tile.FluidLevel / 7f;
        var now = (float)Time.GetTicksMsec() * 0.001f;
        var phase = (phaseSeed % 97) * 0.19f;
        var surfaceCenter = center + new Vector3(0f, WaterSurfaceOffset, 0f);

        var rippleRadius = (7f + (depthNorm * 10f) + (Mathf.Sin((now * 3.2f) + phase) * 1.35f)) * style.RippleScale / RenderCache.TileSize;
        var rippleAlpha = (0.20f + (depthNorm * 0.18f)) * Mathf.Clamp(0.74f + (style.RippleScale * 0.30f), 0.55f, 1.35f);
        AddSurfaceQuad(quads, surfaceCenter, rippleRadius, rippleRadius, new Color(0.68f, 0.89f, 1f, rippleAlpha));

        if (!style.SuppressBubbles)
        {
            var bubbleLift = Mathf.Abs(Mathf.Sin((now * 5.3f) + phase));
            var bubbleAlpha = (0.40f + (depthNorm * 0.35f)) * Mathf.Clamp(style.BubbleScale, 0.35f, 1.4f);
            var bubbleSize = 0.020f * style.BubbleScale;
            var bubbleY = surfaceCenter.Y + 0.02f + (bubbleLift * (0.04f + (depthNorm * 0.08f)));
            AddSurfaceQuad(quads, surfaceCenter + new Vector3(-0.10f, bubbleY - surfaceCenter.Y, -0.03f), bubbleSize, bubbleSize, new Color(0.88f, 0.97f, 1f, bubbleAlpha));
            if (tile.FluidLevel >= 3)
                AddSurfaceQuad(quads, surfaceCenter + new Vector3(0.08f, bubbleY - surfaceCenter.Y + 0.03f, 0.04f), bubbleSize * 1.12f, bubbleSize * 1.12f, new Color(0.88f, 0.97f, 1f, bubbleAlpha));
            if (tile.FluidLevel >= 5)
                AddSurfaceQuad(quads, surfaceCenter + new Vector3(0.01f, bubbleY - surfaceCenter.Y + 0.06f, -0.05f), bubbleSize * 0.92f, bubbleSize * 0.92f, new Color(0.88f, 0.97f, 1f, bubbleAlpha));
        }

        var speed = motion.Length();
        if (speed < style.MotionThreshold)
            return;

        var direction2 = motion.Normalized();
        var forward = new Vector3(direction2.X, 0f, direction2.Y);
        var right = new Vector3(-direction2.Y, 0f, direction2.X);
        var speedNorm = Mathf.Clamp(speed / 6f, 0f, 1f);
        var wakeAlpha = (0.10f + (speedNorm * 0.22f) + (depthNorm * 0.10f)) * Mathf.Clamp(style.WakeScale, 0.55f, 1.45f);
        var wakeColor = new Color(0.72f, 0.92f, 1f, wakeAlpha);
        var wakeBase = surfaceCenter - (forward * ((5f + (depthNorm * 8f * style.WakeScale)) / RenderCache.TileSize));
        var wakeLength = (9f + (speedNorm * 12f) + (depthNorm * 8f)) * style.WakeScale / RenderCache.TileSize;
        var wakeWidth = (1.4f + (speedNorm * 1.4f)) * Mathf.Clamp(0.82f + (style.WakeScale * 0.18f), 0.72f, 1.35f) / RenderCache.TileSize;
        var spread = (4f + (depthNorm * 3.5f)) * Mathf.Clamp(style.WakeScale, 0.7f, 1.45f) / RenderCache.TileSize;

        if (style.WakePattern == RenderCache.WaterWakePattern.SwimV)
        {
            var armLength = wakeLength * 0.94f;
            var armSpread = spread * 0.86f;
            var apex = wakeBase - (forward * ((2f + (depthNorm * 3f)) / RenderCache.TileSize));
            var leftArm = apex + (right * armSpread) - (forward * armLength);
            var rightArm = apex - (right * armSpread) - (forward * armLength);
            AddLineQuad(quads, apex, leftArm, Mathf.Max(0.012f, wakeWidth - (0.05f / RenderCache.TileSize)), wakeColor with { A = wakeColor.A * 0.90f });
            AddLineQuad(quads, apex, rightArm, Mathf.Max(0.012f, wakeWidth - (0.05f / RenderCache.TileSize)), wakeColor with { A = wakeColor.A * 0.90f });
            AddLineQuad(quads, apex, apex - (forward * (armLength * 0.35f)), Mathf.Max(0.010f, wakeWidth - (0.35f / RenderCache.TileSize)), wakeColor with { A = wakeColor.A * 0.45f });
        }
        else
        {
            AddLineQuad(quads, wakeBase, wakeBase - (forward * wakeLength), wakeWidth, wakeColor);
            AddLineQuad(quads, wakeBase + (right * spread), wakeBase + (right * spread * 0.85f) - (forward * (wakeLength * 0.88f)), Mathf.Max(0.012f, wakeWidth - (0.20f / RenderCache.TileSize)), wakeColor with { A = wakeColor.A * 0.82f });
            AddLineQuad(quads, wakeBase - (right * spread), wakeBase - (right * spread * 0.85f) - (forward * (wakeLength * 0.88f)), Mathf.Max(0.012f, wakeWidth - (0.20f / RenderCache.TileSize)), wakeColor with { A = wakeColor.A * 0.82f });
        }
    }

    private void ApplyBillboardWaterTint(BillboardState state, WorldTileData tile, RenderCache.WaterEffectStyle style)
    {
        var depthNorm = tile.FluidLevel / 7f;
        var submerge = Mathf.Clamp((tile.FluidLevel - 1f) / 6f, 0f, 1f);
        var submergedHeight = state.Size.Y * Mathf.Lerp(0.12f, 0.46f, submerge) * style.SubmergeScale;
        if (submergedHeight <= 0.01f)
        {
            HideBillboardWaterTint(state);
            return;
        }

        var tintMesh = state.WaterTint.Mesh as QuadMesh ?? new QuadMesh();
        tintMesh.Size = new Vector2(state.Size.X, submergedHeight);
        state.WaterTint.Mesh = tintMesh;
        state.WaterTintMaterial.AlbedoColor = new Color(0.20f, 0.52f, 0.86f, 0.10f + (depthNorm * 0.16f));
        state.WaterTint.Position = new Vector3(0f, submergedHeight * 0.5f, 0.010f);
        state.WaterTint.Visible = _isActive;

        var lineHeight = Mathf.Max(0.018f, submergedHeight * 0.08f);
        var lineMesh = state.WaterLine.Mesh as QuadMesh ?? new QuadMesh();
        lineMesh.Size = new Vector2(Mathf.Max(0.14f, state.Size.X * 0.96f), lineHeight);
        state.WaterLine.Mesh = lineMesh;
        state.WaterLineMaterial.AlbedoColor = new Color(0.82f, 0.95f, 1f, 0.22f + (depthNorm * 0.12f));
        state.WaterLine.Position = new Vector3(0f, submergedHeight - (lineHeight * 0.5f), 0.012f);
        state.WaterLine.Visible = _isActive;
    }

    private void SyncEmoteLabels(EntityRegistry registry, RenderCache renderCache, int currentZ)
    {
        _visibleEmoteBubbleIds.Clear();

        foreach (var dwarfId in _visibleDwarfIds)
        {
            if (!registry.TryGetById<Dwarf>(dwarfId, out var dwarf) || dwarf is null || !dwarf.Emotes.HasEmote)
                continue;

            _visibleEmoteBubbleIds.Add(dwarf.Id);
            SyncEmoteBubble(
                dwarf.Id,
                dwarf.Emotes.CurrentEmote!,
                ResolveSmoothedFeedbackAnchor(renderCache, dwarf.Id, dwarf.Position.Position) + new Vector3(0f, EmoteVisuals.ResolveWorldLift(dwarf.Emotes.CurrentEmote!, isCreature: false), 0f));
        }

        foreach (var creatureId in _visibleCreatureIds)
        {
            if (!registry.TryGetById<Creature>(creatureId, out var creature) || creature is null || !creature.Emotes.HasEmote)
                continue;

            _visibleEmoteBubbleIds.Add(creature.Id);
            SyncEmoteBubble(
                creature.Id,
                creature.Emotes.CurrentEmote!,
                ResolveSmoothedFeedbackAnchor(renderCache, creature.Id, creature.Position.Position) + new Vector3(0f, EmoteVisuals.ResolveWorldLift(creature.Emotes.CurrentEmote!, isCreature: true), 0f));
        }

        RemoveStaleEmoteBubbles(_emoteBubbles, _visibleEmoteBubbleIds);
    }

    private void SyncEmoteBubble(int entityId, Emote emote, Vector3 position)
    {
        var bubbleState = _emoteBubbles.TryGetValue(entityId, out var existing)
            ? existing
            : CreateEmoteBubbleState(entityId);

        _emoteBubbles[entityId] = bubbleState;

        var alpha = EmoteVisuals.ResolveAlpha(emote);
        bubbleState.Root.Position = position;
        bubbleState.Root.Scale = Vector3.One * EmoteVisuals.ResolveScale(emote);
        bubbleState.Root.Visible = _isActive;

        var iconColor = EmoteVisuals.ResolveIconColor(emote);
        ApplyEmoteQuadVisual(
            bubbleState.Icon,
            PixelArtFactory.GetEmoteIcon(emote),
            emote.VisualStyle == EmoteVisualStyle.Balloon ? EmoteBubbleIconSize : EmoteSymbolIconSize,
            emote.VisualStyle == EmoteVisualStyle.Balloon ? new Vector3(0f, 0.03f, 0f) : Vector3.Zero,
            new Color(iconColor.R, iconColor.G, iconColor.B, iconColor.A * alpha),
            visible: true);

        if (emote.VisualStyle == EmoteVisualStyle.Balloon)
        {
            var bubbleColor = EmoteVisuals.ResolveBubbleColor(emote);
            var bubbleTint = new Color(bubbleColor.R, bubbleColor.G, bubbleColor.B, bubbleColor.A * alpha);
            ApplyEmoteQuadVisual(
                bubbleState.Bubble,
                PixelArtFactory.GetEmoteBubble(),
                EmoteBubbleSize,
                Vector3.Zero,
                bubbleTint,
                visible: true);
            ApplyEmoteQuadVisual(
                bubbleState.Tail,
                PixelArtFactory.GetEmoteBubbleTail(),
                EmoteTailSize,
                new Vector3(0f, -0.30f, 0f),
                bubbleTint,
                visible: true);
        }
        else
        {
            bubbleState.Bubble.Mesh.Visible = false;
            bubbleState.Tail.Mesh.Visible = false;
        }
    }

    private void SyncWorldFxLabels(RenderCache renderCache, GameFeedbackController? feedback, int currentZ, Rect2I visibleTileBounds)
    {
        if (feedback is null)
        {
            ClearLabels(_worldFxLabels);
            return;
        }

        var visibleIds = new HashSet<int>();
        foreach (var worldFx in feedback.GetWorldFxViews(currentZ))
        {
            if (!TileBoundsContains(visibleTileBounds, worldFx.Position.X, worldFx.Position.Y))
                continue;

            visibleIds.Add(worldFx.Id);
            var progress = 1f - (worldFx.TimeLeft / worldFx.Duration);
            var position = ResolveSmoothedFeedbackAnchor(renderCache, worldFx.FollowEntityId, worldFx.Position)
                + new Vector3(0f, 1.05f + (progress * 0.34f), 0f);
            SyncWorldFxLabel(worldFx, position);
        }

        RemoveStaleLabels(_worldFxLabels, visibleIds);
    }

    private void SyncWorldFxLabel(GameFeedbackController.WorldFxView worldFx, Vector3 position)
    {
        var label = _worldFxLabels.TryGetValue(worldFx.Id, out var existing)
            ? existing
            : CreateLabel($"WorldFx_{worldFx.Id}", WorldFxLabelFontSize, WorldFxLabelPixelSize);

        _worldFxLabels[worldFx.Id] = label;
        label.Text = worldFx.Text;
        label.Position = position;
        label.Modulate = new Color(worldFx.Color.R, worldFx.Color.G, worldFx.Color.B, Mathf.Clamp(worldFx.TimeLeft / worldFx.Duration, 0f, 1f));
        label.Visible = _isActive;
    }

    private void SyncBillboard<TKey>(Dictionary<TKey, BillboardState> states, TKey entityId, string name, Texture2D texture, Vector3 position, Vector2 size, bool useCarriedMaterial = false)
        where TKey : notnull
    {
        if (!states.TryGetValue(entityId, out var state))
        {
            state = CreateBillboardState(name, texture, size, useCarriedMaterial);
            states[entityId] = state;
        }
        else if (state.Texture != texture || state.Size != size)
        {
            ApplyBillboardVisual(state, texture, size, useCarriedMaterial);
        }

        state.Root.Position = position;
        state.Root.Visible = _isActive;
    }

    private void SyncBillboard<TKey>(Dictionary<TKey, BillboardState> states, TKey entityId, Vec3i tilePosition, string name, Texture2D texture, Vector3 position, Vector2 size, bool useCarriedMaterial = false)
        where TKey : notnull
    {
        SyncBillboard(states, entityId, name, texture, position, size, useCarriedMaterial);
        states[entityId].TilePosition = tilePosition;
    }

    private BillboardState CreateBillboardState(string name, Texture2D texture, Vector2 size, bool useCarriedMaterial)
    {
        var root = new Node3D { Name = name };
        var mesh = new MeshInstance3D
        {
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var previewMeshes = Enumerable.Range(0, 3)
            .Select(_ => new MeshInstance3D
            {
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            })
            .ToArray();
        var waterTint = new MeshInstance3D
        {
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        var waterLine = new MeshInstance3D
        {
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };

        var waterTintMaterial = CreateFlatColorMaterial();
        var waterLineMaterial = CreateFlatColorMaterial();
        waterTint.MaterialOverride = waterTintMaterial;
        waterLine.MaterialOverride = waterLineMaterial;

        root.AddChild(mesh);
        foreach (var previewMesh in previewMeshes)
            root.AddChild(previewMesh);
        root.AddChild(waterTint);
        root.AddChild(waterLine);
        AddChild(root);

        var state = new BillboardState(root, mesh, previewMeshes, waterTint, waterTintMaterial, waterLine, waterLineMaterial, texture, size);
        ApplyBillboardVisual(state, texture, size, useCarriedMaterial);
        return state;
    }

    private void ApplyBillboardVisual(BillboardState state, Texture2D texture, Vector2 size, bool useCarriedMaterial)
    {
        state.Texture = texture;
        state.Size = size;
        var quadMesh = state.Mesh.Mesh as QuadMesh ?? new QuadMesh();
        quadMesh.Size = size;
        state.Mesh.Mesh = quadMesh;
        state.Mesh.MaterialOverride = GetBillboardMaterial(texture, useCarriedMaterial);
        state.Mesh.Position = new Vector3(0f, size.Y * 0.5f, 0f);
    }

    private StandardMaterial3D GetBillboardMaterial(Texture2D texture, bool useCarriedMaterial = false)
    {
        var materials = useCarriedMaterial ? _carriedBillboardMaterials : _billboardMaterials;
        if (materials.TryGetValue(texture, out var material))
            return material;

        material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoTexture = texture,
            RenderPriority = useCarriedMaterial ? CarriedBillboardRenderPriority : BillboardRenderPriority,
        };

        materials[texture] = material;
        return material;
    }

    private void EnsureWaterEffectMaterial()
    {
        if (_waterEffectMaterial is not null)
            return;

        _waterEffectMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = BillboardRenderPriority,
        };
    }

    private void SyncWaterEffectMesh(ArrayMesh? mesh)
    {
        if (mesh is null)
        {
            ClearOverlayMesh(ref _waterEffectMesh);
            return;
        }

        EnsureWaterEffectMaterial();
        var instance = _waterEffectMesh ?? CreateWaterEffectMeshInstance();
        ReplaceOwnedMesh(instance, mesh);
        instance.MaterialOverride = _waterEffectMaterial;
        instance.Visible = _isActive;
        _waterEffectMesh = instance;
    }

    private MeshInstance3D CreateWaterEffectMeshInstance()
    {
        var meshInstance = new MeshInstance3D
        {
            Name = "WaterEffects",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        AddChild(meshInstance);
        return meshInstance;
    }

    private static void AddSurfaceQuad(List<WorldWaterEffectMesher.WaterQuad> quads, Vector3 center, float halfWidth, float halfDepth, Color color)
    {
        quads.Add(new WorldWaterEffectMesher.WaterQuad(
            new Vector3(center.X - halfWidth, center.Y, center.Z - halfDepth),
            new Vector3(center.X + halfWidth, center.Y, center.Z - halfDepth),
            new Vector3(center.X + halfWidth, center.Y, center.Z + halfDepth),
            new Vector3(center.X - halfWidth, center.Y, center.Z + halfDepth),
            color));
    }

    private static void AddLineQuad(List<WorldWaterEffectMesher.WaterQuad> quads, Vector3 start, Vector3 end, float width, Color color)
    {
        var direction = end - start;
        if (direction.LengthSquared() <= Mathf.Epsilon)
            return;

        var tangent = new Vector3(-direction.Z, 0f, direction.X).Normalized() * (width * 0.5f);
        quads.Add(new WorldWaterEffectMesher.WaterQuad(
            start + tangent,
            end + tangent,
            end - tangent,
            start - tangent,
            color));
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

    private static void ClearLabels(Dictionary<int, Label3D> labels)
    {
        foreach (var label in labels.Values)
            label.QueueFree();

        labels.Clear();
    }

    private static void ClearEmoteBubbles(Dictionary<int, EmoteBubbleState> bubbles)
    {
        foreach (var bubble in bubbles.Values)
            ReleaseEmoteBubbleState(bubble);

        bubbles.Clear();
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

    private static void ReleaseBillboardState(BillboardState state)
    {
        ReplaceOwnedMesh(state.Mesh, null);
        state.Mesh.MaterialOverride = null;
        foreach (var previewMesh in state.PreviewMeshes)
        {
            ReplaceOwnedMesh(previewMesh, null);
            previewMesh.MaterialOverride = null;
        }
        ReplaceOwnedMesh(state.WaterTint, null);
        state.WaterTint.MaterialOverride = null;
        ReplaceOwnedMesh(state.WaterLine, null);
        state.WaterLine.MaterialOverride = null;
        DisposeResource(state.WaterTintMaterial);
        DisposeResource(state.WaterLineMaterial);
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
    }

    private static void DisposeResource(IDisposable? resource)
        => resource?.Dispose();

    private static void ReleaseEmoteBubbleState(EmoteBubbleState state)
    {
        ReplaceOwnedMesh(state.Bubble.Mesh, null);
        state.Bubble.Mesh.MaterialOverride = null;
        DisposeResource(state.Bubble.Material);
        ReplaceOwnedMesh(state.Tail.Mesh, null);
        state.Tail.Mesh.MaterialOverride = null;
        DisposeResource(state.Tail.Material);
        ReplaceOwnedMesh(state.Icon.Mesh, null);
        state.Icon.Mesh.MaterialOverride = null;
        DisposeResource(state.Icon.Material);
        state.Root.QueueFree();
    }

    private static void RemoveStaleLabels(Dictionary<int, Label3D> labels, HashSet<int> visibleIds)
    {
        if (labels.Count == 0)
            return;

        var staleIds = labels.Keys.Where(id => !visibleIds.Contains(id)).ToArray();
        foreach (var staleId in staleIds)
        {
            labels[staleId].QueueFree();
            labels.Remove(staleId);
        }
    }

    private static void RemoveStaleEmoteBubbles(Dictionary<int, EmoteBubbleState> bubbles, HashSet<int> visibleIds)
    {
        if (bubbles.Count == 0)
            return;

        var staleIds = bubbles.Keys.Where(id => !visibleIds.Contains(id)).ToArray();
        foreach (var staleId in staleIds)
        {
            ReleaseEmoteBubbleState(bubbles[staleId]);
            bubbles.Remove(staleId);
        }
    }

    private static void HideBillboardWaterTints(Dictionary<int, BillboardState> states)
    {
        foreach (var state in states.Values)
            HideBillboardWaterTint(state);
    }

    private static void HideMissingBillboardWaterTints(Dictionary<int, BillboardState> states, HashSet<int> visibleIds)
    {
        foreach (var state in states)
        {
            if (!visibleIds.Contains(state.Key))
                HideBillboardWaterTint(state.Value);
        }
    }

    private static void HideBillboardWaterTint(BillboardState state)
    {
        state.WaterTint.Visible = false;
        state.WaterLine.Visible = false;
    }

    private void ClearVisibleEntityIds()
    {
        _visibleDwarfIds.Clear();
        _visibleCreatureIds.Clear();
        _visibleItemIds.Clear();
        _visibleContainerIds.Clear();
        _visibleDwarfIdSet.Clear();
        _visibleCreatureIdSet.Clear();
        _visibleItemIdSet.Clear();
        _visibleCarriedItemIdSet.Clear();
        _visibleEmoteBubbleIds.Clear();
    }

    private static bool TileBoundsContains(Rect2I visibleTileBounds, int x, int y)
    {
        if (visibleTileBounds.Size.X <= 0 || visibleTileBounds.Size.Y <= 0)
            return false;

        return x >= visibleTileBounds.Position.X
            && x < visibleTileBounds.Position.X + visibleTileBounds.Size.X
            && y >= visibleTileBounds.Position.Y
            && y < visibleTileBounds.Position.Y + visibleTileBounds.Size.Y;
    }

    private static bool TryResolveVisibleTileRange(Rect2I visibleTileBounds, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = visibleTileBounds.Position.X;
        minY = visibleTileBounds.Position.Y;
        maxX = minX + visibleTileBounds.Size.X - 1;
        maxY = minY + visibleTileBounds.Size.Y - 1;
        return visibleTileBounds.Size.X > 0 && visibleTileBounds.Size.Y > 0;
    }

    private static float ResolveWaterSurfaceY(int currentZ, WorldTileData tile)
        => (currentZ * WorldRender3D.VerticalSliceSpacing) + 0.05f + ((tile.FluidLevel / 7f) * 0.16f);

    private static Vector2 ResolveCurrentScreenPosition(Dictionary<int, Vector2> positions, int entityId, Vec3i position, float yOffsetPixels)
        => positions.TryGetValue(entityId, out var current)
            ? current
            : RenderCache.WorldToScreenCenter(position) + new Vector2(0f, yOffsetPixels);

    private static bool TryGetWaterTile(WorldMap map, Vec3i position, out WorldTileData tile)
    {
        if (!map.IsInBounds(position))
        {
            tile = WorldTileData.Empty;
            return false;
        }

        tile = map.GetTile(position);
        return tile.FluidType == FluidType.Water && tile.FluidLevel > 0;
    }

    private Vector3 ResolveSmoothedFeedbackAnchor(RenderCache renderCache, int entityId, Vec3i position)
    {
        if (entityId >= 0)
        {
            if (_dwarfBillboards.TryGetValue(entityId, out var dwarfBillboard))
                return dwarfBillboard.Root.Position;

            if (_creatureBillboards.TryGetValue(entityId, out var creatureBillboard))
                return creatureBillboard.Root.Position;

            if (_itemBillboards.TryGetValue(entityId, out var itemBillboard))
                return itemBillboard.Root.Position;

            if (_carriedItemBillboards.TryGetValue(entityId, out var carriedItemBillboard))
                return carriedItemBillboard.Root.Position;
        }

        if (entityId >= 0 && renderCache.DwarfPositions.ContainsKey(entityId))
            return ResolveBillboardPosition(position, EntityFeetHeight);

        if (entityId >= 0 && renderCache.CreaturePositions.ContainsKey(entityId))
            return ResolveBillboardPosition(position, EntityFeetHeight);

        if (entityId >= 0 && renderCache.ItemPositions.ContainsKey(entityId))
            return ResolveBillboardPosition(position, ItemFeetHeight);

        return ResolveBillboardPosition(position, EntityFeetHeight);
    }

    private Label3D CreateLabel(string name, int fontSize, float pixelSize, Node3D? parent = null)
    {
        var label = new Label3D
        {
            Name = name,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            FixedSize = true,
            FontSize = fontSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            NoDepthTest = true,
            OutlineModulate = new Color(0f, 0f, 0f, 0.9f),
            OutlineSize = 10,
            PixelSize = pixelSize,
            RenderPriority = 10,
            Shaded = false,
            VerticalAlignment = VerticalAlignment.Center,
        };

        (parent ?? this).AddChild(label);
        return label;
    }

    private void ApplyEmoteQuadVisual(EmoteQuadState state, Texture2D texture, Vector2 size, Vector3 position, Color tint, bool visible)
    {
        state.Texture = texture;
        state.Quad.Size = size;
        state.Mesh.Position = position;
        state.Material.AlbedoTexture = texture;
        state.Material.AlbedoColor = tint;
        state.Mesh.Visible = _isActive && visible;
    }

    private EmoteBubbleState CreateEmoteBubbleState(int entityId)
    {
        var root = new Node3D
        {
            Name = $"Emote_{entityId}",
            Visible = _isActive,
        };

        AddChild(root);

        var bubble = CreateEmoteQuad($"EmoteBubble_{entityId}", root, EmoteBubbleRenderPriority);
        var tail = CreateEmoteQuad($"EmoteTail_{entityId}", root, EmoteBubbleRenderPriority);
        var icon = CreateEmoteQuad($"EmoteIcon_{entityId}", root, EmoteIconRenderPriority);

        return new EmoteBubbleState(root, bubble, tail, icon);
    }

    private static EmoteQuadState CreateEmoteQuad(string name, Node3D parent, int renderPriority)
    {
        var mesh = new MeshInstance3D
        {
            Name = name,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        var quad = new QuadMesh();
        mesh.Mesh = quad;
        var material = CreateEmoteMaterial(renderPriority);
        mesh.MaterialOverride = material;
        parent.AddChild(mesh);
        return new EmoteQuadState(mesh, quad, material);
    }

    private static StandardMaterial3D CreateEmoteMaterial(int renderPriority)
    {
        return new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            RenderPriority = renderPriority,
        };
    }

    private static StandardMaterial3D CreateFlatColorMaterial()
    {
        return new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            RenderPriority = BillboardRenderPriority,
        };
    }

    private static Vector3 ResolveBillboardPosition(Vec3i position, float localFeetHeight)
        => new(position.X + 0.5f, (position.Z * WorldRender3D.VerticalSliceSpacing) + localFeetHeight, position.Y + 0.5f);

    private static Vector3 ResolveBillboardPosition(Vector3 tilePosition, float localFeetHeight)
        => new(tilePosition.X + 0.5f, (tilePosition.Z * WorldRender3D.VerticalSliceSpacing) + localFeetHeight, tilePosition.Y + 0.5f);

    private static float ResolvePulseLiftWorldUnits(float liftPixels)
        => -liftPixels / RenderCache.TileSize;

    private void TryPickBillboards(Camera3D camera, Viewport viewport, Vector2 screenPosition, IEnumerable<BillboardState> states, ref BillboardPickCandidate? best)
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
                best = new BillboardPickCandidate(state, state.TilePosition, depthSquared, screenDistanceSquared);
            }
        }
    }

    private bool TryGetDebugBillboardProbe(Camera3D camera, Viewport viewport, IEnumerable<BillboardState> states, out Vector2 screenPosition, out Vector2I tile)
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

    private sealed class BillboardState
    {
        public BillboardState(
            Node3D root,
            MeshInstance3D mesh,
            MeshInstance3D[] previewMeshes,
            MeshInstance3D waterTint,
            StandardMaterial3D waterTintMaterial,
            MeshInstance3D waterLine,
            StandardMaterial3D waterLineMaterial,
            Texture2D texture,
            Vector2 size)
        {
            Root = root;
            Mesh = mesh;
            PreviewMeshes = previewMeshes;
            WaterTint = waterTint;
            WaterTintMaterial = waterTintMaterial;
            WaterLine = waterLine;
            WaterLineMaterial = waterLineMaterial;
            Texture = texture;
            Size = size;
        }

        public Node3D Root { get; }

        public MeshInstance3D Mesh { get; }

        public MeshInstance3D[] PreviewMeshes { get; }

        public MeshInstance3D WaterTint { get; }

        public StandardMaterial3D WaterTintMaterial { get; }

        public MeshInstance3D WaterLine { get; }

        public StandardMaterial3D WaterLineMaterial { get; }

        public Texture2D Texture { get; set; }

        public Vector2 Size { get; set; }

        public Vec3i TilePosition { get; set; }

        public bool HasCarryAnchor { get; set; }

        public Vector3 CarryAnchorOffset { get; set; }
    }

    private sealed class EmoteBubbleState
    {
        public EmoteBubbleState(Node3D root, EmoteQuadState bubble, EmoteQuadState tail, EmoteQuadState icon)
        {
            Root = root;
            Bubble = bubble;
            Tail = tail;
            Icon = icon;
        }

        public Node3D Root { get; }

        public EmoteQuadState Bubble { get; }

        public EmoteQuadState Tail { get; }

        public EmoteQuadState Icon { get; }
    }

    private sealed class EmoteQuadState
    {
        public EmoteQuadState(MeshInstance3D mesh, QuadMesh quad, StandardMaterial3D material)
        {
            Mesh = mesh;
            Quad = quad;
            Material = material;
        }

        public MeshInstance3D Mesh { get; }

        public QuadMesh Quad { get; }

        public StandardMaterial3D Material { get; }

        public Texture2D? Texture { get; set; }
    }

    private readonly record struct BillboardPickCandidate(
        BillboardState State,
        Vec3i TilePosition,
        float DepthSquared,
        float ScreenDistanceSquared);

    private sealed class MovementInterpolationState
    {
        public MovementInterpolationState(Vector3 tilePosition, double nowSeconds)
        {
            LastLogicalTilePosition = tilePosition;
            StartTilePosition = tilePosition;
            TargetTilePosition = tilePosition;
            SegmentStartTimeSeconds = nowSeconds;
            SegmentDurationSeconds = 0f;
        }

        public Vector3 LastLogicalTilePosition { get; set; }

        public Vector3 StartTilePosition { get; set; }

        public Vector3 TargetTilePosition { get; set; }

        public double SegmentStartTimeSeconds { get; set; }

        public float SegmentDurationSeconds { get; set; }

        public long LastAppliedSequence { get; set; }
    }

    private readonly record struct InterpolatedMovementView(
        Vector3 TilePosition,
        float Progress,
        MovementPresentationSegment? ActiveSegment);
}

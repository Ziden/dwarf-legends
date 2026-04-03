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

public sealed partial class WorldActorPresentation3D : Node3D
{
    private const float EntityFeetHeight = 0.18f;
    private const float ItemFeetHeight = 0.12f;
    private const float WaterSurfaceOffset = 0.012f;
    private const float HoverOutlineScale = 1.14f;
    private const float HoverOutlineDepthOffset = -0.0025f;
    private const int MaxVisibleCreatureBillboards = 96;
    private const int MaxVisibleItemLikeBillboards = 160;
    private const int EmoteLabelFontSize = 40;
    private const float EmoteLabelPixelSize = 0.0032f;
    private const int WorldFxLabelFontSize = 24;
    private const float WorldFxLabelPixelSize = 0.0030f;
    private static readonly Color HoverOutlineTint = new(1f, 0.95f, 0.58f, 0.9f);

    private readonly Dictionary<int, BillboardState> _dwarfBillboards = new();
    private readonly Dictionary<int, BillboardState> _creatureBillboards = new();
    private readonly Dictionary<int, BillboardState> _itemBillboards = new();
    private readonly Dictionary<int, MovementInterpolationState> _movementInterpolations = new();
    private readonly Dictionary<int, Label3D> _emoteLabels = new();
    private readonly Dictionary<int, Label3D> _worldFxLabels = new();
    private readonly Dictionary<Texture2D, StandardMaterial3D> _billboardMaterials = new();
    private readonly Dictionary<Texture2D, StandardMaterial3D> _outlineBillboardMaterials = new();
    private readonly List<int> _visibleDwarfIds = new();
    private readonly List<int> _visibleCreatureIds = new();
    private readonly List<int> _visibleItemIds = new();
    private readonly List<ItemLikeSceneEntry> _visibleItemEntries = new();
    private readonly HashSet<int> _visibleDwarfIdSet = new();
    private readonly HashSet<int> _visibleCreatureIdSet = new();
    private readonly HashSet<int> _visibleItemIdSet = new();
    private readonly HashSet<int> _visibleInterpolatedIds = new();
    private readonly HashSet<int> _visibleEmoteLabelIds = new();

    private MeshInstance3D? _waterEffectMesh;
    private StandardMaterial3D? _waterEffectMaterial;
    private BillboardState? _hoveredBillboard;
    private bool _isActive;

    public override void _Ready()
    {
        EnsureWaterEffectMaterial();
        SetActive(false);
    }

    public (int Dwarves, int Creatures, int Items) GetDebugSpriteCounts()
        => (_dwarfBillboards.Count, _creatureBillboards.Count, _itemBillboards.Count);

    public bool HasDebugHoveredBillboardOutline()
        => _hoveredBillboard?.Outline.Visible == true;

    public bool TryResolveHoveredBillboardTile(Camera3D? camera, Viewport viewport, Vector2 screenPosition, out Vector2I tile)
    {
        tile = default;
        if (camera is null)
        {
            SetHoveredBillboard(null);
            return false;
        }

        BillboardPickCandidate? best = null;
        TryPickBillboards(camera, viewport, screenPosition, _dwarfBillboards.Values, ref best);
        TryPickBillboards(camera, viewport, screenPosition, _creatureBillboards.Values, ref best);
        TryPickBillboards(camera, viewport, screenPosition, _itemBillboards.Values, ref best);

        SetHoveredBillboard(best?.State);
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
            || TryGetDebugBillboardProbe(camera, viewport, _itemBillboards.Values, out screenPosition, out tile);
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

        return false;
    }

    public void Reset()
    {
        SetHoveredBillboard(null);
        ClearBillboards(_dwarfBillboards);
        ClearBillboards(_creatureBillboards);
        ClearBillboards(_itemBillboards);
        _movementInterpolations.Clear();
        ClearLabels(_emoteLabels);
        ClearLabels(_worldFxLabels);
        ClearOverlayMesh(ref _waterEffectMesh);
        DisposeMaterials(_billboardMaterials);
        DisposeMaterials(_outlineBillboardMaterials);
        _billboardMaterials.Clear();
        _outlineBillboardMaterials.Clear();
        ClearVisibleEntityIds();
    }

    public void SetActive(bool active)
    {
        _isActive = active;

        foreach (var state in _dwarfBillboards.Values)
        {
            state.Root.Visible = active;
            state.Outline.Visible = active && ReferenceEquals(state, _hoveredBillboard);
        }

        foreach (var state in _creatureBillboards.Values)
        {
            state.Root.Visible = active;
            state.Outline.Visible = active && ReferenceEquals(state, _hoveredBillboard);
        }

        foreach (var state in _itemBillboards.Values)
        {
            state.Root.Visible = active;
            state.Outline.Visible = active && ReferenceEquals(state, _hoveredBillboard);
        }

        foreach (var label in _emoteLabels.Values)
            label.Visible = active;

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
            ClearBillboards(_dwarfBillboards);
            ClearBillboards(_creatureBillboards);
            ClearBillboards(_itemBillboards);
            ClearLabels(_emoteLabels);
            ClearLabels(_worldFxLabels);
            ClearOverlayMesh(ref _waterEffectMesh);
            return;
        }

        var nowSeconds = presentationTimeSeconds;

        using (profiler?.Measure("billboards") ?? default)
            SyncBillboards(registry, items, spatial, movementPresentation, feedback, currentZ, minX, minY, maxX, maxY, nowSeconds);

        using (profiler?.Measure("water_effects") ?? default)
            SyncWaterEffects(map, data, registry, renderCache, currentZ);

        using (profiler?.Measure("emote_labels") ?? default)
            SyncEmoteLabels(registry, renderCache, currentZ);

        using (profiler?.Measure("world_fx_labels") ?? default)
            SyncWorldFxLabels(renderCache, feedback, currentZ, visibleTileBounds);
    }

    private void SyncBillboards(EntityRegistry registry, ItemSystem? items, SpatialIndexSystem spatial, MovementPresentationSystem? movementPresentation, GameFeedbackController? feedback, int currentZ, int minX, int minY, int maxX, int maxY, double nowSeconds)
    {
        _visibleInterpolatedIds.Clear();
        SyncDwarfBillboards(registry, spatial, movementPresentation, feedback, currentZ, minX, minY, maxX, maxY, nowSeconds);
        SyncCreatureBillboards(registry, spatial, movementPresentation, currentZ, minX, minY, maxX, maxY, nowSeconds);

        if (items is null)
        {
            _visibleItemIds.Clear();
            _visibleItemIdSet.Clear();
            ClearBillboards(_itemBillboards);
            RemoveStaleInterpolations();
            return;
        }

        SyncItemBillboards(registry, items, spatial, movementPresentation, currentZ, minX, minY, maxX, maxY, nowSeconds);
        RemoveStaleInterpolations();
    }

    private void SyncDwarfBillboards(EntityRegistry registry, SpatialIndexSystem spatial, MovementPresentationSystem? movementPresentation, GameFeedbackController? feedback, int currentZ, int minX, int minY, int maxX, int maxY, double nowSeconds)
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
            var visual = WorldSpriteVisuals.Dwarf(dwarf.Appearance);
            var segment = movementPresentation?.TryGetEntitySegment(dwarf.Id, out var movementSegment) == true ? movementSegment : (MovementPresentationSegment?)null;
            var billboardPosition = ResolveInterpolatedBillboardPosition(dwarf.Id, position, EntityFeetHeight, segment, nowSeconds);
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
        }

        RemoveStaleBillboards(_dwarfBillboards, _visibleDwarfIdSet);
    }

    private void SyncCreatureBillboards(EntityRegistry registry, SpatialIndexSystem spatial, MovementPresentationSystem? movementPresentation, int currentZ, int minX, int minY, int maxX, int maxY, double nowSeconds)
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
            var visual = WorldSpriteVisuals.Creature(creature.DefId);
            var segment = movementPresentation?.TryGetEntitySegment(creature.Id, out var movementSegment) == true ? movementSegment : (MovementPresentationSegment?)null;
            SyncBillboard(
                _creatureBillboards,
                creature.Id,
                position,
                $"Creature_{creature.Id}",
                visual.Texture,
                ResolveInterpolatedBillboardPosition(creature.Id, position, EntityFeetHeight, segment, nowSeconds),
                visual.WorldSize);
        }

        RemoveStaleBillboards(_creatureBillboards, _visibleCreatureIdSet);
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
            _visibleItemIds,
            _visibleItemEntries,
            MaxVisibleItemLikeBillboards);

        foreach (var entry in _visibleItemEntries)
        {
            _visibleItemIdSet.Add(entry.RuntimeId);
            _visibleInterpolatedIds.Add(entry.RuntimeId);
            SyncBillboard(
                _itemBillboards,
                entry.RuntimeId,
                entry.TilePosition,
                entry.NodeName,
                entry.Descriptor.Visual.Texture,
                ResolveInterpolatedBillboardPosition(entry.RuntimeId, entry.TilePosition, ItemFeetHeight, entry.MovementSegment, nowSeconds),
                entry.Descriptor.Visual.WorldSize);
        }

        RemoveStaleBillboards(_itemBillboards, _visibleItemIdSet);
    }

    private Vector3 ResolveInterpolatedBillboardPosition(
        int entityId,
        Vec3i logicalPosition,
        float localFeetHeight,
        MovementPresentationSegment? segment,
        double nowSeconds)
    {
        var targetTilePosition = new Vector3(logicalPosition.X, logicalPosition.Y, logicalPosition.Z);
        if (!_movementInterpolations.TryGetValue(entityId, out var state))
        {
            _movementInterpolations[entityId] = new MovementInterpolationState(targetTilePosition, nowSeconds);
            return ResolveBillboardPosition(targetTilePosition, localFeetHeight);
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

        return ResolveBillboardPosition(EvaluateInterpolatedTilePosition(state, nowSeconds), localFeetHeight);
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
        _visibleEmoteLabelIds.Clear();

        foreach (var dwarfId in _visibleDwarfIds)
        {
            if (!registry.TryGetById<Dwarf>(dwarfId, out var dwarf) || dwarf is null || !dwarf.Emotes.HasEmote)
                continue;

            _visibleEmoteLabelIds.Add(dwarf.Id);
            SyncEmoteLabel(
                dwarf.Id,
                dwarf.Emotes.CurrentEmote!,
                ResolveSmoothedFeedbackAnchor(renderCache, dwarf.Id, dwarf.Position.Position) + new Vector3(0f, 1.30f, 0f));
        }

        foreach (var creatureId in _visibleCreatureIds)
        {
            if (!registry.TryGetById<Creature>(creatureId, out var creature) || creature is null || !creature.Emotes.HasEmote)
                continue;

            _visibleEmoteLabelIds.Add(creature.Id);
            SyncEmoteLabel(
                creature.Id,
                creature.Emotes.CurrentEmote!,
                ResolveSmoothedFeedbackAnchor(renderCache, creature.Id, creature.Position.Position) + new Vector3(0f, 1.36f, 0f));
        }

        RemoveStaleLabels(_emoteLabels, _visibleEmoteLabelIds);
    }

    private void SyncEmoteLabel(int entityId, Emote emote, Vector3 position)
    {
        var label = _emoteLabels.TryGetValue(entityId, out var existing)
            ? existing
            : CreateLabel($"Emote_{entityId}", EmoteLabelFontSize, EmoteLabelPixelSize);

        _emoteLabels[entityId] = label;
        label.Text = EmoteVisuals.ResolveSymbol(emote.Id);
        label.Position = position;
        var color = EmoteVisuals.ResolveColor(emote.Id);
        var alpha = EmoteVisuals.ResolveAlpha(emote.TimeLeft);
        label.Modulate = new Color(color.R, color.G, color.B, color.A * alpha);
        label.Visible = _isActive;
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

    private void SyncBillboard<TKey>(Dictionary<TKey, BillboardState> states, TKey entityId, string name, Texture2D texture, Vector3 position, Vector2 size)
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
        state.Outline.Visible = _isActive && ReferenceEquals(state, _hoveredBillboard);
    }

    private void SyncBillboard<TKey>(Dictionary<TKey, BillboardState> states, TKey entityId, Vec3i tilePosition, string name, Texture2D texture, Vector3 position, Vector2 size)
        where TKey : notnull
    {
        SyncBillboard(states, entityId, name, texture, position, size);
        states[entityId].TilePosition = tilePosition;
    }

    private BillboardState CreateBillboardState(string name, Texture2D texture, Vector2 size)
    {
        var root = new Node3D { Name = name };
        var mesh = new MeshInstance3D
        {
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var outline = new MeshInstance3D
        {
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
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

        root.AddChild(outline);
        root.AddChild(mesh);
        root.AddChild(waterTint);
        root.AddChild(waterLine);
        AddChild(root);

        var state = new BillboardState(root, mesh, outline, waterTint, waterTintMaterial, waterLine, waterLineMaterial, texture, size);
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

        var outlineMesh = state.Outline.Mesh as QuadMesh ?? new QuadMesh();
        outlineMesh.Size = size * HoverOutlineScale;
        state.Outline.Mesh = outlineMesh;
        state.Outline.MaterialOverride = GetOutlineBillboardMaterial(texture);
        state.Outline.Position = new Vector3(0f, size.Y * 0.5f, HoverOutlineDepthOffset);
        state.Outline.Visible = _isActive && ReferenceEquals(state, _hoveredBillboard);
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
        };

        _billboardMaterials[texture] = material;
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
            AlbedoColor = HoverOutlineTint,
        };

        _outlineBillboardMaterials[texture] = material;
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
        ReplaceOwnedMesh(state.Outline, null);
        state.Outline.MaterialOverride = null;
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
        _visibleDwarfIdSet.Clear();
        _visibleCreatureIdSet.Clear();
        _visibleItemIdSet.Clear();
        _visibleEmoteLabelIds.Clear();
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
        }

        if (entityId >= 0 && renderCache.DwarfPositions.ContainsKey(entityId))
            return ResolveBillboardPosition(position, EntityFeetHeight);

        if (entityId >= 0 && renderCache.CreaturePositions.ContainsKey(entityId))
            return ResolveBillboardPosition(position, EntityFeetHeight);

        if (entityId >= 0 && renderCache.ItemPositions.ContainsKey(entityId))
            return ResolveBillboardPosition(position, ItemFeetHeight);

        return ResolveBillboardPosition(position, EntityFeetHeight);
    }

    private Label3D CreateLabel(string name, int fontSize, float pixelSize)
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

        AddChild(label);
        return label;
    }

    private static StandardMaterial3D CreateFlatColorMaterial()
    {
        return new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
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

    private void SetHoveredBillboard(BillboardState? next)
    {
        if (ReferenceEquals(_hoveredBillboard, next))
        {
            if (_hoveredBillboard is not null)
                _hoveredBillboard.Outline.Visible = _isActive;

            return;
        }

        if (_hoveredBillboard is not null)
            _hoveredBillboard.Outline.Visible = false;

        _hoveredBillboard = next;
        if (_hoveredBillboard is not null)
            _hoveredBillboard.Outline.Visible = _isActive;
    }

    private sealed class BillboardState
    {
        public BillboardState(
            Node3D root,
            MeshInstance3D mesh,
            MeshInstance3D outline,
            MeshInstance3D waterTint,
            StandardMaterial3D waterTintMaterial,
            MeshInstance3D waterLine,
            StandardMaterial3D waterLineMaterial,
            Texture2D texture,
            Vector2 size)
        {
            Root = root;
            Mesh = mesh;
            Outline = outline;
            WaterTint = waterTint;
            WaterTintMaterial = waterTintMaterial;
            WaterLine = waterLine;
            WaterLineMaterial = waterLineMaterial;
            Texture = texture;
            Size = size;
        }

        public Node3D Root { get; }

        public MeshInstance3D Mesh { get; }

    public MeshInstance3D Outline { get; }

        public MeshInstance3D WaterTint { get; }

        public StandardMaterial3D WaterTintMaterial { get; }

        public MeshInstance3D WaterLine { get; }

        public StandardMaterial3D WaterLineMaterial { get; }

        public Texture2D Texture { get; set; }

        public Vector2 Size { get; set; }

        public Vec3i TilePosition { get; set; }
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
}

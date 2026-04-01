using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Godot;

using WorldTileData = DwarfFortress.GameLogic.World.TileData;

public partial class GameRoot : Node2D
{
    public const  int   TileSize       = 64;
    private const float SimulationStep = 0.1f;
    private const float PanSpeed       = 900f;
    private const float RenderSmoothing = 14f;

    // ── Simulation ─────────────────────────────────────────────────────────
    private GameSimulation?    _simulation;
    private WorldMap?          _map;
    private EntityRegistry?    _registry;
    private ItemSystem?        _items;
    private BuildingSystem?    _buildings;
    private StockpileManager?  _stockpiles;
    private DataManager?       _data;
    private WorldQuerySystem?  _query;
    private double             _accumulator;

    // ── Camera / map state ────────────────────────────────────────────────
    private Camera2D? _camera;
    private int       _currentZ;
    private Vec3i?    _focusedLogTile;

    // ── Input ──────────────────────────────────────────────────────────────
    private InputController? _input;

    // ── Tile cache (rebuilt each frame, used by both Draw and hint) ────────
    private readonly Dictionary<(int X, int Y), WorldTileData> _tileCache = new();
    private readonly Dictionary<(int X, int Y), TerrainTransitionSet> _groundTransitionCache = new();
    private readonly Dictionary<int, Vector2> _dwarfRenderPositions = new();
    private readonly Dictionary<int, Vector2> _creatureRenderPositions = new();
    private readonly Dictionary<int, Vector2> _previousDwarfRenderPositions = new();
    private readonly Dictionary<int, Vector2> _previousCreatureRenderPositions = new();
    private readonly Dictionary<int, Vector2> _itemRenderPositions = new();
    private readonly HashSet<int> _aliveDwarfRenderIds = new();
    private readonly HashSet<int> _aliveCreatureRenderIds = new();
    private readonly HashSet<int> _aliveItemRenderIds = new();
    private readonly Dictionary<string, WaterEffectStyle> _creatureWaterEffectStyles = new(StringComparer.OrdinalIgnoreCase);
    private GameFeedbackController? _feedback;
    private Rect2I _visibleTileBounds = new();
    private bool _tileCacheDirty = true;
    private readonly HashSet<Vec3i> _dirtyVisibleTiles = new();
    private static readonly WaterEffectStyle DwarfWaterEffectStyle = new(
        RippleScale: 1.00f,
        BubbleScale: 1.00f,
        WakeScale: 1.00f,
        SubmergeScale: 1.00f,
        MotionThreshold: 0.35f,
        SuppressBubbles: false,
        WakePattern: WaterWakePattern.Default);
    private static readonly WaterEffectStyle CreatureWaterEffectStyle = new(
        RippleScale: 0.95f,
        BubbleScale: 0.90f,
        WakeScale: 0.95f,
        SubmergeScale: 0.94f,
        MotionThreshold: 0.35f,
        SuppressBubbles: false,
        WakePattern: WaterWakePattern.Default);
    private static readonly WaterEffectStyle LargeCreatureWaterEffectStyle = new(
        RippleScale: 1.22f,
        BubbleScale: 1.20f,
        WakeScale: 1.26f,
        SubmergeScale: 1.08f,
        MotionThreshold: 0.24f,
        SuppressBubbles: false,
        WakePattern: WaterWakePattern.Default);
    private static readonly WaterEffectStyle PetCreatureWaterEffectStyle = new(
        RippleScale: 0.86f,
        BubbleScale: 0.74f,
        WakeScale: 0.82f,
        SubmergeScale: 0.86f,
        MotionThreshold: 0.32f,
        SuppressBubbles: false,
        WakePattern: WaterWakePattern.Default);
    private static readonly WaterEffectStyle AquaticCreatureWaterEffectStyle = new(
        RippleScale: 0.74f,
        BubbleScale: 0.36f,
        WakeScale: 1.06f,
        SubmergeScale: 0.78f,
        MotionThreshold: 0.12f,
        SuppressBubbles: true,
        WakePattern: WaterWakePattern.SwimV);

    // ── UI panels ──────────────────────────────────────────────────────────
    private TopBar?          _topBar;
    private ActionBar?       _actionBar;
    private HoverInfoPanel?  _hoverInfo;
    private TileInfoPanel?   _tileInfo;
    private DwarfPanel?      _dwarfPanel;
    private WorkshopPanel?   _workshopPanel;
    private AnnouncementLog? _announcementLog;
    private KnowledgePanel?  _knowledgePanel;
    private EmoteBubbleRenderer? _emoteRenderer;

    // ── Public API (used by smoke tests) ──────────────────────────────────
    public bool    IsSimulationReady => _simulation is not null && _map is not null && _query is not null;
    public string? StartupError      { get; private set; }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    public override void _Ready()
    {
        if (PixelArtFactory.UseSprites)
            SpriteRegistry.Load();
        else
            GD.Print("[GameRoot] Sprite feature flag is OFF. Using generated artwork.");

        _camera          = GetNode<Camera2D>("%MainCamera");
        _input           = GetNode<InputController>("%InputController");
        _topBar          = GetNode<TopBar>("%TopBar");
        _actionBar       = GetNode<ActionBar>("%ActionBar");
        _hoverInfo       = GetNode<HoverInfoPanel>("%HoverInfoPanel");
        _tileInfo        = GetNode<TileInfoPanel>("%TileInfoPanel");
        _dwarfPanel      = GetNode<DwarfPanel>("%DwarfPanel");
        _workshopPanel   = GetNode<WorkshopPanel>("%WorkshopPanel");
        _announcementLog = GetNode<AnnouncementLog>("%AnnouncementLog");
        _knowledgePanel  = GetNodeOrNull<KnowledgePanel>("%KnowledgePanel");
        _emoteRenderer   = GetNodeOrNull<EmoteBubbleRenderer>("%EmoteBubbleRenderer");
        _feedback        = new GameFeedbackController(this);

        TryStartSimulation();
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        HandleCameraMovement(delta);

        if (_simulation is null) return;

        if (!(_actionBar?.IsPaused ?? false))
        {
            _accumulator += delta * (_actionBar?.SpeedMultiplier ?? 1f);
            while (_accumulator >= SimulationStep)
            {
                _simulation.Tick(SimulationStep);
                _accumulator -= SimulationStep;
            }
        }

        _input?.SetCurrentZ(_currentZ);
        UpdateRenderPositions((float)delta);
        _feedback?.Update((float)delta);

        UpdateTileCacheIfNeeded();

        if (_query is not null)
            _topBar?.Refresh(_query.GetTimeView(), _input?.CurrentMode ?? InputMode.Select, GetModeHint());

        if (_query is not null)
            _announcementLog?.Refresh(_query.GetFortressAnnouncements());

        if (_input is not null)
            _hoverInfo?.Refresh(_input.HoveredTile, _currentZ);

        // Dwarf / workshop panel (also refresh queue display each frame)
        UpdateSelectionPanels();
        _workshopPanel?.Refresh();

        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Zoom
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            if      (mb.ButtonIndex == MouseButton.WheelUp)   ApplyZoom(0.9f);
            else if (mb.ButtonIndex == MouseButton.WheelDown) ApplyZoom(1.1f);
        }

        // Z-layer
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Space)
            {
                _actionBar?.TogglePause();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.Q)
            {
                _currentZ = Mathf.Max(0, _currentZ - 1);
                _input?.SetCurrentZ(_currentZ);
                InvalidateTileCache();
                QueueRedraw();
            }
            else if (key.Keycode == Key.E)
            {
                var maxZ = (_map?.Depth ?? 1) - 1;
                _currentZ = Mathf.Min(_currentZ + 1, maxZ);
                _input?.SetCurrentZ(_currentZ);
                InvalidateTileCache();
                QueueRedraw();
            }
        }
    }

    // ── Drawing ────────────────────────────────────────────────────────────

    public override void _Draw()
    {
        if (_simulation is null || _map is null || _registry is null) return;

        DrawTiles();
        DrawStockpiles();
        DrawBuildings();
        DrawBoxes();
        DrawItems();
        DrawCreatures();
        DrawDwarves();
        DrawTrees();
        DrawWorldFx();
        DrawDesignationOverlays();
        DrawSelectionOverlays();
        DrawEmotes();
    }

    // ── Draw layers ────────────────────────────────────────────────────────

    private void DrawTiles()
    {
        var undergroundShade = _currentZ <= 0
            ? 0f
            : Mathf.Clamp(_currentZ * 0.045f, 0f, 0.28f);

        foreach (var ((x, y), tile) in _tileCache)
        {
            var rect = TileRect(x, y);
            var tilePos = new Vec3i(x, y, _currentZ);
            if (ShouldObscureTile(tilePos))
            {
                DrawHiddenTile(rect);
                continue;
            }

            var visibleOreItemDefId = ResolveVisibleOreItemDefId(tilePos, tile);
            TileRenderHelper.DrawTile(this, rect, new TileRenderData(tile.TileDefId, tile.MaterialId, tile.FluidType, tile.FluidLevel, tile.FluidMaterialId, visibleOreItemDefId, tile.PlantDefId, tile.PlantGrowthStage, tile.PlantYieldLevel, tile.PlantSeedLevel), x, y, _currentZ,
                TryGetTileRenderData, ResolveGroundTileDefIdFromMaterial, TryGetCachedGroundTransitions);

            if (undergroundShade > 0f)
                DrawRect(rect, new Color(0f, 0f, 0f, undergroundShade));

            if (tile.CoatingAmount > 0f)
                DrawRect(rect, new Color(0.75f, 0.55f, 0.2f, 0.10f + tile.CoatingAmount * 0.2f));

            _feedback?.DrawTilePulse(this, rect, new Vec3i(x, y, _currentZ));
        }
    }

    private TileRenderData? TryGetTileRenderData(int x, int y, int z)
    {
        if (_map is null || x < 0 || y < 0 || z < 0 || x >= _map.Width || y >= _map.Height || z >= _map.Depth)
            return null;

        var tile = _map.GetTile(new Vec3i(x, y, z));
        return new TileRenderData(tile.TileDefId, tile.MaterialId, tile.FluidType, tile.FluidLevel, tile.FluidMaterialId, tile.OreItemDefId, tile.PlantDefId, tile.PlantGrowthStage, tile.PlantYieldLevel, tile.PlantSeedLevel);
    }

    private string? ResolveGroundTileDefIdFromMaterial(string? materialId)
    {
        if (string.IsNullOrWhiteSpace(materialId) || _data is null)
            return null;

        var material = _data.Materials.GetOrNull(materialId);
        return GroundMaterialResolver.ResolveGroundTileDefId(materialId, material?.Tags);
    }

    private TerrainTransitionSet? TryGetCachedGroundTransitions(int x, int y, int z)
    {
        if (z != _currentZ)
            return null;

        return _groundTransitionCache.TryGetValue((x, y), out var transitions)
            ? transitions
            : null;
    }

    private bool ShouldObscureTile(Vec3i pos)
    {
        if (_map is null)
            return false;

        return !MiningLineOfSight.IsTileVisible(_map, pos);
    }

    private void DrawHiddenTile(Rect2 rect)
    {
        DrawRect(rect, new Color(0.02f, 0.02f, 0.03f, 0.98f));
        DrawRect(rect, new Color(0.10f, 0.10f, 0.11f, 0.95f), false, 1f);
    }

    private string? ResolveVisibleOreItemDefId(Vec3i pos, WorldTileData tile)
    {
        if (_map is null || string.IsNullOrWhiteSpace(tile.OreItemDefId))
            return null;

        return MiningLineOfSight.IsOreVisible(_map, pos)
            ? tile.OreItemDefId
            : null;
    }

    private void DrawStockpiles()
    {
        if (_stockpiles is null) return;

        var fillColor = new Color(0.12f, 0.78f, 0.74f, 0.09f);
        var gridColor = new Color(0.24f, 0.95f, 0.88f, 0.14f);
        var borderColor = new Color(0.20f, 0.92f, 0.84f, 0.85f);
        var innerBorderColor = new Color(0.55f, 1.0f, 0.95f, 0.45f);

        foreach (var s in _stockpiles.GetAll())
        {
            if (s.From.Z > _currentZ || s.To.Z < _currentZ)
                continue;

            var rect = SpanRect(s.From.X, s.From.Y, s.To.X, s.To.Y).Grow(-1f);
            DrawRect(rect, fillColor);
            DrawStockpileGrid(rect, gridColor);
            DrawRect(rect, borderColor, false, 2f);
            DrawRect(rect.Grow(-3f), innerBorderColor, false, 1f);
            DrawStockpileCornerAccents(rect, borderColor);
            DrawStockpileLabelChip(rect, s.AcceptedTags);
        }
    }

    private void DrawStockpileGrid(Rect2 rect, Color lineColor)
    {
        if (rect.Size.X <= TileSize || rect.Size.Y <= TileSize)
            return;

        for (var x = rect.Position.X + TileSize; x < rect.End.X; x += TileSize)
            DrawLine(new Vector2(x, rect.Position.Y + 1f), new Vector2(x, rect.End.Y - 1f), lineColor, 1f);

        for (var y = rect.Position.Y + TileSize; y < rect.End.Y; y += TileSize)
            DrawLine(new Vector2(rect.Position.X + 1f, y), new Vector2(rect.End.X - 1f, y), lineColor, 1f);
    }

    private void DrawStockpileCornerAccents(Rect2 rect, Color color)
    {
        const float length = 12f;
        const float width = 2f;
        var tl = rect.Position;
        var tr = new Vector2(rect.End.X, rect.Position.Y);
        var bl = new Vector2(rect.Position.X, rect.End.Y);
        var br = rect.End;

        DrawLine(tl, tl + new Vector2(length, 0f), color, width);
        DrawLine(tl, tl + new Vector2(0f, length), color, width);

        DrawLine(tr, tr + new Vector2(-length, 0f), color, width);
        DrawLine(tr, tr + new Vector2(0f, length), color, width);

        DrawLine(bl, bl + new Vector2(length, 0f), color, width);
        DrawLine(bl, bl + new Vector2(0f, -length), color, width);

        DrawLine(br, br + new Vector2(-length, 0f), color, width);
        DrawLine(br, br + new Vector2(0f, -length), color, width);
    }

    private void DrawStockpileLabelChip(Rect2 rect, string[] acceptedTags)
    {
        var tagText = acceptedTags.Length > 0 ? acceptedTags[0] : "SP";
        var label = tagText.Length > 10 ? tagText[..10] : tagText;
        var chipWidth = Mathf.Clamp(12f + (label.Length * 7f), 48f, rect.Size.X - 8f);
        var chipRect = new Rect2(rect.Position + new Vector2(4f, 4f), new Vector2(chipWidth, 16f));

        DrawRect(chipRect, new Color(0.04f, 0.18f, 0.17f, 0.72f));
        DrawRect(chipRect, new Color(0.45f, 1f, 0.94f, 0.70f), false, 1f);
        DrawString(ThemeDB.FallbackFont, chipRect.Position + new Vector2(4f, 12f), label.ToUpperInvariant(),
            fontSize: 10, modulate: new Color(0.80f, 1f, 0.96f, 1f));
    }

    private void DrawBuildings()
    {
        if (_buildings is null || _data is null) return;

        foreach (var b in _buildings.GetAll())
        {
            if (b.Origin.Z != _currentZ)
                continue;

            var def = _data.Buildings.GetOrNull(b.BuildingDefId);
            int minX = b.Origin.X;
            int maxX = b.Origin.X;
            int minY = b.Origin.Y;
            int maxY = b.Origin.Y;
            if (def is not null)
            {
                foreach (var tile in def.Footprint)
                {
                    var x = b.Origin.X + tile.Offset.X;
                    var y = b.Origin.Y + tile.Offset.Y;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }

            var spanRect = SpanRect(minX, minY, maxX, maxY).Grow(-6);
            spanRect = _feedback?.ApplyBuildingTransform(b.Id, spanRect) ?? spanRect;

            DrawTextureRect(PixelArtFactory.GetBuilding(b.BuildingDefId), spanRect, false);
            DrawRect(spanRect, new Color(0.95f, 0.78f, 0.42f, 0.90f), false, 2.5f);
            _feedback?.DrawBuildingPulse(this, b.Id, spanRect);
        }
    }

    private void DrawBoxes()
    {
        if (_registry is null || _items is null) return;

        foreach (var box in _registry.GetAlive<DwarfFortress.GameLogic.Entities.Box>())
        {
            var pos = box.Position.Position;
            if (pos.Z != _currentZ) continue;

            var tileCenter = TileRect(pos.X, pos.Y).GetCenter();
            var boxSize = new Vector2(36, 36);
            DrawTextureRect(PixelArtFactory.GetItem(DwarfFortress.GameLogic.Items.ItemDefIds.Box),
                new Rect2(tileCenter - boxSize / 2f, boxSize), false);

            // Peek: up to 3 distinct item types by frequency, drawn as if overflowing the top edge
            var storedItems = _items.GetItemsInBox(box.Id).ToList();
            if (storedItems.Count > 0)
            {
                var topTypes = storedItems
                    .GroupBy(i => i.DefId)
                    .OrderByDescending(g => g.Count())
                    .Take(3)
                    .Select(g => g.Key)
                    .ToArray();

                const float peekSize    = 20f;
                const float peekSpacing = 22f;
                // Y-offsets from the box top edge for left/mid/right — middle pokes up most
                float[] yOffsets = topTypes.Length switch
                {
                    1 => [0f],
                    2 => [4f, 4f],
                    _ => [5f, -3f, 5f], // arc: edges lower, centre highest
                };

                float boxTopY = tileCenter.Y - boxSize.Y / 2f;  // top edge of box sprite
                float totalW  = (topTypes.Length - 1) * peekSpacing;
                float startX  = tileCenter.X - totalW / 2f;

                for (int i = 0; i < topTypes.Length; i++)
                {
                    // Center each icon so its lower half overlaps the box top edge
                    float iconCenterY = boxTopY + yOffsets[i];
                    var iconPos = new Vector2(startX + i * peekSpacing, iconCenterY);
                    var half = new Vector2(peekSize / 2f, peekSize / 2f);
                    DrawTextureRect(PixelArtFactory.GetItem(topTypes[i]),
                        new Rect2(iconPos - half, new Vector2(peekSize, peekSize)), false);
                }
            }

            // Count badge: stored / capacity
            var count = box.Container.Count;
            var cap   = box.Container.Capacity;
            var label = $"{count}/{cap}";
            var badgePos = tileCenter + new Vector2(-16f, 13f);
            DrawString(ThemeDB.FallbackFont, badgePos, label,
                fontSize: 10, modulate: new Color(1f, 0.94f, 0.60f, 1f));
        }
    }

    private void DrawItems()
    {
        if (_items is null) return;

        foreach (var item in _items.GetAllItems())
        {
            // Items stored inside a box are not rendered separately
            if (item.ContainerItemId >= 0)
                continue;
            if (item.Position.Position.Z != _currentZ)
                continue;

            var drawPos = GetSmoothedItemCenter(item);
            var size = new Vector2(32, 32);
            DrawTextureRect(PixelArtFactory.GetItem(item.DefId), new Rect2(drawPos - size / 2f, size), false);

            if (item.CarriedByEntityId >= 0)
                DrawCircle(drawPos + new Vector2(12f, -8f), 3.5f, new Color(1f, 0.92f, 0.35f, 0.9f));
        }
    }

    private void DrawCreatures()
    {
        if (_registry is null) return;

        foreach (var c in _registry.GetAlive<Creature>())
        {
            if (c.Position.Position.Z != _currentZ)
                continue;

            var drawPos = GetSmoothedEntityCenter(_creatureRenderPositions, c.Id, c.Position.Position, 10f);
            var motion = ResolveEntityMotionVector(_previousCreatureRenderPositions, c.Id, drawPos);
            var style = ResolveCreatureWaterEffectStyle(c);
            var size = new Vector2(40, 40);
            DrawTextureRect(PixelArtFactory.GetEntity(c.DefId), new Rect2(drawPos - size / 2f, size), false);
            DrawWaterContactEffect(c.Position.Position, drawPos, size, c.Id, motion, style);
        }
    }

    private void DrawDwarves()
    {
        if (_registry is null) return;

        foreach (var d in _registry.GetAlive<Dwarf>())
        {
            if (d.Position.Position.Z != _currentZ)
                continue;

            var drawPos = GetSmoothedEntityCenter(_dwarfRenderPositions, d.Id, d.Position.Position, 8f);
            var motion = ResolveEntityMotionVector(_previousDwarfRenderPositions, d.Id, drawPos);
            var transformed = _feedback?.ApplyDwarfTransform(d.Id, drawPos, new Vector2(52, 52))
                ?? (drawPos, new Vector2(52, 52));
            var center = transformed.Center;
            var size = transformed.Size;
            _feedback?.DrawDwarfPulse(this, d.Id, center);

            DrawTextureRect(PixelArtFactory.GetDwarf(d.Appearance), new Rect2(center - size / 2f, size), false);
            DrawWaterContactEffect(d.Position.Position, center, size, d.Id, motion, DwarfWaterEffectStyle);
        }
    }

    private void DrawEmotes()
    {
        _emoteRenderer?.DrawEmotes(this, _currentZ);
    }

    private void DrawTrees()
    {
        // Draw tree sprites in a 1×2 tile rect (extends one tile above the tree tile).
        // Called after entities so canopy renders on top — entities at y-1 appear behind it.
        foreach (var ((x, y), tile) in _tileCache)
        {
            if (tile.TileDefId != TileDefIds.Tree) continue;
            var tilePos = new Vec3i(x, y, _currentZ);
            if (ShouldObscureTile(tilePos)) continue;

            var treeRect = new Rect2(x * TileSize, (y - 1) * TileSize, TileSize, TileSize * 2);
            DrawTextureRect(PixelArtFactory.GetTile(TileDefIds.Tree), treeRect, false);

            // Plant overlay (e.g. fruit/berries on tree) at normal tile level
            if (!string.IsNullOrWhiteSpace(tile.PlantDefId))
            {
                var tileRect = TileRect(x, y);
                DrawTextureRect(
                    PixelArtFactory.GetPlantOverlay(tile.PlantDefId, tile.PlantGrowthStage, tile.PlantYieldLevel, tile.PlantSeedLevel),
                    tileRect, false);
            }
        }
    }

    private void DrawWaterContactEffect(
        Vec3i tilePos,
        Vector2 center,
        Vector2 size,
        int phaseSeed,
        Vector2 motion,
        WaterEffectStyle style)
    {
        if (_map is null)
            return;

        var tile = _map.GetTile(tilePos);
        if (tile.FluidType != FluidType.Water || tile.FluidLevel == 0)
            return;

        var depthNorm = tile.FluidLevel / 7f;
        var now = (float)Time.GetTicksMsec() * 0.001f;
        var phase = (phaseSeed % 97) * 0.19f;
        var contactY = center.Y + (size.Y * (depthNorm >= 0.55f ? 0.08f : 0.22f));

        var rippleRadius = (7f + (depthNorm * 10f) + (Mathf.Sin((now * 3.2f) + phase) * 1.35f)) * style.RippleScale;
        var rippleAlpha = (0.20f + (depthNorm * 0.18f)) * Mathf.Clamp(0.74f + (style.RippleScale * 0.30f), 0.55f, 1.35f);
        var rippleColor = new Color(0.68f, 0.89f, 1f, rippleAlpha);
        DrawArc(new Vector2(center.X, contactY), rippleRadius, 0f, Mathf.Tau, 24, rippleColor, 1.8f);

        if (!style.SuppressBubbles)
        {
            var bubbleLift = MathF.Abs(MathF.Sin((now * 5.3f) + phase));
            var bubbleBaseY = contactY - (2f + bubbleLift * (2f + (depthNorm * 4f)));
            var bubbleAlpha = (0.40f + (depthNorm * 0.35f)) * Mathf.Clamp(style.BubbleScale, 0.35f, 1.4f);
            var bubbleColor = new Color(0.88f, 0.97f, 1f, bubbleAlpha);
            DrawCircle(new Vector2(center.X - 7f, bubbleBaseY), 1.6f * style.BubbleScale, bubbleColor);

            if (tile.FluidLevel >= 3)
                DrawCircle(new Vector2(center.X + 6f, bubbleBaseY - 2f), 1.9f * style.BubbleScale, bubbleColor);
            if (tile.FluidLevel >= 5)
                DrawCircle(new Vector2(center.X + 1f, bubbleBaseY - 4.5f), 1.4f * style.BubbleScale, bubbleColor);
        }

        var submerge = Mathf.Clamp((tile.FluidLevel - 1f) / 6f, 0f, 1f);
        var submergedHeight = size.Y * Mathf.Lerp(0.12f, 0.46f, submerge) * style.SubmergeScale;
        if (submergedHeight > 1f)
        {
            var submergedRect = new Rect2(
                new Vector2(center.X - (size.X / 2f), center.Y + (size.Y / 2f) - submergedHeight),
                new Vector2(size.X, submergedHeight));
            DrawRect(submergedRect, new Color(0.20f, 0.52f, 0.86f, 0.10f + (depthNorm * 0.16f)));
            DrawLine(
                new Vector2(submergedRect.Position.X, submergedRect.Position.Y),
                new Vector2(submergedRect.End.X, submergedRect.Position.Y),
                new Color(0.82f, 0.95f, 1f, 0.22f + (depthNorm * 0.12f)),
                1.4f);
        }

        var speed = motion.Length();
        if (speed < style.MotionThreshold)
            return;

        var direction = motion.Normalized();
        var tangent = new Vector2(-direction.Y, direction.X);
        var speedNorm = Mathf.Clamp(speed / 6f, 0f, 1f);
        var wakeAlpha = (0.10f + (speedNorm * 0.22f) + (depthNorm * 0.10f)) * Mathf.Clamp(style.WakeScale, 0.55f, 1.45f);
        var wakeColor = new Color(0.72f, 0.92f, 1f, wakeAlpha);
        var wakeBase = new Vector2(center.X, contactY) - (direction * (5f + (depthNorm * 8f * style.WakeScale)));
        var wakeLength = (9f + (speedNorm * 12f) + (depthNorm * 8f)) * style.WakeScale;
        var wakeWidth = (1.4f + (speedNorm * 1.4f)) * Mathf.Clamp(0.82f + (style.WakeScale * 0.18f), 0.72f, 1.35f);
        var spread = (4f + (depthNorm * 3.5f)) * Mathf.Clamp(style.WakeScale, 0.7f, 1.45f);

        if (style.WakePattern == WaterWakePattern.SwimV)
        {
            var armLength = wakeLength * 0.94f;
            var armSpread = spread * 0.86f;
            var apex = wakeBase - (direction * (2f + depthNorm * 3f));
            var leftArm = apex + (tangent * armSpread) - (direction * armLength);
            var rightArm = apex - (tangent * armSpread) - (direction * armLength);
            var armColor = wakeColor with { A = wakeColor.A * 0.90f };

            DrawLine(apex, leftArm, armColor, Mathf.Max(1f, wakeWidth - 0.05f));
            DrawLine(apex, rightArm, armColor, Mathf.Max(1f, wakeWidth - 0.05f));

            var tailColor = wakeColor with { A = wakeColor.A * 0.45f };
            DrawLine(
                apex,
                apex - (direction * (armLength * 0.35f)),
                tailColor,
                Mathf.Max(1f, wakeWidth - 0.35f));
        }
        else
        {
            DrawLine(wakeBase, wakeBase - (direction * wakeLength), wakeColor, wakeWidth);
            DrawLine(
                wakeBase + (tangent * spread),
                wakeBase + (tangent * spread * 0.85f) - (direction * (wakeLength * 0.88f)),
                wakeColor with { A = wakeColor.A * 0.82f },
                Mathf.Max(1f, wakeWidth - 0.2f));
            DrawLine(
                wakeBase - (tangent * spread),
                wakeBase - (tangent * spread * 0.85f) - (direction * (wakeLength * 0.88f)),
                wakeColor with { A = wakeColor.A * 0.82f },
                Mathf.Max(1f, wakeWidth - 0.2f));
        }
    }

    private void DrawWorldFx()
    {
        _feedback?.DrawWorldFx(this, _currentZ, WorldToScreenCenter, ResolveSmoothedEntityCenterForFx);
    }

    private void DrawDesignationOverlays()
    {
        // Golden fill + border + X mark — clearly shows "queued for a job"
        const float pad       = 10f;
        var         fill      = new Color(1f, 0.78f, 0.05f, 0.38f);
        var         border    = new Color(1f, 0.78f, 0.05f, 0.92f);

        foreach (var ((x, y), tile) in _tileCache)
        {
            if (!tile.IsDesignated)
                continue;

            var r = TileRect(x, y);
            DrawRect(r, fill);
            DrawRect(r, border, false, 2.5f);
            DrawLine(r.Position + new Vector2(pad, pad),
                     r.Position + new Vector2(r.Size.X - pad, r.Size.Y - pad),
                     border, 2.5f);
            DrawLine(r.Position + new Vector2(r.Size.X - pad, pad),
                     r.Position + new Vector2(pad, r.Size.Y - pad),
                     border, 2.5f);
        }
    }

    private void DrawSelectionOverlays()
    {
        if (_input is null) return;

        // Hover highlight
        DrawRect(TileRect(_input.HoveredTile.X, _input.HoveredTile.Y), new Color(1f, 1f, 1f, 0.15f));

        if (_focusedLogTile is Vec3i focusedTile && focusedTile.Z == _currentZ)
        {
            var focusRect = TileRect(focusedTile.X, focusedTile.Y);
            DrawRect(focusRect, new Color(0.22f, 0.88f, 1f, 0.18f));
            DrawRect(focusRect, new Color(0.58f, 0.95f, 1f, 0.98f), false, 3f);
            DrawRect(focusRect.Grow(-6f), new Color(0.92f, 0.98f, 1f, 0.55f), false, 1.5f);
        }

        // Drag selection rectangle — per-tile highlighting
        var sel = _input.GetSelectionRect();
        if (sel.HasValue)
        {
            var (from, to) = sel.Value;
            var mode = _input.CurrentMode;

            // Mode-specific fill colors
            var applicable = mode switch
            {
                InputMode.DesignateClear    => new Color(0.95f, 0.72f, 0.12f, 0.55f),
                InputMode.DesignateMine     => new Color(1f,    0.78f, 0.05f, 0.55f),
                InputMode.DesignateCutTrees => new Color(0.25f, 0.92f, 0.15f, 0.55f),
                InputMode.DesignateCancel   => new Color(0.95f, 0.2f,  0.10f, 0.55f),
                InputMode.StockpileZone     => new Color(0.95f, 0.88f, 0.10f, 0.50f),
                _                           => new Color(0.35f, 0.70f, 1.00f, 0.50f),
            };
            var notApplicable = new Color(0.5f, 0.5f, 0.5f, 0.10f);
            var border        = applicable.Lightened(0.35f);

            int x0 = Mathf.Min(from.X, to.X), x1 = Mathf.Max(from.X, to.X);
            int y0 = Mathf.Min(from.Y, to.Y), y1 = Mathf.Max(from.Y, to.Y);

            for (int tx = x0; tx <= x1; tx++)
            for (int ty = y0; ty <= y1; ty++)
            {
                TryGetTileAtCurrentZ(tx, ty, out var tile);
                var color = IsApplicable(mode, tile) ? applicable : notApplicable;
                DrawRect(TileRect(tx, ty), color);
            }

            // Outer border
            DrawRect(SpanRect(x0, y0, x1, y1), border, false, 2.5f);
        }

        // Selected dwarf highlight
        if (_input.SelectedDwarfId is int dId)
        {
            var dwarf = _query?.GetDwarfView(dId);
            if (dwarf is not null)
            {
                var rect = TileRect(dwarf.Position.X, dwarf.Position.Y);
                DrawRect(rect, new Color(1f, 1f, 0f, 0.25f));
                DrawRect(rect, new Color(1f, 1f, 0f, 0.9f), false, 2.5f);
            }
        }

        // Selected creature highlight
        if (_input.SelectedCreatureId is int cId)
        {
            var creature = _query?.GetCreatureView(cId);
            if (creature is not null)
            {
                var rect = TileRect(creature.Position.X, creature.Position.Y);
                DrawRect(rect, new Color(1f, 0.45f, 0.15f, 0.24f));
                DrawRect(rect, new Color(1f, 0.45f, 0.15f, 0.95f), false, 2.5f);
            }
        }

        // Selected building highlight (covers full footprint)
        if (_input.SelectedBuildingId is int bId)
        {
            var building = _query?.GetBuildingView(bId);
            if (building is not null && _data is not null)
            {
                var def = _data.Buildings.GetOrNull(building.BuildingDefId);
                var cells = def is not null
                    ? def.Footprint.Select(t => (building.Origin.X + t.Offset.X, building.Origin.Y + t.Offset.Y))
                    : new[] { (building.Origin.X, building.Origin.Y) };
                foreach (var (cx, cy) in cells)
                {
                    var rect = TileRect(cx, cy);
                    DrawRect(rect, new Color(0.4f, 0.85f, 1f, 0.25f));
                    DrawRect(rect, new Color(0.4f, 0.85f, 1f, 0.9f), false, 2.5f);
                }
            }
        }

        // Building ghost (preview placement)
        if (_input.CurrentMode == InputMode.BuildingPreview && _input.PendingBuildingDefId is not null)
        {
            var ghost = TileRect(_input.HoveredTile.X, _input.HoveredTile.Y);
            DrawRect(ghost, new Color(0.35f, 0.7f, 1f, 0.35f));
            DrawRect(ghost, new Color(0.35f, 0.7f, 1f, 0.85f), false, 2f);
        }
    }

    // ── UI wiring ──────────────────────────────────────────────────────────

    private void UpdateSelectionPanels()
    {
        if (_input is null || _query is null) return;

        // Dwarf selected
        if (_input.SelectedDwarfId is int dId)
        {
            var dwarf = _query.GetDwarfView(dId);
            if (dwarf is not null)
            {
                _tileInfo?.Hide();
                _dwarfPanel?.ShowDwarf(dwarf);
                _workshopPanel?.Hide();
                return;
            }
        }

        // Creature selected
        if (_input.SelectedCreatureId is int cId)
        {
            var creature = _query.GetCreatureView(cId);
            if (creature is not null)
            {
                _tileInfo?.Hide();
                _dwarfPanel?.ShowCreature(creature);
                _workshopPanel?.Hide();
                return;
            }
        }

        // Building selected
        if (_input.SelectedBuildingId is int bId)
        {
            var building = _query.GetBuildingView(bId);
            if (building is not null && building.IsWorkshop)
            {
                _tileInfo?.Hide();
                _workshopPanel?.ShowBuilding(building);
                _dwarfPanel?.Hide();
                return;
            }
        }

        if (_input?.SelectedTile is Vector2I selectedTile)
        {
            _tileInfo?.Refresh(selectedTile, _currentZ);
            _tileInfo?.Show();
            _dwarfPanel?.Hide();
            _workshopPanel?.Hide();
            return;
        }

        _tileInfo?.Hide();
        _dwarfPanel?.Hide();
        _workshopPanel?.Hide();
    }

    private void TryStartSimulation()
    {
        try
        {
            _simulation  = ClientSimulationFactory.CreateSimulation();
            _map         = _simulation.Context.Get<WorldMap>();
            _registry    = _simulation.Context.Get<EntityRegistry>();
            _items       = _simulation.Context.Get<ItemSystem>();
            _buildings   = _simulation.Context.Get<BuildingSystem>();
            _stockpiles  = _simulation.Context.Get<StockpileManager>();
            _data        = _simulation.Context.Get<DataManager>();
            _query       = _simulation.Context.Get<WorldQuerySystem>();
            _creatureWaterEffectStyles.Clear();

            _input?.Setup(_simulation);
            _hoverInfo?.Setup(_simulation);
            _tileInfo?.Setup(_simulation);
            _dwarfPanel?.Setup(_simulation);
            _dwarfPanel?.SetTileNavigator(JumpToTile);
            _announcementLog?.SetTileNavigator(JumpToTile);
            _workshopPanel?.Setup(_simulation);
            _actionBar?.Setup(_input!, _simulation);
            _knowledgePanel?.Setup(_simulation);

            // Wire up Knowledge button
            if (_actionBar is not null && _knowledgePanel is not null)
            {
                _actionBar.OnKnowledgePressed += () =>
                {
                    _knowledgePanel.Refresh();
                    _knowledgePanel.Show();
                };
            }

            if (_camera is not null)
                _camera.Position = new Vector2(24 * TileSize, 24 * TileSize);

            _feedback?.Bind(_simulation, _map, _query);
            _emoteRenderer?.Setup(_simulation, ResolveSmoothedEntityCenter);
            _simulation.EventBus.On<TileChangedEvent>(OnTileChanged);
            InvalidateTileCache();
            _announcementLog?.Refresh(_query.GetFortressAnnouncements());
        }
        catch (System.Exception exception)
        {
            StartupError = exception.ToString();
            GD.PushError(StartupError);
            _announcementLog?.AddMessage($"Startup failed: {exception.Message}", Colors.Red);
        }
    }

    private void UpdateRenderPositions(float delta)
    {
        if (_registry is null || _items is null)
            return;

        UpdateEntityRenderPositions(
            _dwarfRenderPositions,
            _previousDwarfRenderPositions,
            _registry.GetAlive<Dwarf>(),
            _aliveDwarfRenderIds,
            delta,
            8f);
        UpdateEntityRenderPositions(
            _creatureRenderPositions,
            _previousCreatureRenderPositions,
            _registry.GetAlive<Creature>(),
            _aliveCreatureRenderIds,
            delta,
            10f);
        UpdateEntityRenderPositions(
            _itemRenderPositions,
            null,
            _items.GetAllItems(),
            _aliveItemRenderIds,
            delta,
            8f);
    }

    private void UpdateEntityRenderPositions(
        Dictionary<int, Vector2> cache,
        Dictionary<int, Vector2>? previousCache,
        IEnumerable<Dwarf> entities,
        HashSet<int> aliveIds,
        float delta,
        float yOffset)
    {
        aliveIds.Clear();
        foreach (var entity in entities)
            UpdateEntityRenderPosition(cache, previousCache, aliveIds, entity.Id, entity.Position.Position, delta, yOffset);

        RemoveStaleRenderPositions(cache, previousCache, aliveIds);
    }

    private void UpdateEntityRenderPositions(
        Dictionary<int, Vector2> cache,
        Dictionary<int, Vector2>? previousCache,
        IEnumerable<Creature> entities,
        HashSet<int> aliveIds,
        float delta,
        float yOffset)
    {
        aliveIds.Clear();
        foreach (var entity in entities)
            UpdateEntityRenderPosition(cache, previousCache, aliveIds, entity.Id, entity.Position.Position, delta, yOffset);

        RemoveStaleRenderPositions(cache, previousCache, aliveIds);
    }

    private void UpdateEntityRenderPositions(
        Dictionary<int, Vector2> cache,
        Dictionary<int, Vector2>? previousCache,
        IEnumerable<Item> entities,
        HashSet<int> aliveIds,
        float delta,
        float yOffset)
    {
        aliveIds.Clear();
        foreach (var entity in entities)
            UpdateEntityRenderPosition(cache, previousCache, aliveIds, entity.Id, entity.Position.Position, delta, yOffset);

        RemoveStaleRenderPositions(cache, previousCache, aliveIds);
    }

    private static void UpdateEntityRenderPosition(
        Dictionary<int, Vector2> cache,
        Dictionary<int, Vector2>? previousCache,
        HashSet<int> aliveIds,
        int id,
        Vec3i pos,
        float delta,
        float yOffset)
    {
        aliveIds.Add(id);
        var target = WorldToScreenCenter(pos) + new Vector2(0f, yOffset);
        if (!cache.TryGetValue(id, out var current))
        {
            cache[id] = target;
            if (previousCache is not null)
                previousCache[id] = target;
            return;
        }

        if (previousCache is not null)
            previousCache[id] = current;
        cache[id] = current.Lerp(target, 1f - Mathf.Exp(-RenderSmoothing * delta));
    }

    private static void RemoveStaleRenderPositions(
        Dictionary<int, Vector2> cache,
        Dictionary<int, Vector2>? previousCache,
        HashSet<int> aliveIds)
    {
        var staleIds = new List<int>();
        foreach (var id in cache.Keys)
            if (!aliveIds.Contains(id))
                staleIds.Add(id);

        foreach (var staleId in staleIds)
        {
            cache.Remove(staleId);
            previousCache?.Remove(staleId);
        }
    }

    private Vector2 GetSmoothedEntityCenter(Dictionary<int, Vector2> cache, int entityId, Vec3i pos, float yOffset)
    {
        if (cache.TryGetValue(entityId, out var drawPos))
            return drawPos;

        drawPos = WorldToScreenCenter(pos) + new Vector2(0f, yOffset);
        cache[entityId] = drawPos;
        return drawPos;
    }

    private Vector2 ResolveSmoothedEntityCenter(int entityId, Vec3i pos, float yOffset)
    {
        if (_dwarfRenderPositions.ContainsKey(entityId))
            return GetSmoothedEntityCenter(_dwarfRenderPositions, entityId, pos, 8f);

        if (_creatureRenderPositions.ContainsKey(entityId))
            return GetSmoothedEntityCenter(_creatureRenderPositions, entityId, pos, 10f);

        if (_itemRenderPositions.ContainsKey(entityId))
            return GetSmoothedEntityCenter(_itemRenderPositions, entityId, pos, 8f);

        return GetSmoothedEntityCenter(_dwarfRenderPositions, entityId, pos, yOffset);
    }

    private Vector2? ResolveSmoothedEntityCenterForFx(int entityId, Vec3i pos)
    {
        if (_dwarfRenderPositions.ContainsKey(entityId))
            return GetSmoothedEntityCenter(_dwarfRenderPositions, entityId, pos, 8f);

        if (_creatureRenderPositions.ContainsKey(entityId))
            return GetSmoothedEntityCenter(_creatureRenderPositions, entityId, pos, 10f);

        if (_itemRenderPositions.ContainsKey(entityId))
            return GetSmoothedEntityCenter(_itemRenderPositions, entityId, pos, 8f);

        return null;
    }

    private Vector2 GetSmoothedItemCenter(Item item)
    {
        var basePos = GetSmoothedEntityCenter(_itemRenderPositions, item.Id, item.Position.Position, 8f);
        if (item.CarriedByEntityId < 0)
            return basePos;

        var bob = Mathf.Sin((float)Time.GetTicksMsec() / 110f + item.Id) * 2.5f;
        return basePos + new Vector2(14f, -18f + bob);
    }

    private static Vector2 ResolveEntityMotionVector(Dictionary<int, Vector2> previousCache, int entityId, Vector2 currentPos)
    {
        if (!previousCache.TryGetValue(entityId, out var previousPos))
            return Vector2.Zero;

        return currentPos - previousPos;
    }

    private WaterEffectStyle ResolveCreatureWaterEffectStyle(Creature creature)
    {
        if (_creatureWaterEffectStyles.TryGetValue(creature.DefId, out var cached))
            return cached;

        var resolved = ResolveCreatureWaterEffectStyleFromDef(creature.DefId);
        _creatureWaterEffectStyles[creature.DefId] = resolved;
        return resolved;
    }

    private WaterEffectStyle ResolveCreatureWaterEffectStyleFromDef(string creatureDefId)
    {
        var def = _data?.Creatures.GetOrNull(creatureDefId);
        var likelyAquatic =
            (def?.IsAquatic() == true) ||
            CreatureDefTagExtensions.IsLikelyAquaticId(creatureDefId);

        if (likelyAquatic)
            return AquaticCreatureWaterEffectStyle;

        if (def?.Tags.Contains(TagIds.Large) == true)
            return LargeCreatureWaterEffectStyle;

        if (def?.Tags.Contains(TagIds.Pet) == true)
            return PetCreatureWaterEffectStyle;

        return CreatureWaterEffectStyle;
    }

    private enum WaterWakePattern : byte
    {
        Default = 0,
        SwimV = 1,
    }

    private readonly record struct WaterEffectStyle(
        float RippleScale,
        float BubbleScale,
        float WakeScale,
        float SubmergeScale,
        float MotionThreshold,
        bool SuppressBubbles,
        WaterWakePattern WakePattern);

    private void UpdateTileCacheIfNeeded()
    {
        if (_map is null)
            return;

        var nextVisibleBounds = CalculateVisibleTileBounds();
        if (nextVisibleBounds != _visibleTileBounds)
        {
            _visibleTileBounds = nextVisibleBounds;
            _tileCacheDirty = true;
            _dirtyVisibleTiles.Clear();
        }

        if (_tileCacheDirty)
        {
            RebuildVisibleTileCache();
            _tileCacheDirty = false;
            return;
        }

        if (_dirtyVisibleTiles.Count == 0)
            return;

        foreach (var pos in _dirtyVisibleTiles)
            RefreshCachedTile(pos);

        _dirtyVisibleTiles.Clear();
    }

    private void RebuildVisibleTileCache()
    {
        if (_map is null)
            return;

        _tileCache.Clear();
        _groundTransitionCache.Clear();
        var maxX = GetVisibleMaxX();
        var maxY = GetVisibleMaxY();
        for (int x = _visibleTileBounds.Position.X; x <= maxX; x++)
        for (int y = _visibleTileBounds.Position.Y; y <= maxY; y++)
        {
            RefreshCachedTile(new Vec3i(x, y, _currentZ));
        }
    }

    // ── Mode hints ─────────────────────────────────────────────────────────

    private string GetModeHint()
    {
        var mode = _input?.CurrentMode ?? InputMode.Select;
        var baseHint = UiText.ModeHint(mode);

        // Show live selection dimensions while dragging
        var sel = _input?.GetSelectionRect();
        if (sel.HasValue && mode != InputMode.Select && mode != InputMode.BuildingPreview)
        {
            var (from, to) = sel.Value;
            int w = Mathf.Abs(to.X - from.X) + 1;
            int h = Mathf.Abs(to.Y - from.Y) + 1;

            int count = 0;
            for (int tx = Mathf.Min(from.X, to.X); tx <= Mathf.Max(from.X, to.X); tx++)
            for (int ty = Mathf.Min(from.Y, to.Y); ty <= Mathf.Max(from.Y, to.Y); ty++)
            {
                TryGetTileAtCurrentZ(tx, ty, out var tile);
                if (IsApplicable(mode, tile)) count++;
            }

            return $"{baseHint}   ·   {w}×{h}  ({count} tiles)";
        }

        return baseHint;
    }

    private static bool IsApplicable(InputMode mode, WorldTileData? tile) => mode switch
    {
        InputMode.DesignateClear    => tile is { IsPassable: false } && tile.Value.TileDefId != TileDefIds.Empty,
        InputMode.DesignateMine     => tile is { IsPassable: false },
        InputMode.DesignateCutTrees => tile?.TileDefId == TileDefIds.Tree,
        InputMode.DesignateCancel   => tile is { IsDesignated: true },
        InputMode.StockpileZone     => tile is { IsPassable: true } && tile.Value.TileDefId != TileDefIds.Empty,
        InputMode.BuildingPreview   => tile is { IsPassable: true } && tile.Value.TileDefId != TileDefIds.Empty,
        _                           => tile is not null,
    };

    // ── Camera ────────────────────────────────────────────────────────────

    private void HandleCameraMovement(double delta)
    {
        if (_camera is null) return;
        var dir = Vector2.Zero;
        if (Input.IsActionPressed("ui_left"))  dir.X -= 1f;
        if (Input.IsActionPressed("ui_right")) dir.X += 1f;
        if (Input.IsActionPressed("ui_up"))    dir.Y -= 1f;
        if (Input.IsActionPressed("ui_down"))  dir.Y += 1f;
        if (dir != Vector2.Zero)
        {
            _camera.Position += dir.Normalized() * (float)delta * PanSpeed / _camera.Zoom.X;
            InvalidateTileCache();
        }
    }

    private void ApplyZoom(float factor)
    {
        if (_camera is null) return;
        var next = (_camera.Zoom * factor).Clamp(new Vector2(0.15f, 0.15f), new Vector2(2.5f, 2.5f));
        if (_camera.Zoom == next) return;
        _camera.Zoom = next;
        InvalidateTileCache();
    }

    private void JumpToTile(Vec3i pos)
    {
        _currentZ = pos.Z;
        _focusedLogTile = pos;
        _input?.SetCurrentZ(_currentZ);

        if (_camera is not null)
            _camera.Position = WorldToScreenCenter(pos);

        InvalidateTileCache();
        QueueRedraw();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static Rect2 TileRect(int x, int y)
        => new(x * TileSize, y * TileSize, TileSize, TileSize);

    private static Vector2 WorldToScreenCenter(Vec3i pos)
        => new(pos.X * TileSize + TileSize / 2f, pos.Y * TileSize + TileSize / 2f);

    private static Rect2 SpanRect(int x0, int y0, int x1, int y1)
        => new(Mathf.Min(x0, x1) * TileSize,
               Mathf.Min(y0, y1) * TileSize,
               (Mathf.Abs(x1 - x0) + 1) * TileSize,
               (Mathf.Abs(y1 - y0) + 1) * TileSize);

    private static Rect2 CenteredRect(int x, int y, int width, int height, int yOffset = 0)
        => new(x * TileSize + (TileSize - width) / 2f,
               y * TileSize + (TileSize - height) / 2f + yOffset,
               width,
               height);

    private Rect2I CalculateVisibleTileBounds()
    {
        if (_map is null)
            return new Rect2I();

        var viewRect = GetViewportRect();
        var zoom = _camera?.Zoom ?? Vector2.One;
        var cameraPosition = _camera?.Position ?? Vector2.Zero;
        var worldSize = viewRect.Size / zoom;
        var marginTiles = 2;

        var minWorld = cameraPosition - worldSize / 2f;
        var maxWorld = cameraPosition + worldSize / 2f;

        var minX = Mathf.Clamp((int)Mathf.Floor(minWorld.X / TileSize) - marginTiles, 0, _map.Width - 1);
        var minY = Mathf.Clamp((int)Mathf.Floor(minWorld.Y / TileSize) - marginTiles, 0, _map.Height - 1);
        var maxX = Mathf.Clamp((int)Mathf.Ceil(maxWorld.X / TileSize) + marginTiles, 0, _map.Width - 1);
        var maxY = Mathf.Clamp((int)Mathf.Ceil(maxWorld.Y / TileSize) + marginTiles, 0, _map.Height - 1);

        return new Rect2I(minX, minY, Math.Max(1, maxX - minX + 1), Math.Max(1, maxY - minY + 1));
    }

    private bool TryGetTileAtCurrentZ(int x, int y, out WorldTileData? tile)
    {
        tile = null;
        if (_map is null || x < 0 || y < 0 || x >= _map.Width || y >= _map.Height)
            return false;

        if (_tileCache.TryGetValue((x, y), out var cached))
        {
            tile = cached;
            return true;
        }

        var resolved = _map.GetTile(new Vec3i(x, y, _currentZ));
        if (resolved.TileDefId == TileDefIds.Empty)
            return false;

        tile = resolved;
        return true;
    }

    private void OnTileChanged(TileChangedEvent e)
    {
        if (e.Pos.Z != _currentZ)
            return;

        MarkTileAndNeighborsDirty(e.Pos);
    }

    private void RefreshCachedTile(Vec3i pos)
    {
        if (_map is null || pos.Z != _currentZ)
            return;

        var tile = _map.GetTile(pos);
        if (tile.TileDefId == TileDefIds.Empty)
        {
            _tileCache.Remove((pos.X, pos.Y));
            _groundTransitionCache.Remove((pos.X, pos.Y));
            return;
        }

        _tileCache[(pos.X, pos.Y)] = tile;
        RefreshCachedGroundTransition(pos, tile);
    }

    private void InvalidateTileCache()
    {
        _tileCacheDirty = true;
        _dirtyVisibleTiles.Clear();
        _groundTransitionCache.Clear();
    }

    private void MarkTileAndNeighborsDirty(Vec3i center)
    {
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            var x = center.X + dx;
            var y = center.Y + dy;
            if (!IsWithinVisibleBounds(x, y))
                continue;

            _dirtyVisibleTiles.Add(new Vec3i(x, y, center.Z));
        }
    }

    private void RefreshCachedGroundTransition(Vec3i pos, WorldTileData tile)
    {
        GroundVisualData? baseGround = null;
        var renderTile = new TileRenderData(tile.TileDefId, tile.MaterialId, tile.FluidType, tile.FluidLevel, tile.FluidMaterialId, tile.OreItemDefId);

        if (tile.TileDefId == TileDefIds.Tree)
        {
            baseGround = TerrainGroundResolver.ResolveTreeGroundVisual(
                pos.X,
                pos.Y,
                pos.Z,
                TryGetTileRenderData,
                ResolveGroundTileDefIdFromMaterial);
        }
        else if (TerrainGroundResolver.IsNaturalBlendTileDefId(tile.TileDefId))
        {
            baseGround = TerrainGroundResolver.ResolveGroundVisual(renderTile, ResolveGroundTileDefIdFromMaterial);
        }

        if (baseGround is not { } ground || !ground.IsNaturalBlendTile)
        {
            _groundTransitionCache.Remove((pos.X, pos.Y));
            return;
        }

        var transitions = TerrainTransitionResolver.Resolve(
            ground,
            pos.X,
            pos.Y,
            pos.Z,
            TryGetTileRenderData,
            ResolveGroundTileDefIdFromMaterial);
        _groundTransitionCache[(pos.X, pos.Y)] = transitions;
    }

    private int GetVisibleMaxX() => _visibleTileBounds.Position.X + _visibleTileBounds.Size.X - 1;

    private int GetVisibleMaxY() => _visibleTileBounds.Position.Y + _visibleTileBounds.Size.Y - 1;

    private bool IsWithinVisibleBounds(int x, int y)
        => x >= _visibleTileBounds.Position.X
        && x <= GetVisibleMaxX()
        && y >= _visibleTileBounds.Position.Y
        && y <= GetVisibleMaxY();

}

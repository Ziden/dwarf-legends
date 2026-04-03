using System;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using DwarfFortress.GodotClient.Rendering;
using Godot;

using WorldTileData = DwarfFortress.GameLogic.World.TileData;

namespace DwarfFortress.GodotClient.App;

public partial class GameRoot : Node
{
    private const float DefaultCameraFocusTile = 24f;
    private const float DefaultCameraSize = 24f;

    private GameSimulation? _simulation;
    private WorldMap? _map;
    private EntityRegistry? _registry;
    private ItemSystem? _items;
    private BuildingSystem? _buildings;
    private StockpileManager? _stockpiles;
    private DataManager? _data;
    private WorldQuerySystem? _query;
    private SpatialIndexSystem? _spatialIndex;
    private MovementPresentationSystem? _movementPresentation;
    private readonly SimulationLoopController _simulationLoop = new();
    private readonly SimulationProfiler _renderProfiler = new(maxFrames: 300);

    private Camera3D? _camera3D;
    private Vec3i? _focusedLogTile;
    private int _currentZ;
    private bool _world3DDirty = true;
    private Rect2I _world3DVisibleTileBounds;
    private int _world3DVisibleZ;
    private bool _hasWorld3DVisibleState;

    private WorldRender3D? _world3DPlaceholder;

    private InputController? _input;

    private readonly RenderCache _renderCache = new();
    private readonly WorldCamera3DController _worldCamera3D = new();
    private readonly WorldSliceHoverResolver3D _sliceHoverResolver3D = new();
    private GameFeedbackController? _feedback;
    private SelectionPanelRouter? _selectionPanels;

    private TopBar? _topBar;
    private ActionBar? _actionBar;
    private HoverInfoPanel? _hoverInfo;
    private SelectionViewPanel? _selectionView;
    private TileInfoPanel? _tileInfo;
    private DwarfPanel? _dwarfPanel;
    private WorkshopPanel? _workshopPanel;
    private AnnouncementLog? _announcementLog;
    private KnowledgePanel? _knowledgePanel;
    private DebugWindow? _debugWindow;
    private StoryInspectorPanel? _storyInspectorPanel;
    private DebugProfilerMonitors? _debugProfilerMonitors;

    public bool IsSimulationReady => _simulation is not null && _map is not null && _query is not null;
    public string? StartupError { get; private set; }

    public override void _Ready()
    {
        if (PixelArtFactory.UseSprites)
            SpriteRegistry.Load();
        else
            GD.Print("[GameRoot] Sprite feature flag is OFF. Using generated artwork.");

        _camera3D = GetNodeOrNull<Camera3D>("%MainCamera3D");
        _world3DPlaceholder = GetNodeOrNull<WorldRender3D>("%World3DRoot");
        _worldCamera3D.Initialize(_camera3D);
        _worldCamera3D.SetView(new Vector2(DefaultCameraFocusTile, DefaultCameraFocusTile), DefaultCameraSize);

        _input = GetNode<InputController>("%InputController");
        _topBar = GetNode<TopBar>("%TopBar");
        _actionBar = GetNode<ActionBar>("%ActionBar");
        _hoverInfo = GetNode<HoverInfoPanel>("%HoverInfoPanel");
        _selectionView = GetNode<SelectionViewPanel>("%SelectionViewPanel");
        _tileInfo = GetNode<TileInfoPanel>("%TileInfoPanel");
        _dwarfPanel = GetNode<DwarfPanel>("%DwarfPanel");
        _workshopPanel = GetNode<WorkshopPanel>("%WorkshopPanel");
        _announcementLog = GetNode<AnnouncementLog>("%AnnouncementLog");
        _knowledgePanel = GetNodeOrNull<KnowledgePanel>("%KnowledgePanel");
        _debugWindow = GetNodeOrNull<DebugWindow>("%DebugWindow");
        _storyInspectorPanel = GetNodeOrNull<StoryInspectorPanel>("%StoryInspectorPanel");
        _feedback = new GameFeedbackController(this);

        TryStartSimulation();
    }

    public override void _ExitTree()
    {
        _debugProfilerMonitors?.Detach();
    }

    public override void _Process(double delta)
    {
        _worldCamera3D.HandleCameraMovement(delta);
        if (_worldCamera3D.CameraMoved)
            _world3DDirty = true;

        if (_simulation is null)
            return;

        _simulationLoop.Advance(delta);
        _input?.SetCurrentZ(_currentZ);
        _feedback?.Update((float)delta);
        UpdateEntityRenderPositions();

        _renderProfiler.BeginFrame((float)delta);
        try
        {
            Update3DWorldState();
        }
        finally
        {
            _renderProfiler.EndFrame();
        }

        if (_query is not null)
            _topBar?.Refresh(_query.GetTimeView(), _input?.CurrentMode ?? InputMode.Select, GetModeHint());

        if (_query is not null)
            _announcementLog?.Refresh(_query.GetFortressAnnouncements());

        if (_input is not null)
            _hoverInfo?.Refresh(_input.HoveredTile, _currentZ);

        _selectionPanels?.Refresh(_currentZ);
        _workshopPanel?.Refresh();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, ShiftPressed: true } && IsPointerOverUi())
            return;

        if (_worldCamera3D.HandlePointerInput(@event))
        {
            _world3DDirty = true;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                _worldCamera3D.ApplyZoom(0.9f);
                _world3DDirty = true;
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                _worldCamera3D.ApplyZoom(1.1f);
                _world3DDirty = true;
            }
        }

        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
            return;

        if (key.Keycode == Key.Space)
        {
            _actionBar?.TogglePause();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == Key.Q)
        {
            SetCurrentZ(_currentZ - 1);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == Key.E)
        {
            SetCurrentZ(_currentZ + 1);
            GetViewport().SetInputAsHandled();
        }
    }

    private void TryStartSimulation()
    {
        try
        {
            _simulation = ClientSimulationFactory.CreateSimulation();
            _map = _simulation.Context.Get<WorldMap>();
            _registry = _simulation.Context.Get<EntityRegistry>();
            _items = _simulation.Context.Get<ItemSystem>();
            _buildings = _simulation.Context.Get<BuildingSystem>();
            _stockpiles = _simulation.Context.Get<StockpileManager>();
            _data = _simulation.Context.Get<DataManager>();
            _query = _simulation.Context.Get<WorldQuerySystem>();
            _spatialIndex = _simulation.Context.Get<SpatialIndexSystem>();
            _movementPresentation = _simulation.Context.Get<MovementPresentationSystem>();
            _renderCache.Clear();

            if (PixelArtFactory.UseSprites)
                SpriteRegistry.UpsertCreatureVisuals(_data?.SharedContent);

            if (PixelArtFactory.Strict2DArt && _data is not null)
            {
                GD.Print("[GameRoot] Strict 2D art validation is ON.");
                Strict2DArtValidator.ValidateRequiredContent(_data);
            }

            _input?.Setup(_simulation);
            if (_input is not null)
            {
                _input.TileSelectionCommitted -= OnTileSelectionCommitted;
                _input.AreaSelectionCommitted -= OnAreaSelectionCommitted;
                _input.TileSelectionCommitted += OnTileSelectionCommitted;
                _input.AreaSelectionCommitted += OnAreaSelectionCommitted;
            }
            _input?.SetCurrentZ(_currentZ);
            _hoverInfo?.Setup(_simulation);
            _selectionView?.Setup(_simulation);
            _tileInfo?.Setup(_simulation);
            _tileInfo?.SetItemInspector(itemId => _input?.SelectItem(itemId));
            _dwarfPanel?.Setup(_simulation);
            _dwarfPanel?.SetTileNavigator(JumpToTile);
            _dwarfPanel?.SetLinkedTargetNavigator(JumpToLinkedEventTarget);
            _announcementLog?.SetTileNavigator(JumpToTile);
            _workshopPanel?.Setup(_simulation);
            if (_input is not null)
                _actionBar?.Setup(_input, _simulation);
            _simulationLoop.Bind(_simulation, _actionBar);
            _knowledgePanel?.Setup(_simulation);
            _debugWindow?.Setup(_simulation);
            _debugProfilerMonitors ??= new DebugProfilerMonitors();
            _debugProfilerMonitors.Attach(_simulation, _renderProfiler);

            if (_actionBar is not null && _knowledgePanel is not null)
            {
                _actionBar.OnKnowledgePressed += () =>
                {
                    _knowledgePanel.Refresh();
                    _knowledgePanel.Show();
                };
            }

            if (_topBar is not null && _debugWindow is not null)
            {
                _topBar.OnDebugPressed = () =>
                {
                    _debugWindow.ToggleWindow();
                    if (_debugWindow.Visible)
                        _debugWindow.Refresh();
                };
            }

            if (_debugWindow is not null && _storyInspectorPanel is not null)
            {
                _debugWindow.OnStoryPressed = () =>
                {
                    _storyInspectorPanel.ShowGameplayStory(_simulation);
                };
            }

            _feedback?.Bind(_simulation, _map, _query);
            _selectionPanels = new SelectionPanelRouter(_input, _query, _selectionView, _tileInfo, _dwarfPanel, _workshopPanel);

            _world3DPlaceholder?.Reset();
            _world3DPlaceholder?.SetActive(true);
            _hasWorld3DVisibleState = false;
            _world3DDirty = true;
            _worldCamera3D.SyncTransform(_currentZ);
            Update3DWorldState();

            _simulation.EventBus.On<TileChangedEvent>(OnTileChanged);
            _simulation.EventBus.On<StockpileCreatedEvent>(_ => _world3DDirty = true);
            _simulation.EventBus.On<StockpileRemovedEvent>(_ => _world3DDirty = true);
            _announcementLog?.Refresh(_query.GetFortressAnnouncements());
        }
        catch (Exception exception)
        {
            _debugProfilerMonitors?.Detach();
            StartupError = exception.ToString();
            GD.PushError(StartupError);
            _announcementLog?.AddMessage($"Startup failed: {exception.Message}", Colors.Red);
        }
    }

    private void UpdateEntityRenderPositions()
    {
        if (_registry is null)
            return;

        var delta = (float)GetProcessDeltaTime();
        _renderCache.AliveDwarfIds.Clear();
        _renderCache.AliveCreatureIds.Clear();
        _renderCache.AliveItemIds.Clear();

        foreach (var dwarf in _registry.GetAlive<Dwarf>())
        {
            _renderCache.UpdateEntityRenderPosition(
                dwarf.Id,
                dwarf.Position.Position,
                delta,
                8f,
                _renderCache.DwarfPositions,
                _renderCache.DwarfPreviousPositions,
                _renderCache.AliveDwarfIds);
        }

        foreach (var creature in _registry.GetAlive<Creature>())
        {
            _renderCache.UpdateEntityRenderPosition(
                creature.Id,
                creature.Position.Position,
                delta,
                10f,
                _renderCache.CreaturePositions,
                _renderCache.CreaturePreviousPositions,
                _renderCache.AliveCreatureIds);
        }

        if (_items is not null)
        {
            foreach (var item in _items.GetAllItems())
            {
                _renderCache.UpdateEntityRenderPosition(
                    item.Id,
                    item.Position.Position,
                    delta,
                    8f,
                    _renderCache.ItemPositions,
                    null,
                    _renderCache.AliveItemIds);
            }
        }

        _renderCache.RemoveStaleRenderPositions(_renderCache.DwarfPositions, _renderCache.DwarfPreviousPositions, _renderCache.AliveDwarfIds);
        _renderCache.RemoveStaleRenderPositions(_renderCache.CreaturePositions, _renderCache.CreaturePreviousPositions, _renderCache.AliveCreatureIds);
        _renderCache.RemoveStaleRenderPositions(_renderCache.ItemPositions, null, _renderCache.AliveItemIds);
    }

    private string GetModeHint()
    {
        var mode = _input?.CurrentMode ?? InputMode.Select;
        var baseHint = UiText.ModeHint(mode);
        var selection = _input?.GetSelectionRect();
        if (!selection.HasValue || mode == InputMode.Select || mode == InputMode.BuildingPreview)
            return baseHint;

        var (from, to) = selection.Value;
        var width = Mathf.Abs(to.X - from.X) + 1;
        var height = Mathf.Abs(to.Y - from.Y) + 1;

        var applicableCount = 0;
        for (var tileX = from.X; tileX <= to.X; tileX++)
        {
            for (var tileY = from.Y; tileY <= to.Y; tileY++)
            {
                if (TryGetTileAtCurrentZ(tileX, tileY, out var tile) && IsApplicable(mode, tile))
                    applicableCount++;
            }
        }

        return $"{baseHint}   Â·   {width}Ã—{height}  ({applicableCount} tiles)";
    }

    private static bool IsApplicable(InputMode mode, WorldTileData? tile) => mode switch
    {
        InputMode.DesignateClear => tile is { IsPassable: false } && tile.Value.TileDefId != TileDefIds.Empty,
        InputMode.DesignateMine => tile is { IsPassable: false },
        InputMode.DesignateCutTrees => tile?.TileDefId == TileDefIds.Tree,
        InputMode.DesignateCancel => tile is { IsDesignated: true },
        InputMode.StockpileZone => tile is { IsPassable: true } && tile.Value.TileDefId != TileDefIds.Empty,
        InputMode.BuildingPreview => tile is { IsPassable: true } && tile.Value.TileDefId != TileDefIds.Empty,
        _ => tile is not null,
    };

    private void JumpToTile(Vec3i pos)
    {
        SetCurrentZ(pos.Z, refreshImmediately: false);
        _worldCamera3D.JumpToTile(pos);
        _focusedLogTile = pos;
        _world3DDirty = true;
        Update3DWorldState();
    }

    private void SetCurrentZ(int nextZ, bool refreshImmediately = true)
    {
        var maxZ = Mathf.Max(0, (_map?.Depth ?? 1) - 1);
        var clampedZ = Mathf.Clamp(nextZ, 0, maxZ);
        if (_currentZ == clampedZ)
            return;

        _currentZ = clampedZ;
        _input?.SetCurrentZ(_currentZ);
        _world3DDirty = true;
        if (refreshImmediately)
            Update3DWorldState();
    }

    private bool JumpToLinkedEventTarget(EventLogLinkTarget linkedTarget)
    {
        switch (linkedTarget.Type)
        {
            case EventLogLinkType.Item:
                if (_items?.TryGetItem(linkedTarget.Id, out var item) == true && item is not null)
                {
                    JumpToTile(item.Position.Position);
                    _input?.TrySelectItem(item.Id);
                    return true;
                }
                break;

            case EventLogLinkType.Entity:
                if (_registry?.TryGetById(linkedTarget.Id)?.Components.TryGet<PositionComponent>() is { } position)
                {
                    JumpToTile(position.Position);
                    if (_input?.TrySelectDwarf(linkedTarget.Id) != true)
                        _input?.TrySelectCreature(linkedTarget.Id);
                    return true;
                }
                break;
        }

        return false;
    }

    private bool TryGetTileAtCurrentZ(int x, int y, out WorldTileData? tile)
    {
        tile = null;
        if (_map is null || x < 0 || y < 0 || x >= _map.Width || y >= _map.Height)
            return false;

        var resolvedTile = _map.GetTile(new Vec3i(x, y, _currentZ));
        if (resolvedTile.TileDefId == TileDefIds.Empty)
            return false;

        tile = resolvedTile;
        return true;
    }

    private void OnTileChanged(TileChangedEvent tileChanged)
    {
        if (ShouldRefresh3DForTileChange(tileChanged.Pos))
            _world3DDirty = true;
    }

    private void Update3DWorldState()
    {
        if (_camera3D is null || _map is null)
            return;

        var visibleTileBounds = new Rect2I();
        ProfileRenderSystem("render3d_visibility", 20, () =>
        {
            _worldCamera3D.SyncTransform(_currentZ);
            visibleTileBounds = _worldCamera3D.CalculateVisibleTileBounds(GetViewport(), _map);
            Track3DVisibleTileBounds(visibleTileBounds);

            if (_world3DPlaceholder?.TryResolveHoveredBillboardTarget(_camera3D, GetViewport(), out var hoveredTile, out var hoverSelectionMode) == true)
                _input?.UseExternalHoveredTile(hoveredTile, hoverSelectionMode);
            else if (_sliceHoverResolver3D.TryResolveHoveredTile(_camera3D, GetViewport(), _map, _currentZ, out hoveredTile))
                _input?.UseExternalHoveredTile(hoveredTile, HoverSelectionMode.QueryTile);
            else
                _input?.UseExternalHoveredTile(_input?.HoveredTile ?? Vector2I.Zero, HoverSelectionMode.QueryTile);
        });

        if (_world3DDirty || (_world3DPlaceholder?.HasPendingChunkBuilds ?? false))
        {
            ProfileRenderSystem("render3d_sync_slice", 21, () =>
                _world3DPlaceholder?.SyncSlice(_map, _buildings, _stockpiles, _data, _currentZ, visibleTileBounds, _renderProfiler));
            _world3DDirty = _world3DPlaceholder?.HasPendingChunkBuilds ?? false;
        }

        ProfileRenderSystem("render3d_dynamic_state", 22, () =>
            _world3DPlaceholder?.SyncDynamicState(
                _camera3D,
                _map,
                _query,
                _registry,
                _items,
                _spatialIndex,
                _movementPresentation,
                _data,
                _input,
                _renderCache,
                _feedback,
                _currentZ,
                visibleTileBounds,
                _focusedLogTile,
                _simulationLoop.PresentationTimeSeconds,
                _renderProfiler));
    }

    private void ProfileRenderSystem(string systemId, int updateOrder, Action action)
    {
        _renderProfiler.BeginSystem(systemId, updateOrder);
        try
        {
            action();
        }
        finally
        {
            _renderProfiler.EndSystem();
        }
    }

    private void Track3DVisibleTileBounds(Rect2I visibleTileBounds)
    {
        if (_hasWorld3DVisibleState
            && _world3DVisibleZ == _currentZ
            && _world3DVisibleTileBounds == visibleTileBounds)
        {
            return;
        }

        _world3DVisibleTileBounds = visibleTileBounds;
        _world3DVisibleZ = _currentZ;
        _hasWorld3DVisibleState = true;
        _world3DDirty = true;
    }

    private bool ShouldRefresh3DForTileChange(Vec3i position)
    {
        if (!_hasWorld3DVisibleState)
            return true;

        if (position.Z != _world3DVisibleZ)
            return false;

        return position.X >= _world3DVisibleTileBounds.Position.X
            && position.X < _world3DVisibleTileBounds.Position.X + _world3DVisibleTileBounds.Size.X
            && position.Y >= _world3DVisibleTileBounds.Position.Y
            && position.Y < _world3DVisibleTileBounds.Position.Y + _world3DVisibleTileBounds.Size.Y;
    }

    private bool IsPointerOverUi()
    {
        var hovered = GetViewport().GuiGetHoveredControl();
        return hovered is not null && hovered.MouseFilter != Control.MouseFilterEnum.Ignore;
    }

    private void OnTileSelectionCommitted(Vec3i position)
        => _feedback?.TriggerTileSelectionPulse(position);

    private void OnAreaSelectionCommitted(Vec3i from, Vec3i to)
        => _feedback?.TriggerAreaSelectionPulse(from, to);
}

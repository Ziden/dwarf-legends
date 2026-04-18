using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DwarfFortress.WorldGen.Analysis;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.History;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Local;
using DwarfFortress.WorldGen.Maps;
using DwarfFortress.WorldGen.Regions;
using DwarfFortress.WorldGen.World;
using DwarfFortress.GameLogic.World;
using Godot;

namespace DwarfFortress.GodotClient.WorldGen;

public partial class WorldGenViewerRoot : Node2D
{
    private readonly record struct DeltaHistogram(
        int Total,
        int FarBelow,
        int Below,
        int Aligned,
        int Above,
        int FarAbove,
        float MeanDelta,
        float MeanAbsDelta);

    private enum ViewMode
    {
        World,
        Region,
        Local,
    }

    private enum OverlayMode
    {
        None,
        Elevation,
        FlowAccumulation,
        ForestCover,
        Relief,
        MountainCover,
        Temperature,
        Moisture,
        Aridity,
        SoilDepth,
        VegetationSuitability,
        ParentForestDelta,
        ParentMountainDelta,
        ParentMoistureDelta,
        RiverContinuity,
        RoadContinuity,
        LocalSurfaceContinuity,
        LocalWaterContinuity,
        LocalEcologyContinuity,
        RiverOrder,
        HistoryTerritory,
    }

    private const int LeftPanelWidth = 270;
    private const int RightPanelWidth = 300;
    private const int InfoBarHeight = 32;
    private const int WorldCellSize = 26;
    private const int RegionCellSize = 36;
    private const int LocalCellSize = 16;
    private const float ParentDeltaToleranceMin = 0.02f;
    private const float ParentDeltaToleranceMax = 0.30f;
    private const float ParentDeltaToleranceDefault = 0.08f;

    private int _seed = 42;
    private int _worldWidth = 24;
    private int _worldHeight = 24;
    private int _regionWidth = 16;
    private int _regionHeight = 16;
    private int _localWidth = 48;
    private int _localHeight = 48;
    private int _localDepth = 8;
    private bool _historyEnabled = true;
    private int _historyYears = 125;
    private int _pipelineSeedCount = 4;
    private float _parentDeltaTolerance = ParentDeltaToleranceDefault;

    private int _selectedWorldX = 12;
    private int _selectedWorldY = 12;
    private int _selectedRegionX = 8;
    private int _selectedRegionY = 8;
    private int _currentZ;
    private ViewMode _mode = ViewMode.World;
    private OverlayMode _overlayMode = OverlayMode.None;

    private GeneratedWorldMap? _worldMap;
    private GeneratedRegionMap? _regionMap;
    private GeneratedEmbarkMap? _localMap;
    private GeneratedEmbarkMap? _northLocal;
    private GeneratedEmbarkMap? _eastLocal;
    private GeneratedEmbarkMap? _southLocal;
    private GeneratedEmbarkMap? _westLocal;
    private GeneratedRegionMap? _northRegion;
    private GeneratedRegionMap? _eastRegion;
    private GeneratedRegionMap? _southRegion;
    private GeneratedRegionMap? _westRegion;
    private HistorySimulator.HistorySimulationSession? _historySession;
    private GeneratedWorldHistoryTimeline? _historyTimeline;
    private WorldPipelineReport? _pipelineReport;
    private int _historyYearIndex;
    private bool _historyAutoPlay;
    private float _historyPlaybackAccumulator;
    private bool _pipelineDiagnosticsRunning;

    private readonly WorldLayerGenerator _worldGenerator = new();
    private readonly RegionLayerGenerator _regionGenerator = new();
    private readonly LocalLayerGenerator _localGenerator = new();
    private readonly HistorySimulator _historySimulator = new();
    private readonly Dictionary<string, Color> _historyOwnerColorCache = new(StringComparer.OrdinalIgnoreCase);

    private SpinBox? _seedInput;
    private Button? _regenerateButton;
    private Button? _randomizeButton;
    private Button? _worldViewButton;
    private Button? _regionViewButton;
    private Button? _localViewButton;
    private OptionButton? _overlayModeSelect;
    private Label? _zLabel;
    private HSlider? _zSlider;
    private VBoxContainer? _statsBox;
    private Label? _infoLabel;
    private Label? _historyYearLabel;
    private CheckButton? _historyEnabledToggle;
    private SpinBox? _historyYearsInput;
    private HSlider? _historyYearSlider;
    private Button? _historyStepButton;
    private Button? _historyPlayButton;
    private Button? _historyResetButton;
    private Button? _storyInspectorButton;
    private RichTextLabel? _historyFeedText;
    private Button? _pipelineRunButton;
    private SpinBox? _pipelineSeedCountInput;
    private Label? _pipelineStatusLabel;
    private Label? _parentDeltaThresholdLabel;
    private HSlider? _parentDeltaThresholdSlider;
    private Label? _parentDeltaLegendLabel;
    private StoryInspectorPanel? _storyInspectorPanel;

    public override void _Ready()
    {
        _seedInput = GetNode<SpinBox>("%SeedInput");
        _regenerateButton = GetNode<Button>("%RegenerateBtn");
        _randomizeButton = GetNode<Button>("%RandomizeBtn");
        _worldViewButton = GetNode<Button>("%WorldViewBtn");
        _regionViewButton = GetNode<Button>("%RegionViewBtn");
        _localViewButton = GetNode<Button>("%LocalViewBtn");
        _overlayModeSelect = GetNode<OptionButton>("%OverlayModeSelect");
        _zLabel = GetNode<Label>("%ZLabel");
        _zSlider = GetNode<HSlider>("%ZSlider");
        _statsBox = GetNode<VBoxContainer>("%StatsBox");
        _infoLabel = GetNode<Label>("%InfoLabel");
        _historyYearLabel = GetNode<Label>("%HistoryYearLabel");
        _historyEnabledToggle = GetNode<CheckButton>("%HistoryEnabledToggle");
        _historyYearsInput = GetNode<SpinBox>("%HistoryYearsInput");
        _historyYearSlider = GetNode<HSlider>("%HistoryYearSlider");
        _historyStepButton = GetNode<Button>("%HistoryStepBtn");
        _historyPlayButton = GetNode<Button>("%HistoryPlayBtn");
        _historyResetButton = GetNode<Button>("%HistoryResetBtn");
        _storyInspectorButton = GetNode<Button>("%StoryInspectorBtn");
        _historyFeedText = GetNode<RichTextLabel>("%HistoryFeedText");
        _pipelineRunButton = GetNode<Button>("%RunPipelineBtn");
        _pipelineSeedCountInput = GetNode<SpinBox>("%PipelineSeedCountInput");
        _pipelineStatusLabel = GetNode<Label>("%PipelineStatusLabel");
        _parentDeltaThresholdLabel = GetNode<Label>("%ParentDeltaThresholdLabel");
        _parentDeltaThresholdSlider = GetNode<HSlider>("%ParentDeltaThresholdSlider");
        _parentDeltaLegendLabel = GetNode<Label>("%ParentDeltaLegendLabel");
        _storyInspectorPanel = GetNodeOrNull<StoryInspectorPanel>("%StoryInspectorPanel");

        _seedInput!.MinValue = 0;
        _seedInput.MaxValue = 999999;
        _seedInput.Value = _seed;
        _seedInput.ValueChanged += OnSeedChanged;

        _regenerateButton!.Pressed += Regenerate;
        _randomizeButton!.Pressed += RandomizeAndRegenerate;

        _worldViewButton!.Pressed += () => SetMode(ViewMode.World);
        _regionViewButton!.Pressed += () => SetMode(ViewMode.Region);
        _localViewButton!.Pressed += () => SetMode(ViewMode.Local);

        _overlayModeSelect!.Clear();
        _overlayModeSelect.AddItem("None");
        _overlayModeSelect.AddItem("Elevation");
        _overlayModeSelect.AddItem("Flow Accumulation");
        _overlayModeSelect.AddItem("Forest Cover");
        _overlayModeSelect.AddItem("Relief");
        _overlayModeSelect.AddItem("Mountain Cover");
        _overlayModeSelect.AddItem("Temperature");
        _overlayModeSelect.AddItem("Moisture");
        _overlayModeSelect.AddItem("Aridity");
        _overlayModeSelect.AddItem("Soil Depth");
        _overlayModeSelect.AddItem("Vegetation Suitability");
        _overlayModeSelect.AddItem("Parent Forest Delta");
        _overlayModeSelect.AddItem("Parent Mountain Delta");
        _overlayModeSelect.AddItem("Parent Moisture Delta");
        _overlayModeSelect.AddItem("River Continuity");
        _overlayModeSelect.AddItem("Road Continuity");
        _overlayModeSelect.AddItem("Local Surface Continuity");
        _overlayModeSelect.AddItem("Local Water Continuity");
        _overlayModeSelect.AddItem("Local Ecology Continuity");
        _overlayModeSelect.AddItem("River Order");
        _overlayModeSelect.AddItem("History Territory");
        _overlayModeSelect.Selected = (int)_overlayMode;
        _overlayModeSelect.ItemSelected += OnOverlayModeChanged;

        _parentDeltaThresholdSlider!.MinValue = ParentDeltaToleranceMin;
        _parentDeltaThresholdSlider.MaxValue = ParentDeltaToleranceMax;
        _parentDeltaThresholdSlider.Step = 0.01f;
        _parentDeltaThresholdSlider.Value = _parentDeltaTolerance;
        _parentDeltaThresholdSlider.ValueChanged += OnParentDeltaThresholdChanged;
        SyncParentDeltaThresholdLabel();

        GetNode<Button>("%BackBtn").Pressed +=
            () => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");

        _zSlider!.MinValue = 0;
        _zSlider.MaxValue = _localDepth - 1;
        _zSlider.Step = 1;
        _zSlider.Value = _currentZ;
        _zSlider.ValueChanged += OnZChanged;

        _historyYearSlider!.MinValue = 0;
        _historyYearSlider.MaxValue = 0;
        _historyYearSlider.Step = 1;
        _historyYearSlider.Value = 0;
        _historyYearSlider.ValueChanged += OnHistoryYearChanged;

        _historyEnabledToggle!.ButtonPressed = _historyEnabled;
        _historyEnabledToggle.Toggled += OnHistoryEnabledToggled;
        _historyYearsInput!.MinValue = 1;
        _historyYearsInput.MaxValue = 2000;
        _historyYearsInput.Step = 1;
        _historyYearsInput.Value = _historyYears;
        _historyYearsInput.ValueChanged += OnHistoryYearsChanged;

        _historyStepButton!.Pressed += () => StepHistoryYear(1);
        _historyPlayButton!.Pressed += ToggleHistoryPlayback;
        _historyResetButton!.Pressed += ResetHistoryPlayback;
        _storyInspectorButton!.Pressed += OpenStoryInspector;

        _pipelineSeedCountInput!.MinValue = 1;
        _pipelineSeedCountInput.MaxValue = 24;
        _pipelineSeedCountInput.Step = 1;
        _pipelineSeedCountInput.Value = _pipelineSeedCount;
        _pipelineSeedCountInput.ValueChanged += OnPipelineSeedCountChanged;
        _pipelineRunButton!.Pressed += RunPipelineDiagnostics;
        UpdatePipelineStatusLabel();
        UpdateParentDeltaControlsVisibility();

        Regenerate();
    }

    public override void _Process(double delta)
    {
        if (!_historyAutoPlay || _historySession is null)
            return;

        _historyPlaybackAccumulator += (float)delta;
        if (_historyPlaybackAccumulator < 0.25f)
            return;

        _historyPlaybackAccumulator = 0f;
        if (_historySession.IsCompleted)
        {
            _historyAutoPlay = false;
            if (_historyPlayButton is not null)
                _historyPlayButton.Text = "Play";
            return;
        }

        StepHistoryYear(1);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.R)
            {
                Regenerate();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (key.Keycode == Key.O)
            {
                CycleOverlayMode();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (key.Keycode == Key.P)
            {
                RunPipelineDiagnostics();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (key.Keycode == Key.Escape)
            {
                GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
                GetViewport().SetInputAsHandled();
                return;
            }

            if (_mode == ViewMode.Region && IsParentDeltaOverlay(_overlayMode))
            {
                if (key.Keycode is Key.Bracketleft or Key.KpSubtract or Key.Minus)
                {
                    SetParentDeltaTolerance(_parentDeltaTolerance - 0.01f);
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (key.Keycode is Key.Bracketright or Key.KpAdd or Key.Equal)
                {
                    SetParentDeltaTolerance(_parentDeltaTolerance + 0.01f);
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }
        }

        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouseButton)
            return;

        var (origin, cellSize) = GetMapLayout();
        var localPosition = mouseButton.Position - origin;
        var col = (int)Mathf.Floor(localPosition.X / cellSize);
        var row = (int)Mathf.Floor(localPosition.Y / cellSize);

        switch (_mode)
        {
            case ViewMode.World when col >= 0 && col < _worldWidth && row >= 0 && row < _worldHeight:
                _selectedWorldX = col;
                _selectedWorldY = row;
                GenerateRegionAndLocal();
                RefreshStats();          // show info + button, stay on World view
                QueueRedraw();           // update selection highlight
                GetViewport().SetInputAsHandled();
                break;

            case ViewMode.Region when col >= 0 && col < _regionWidth && row >= 0 && row < _regionHeight:
                _selectedRegionX = col;
                _selectedRegionY = row;
                GenerateLocal();
                RefreshStats();
                QueueRedraw();
                GetViewport().SetInputAsHandled();
                break;

            case ViewMode.Local when col >= 0 && col < _localWidth && row >= 0 && row < _localHeight:
                ShowLocalTileInfo(col, row);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    public override void _Draw()
    {
        if (_worldMap is null)
            return;

        switch (_mode)
        {
            case ViewMode.World:
                DrawWorldMap();
                break;
            case ViewMode.Region:
                DrawRegionMap();
                break;
            case ViewMode.Local:
                DrawLocalMap();
                break;
        }
    }

    private void DrawWorldMap()
    {
        var (origin, _) = GetMapLayout();
        var maxFlow = ComputeWorldMaxFlowAccumulation();

        for (var y = 0; y < _worldHeight; y++)
        for (var x = 0; x < _worldWidth; x++)
        {
            var tile = _worldMap!.GetTile(x, y);
            var rect = CellRect(origin, x, y, WorldCellSize);
            var forestLevel = ForestCoverageLevel(tile.ForestCover);
            var mountainLevel = MountainCoverageLevel(tile.MountainCover);

            DrawTextureRect(PixelArtFactory.GetWorldTile(tile.MacroBiomeId, forestLevel, mountainLevel), rect, false);
            DrawWorldOverlay(rect, tile, x, y, maxFlow);
            if (tile.HasRiver)
                DrawWorldRiverCell(rect, tile);
            if (tile.HasRoad)
                DrawWorldRoadCell(rect, tile);

            DrawRect(rect, new Color(0f, 0f, 0f, 0.20f), false, 1f);
        }

        DrawRect(
            CellRect(origin, _selectedWorldX, _selectedWorldY, WorldCellSize),
            PixelArtFactory.GetWorldGenSelectionColor(),
            false,
            2.5f);

        if (_infoLabel is not null)
        {
            var snapshot = GetCurrentHistoryYear();
            var targetYears = _historySession?.TargetYears ?? (_historyTimeline?.Years.Count ?? 0);
            var currentYear = snapshot?.Year ?? 0;
            var historyStatus = !_historyEnabled
                ? "History: off"
                : targetYears <= 0
                ? "History: n/a"
                : $"History Year {currentYear}/{targetYears}";
            if (_historyEnabled && _historySession is not null && _historySession.IsCompleted && targetYears > 0)
                historyStatus += " (complete)";
            _infoLabel.Text = $"World view | Overlay: {GetOverlayLabel()} | {historyStatus} | Click tile to select";
        }
    }

    private void DrawWorldRiverCell(Rect2 rect, GeneratedWorldTile tile)
    {
        var center = rect.GetCenter();
        var riverColor = PixelArtFactory.GetWorldGenRiverColor(0.86f);
        var width = Mathf.Max(1.75f, rect.Size.X * 0.14f);
        DrawCircle(center, width * 0.58f, riverColor);

        if (WorldRiverEdgeMask.Has(tile.RiverEdges, WorldRiverEdges.North))
            DrawLine(center, new Vector2(center.X, rect.Position.Y), riverColor, width);
        if (WorldRiverEdgeMask.Has(tile.RiverEdges, WorldRiverEdges.East))
            DrawLine(center, new Vector2(rect.End.X, center.Y), riverColor, width);
        if (WorldRiverEdgeMask.Has(tile.RiverEdges, WorldRiverEdges.South))
            DrawLine(center, new Vector2(center.X, rect.End.Y), riverColor, width);
        if (WorldRiverEdgeMask.Has(tile.RiverEdges, WorldRiverEdges.West))
            DrawLine(center, new Vector2(rect.Position.X, center.Y), riverColor, width);
    }

    private void DrawWorldRoadCell(Rect2 rect, GeneratedWorldTile tile)
    {
        var center = rect.GetCenter();
        var roadColor = PixelArtFactory.GetWorldGenRoadColor(0.84f);
        var width = Mathf.Max(1.55f, rect.Size.X * 0.11f);
        DrawCircle(center, width * 0.52f, roadColor);

        if (WorldRoadEdgeMask.Has(tile.RoadEdges, WorldRoadEdges.North))
            DrawLine(center, new Vector2(center.X, rect.Position.Y), roadColor, width);
        if (WorldRoadEdgeMask.Has(tile.RoadEdges, WorldRoadEdges.East))
            DrawLine(center, new Vector2(rect.End.X, center.Y), roadColor, width);
        if (WorldRoadEdgeMask.Has(tile.RoadEdges, WorldRoadEdges.South))
            DrawLine(center, new Vector2(center.X, rect.End.Y), roadColor, width);
        if (WorldRoadEdgeMask.Has(tile.RoadEdges, WorldRoadEdges.West))
            DrawLine(center, new Vector2(rect.Position.X, center.Y), roadColor, width);
    }

    private void DrawRegionMap()
    {
        if (_regionMap is null)
            return;

        var (origin, _) = GetMapLayout();
        var maxDischarge = ComputeRegionMaxDischarge();

        for (var y = 0; y < _regionHeight; y++)
        for (var x = 0; x < _regionWidth; x++)
        {
            var tile = _regionMap.GetTile(x, y);
            var rect = CellRect(origin, x, y, RegionCellSize);
            var patternVariant = ComputeRegionPatternVariant(_regionMap, x, y);

            DrawTextureRect(PixelArtFactory.GetRegionTile(
                tile.BiomeVariantId,
                tile.VegetationDensity,
                tile.Slope / 255f,
                tile.Groundwater,
                tile.HasRiver,
                tile.HasLake,
                tile.RiverEdges,
                tile.HasRoad,
                tile.RoadEdges,
                tile.TemperatureBand,
                tile.MoistureBand,
                tile.FlowAccumulationBand,
                _regionMap.ParentMacroBiomeId,
                tile.SurfaceClassId,
                patternVariant), rect, false);
            DrawRegionOverlay(rect, tile, x, y, maxDischarge);
            DrawRect(rect, new Color(0f, 0f, 0f, 0.18f), false, 1f);

            if (tile.HasLake && !tile.HasRiver)
                DrawRect(rect, PixelArtFactory.GetWorldGenLakeColor(0.50f));

            if (tile.HasSettlement)
                DrawCircle(rect.GetCenter(), RegionCellSize * 0.22f, PixelArtFactory.GetWorldGenSettlementColor(0.92f));
        }

        DrawRect(
            CellRect(origin, _selectedRegionX, _selectedRegionY, RegionCellSize),
            PixelArtFactory.GetWorldGenSelectionColor(),
            false,
            2.5f);

        if (_infoLabel is not null)
        {
            if (IsParentDeltaOverlay(_overlayMode))
            {
                _infoLabel.Text = $"Region view | Overlay: {GetOverlayLabel()} | Tolerance: +/-{_parentDeltaTolerance:0.00} | Click cell to select";
            }
            else
            {
                _infoLabel.Text = $"Region view | Overlay: {GetOverlayLabel()} | Click cell to select";
            }
        }
    }

    private void DrawRiverNetworkCell(Vector2 origin, int x, int y)
    {
        var rect = CellRect(origin, x, y, RegionCellSize);
        var center = rect.GetCenter();
        var riverColor = PixelArtFactory.GetWorldGenRiverColor(0.86f);
        var width = Mathf.Max(2.5f, RegionCellSize * 0.18f);
        var tile = _regionMap!.GetTile(x, y);

        DrawCircle(center, width * 0.52f, riverColor);

        if (RegionRiverEdgeMask.Has(tile.RiverEdges, RegionRiverEdges.North))
            DrawLine(center, new Vector2(center.X, rect.Position.Y), riverColor, width);
        if (RegionRiverEdgeMask.Has(tile.RiverEdges, RegionRiverEdges.South))
            DrawLine(center, new Vector2(center.X, rect.End.Y), riverColor, width);
        if (RegionRiverEdgeMask.Has(tile.RiverEdges, RegionRiverEdges.West))
            DrawLine(center, new Vector2(rect.Position.X, center.Y), riverColor, width);
        if (RegionRiverEdgeMask.Has(tile.RiverEdges, RegionRiverEdges.East))
            DrawLine(center, new Vector2(rect.End.X, center.Y), riverColor, width);
    }

    private void DrawRoadNetworkCell(Vector2 origin, int x, int y)
    {
        var rect = CellRect(origin, x, y, RegionCellSize);
        var center = rect.GetCenter();
        var roadColor = PixelArtFactory.GetWorldGenRoadColor(0.82f);
        var width = Mathf.Max(2f, RegionCellSize * 0.14f);
        var tile = _regionMap!.GetTile(x, y);

        DrawCircle(center, width * 0.38f, roadColor);

        if (RegionRoadEdgeMask.Has(tile.RoadEdges, RegionRoadEdges.North))
            DrawLine(center, new Vector2(center.X, rect.Position.Y), roadColor, width);
        if (RegionRoadEdgeMask.Has(tile.RoadEdges, RegionRoadEdges.South))
            DrawLine(center, new Vector2(center.X, rect.End.Y), roadColor, width);
        if (RegionRoadEdgeMask.Has(tile.RoadEdges, RegionRoadEdges.West))
            DrawLine(center, new Vector2(rect.Position.X, center.Y), roadColor, width);
        if (RegionRoadEdgeMask.Has(tile.RoadEdges, RegionRoadEdges.East))
            DrawLine(center, new Vector2(rect.End.X, center.Y), roadColor, width);
    }

    private void DrawWorldOverlay(Rect2 rect, GeneratedWorldTile tile, int x, int y, float maxFlow)
    {
        switch (_overlayMode)
        {
            case OverlayMode.Elevation:
                DrawRect(rect, HeatColor(tile.ElevationBand, 0.44f));
                break;
            case OverlayMode.FlowAccumulation:
                var flowNorm = NormalizeFlow(tile.FlowAccumulation, maxFlow);
                DrawRect(rect, HeatColor(flowNorm, 0.50f));
                break;
            case OverlayMode.ForestCover:
                DrawRect(rect, HeatColor(tile.ForestCover, 0.50f));
                break;
            case OverlayMode.Relief:
                DrawRect(rect, HeatColor(tile.Relief, 0.50f));
                break;
            case OverlayMode.MountainCover:
                DrawRect(rect, HeatColor(tile.MountainCover, 0.50f));
                break;
            case OverlayMode.Temperature:
                DrawRect(rect, TemperatureColor(tile.TemperatureBand, 0.52f));
                break;
            case OverlayMode.Moisture:
                DrawRect(rect, MoistureColor(tile.MoistureBand, 0.52f));
                break;
            case OverlayMode.Aridity:
                DrawRect(rect, AridityColor(ComputeWorldAridity(tile), 0.52f));
                break;
            case OverlayMode.SoilDepth:
                DrawRect(rect, HeatColor((tile.MoistureBand * 0.45f) + (tile.DrainageBand * 0.55f), 0.50f));
                break;
            case OverlayMode.VegetationSuitability:
                var worldSuitability = Mathf.Clamp(
                    (tile.ForestCover * 0.44f) +
                    (tile.MoistureBand * 0.30f) +
                    ((1f - tile.MountainCover) * 0.16f) +
                    ((1f - Mathf.Abs((tile.TemperatureBand * 2f) - 1f)) * 0.10f),
                    0f,
                    1f);
                DrawRect(rect, HeatColor(worldSuitability, 0.50f));
                break;
            case OverlayMode.RiverContinuity:
                var mismatches = CountWorldRiverMismatches(x, y, tile);
                if (mismatches > 0)
                    DrawRect(rect, PixelArtFactory.GetWorldGenMismatchColor(0.45f));
                else if (tile.HasRiver)
                    DrawRect(rect, PixelArtFactory.GetWorldGenResolvedColor(0.20f));
                break;
            case OverlayMode.RiverOrder:
                if (tile.HasRiver)
                    DrawRect(rect, HeatColor(Mathf.Clamp(tile.RiverOrder / 8f, 0f, 1f), 0.50f));
                break;
            case OverlayMode.RoadContinuity:
                var roadMismatches = CountWorldRoadMismatches(x, y, tile);
                if (roadMismatches > 0)
                    DrawRect(rect, PixelArtFactory.GetWorldGenMismatchColor(0.45f));
                else if (tile.HasRoad)
                    DrawRect(rect, PixelArtFactory.GetWorldGenRoadColor(0.30f));
                break;
            case OverlayMode.HistoryTerritory:
                var snapshot = GetCurrentHistoryYear();
                if (snapshot is not null &&
                    snapshot.TryGetOwner(new WorldCoord(x, y), out var ownerCivilizationId))
                {
                    var ownerColor = ResolveHistoryOwnerColor(ownerCivilizationId);
                    DrawRect(rect, new Color(ownerColor.R, ownerColor.G, ownerColor.B, 0.48f));
                }
                break;
        }
    }

    private void DrawRegionOverlay(Rect2 rect, GeneratedRegionTile tile, int x, int y, float maxDischarge)
    {
        switch (_overlayMode)
        {
            case OverlayMode.Elevation:
                DrawRect(rect, HeatColor(tile.Slope / 255f, 0.44f));
                break;
            case OverlayMode.Temperature:
                DrawRect(rect, TemperatureColor(tile.TemperatureBand, 0.52f));
                break;
            case OverlayMode.Moisture:
                DrawRect(rect, MoistureColor(tile.MoistureBand, 0.52f));
                break;
            case OverlayMode.Aridity:
                DrawRect(rect, AridityColor(ComputeRegionAridity(tile), 0.52f));
                break;
            case OverlayMode.FlowAccumulation:
                var norm = tile.FlowAccumulationBand;
                if (tile.HasRiver)
                    norm = Mathf.Max(norm, Mathf.Clamp(tile.RiverDischarge / Mathf.Max(1f, maxDischarge), 0f, 1f));
                else if (tile.HasLake)
                    norm = Mathf.Max(norm, 0.15f);
                if (norm > 0f)
                    DrawRect(rect, HeatColor(norm, 0.50f));
                break;
            case OverlayMode.SoilDepth:
                DrawRect(rect, HeatColor(tile.SoilDepth, 0.50f));
                break;
            case OverlayMode.VegetationSuitability:
                DrawRect(rect, HeatColor(tile.VegetationSuitability, 0.50f));
                break;
            case OverlayMode.ParentForestDelta:
                if (_regionMap is not null)
                {
                    var localForestSignal = ResolveRegionForestSignal(tile);
                    var delta = localForestSignal - _regionMap.ParentForestCover;
                    DrawRect(rect, DeltaColor(delta, _parentDeltaTolerance, 0.56f));
                }
                break;
            case OverlayMode.ParentMountainDelta:
                if (_regionMap is not null)
                {
                    var localMountainSignal = ResolveRegionMountainSignal(tile);
                    var delta = localMountainSignal - _regionMap.ParentMountainCover;
                    DrawRect(rect, DeltaColor(delta, _parentDeltaTolerance, 0.56f));
                }
                break;
            case OverlayMode.ParentMoistureDelta:
                if (_regionMap is not null)
                {
                    var delta = tile.MoistureBand - _regionMap.ParentMoistureBand;
                    DrawRect(rect, DeltaColor(delta, _parentDeltaTolerance, 0.56f));
                }
                break;
            case OverlayMode.RiverContinuity:
                var hasRiverMismatch = IsRegionBorderRiverMismatch(x, y, tile, out _);
                if (hasRiverMismatch)
                    DrawRect(rect, PixelArtFactory.GetWorldGenMismatchColor(0.45f));
                else if (IsRegionBorderCell(x, y) && tile.HasRiver)
                    DrawRect(rect, PixelArtFactory.GetWorldGenResolvedColor(0.20f));
                break;
            case OverlayMode.RoadContinuity:
                var hasRoadMismatch = IsRegionBorderRoadMismatch(x, y, tile);
                if (hasRoadMismatch)
                    DrawRect(rect, PixelArtFactory.GetWorldGenMismatchColor(0.45f));
                else if (IsRegionBorderCell(x, y) && tile.HasRoad)
                    DrawRect(rect, PixelArtFactory.GetWorldGenResolvedColor(0.20f));
                break;
            case OverlayMode.RiverOrder:
                if (tile.HasRiver)
                    DrawRect(rect, HeatColor(Mathf.Clamp(tile.RiverOrder / 8f, 0f, 1f), 0.50f));
                break;
        }
    }

    private float ComputeWorldMaxFlowAccumulation()
    {
        if (_worldMap is null)
            return 1f;

        var max = 1f;
        for (var y = 0; y < _worldHeight; y++)
        for (var x = 0; x < _worldWidth; x++)
        {
            var flow = _worldMap.GetTile(x, y).FlowAccumulation;
            if (flow > max)
                max = flow;
        }

        return max;
    }

    private float ComputeRegionMaxDischarge()
    {
        if (_regionMap is null)
            return 1f;

        var max = 1f;
        for (var y = 0; y < _regionHeight; y++)
        for (var x = 0; x < _regionWidth; x++)
        {
            var discharge = _regionMap.GetTile(x, y).RiverDischarge;
            if (discharge > max)
                max = discharge;
        }

        return max;
    }

    private static float NormalizeFlow(float flow, float maxFlow)
    {
        var numerator = Mathf.Log(flow + 1f);
        var denominator = Mathf.Log(Mathf.Max(1f, maxFlow) + 1f);
        if (denominator <= 0f)
            return 0f;
        return Mathf.Clamp(numerator / denominator, 0f, 1f);
    }

    private static byte ForestCoverageLevel(float value)
    {
        var clamped = Mathf.Clamp(value, 0f, 1f);
        if (clamped >= 0.48f)
            return 2;
        if (clamped >= 0.14f)
            return 1;
        return 0;
    }

    private static byte MountainCoverageLevel(float value)
    {
        var clamped = Mathf.Clamp(value, 0f, 1f);
        if (clamped >= 0.60f)
            return 2;
        if (clamped >= 0.30f)
            return 1;
        return 0;
    }

    private static byte ComputeRegionPatternVariant(GeneratedRegionMap regionMap, int x, int y)
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + regionMap.Seed;
            hash = (hash * 31) + regionMap.WorldCoord.X;
            hash = (hash * 31) + regionMap.WorldCoord.Y;
            hash = (hash * 31) + x;
            hash = (hash * 31) + y;
            hash ^= hash >> 13;
            hash *= 1274126177;
            hash ^= hash >> 16;
            return (byte)((uint)hash % 32u);
        }
    }

    private static Color HeatColor(float value, float alpha)
    {
        var t = Mathf.Clamp(value, 0f, 1f);
        var greenMid = 1f - Mathf.Abs((t * 2f) - 1f);
        var r = Mathf.Lerp(0.08f, 0.94f, t);
        var g = Mathf.Lerp(0.24f, 0.84f, greenMid);
        var b = Mathf.Lerp(0.92f, 0.12f, t);
        return new Color(r, g, b, alpha);
    }

    private static Color TemperatureColor(float value, float alpha)
    {
        var t = Mathf.Clamp(value, 0f, 1f);
        var hue = Mathf.Lerp(0.62f, 0.02f, t);
        var color = Color.FromHsv(hue, 0.82f, 0.92f);
        return new Color(color.R, color.G, color.B, alpha);
    }

    private static Color MoistureColor(float value, float alpha)
    {
        var t = Mathf.Clamp(value, 0f, 1f);
        var hue = Mathf.Lerp(0.10f, 0.52f, t);
        var color = Color.FromHsv(hue, 0.78f, 0.90f);
        return new Color(color.R, color.G, color.B, alpha);
    }

    private static Color AridityColor(float value, float alpha)
    {
        var t = Mathf.Clamp(value, 0f, 1f);
        var hue = Mathf.Lerp(0.56f, 0.03f, t);
        var color = Color.FromHsv(hue, 0.80f, 0.92f);
        return new Color(color.R, color.G, color.B, alpha);
    }

    private static Color DeltaColor(float delta, float tolerance, float alpha)
    {
        var clampedTolerance = Mathf.Clamp(tolerance, ParentDeltaToleranceMin, ParentDeltaToleranceMax);
        var excess = MathF.Max(0f, Mathf.Abs(delta) - clampedTolerance);
        var magnitude = Mathf.Clamp(excess / 0.30f, 0f, 1f);
        if (Mathf.Abs(delta) <= clampedTolerance)
            return PixelArtFactory.GetWorldGenResolvedColor(alpha * 0.16f);

        var tunedAlpha = alpha * (0.26f + (0.74f * magnitude));
        if (delta > 0f)
            return PixelArtFactory.GetWorldGenDeltaPositiveColor(tunedAlpha);

        return PixelArtFactory.GetWorldGenDeltaNegativeColor(tunedAlpha);
    }

    private static float ResolveRegionForestSignal(GeneratedRegionTile tile)
    {
        var hydroBoost = (tile.HasRiver ? 0.06f : 0f) + (tile.HasLake ? 0.10f : 0f);
        return Mathf.Clamp(
            (tile.VegetationDensity * 0.60f) +
            (tile.VegetationSuitability * 0.30f) +
            (tile.Groundwater * 0.10f) +
            hydroBoost,
            0f,
            1f);
    }

    private static float ResolveRegionMountainSignal(GeneratedRegionTile tile)
    {
        var rockyBoost =
            RegionBiomeVariantIds.IsHighlandVariant(tile.BiomeVariantId) ||
            RegionBiomeVariantIds.IsRockyVariant(tile.BiomeVariantId)
                ? 0.15f
                : 0f;
        return Mathf.Clamp(
            ((tile.Slope / 255f) * 0.78f) +
            ((1f - tile.SoilDepth) * 0.12f) +
            rockyBoost,
            0f,
            1f);
    }

    private static float ComputeWorldAridity(GeneratedWorldTile tile)
    {
        return Mathf.Clamp(
            ((1f - tile.MoistureBand) * 0.72f) +
            (tile.TemperatureBand * 0.28f) +
            (Mathf.Max(0f, tile.Relief - 0.52f) * 0.22f),
            0f,
            1f);
    }

    private static float ComputeRegionAridity(GeneratedRegionTile tile)
    {
        return Mathf.Clamp(
            ((1f - tile.MoistureBand) * 0.68f) +
            (tile.TemperatureBand * 0.24f) +
            ((tile.Slope / 255f) * 0.16f),
            0f,
            1f);
    }

    private int CountWorldRiverMismatches(int x, int y, GeneratedWorldTile tile)
    {
        var mismatches = 0;
        mismatches += CheckWorldEdgeMismatch(x, y, tile, 0, -1, WorldRiverEdges.North, WorldRiverEdges.South);
        mismatches += CheckWorldEdgeMismatch(x, y, tile, 1, 0, WorldRiverEdges.East, WorldRiverEdges.West);
        mismatches += CheckWorldEdgeMismatch(x, y, tile, 0, 1, WorldRiverEdges.South, WorldRiverEdges.North);
        mismatches += CheckWorldEdgeMismatch(x, y, tile, -1, 0, WorldRiverEdges.West, WorldRiverEdges.East);
        return mismatches;
    }

    private int CheckWorldEdgeMismatch(
        int x,
        int y,
        GeneratedWorldTile tile,
        int dx,
        int dy,
        WorldRiverEdges localEdge,
        WorldRiverEdges oppositeEdge)
    {
        var localHasEdge = WorldRiverEdgeMask.Has(tile.RiverEdges, localEdge);
        var nx = x + dx;
        var ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= _worldWidth || ny >= _worldHeight)
            return localHasEdge ? 1 : 0;

        var neighbor = _worldMap!.GetTile(nx, ny);
        var neighborHasEdge = WorldRiverEdgeMask.Has(neighbor.RiverEdges, oppositeEdge);
        if (localHasEdge != neighborHasEdge)
            return 1;

        if (localHasEdge && neighborHasEdge)
        {
            var delta = Mathf.Abs(tile.RiverDischarge - neighbor.RiverDischarge);
            if (delta > 0.75f)
                return 1;
        }

        return 0;
    }

    private int CountWorldRoadMismatches(int x, int y, GeneratedWorldTile tile)
    {
        var mismatches = 0;
        mismatches += CheckWorldRoadEdgeMismatch(x, y, tile, 0, -1, WorldRoadEdges.North, WorldRoadEdges.South);
        mismatches += CheckWorldRoadEdgeMismatch(x, y, tile, 1, 0, WorldRoadEdges.East, WorldRoadEdges.West);
        mismatches += CheckWorldRoadEdgeMismatch(x, y, tile, 0, 1, WorldRoadEdges.South, WorldRoadEdges.North);
        mismatches += CheckWorldRoadEdgeMismatch(x, y, tile, -1, 0, WorldRoadEdges.West, WorldRoadEdges.East);
        return mismatches;
    }

    private int CheckWorldRoadEdgeMismatch(
        int x,
        int y,
        GeneratedWorldTile tile,
        int dx,
        int dy,
        WorldRoadEdges localEdge,
        WorldRoadEdges oppositeEdge)
    {
        var localHasEdge = WorldRoadEdgeMask.Has(tile.RoadEdges, localEdge);
        var nx = x + dx;
        var ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= _worldWidth || ny >= _worldHeight)
            return localHasEdge ? 1 : 0;

        var neighbor = _worldMap!.GetTile(nx, ny);
        var neighborHasEdge = WorldRoadEdgeMask.Has(neighbor.RoadEdges, oppositeEdge);
        return localHasEdge != neighborHasEdge ? 1 : 0;
    }

    private static bool IsRegionBorderCell(int x, int y, int width, int height)
        => x == 0 || y == 0 || x == width - 1 || y == height - 1;

    private bool IsRegionBorderCell(int x, int y)
        => IsRegionBorderCell(x, y, _regionWidth, _regionHeight);

    private bool IsRegionBorderMismatch(int x, int y, GeneratedRegionTile tile, out float maxDischargeDelta)
    {
        var riverMismatch = IsRegionBorderRiverMismatch(x, y, tile, out maxDischargeDelta);
        var roadMismatch = IsRegionBorderRoadMismatch(x, y, tile);
        return riverMismatch || roadMismatch;
    }

    private bool IsRegionBorderRiverMismatch(int x, int y, GeneratedRegionTile tile, out float maxDischargeDelta)
    {
        maxDischargeDelta = 0f;
        var mismatch = false;

        if (y == 0)
        {
            mismatch |= CheckRegionBoundaryContract(tile, _northRegion, x, _regionHeight - 1, RegionRiverEdges.North, RegionRiverEdges.South, ref maxDischargeDelta);
        }
        if (x == _regionWidth - 1)
        {
            mismatch |= CheckRegionBoundaryContract(tile, _eastRegion, 0, y, RegionRiverEdges.East, RegionRiverEdges.West, ref maxDischargeDelta);
        }
        if (y == _regionHeight - 1)
        {
            mismatch |= CheckRegionBoundaryContract(tile, _southRegion, x, 0, RegionRiverEdges.South, RegionRiverEdges.North, ref maxDischargeDelta);
        }
        if (x == 0)
        {
            mismatch |= CheckRegionBoundaryContract(tile, _westRegion, _regionWidth - 1, y, RegionRiverEdges.West, RegionRiverEdges.East, ref maxDischargeDelta);
        }

        return mismatch;
    }

    private bool IsRegionBorderRoadMismatch(int x, int y, GeneratedRegionTile tile)
    {
        var mismatch = false;

        if (y == 0)
            mismatch |= CheckRegionBoundaryRoadContract(tile, _northRegion, x, _regionHeight - 1, RegionRoadEdges.North, RegionRoadEdges.South);
        if (x == _regionWidth - 1)
            mismatch |= CheckRegionBoundaryRoadContract(tile, _eastRegion, 0, y, RegionRoadEdges.East, RegionRoadEdges.West);
        if (y == _regionHeight - 1)
            mismatch |= CheckRegionBoundaryRoadContract(tile, _southRegion, x, 0, RegionRoadEdges.South, RegionRoadEdges.North);
        if (x == 0)
            mismatch |= CheckRegionBoundaryRoadContract(tile, _westRegion, _regionWidth - 1, y, RegionRoadEdges.West, RegionRoadEdges.East);

        return mismatch;
    }

    private static bool CheckRegionBoundaryContract(
        GeneratedRegionTile local,
        GeneratedRegionMap? neighborMap,
        int neighborX,
        int neighborY,
        RegionRiverEdges localEdge,
        RegionRiverEdges neighborEdge,
        ref float maxDischargeDelta)
    {
        if (neighborMap is null)
            return false;

        var neighbor = neighborMap.GetTile(neighborX, neighborY);
        var localHas = local.HasRiver && RegionRiverEdgeMask.Has(local.RiverEdges, localEdge);
        var neighborHas = neighbor.HasRiver && RegionRiverEdgeMask.Has(neighbor.RiverEdges, neighborEdge);

        if (localHas != neighborHas)
            return true;

        if (!localHas || !neighborHas)
            return false;

        var delta = Mathf.Abs(local.RiverDischarge - neighbor.RiverDischarge);
        if (delta > maxDischargeDelta)
            maxDischargeDelta = delta;
        return delta > 0.75f;
    }

    private static bool CheckRegionBoundaryRoadContract(
        GeneratedRegionTile local,
        GeneratedRegionMap? neighborMap,
        int neighborX,
        int neighborY,
        RegionRoadEdges localEdge,
        RegionRoadEdges neighborEdge)
    {
        if (neighborMap is null)
            return false;

        var neighbor = neighborMap.GetTile(neighborX, neighborY);
        var localHas = local.HasRoad && RegionRoadEdgeMask.Has(local.RoadEdges, localEdge);
        var neighborHas = neighbor.HasRoad && RegionRoadEdgeMask.Has(neighbor.RoadEdges, neighborEdge);
        return localHas != neighborHas;
    }

    private int CountWorldContinuityWarnings()
    {
        if (_worldMap is null)
            return 0;

        var warnings = 0;
        for (var y = 0; y < _worldHeight; y++)
        for (var x = 0; x < _worldWidth; x++)
        {
            var tile = _worldMap.GetTile(x, y);
            if (CountWorldRiverMismatches(x, y, tile) > 0)
                warnings++;
        }

        return warnings;
    }

    private int CountRegionContinuityWarnings()
    {
        if (_regionMap is null)
            return 0;

        var warnings = 0;
        for (var y = 0; y < _regionHeight; y++)
        for (var x = 0; x < _regionWidth; x++)
        {
            if (!IsRegionBorderCell(x, y))
                continue;

            var tile = _regionMap.GetTile(x, y);
            if (IsRegionBorderMismatch(x, y, tile, out _))
                warnings++;
        }

        return warnings;
    }

    private int CountWorldRoadContinuityWarnings()
    {
        if (_worldMap is null)
            return 0;

        var warnings = 0;
        for (var y = 0; y < _worldHeight; y++)
        for (var x = 0; x < _worldWidth; x++)
        {
            var tile = _worldMap.GetTile(x, y);
            if (CountWorldRoadMismatches(x, y, tile) > 0)
                warnings++;
        }

        return warnings;
    }

    private int CountRegionRiverContinuityWarnings()
    {
        if (_regionMap is null)
            return 0;

        var warnings = 0;
        for (var y = 0; y < _regionHeight; y++)
        for (var x = 0; x < _regionWidth; x++)
        {
            if (!IsRegionBorderCell(x, y))
                continue;

            var tile = _regionMap.GetTile(x, y);
            if (IsRegionBorderRiverMismatch(x, y, tile, out _))
                warnings++;
        }

        return warnings;
    }

    private int CountRegionRoadContinuityWarnings()
    {
        if (_regionMap is null)
            return 0;

        var warnings = 0;
        for (var y = 0; y < _regionHeight; y++)
        for (var x = 0; x < _regionWidth; x++)
        {
            if (!IsRegionBorderCell(x, y))
                continue;

            var tile = _regionMap.GetTile(x, y);
            if (IsRegionBorderRoadMismatch(x, y, tile))
                warnings++;
        }

        return warnings;
    }

    private string GetOverlayLabel()
        => _overlayMode switch
        {
            OverlayMode.Elevation => "Elevation",
            OverlayMode.FlowAccumulation => "Flow Accumulation",
            OverlayMode.ForestCover => "Forest Cover",
            OverlayMode.Relief => "Relief",
            OverlayMode.MountainCover => "Mountain Cover",
            OverlayMode.Temperature => "Temperature",
            OverlayMode.Moisture => "Moisture",
            OverlayMode.Aridity => "Aridity",
            OverlayMode.SoilDepth => "Soil Depth",
            OverlayMode.VegetationSuitability => "Vegetation Suitability",
            OverlayMode.ParentForestDelta => "Parent Forest Delta",
            OverlayMode.ParentMountainDelta => "Parent Mountain Delta",
            OverlayMode.ParentMoistureDelta => "Parent Moisture Delta",
            OverlayMode.RiverContinuity => "River Continuity",
            OverlayMode.RoadContinuity => "Road Continuity",
            OverlayMode.LocalSurfaceContinuity => "Local Surface Continuity",
            OverlayMode.LocalWaterContinuity => "Local Water Continuity",
            OverlayMode.LocalEcologyContinuity => "Local Ecology Continuity",
            OverlayMode.RiverOrder => "River Order",
            OverlayMode.HistoryTerritory => "History Territory",
            _ => "None",
        };

    private static int MaxOverlayModeIndex => (int)OverlayMode.HistoryTerritory;

    private void DrawLocalMap()
    {
        if (_localMap is null)
            return;

        var (origin, _) = GetMapLayout();
        for (var y = 0; y < _localHeight; y++)
        for (var x = 0; x < _localWidth; x++)
        {
            var tile = _localMap.GetTile(x, y, _currentZ);
            var rect = CellRect(origin, x, y, LocalCellSize);

            TileRenderHelper.DrawTile(this, rect, new TileRenderData(tile.TileDefId, tile.MaterialId, ToFluidType(tile.FluidType), tile.FluidLevel, null, tile.OreId, tile.PlantDefId, tile.PlantGrowthStage, tile.PlantYieldLevel, tile.PlantSeedLevel), x, y, _currentZ,
                TryGetLocalTileRenderData);

            if (tile.FluidType != GeneratedFluidType.None && tile.FluidLevel > 0)
                DrawRect(rect, PixelArtFactory.GetWorldGenRiverColor(0.08f + tile.FluidLevel / 18f));
            else if (tile.IsAquifer)
                DrawRect(rect, PixelArtFactory.GetAquiferOverlayColor(0.20f));

            DrawLocalOverlay(rect, x, y, tile);
        }

        DrawLocalCreatureSpawnMarkers(origin);

        if (_infoLabel is not null)
            _infoLabel.Text = $"Local map z={_currentZ}/{_localDepth - 1}. Click a tile for details.";
    }

    private void DrawLocalOverlay(Rect2 rect, int x, int y, GeneratedTile tile)
    {
        var targetMismatch = _overlayMode switch
        {
            OverlayMode.LocalSurfaceContinuity => EmbarkBoundaryMismatchKind.SurfaceFamily,
            OverlayMode.LocalWaterContinuity => EmbarkBoundaryMismatchKind.Water,
            OverlayMode.LocalEcologyContinuity => EmbarkBoundaryMismatchKind.Ecology | EmbarkBoundaryMismatchKind.Tree,
            _ => EmbarkBoundaryMismatchKind.None,
        };

        if (targetMismatch == EmbarkBoundaryMismatchKind.None)
            return;
        if (!TryGetLocalBoundaryMismatchKinds(x, y, tile, out var mismatchKinds))
            return;

        if ((mismatchKinds & targetMismatch) != 0)
            DrawRect(rect, PixelArtFactory.GetWorldGenMismatchColor(0.45f));
        else
            DrawRect(rect, PixelArtFactory.GetWorldGenResolvedColor(0.20f));
    }

    private bool TryGetLocalBoundaryMismatchKinds(int x, int y, GeneratedTile tile, out EmbarkBoundaryMismatchKind mismatchKinds)
    {
        mismatchKinds = EmbarkBoundaryMismatchKind.None;
        if (_localMap is null || !EmbarkBoundaryContinuity.IsBoundaryCell(_localMap, x, y))
            return false;

        var comparable = false;

        if (y == 0 && _northLocal is not null)
        {
            comparable = true;
            mismatchKinds |= EmbarkBoundaryContinuity.CompareTiles(tile, _northLocal.GetTile(x, _northLocal.Height - 1, _currentZ));
        }

        if (x == _localWidth - 1 && _eastLocal is not null)
        {
            comparable = true;
            mismatchKinds |= EmbarkBoundaryContinuity.CompareTiles(tile, _eastLocal.GetTile(0, y, _currentZ));
        }

        if (y == _localHeight - 1 && _southLocal is not null)
        {
            comparable = true;
            mismatchKinds |= EmbarkBoundaryContinuity.CompareTiles(tile, _southLocal.GetTile(x, 0, _currentZ));
        }

        if (x == 0 && _westLocal is not null)
        {
            comparable = true;
            mismatchKinds |= EmbarkBoundaryContinuity.CompareTiles(tile, _westLocal.GetTile(_westLocal.Width - 1, y, _currentZ));
        }

        return comparable;
    }

    private int CountLocalComparableBoundaryTiles()
    {
        if (_localMap is null)
            return 0;

        var comparable = 0;
        for (var y = 0; y < _localHeight; y++)
        for (var x = 0; x < _localWidth; x++)
        {
            var tile = _localMap.GetTile(x, y, _currentZ);
            if (TryGetLocalBoundaryMismatchKinds(x, y, tile, out _))
                comparable++;
        }

        return comparable;
    }

    private int CountLocalContinuityWarnings(EmbarkBoundaryMismatchKind mismatchKind)
    {
        if (_localMap is null)
            return 0;

        var warnings = 0;
        for (var y = 0; y < _localHeight; y++)
        for (var x = 0; x < _localWidth; x++)
        {
            var tile = _localMap.GetTile(x, y, _currentZ);
            if (!TryGetLocalBoundaryMismatchKinds(x, y, tile, out var mismatchKinds))
                continue;
            if ((mismatchKinds & mismatchKind) != 0)
                warnings++;
        }

        return warnings;
    }

    private void DrawLocalCreatureSpawnMarkers(Vector2 origin)
    {
        if (_localMap is null)
            return;

        var byCell = new Dictionary<(int X, int Y), Dictionary<string, int>>();
        for (var i = 0; i < _localMap.CreatureSpawns.Count; i++)
        {
            var spawn = _localMap.CreatureSpawns[i];
            if (spawn.Z != _currentZ)
                continue;

            var key = (spawn.X, spawn.Y);
            if (!byCell.TryGetValue(key, out var speciesCounts))
            {
                speciesCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                byCell[key] = speciesCounts;
            }

            speciesCounts.TryGetValue(spawn.CreatureDefId, out var count);
            speciesCounts[spawn.CreatureDefId] = count + 1;
        }

        foreach (var (cell, speciesCounts) in byCell)
        {
            if (speciesCounts.Count == 0)
                continue;

            var total = speciesCounts.Values.Sum();
            var dominant = speciesCounts
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .First();
            var color = CreatureSpawnColor(dominant.Key);

            var rect = CellRect(origin, cell.X, cell.Y, LocalCellSize);
            var center = rect.GetCenter();
            var radius = Mathf.Clamp((LocalCellSize * 0.16f) + ((total - 1) * 0.75f), 2f, LocalCellSize * 0.44f);

            DrawCircle(center, radius + 1f, new Color(0f, 0f, 0f, 0.76f));
            DrawCircle(center, radius, new Color(color.R, color.G, color.B, 0.92f));
            DrawCircle(center, radius * 0.42f, new Color(1f, 1f, 1f, 0.80f));
        }
    }

    private TileRenderData? TryGetLocalTileRenderData(int x, int y, int z)
    {
        if (_localMap is null || x < 0 || y < 0 || z < 0 || x >= _localMap.Width || y >= _localMap.Height || z >= _localMap.Depth)
            return null;

        var tile = _localMap.GetTile(x, y, z);
        return new TileRenderData(tile.TileDefId, tile.MaterialId, ToFluidType(tile.FluidType), tile.FluidLevel, null, tile.OreId, tile.PlantDefId, tile.PlantGrowthStage, tile.PlantYieldLevel, tile.PlantSeedLevel);
    }

    private static FluidType ToFluidType(GeneratedFluidType fluidType)
        => fluidType switch
        {
            GeneratedFluidType.Water => FluidType.Water,
            GeneratedFluidType.Magma => FluidType.Magma,
            _ => FluidType.None,
        };

    private (Vector2 origin, int cellSize) GetMapLayout()
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var drawWidth = viewportSize.X - LeftPanelWidth - RightPanelWidth;
        var drawHeight = viewportSize.Y - InfoBarHeight;

        return _mode switch
        {
            ViewMode.World => (
                new Vector2(
                    LeftPanelWidth + (drawWidth - _worldWidth * WorldCellSize) / 2f,
                    (drawHeight - _worldHeight * WorldCellSize) / 2f),
                WorldCellSize),
            ViewMode.Region => (
                new Vector2(
                    LeftPanelWidth + (drawWidth - _regionWidth * RegionCellSize) / 2f,
                    (drawHeight - _regionHeight * RegionCellSize) / 2f),
                RegionCellSize),
            ViewMode.Local => (
                new Vector2(
                    LeftPanelWidth + (drawWidth - _localWidth * LocalCellSize) / 2f,
                    (drawHeight - _localHeight * LocalCellSize) / 2f),
                LocalCellSize),
            _ => (Vector2.Zero, 1),
        };
    }

    private static Rect2 CellRect(Vector2 origin, int col, int row, int cellSize)
        => new(origin + new Vector2(col * cellSize, row * cellSize), new Vector2(cellSize, cellSize));

    private static Color LocalTileColor(GeneratedTile tile)
        => PixelArtFactory.GetRepresentativeTileColor(tile.TileDefId, tile.MaterialId);

    private static Color CreatureSpawnColor(string creatureDefId)
        => ClientContentQueries.ResolveCreatureViewerColor(creatureDefId) ?? new Color(0.88f, 0.88f, 0.88f);

    private void OnSeedChanged(double value)
    {
        _seed = (int)value;
        Regenerate();
    }

    private void OnZChanged(double value)
    {
        _currentZ = (int)value;
        if (_zLabel is not null)
            _zLabel.Text = $"Z Layer: {_currentZ}";
        RefreshStats();
        QueueRedraw();
    }

    private void OnOverlayModeChanged(long index)
    {
        var mode = Mathf.Clamp((int)index, 0, MaxOverlayModeIndex);
        _overlayMode = (OverlayMode)mode;
        UpdateParentDeltaControlsVisibility();
        RefreshStats();
        QueueRedraw();
    }

    private void CycleOverlayMode()
    {
        var next = ((int)_overlayMode + 1) % (MaxOverlayModeIndex + 1);
        _overlayMode = (OverlayMode)next;
        if (_overlayModeSelect is not null)
            _overlayModeSelect.Selected = next;
        UpdateParentDeltaControlsVisibility();
        RefreshStats();
        QueueRedraw();
    }

    private void RandomizeAndRegenerate()
    {
        _seed = GD.RandRange(1, int.MaxValue - 1);
        if (_seedInput is not null)
            _seedInput.Value = _seed;
        Regenerate();
    }

    private void Regenerate()
    {
        InvalidatePipelineDiagnostics();
        _worldMap = _worldGenerator.Generate(_seed, _worldWidth, _worldHeight);
        GenerateHistoryTimeline();
        _selectedWorldX = Mathf.Clamp(_selectedWorldX, 0, _worldWidth - 1);
        _selectedWorldY = Mathf.Clamp(_selectedWorldY, 0, _worldHeight - 1);
        GenerateRegionAndLocal();
        SetMode(_mode);
    }

    private void GenerateRegionAndLocal()
    {
        if (_worldMap is null)
            return;

        var history = BuildCurrentHistoryForRegionGeneration();
        _regionMap = _regionGenerator.Generate(
            _worldMap,
            new WorldCoord(_selectedWorldX, _selectedWorldY),
            _regionWidth,
            _regionHeight,
            history);
        GenerateNeighborRegions();

        _selectedRegionX = Mathf.Clamp(_selectedRegionX, 0, _regionWidth - 1);
        _selectedRegionY = Mathf.Clamp(_selectedRegionY, 0, _regionHeight - 1);

        GenerateLocal();
    }

    private void GenerateNeighborRegions()
    {
        _northRegion = GenerateNeighborRegion(0, -1);
        _eastRegion = GenerateNeighborRegion(1, 0);
        _southRegion = GenerateNeighborRegion(0, 1);
        _westRegion = GenerateNeighborRegion(-1, 0);
    }

    private GeneratedRegionMap? GenerateNeighborRegion(int dx, int dy)
    {
        if (_worldMap is null)
            return null;

        var wx = _selectedWorldX + dx;
        var wy = _selectedWorldY + dy;
        if (wx < 0 || wy < 0 || wx >= _worldWidth || wy >= _worldHeight)
            return null;

        var history = BuildCurrentHistoryForRegionGeneration();
        return _regionGenerator.Generate(_worldMap, new WorldCoord(wx, wy), _regionWidth, _regionHeight, history);
    }

    private void GenerateLocal()
    {
        if (_regionMap is null)
            return;

        var coord = new RegionCoord(_selectedWorldX, _selectedWorldY, _selectedRegionX, _selectedRegionY);
        var settings = new LocalGenerationSettings(_localWidth, _localHeight, _localDepth);
        _localMap = _localGenerator.Generate(_regionMap, coord, settings);

        _currentZ = Mathf.Clamp(_currentZ, 0, _localDepth - 1);
        if (_zSlider is not null)
        {
            _zSlider.MaxValue = _localDepth - 1;
            _zSlider.Value = _currentZ;
        }
        if (_zLabel is not null)
            _zLabel.Text = $"Z Layer: {_currentZ}";

        GenerateNeighborLocals();
    }

    private void GenerateNeighborLocals()
    {
        _northLocal = GenerateNeighborLocal(0, -1);
        _eastLocal = GenerateNeighborLocal(1, 0);
        _southLocal = GenerateNeighborLocal(0, 1);
        _westLocal = GenerateNeighborLocal(-1, 0);
    }

    private GeneratedEmbarkMap? GenerateNeighborLocal(int dx, int dy)
    {
        if (_regionMap is null)
            return null;

        var worldX = _selectedWorldX;
        var worldY = _selectedWorldY;
        var regionX = _selectedRegionX + dx;
        var regionY = _selectedRegionY + dy;
        GeneratedRegionMap? targetRegion = _regionMap;

        if (regionX < 0)
        {
            targetRegion = _westRegion;
            worldX--;
            regionX = _regionWidth - 1;
        }
        else if (regionX >= _regionWidth)
        {
            targetRegion = _eastRegion;
            worldX++;
            regionX = 0;
        }

        if (regionY < 0)
        {
            targetRegion = _northRegion;
            worldY--;
            regionY = _regionHeight - 1;
        }
        else if (regionY >= _regionHeight)
        {
            targetRegion = _southRegion;
            worldY++;
            regionY = 0;
        }

        if (targetRegion is null)
            return null;

        var coord = new RegionCoord(worldX, worldY, regionX, regionY);
        var settings = new LocalGenerationSettings(_localWidth, _localHeight, _localDepth);
        return _localGenerator.Generate(targetRegion, coord, settings);
    }

    private void GenerateHistoryTimeline()
    {
        if (_worldMap is null)
            return;

        if (!_historyEnabled)
        {
            _historySession = null;
            _historyTimeline = null;
            _historyOwnerColorCache.Clear();
            _historyYearIndex = 0;
            _historyAutoPlay = false;
            _historyPlaybackAccumulator = 0f;
            if (_historyYearSlider is not null)
                _historyYearSlider.Value = 0;
            SyncHistoryControls();
            RefreshStats();
            QueueRedraw();
            return;
        }

        _historySession = _historySimulator.CreateSession(_worldMap, _seed, simulatedYearsOverride: _historyYears);
        RefreshHistoryTimelineFromSession();
        _historyOwnerColorCache.Clear();
        _historyYearIndex = 0;
        if (_historySession.TargetYears > 0 && _historySession.Years.Count == 0)
        {
            _historySession.TryAdvance(out _);
            RefreshHistoryTimelineFromSession();
        }
        _historyAutoPlay = false;
        _historyPlaybackAccumulator = 0f;
        if (_historyYearSlider is not null)
            _historyYearSlider.Value = _historyYearIndex;
        SyncHistoryControls();
        RefreshStats();
        QueueRedraw();
    }

    private HistoryYearSnapshot? GetCurrentHistoryYear()
    {
        if (_historyTimeline is null || _historyTimeline.Years.Count == 0)
            return null;

        var index = Mathf.Clamp(_historyYearIndex, 0, _historyTimeline.Years.Count - 1);
        return _historyTimeline.Years[index];
    }

    private GeneratedWorldHistory? BuildCurrentHistoryForRegionGeneration()
    {
        if (!_historyEnabled)
            return null;

        var snapshot = GetCurrentHistoryYear();
        if (snapshot is null)
            return _historyTimeline?.FinalHistory;

        return GeneratedWorldHistoryProjector.FromSnapshot(snapshot, _seed, _historyTimeline?.FinalHistory);
    }

    private void OpenStoryInspector()
    {
        _storyInspectorPanel?.ShowWorldgenStory(
            BuildCurrentHistoryForRegionGeneration(),
            GetCurrentHistoryYear(),
            _historyTimeline?.Years.Count ?? 0,
            _historySession?.TargetYears ?? 0);
    }

    private void RefreshStoryInspectorIfVisible()
    {
        if (_storyInspectorPanel is null || !_storyInspectorPanel.Visible)
            return;

        _storyInspectorPanel.ShowWorldgenStory(
            BuildCurrentHistoryForRegionGeneration(),
            GetCurrentHistoryYear(),
            _historyTimeline?.Years.Count ?? 0,
            _historySession?.TargetYears ?? 0);
    }

    private void OnHistoryYearChanged(double value)
    {
        if (!_historyEnabled)
        {
            SyncHistoryControls();
            return;
        }

        _historyYearIndex = (int)value;
        if (_worldMap is not null)
            GenerateRegionAndLocal();
        SyncHistoryControls();
        RefreshStats();
        QueueRedraw();
    }

    private void OnHistoryEnabledToggled(bool enabled)
    {
        _historyEnabled = enabled;
        RegenerateHistoryAndDerivedMaps();
    }

    private void OnHistoryYearsChanged(double value)
    {
        var years = Math.Clamp((int)Math.Round(value), 1, 2000);
        if (years == _historyYears)
            return;

        _historyYears = years;
        if (_historyYearsInput is not null && (int)_historyYearsInput.Value != _historyYears)
            _historyYearsInput.Value = _historyYears;

        if (!_historyEnabled)
        {
            SyncHistoryControls();
            return;
        }

        RegenerateHistoryAndDerivedMaps();
    }

    private void OnPipelineSeedCountChanged(double value)
    {
        var seedCount = Math.Clamp((int)Math.Round(value), 1, 24);
        if (seedCount == _pipelineSeedCount)
            return;

        _pipelineSeedCount = seedCount;
        if (_pipelineSeedCountInput is not null && (int)_pipelineSeedCountInput.Value != _pipelineSeedCount)
            _pipelineSeedCountInput.Value = _pipelineSeedCount;

        InvalidatePipelineDiagnostics();
        RefreshStats();
        QueueRedraw();
    }

    private void RunPipelineDiagnostics()
    {
        if (_pipelineDiagnosticsRunning)
            return;

        if (_worldMap is null || _regionMap is null || _localMap is null)
        {
            SetPipelineStatus("Pipeline: unavailable", new Color(0.95f, 0.55f, 0.35f));
            RefreshStats();
            QueueRedraw();
            return;
        }

        _pipelineDiagnosticsRunning = true;
        SyncPipelineControls();
        SetPipelineStatus("Pipeline: running...", new Color(0.75f, 0.75f, 0.75f));

        try
        {
            var seedCount = Math.Clamp(_pipelineSeedCount, 1, 24);
            var seedStart = _seed - (seedCount / 2);
            var sampledRegionsPerWorld = Math.Clamp((_worldWidth * _worldHeight) / 48, 4, 14);

            _pipelineReport = WorldGenAnalyzer.AnalyzePipelineSamples(
                seedStart: seedStart,
                seedCount: seedCount,
                worldWidth: _worldWidth,
                worldHeight: _worldHeight,
                regionWidth: _regionWidth,
                regionHeight: _regionHeight,
                sampledRegionsPerWorld: sampledRegionsPerWorld,
                localWidth: _localWidth,
                localHeight: _localHeight,
                localDepth: _localDepth,
                ensureBiomeCoverage: true,
                maxAdditionalSeeds: 20);

            var passed = _pipelineReport.Budgets.Count(b => b.Passed);
            var total = _pipelineReport.Budgets.Count;
            var statusColor = _pipelineReport.Passed
                ? new Color(0.44f, 0.92f, 0.44f)
                : new Color(0.95f, 0.50f, 0.36f);
            var statusText = _pipelineReport.Passed
                ? $"Pipeline: pass ({passed}/{total})"
                : $"Pipeline: fail ({passed}/{total})";
            SetPipelineStatus(statusText, statusColor);
        }
        catch (Exception ex)
        {
            _pipelineReport = null;
            SetPipelineStatus($"Pipeline: error ({ex.Message})", new Color(0.95f, 0.50f, 0.36f));
        }
        finally
        {
            _pipelineDiagnosticsRunning = false;
            SyncPipelineControls();
            RefreshStats();
            QueueRedraw();
        }
    }

    private void InvalidatePipelineDiagnostics()
    {
        _pipelineReport = null;
        SetPipelineStatus("Pipeline: stale (run check)", new Color(0.85f, 0.75f, 0.40f));
    }

    private void SetPipelineStatus(string text, Color color)
    {
        if (_pipelineStatusLabel is null)
            return;

        _pipelineStatusLabel.Text = text;
        _pipelineStatusLabel.Modulate = color;
    }

    private void SyncPipelineControls()
    {
        if (_pipelineRunButton is not null)
            _pipelineRunButton.Disabled = _pipelineDiagnosticsRunning;
        if (_pipelineSeedCountInput is not null)
            _pipelineSeedCountInput.Editable = !_pipelineDiagnosticsRunning;
    }

    private void UpdatePipelineStatusLabel()
    {
        if (_pipelineStatusLabel is null)
            return;

        _pipelineStatusLabel.Text = "Pipeline: not run";
        _pipelineStatusLabel.Modulate = new Color(0.75f, 0.75f, 0.75f);
        SyncPipelineControls();
    }

    private void ToggleHistoryPlayback()
    {
        if (_historySession is null || _historySession.IsCompleted)
            return;

        _historyAutoPlay = !_historyAutoPlay;
        _historyPlaybackAccumulator = 0f;
        if (_historyPlayButton is not null)
            _historyPlayButton.Text = _historyAutoPlay ? "Pause" : "Play";
    }

    private void ResetHistoryPlayback()
    {
        if (_worldMap is null)
            return;

        _historyAutoPlay = false;
        _historyPlaybackAccumulator = 0f;
        if (_historyPlayButton is not null)
            _historyPlayButton.Text = "Play";

        GenerateHistoryTimeline();
    }

    private void StepHistoryYear(int delta)
    {
        var advanced = false;
        if (delta > 0)
            advanced = TryAdvanceHistoryGenerationYear();

        if (!advanced)
        {
            if (_historyTimeline is null || _historyTimeline.Years.Count == 0)
            {
                SyncHistoryControls();
                RefreshStats();
                QueueRedraw();
                return;
            }

            var nextIndex = Mathf.Clamp(_historyYearIndex + delta, 0, _historyTimeline.Years.Count - 1);
            _historyYearIndex = nextIndex;
            if (_historyYearSlider is not null && (int)_historyYearSlider.Value != _historyYearIndex)
                _historyYearSlider.Value = _historyYearIndex;
        }

        SyncHistoryControls();
        RefreshStats();
        QueueRedraw();
    }

    private bool TryAdvanceHistoryGenerationYear()
    {
        if (_historySession is null || _historySession.IsCompleted)
            return false;
        if (!_historySession.TryAdvance(out _))
            return false;

        RefreshHistoryTimelineFromSession();
        if (_historyTimeline is not null && _historyTimeline.Years.Count > 0)
            _historyYearIndex = _historyTimeline.Years.Count - 1;
        if (_historyYearSlider is not null && (int)_historyYearSlider.Value != _historyYearIndex)
            _historyYearSlider.Value = _historyYearIndex;

        if (_historySession.IsCompleted)
        {
            _historyAutoPlay = false;
            if (_historyPlayButton is not null)
                _historyPlayButton.Text = "Play";
        }

        return true;
    }

    private void RefreshHistoryTimelineFromSession()
    {
        if (_historySession is null)
        {
            _historyTimeline = null;
            return;
        }

        _historyTimeline = new GeneratedWorldHistoryTimeline
        {
            FinalHistory = _historySession.FinalHistory ?? new GeneratedWorldHistory
            {
                Seed = _seed,
                SimulatedYears = _historySession.CurrentYear,
            },
            Years = _historySession.Years.ToArray(),
        };
    }

    private void SyncHistoryControls()
    {
        if (_historyEnabledToggle is not null && _historyEnabledToggle.ButtonPressed != _historyEnabled)
            _historyEnabledToggle.ButtonPressed = _historyEnabled;
        if (_historyYearsInput is not null)
        {
            _historyYearsInput.Editable = _historyEnabled;
            if ((int)_historyYearsInput.Value != _historyYears)
                _historyYearsInput.Value = _historyYears;
        }

        if (!_historyEnabled)
        {
            if (_historyYearSlider is not null)
            {
                _historyYearSlider.MaxValue = 0;
                _historyYearSlider.Editable = false;
            }
            if (_historyStepButton is not null)
                _historyStepButton.Disabled = true;
            if (_historyResetButton is not null)
                _historyResetButton.Disabled = true;
            if (_historyYearLabel is not null)
                _historyYearLabel.Text = "Year: off";
            if (_historyPlayButton is not null)
            {
                _historyPlayButton.Disabled = true;
                _historyPlayButton.Text = "Play";
            }
            return;
        }

        var generatedCount = _historyTimeline?.Years.Count ?? 0;
        var targetYears = _historySession?.TargetYears ?? generatedCount;

        if (_historyYearSlider is not null)
        {
            _historyYearSlider.MaxValue = System.Math.Max(0, generatedCount - 1);
            _historyYearSlider.Editable = generatedCount > 0;
        }

        if (_historyStepButton is not null)
            _historyStepButton.Disabled = _historySession is null || _historySession.IsCompleted;
        if (_historyResetButton is not null)
            _historyResetButton.Disabled = _historySession is null;

        if (_historyYearLabel is not null)
        {
            if (targetYears <= 0)
            {
                _historyYearLabel.Text = "Year: 0/0";
            }
            else
            {
                var snapshot = GetCurrentHistoryYear();
                var year = snapshot?.Year ?? 0;
                _historyYearLabel.Text = $"Year: {year}/{targetYears}";
            }
        }

        if (_historyPlayButton is not null)
        {
            if (_historySession is null || _historySession.IsCompleted)
            {
                _historyPlayButton.Disabled = true;
                _historyPlayButton.Text = "Play";
            }
            else
            {
                _historyPlayButton.Disabled = false;
                _historyPlayButton.Text = _historyAutoPlay ? "Pause" : "Play";
            }
        }
    }

    private void RefreshHistoryFeed()
    {
        if (_historyFeedText is null)
            return;

        var sb = new StringBuilder(4096);
        if (!_historyEnabled)
        {
            sb.AppendLine("History generation is disabled.");
            sb.AppendLine("Enable it from the left panel to generate year-by-year history.");
            _historyFeedText.Text = sb.ToString();
            return;
        }

        if (_historySession is null)
        {
            sb.AppendLine("History session is not initialized.");
            sb.AppendLine("Regenerate the world to initialize history simulation.");
            _historyFeedText.Text = sb.ToString();
            return;
        }

        var generatedYears = _historyTimeline?.Years.Count ?? 0;
        var targetYears = _historySession.TargetYears;
        if (generatedYears == 0)
        {
            sb.AppendLine($"History session ready: 0/{targetYears} years generated.");
            sb.AppendLine("Press Step +1 or Play to begin simulation.");
            _historyFeedText.Text = sb.ToString();
            return;
        }

        var snapshot = GetCurrentHistoryYear() ?? _historyTimeline!.Years[^1];
        var cumulativeEvents = 0;
        if (_historyTimeline is not null)
        {
            var years = _historyTimeline.Years;
            var maxIndex = Mathf.Clamp(_historyYearIndex, 0, years.Count - 1);
            for (var i = 0; i <= maxIndex; i++)
                cumulativeEvents += years[i].Events.Count;
        }

        sb.AppendLine($"Year {snapshot.Year}/{targetYears} | Generated {generatedYears}/{targetYears}");
        sb.AppendLine($"Events (year/total): {snapshot.Events.Count}/{cumulativeEvents}");
        sb.AppendLine($"Civilizations: {snapshot.Civilizations.Count} | Sites: {snapshot.Sites.Count} | Households: {snapshot.Households.Count} | Figures: {snapshot.Figures.Count} | Roads: {snapshot.Roads.Count}");
        sb.AppendLine();

        sb.AppendLine("Recent Events");
        if (snapshot.Events.Count == 0)
        {
            sb.AppendLine("- No major events this year.");
        }
        else
        {
            foreach (var ev in snapshot.Events.Take(18))
                sb.AppendLine($"- [{ev.Type}] {ev.Summary}");
        }

        sb.AppendLine();
        sb.AppendLine("Civilizations");
        if (snapshot.Civilizations.Count == 0)
        {
            sb.AppendLine("- None");
        }
        else
        {
            foreach (var civ in snapshot.Civilizations
                         .OrderByDescending(c => c.TerritoryTiles)
                         .ThenByDescending(c => c.Prosperity)
                         .ThenBy(c => c.CivilizationId, StringComparer.OrdinalIgnoreCase)
                         .Take(12))
            {
                sb.AppendLine(
                    $"- {civ.Name}: territory {civ.TerritoryTiles}, prosperity {civ.Prosperity:F2}, threat {civ.Threat:F2}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Sites");
        if (snapshot.Sites.Count == 0)
        {
            sb.AppendLine("- None");
        }
        else
        {
            foreach (var site in snapshot.Sites
                         .OrderByDescending(s => s.Development)
                         .ThenByDescending(s => s.Security)
                         .ThenBy(s => s.SiteId, StringComparer.OrdinalIgnoreCase)
                         .Take(16))
            {
                sb.AppendLine(
                    $"- {site.Name} ({site.Kind}) @ ({site.Location.X},{site.Location.Y}) dev {site.Development:F2}, sec {site.Security:F2}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Characters");
        if (snapshot.Figures.Count == 0)
        {
            sb.AppendLine("- No historical figures recorded yet.");
        }
        else
        {
            var siteNamesById = snapshot.Sites.ToDictionary(site => site.SiteId, site => site.Name, StringComparer.OrdinalIgnoreCase);
            var aliveCount = snapshot.Figures.Count(figure => figure.IsAlive);
            var founderCount = snapshot.Figures.Count(figure => figure.IsFounder);

            sb.AppendLine($"- Figures: {snapshot.Figures.Count} total | {aliveCount} alive | {founderCount} founders");
            sb.AppendLine($"- Households: {snapshot.Households.Count}");

            foreach (var figure in snapshot.Figures
                         .OrderByDescending(figure => figure.IsFounder)
                         .ThenByDescending(figure => figure.IsAlive)
                         .ThenBy(figure => figure.BirthYear)
                         .ThenBy(figure => figure.Name, StringComparer.OrdinalIgnoreCase)
                         .Take(12))
            {
                var profession = figure.ProfessionId.Replace('_', ' ');
                var siteName = siteNamesById.TryGetValue(figure.CurrentSiteId, out var currentSiteName) && !string.IsNullOrWhiteSpace(currentSiteName)
                    ? currentSiteName
                    : string.IsNullOrWhiteSpace(figure.CurrentSiteId) ? "unknown site" : figure.CurrentSiteId;
                var status = figure.IsAlive ? "alive" : "dead";
                var founderSuffix = figure.IsFounder ? ", founder" : string.Empty;
                sb.AppendLine($"- {figure.Name} ({profession}, {status}{founderSuffix}) @ {siteName}");
            }
        }

        _historyFeedText.Text = sb.ToString();
    }

    private void RegenerateHistoryAndDerivedMaps()
    {
        if (_worldMap is null)
        {
            SyncHistoryControls();
            RefreshStats();
            QueueRedraw();
            return;
        }

        GenerateHistoryTimeline();
        GenerateRegionAndLocal();
        SyncHistoryControls();
        RefreshStats();
        QueueRedraw();
    }

    private Color ResolveHistoryOwnerColor(string ownerCivilizationId)
    {
        if (_historyOwnerColorCache.TryGetValue(ownerCivilizationId, out var cached))
            return cached;

        var hash = ownerCivilizationId.GetHashCode(StringComparison.OrdinalIgnoreCase);
        var hue = Mathf.Abs(hash % 360) / 360f;
        var saturation = 0.65f;
        var value = 0.78f;
        var color = Color.FromHsv(hue, saturation, value);
        _historyOwnerColorCache[ownerCivilizationId] = color;
        return color;
    }

    private void SetMode(ViewMode mode)
    {
        _mode = mode;

        _worldViewButton!.Disabled = mode == ViewMode.World;
        _regionViewButton!.Disabled = mode == ViewMode.Region;
        _localViewButton!.Disabled = mode == ViewMode.Local;

        var showLocalDepth = mode == ViewMode.Local;
        _zLabel!.Visible = showLocalDepth;
        _zSlider!.Visible = showLocalDepth;
        UpdateParentDeltaControlsVisibility();

        RefreshStats();
        QueueRedraw();
    }

    private void RefreshStats()
    {
        if (_statsBox is null)
        {
            RefreshHistoryFeed();
            RefreshStoryInspectorIfVisible();
            return;
        }

        foreach (var child in _statsBox.GetChildren())
            child.QueueFree();

        switch (_mode)
        {
            case ViewMode.World when _worldMap is not null:
                ShowWorldStats();
                break;
            case ViewMode.Region when _regionMap is not null:
                ShowRegionStats();
                break;
            case ViewMode.Local when _localMap is not null:
                ShowLocalStats();
                break;
        }

        RefreshHistoryFeed();
        RefreshStoryInspectorIfVisible();
    }

    private void ShowWorldStats()
    {
        var tile = _worldMap!.GetTile(_selectedWorldX, _selectedWorldY);
        StatHeader("SELECTED WORLD TILE");
        Stat("Overlay", GetOverlayLabel());
        Stat("Position", $"({_selectedWorldX}, {_selectedWorldY})");
        Stat("Biome", tile.MacroBiomeId.Replace('_', ' '));
        Stat("Geology", tile.GeologyProfileId.Replace('_', ' '));
        Stat("Elevation", $"{tile.ElevationBand:F2}");
        Stat("Temperature", $"{tile.TemperatureBand:F2}");
        Stat("Moisture", $"{tile.MoistureBand:F2}");
        Stat("Aridity", $"{ComputeWorldAridity(tile):F2}");
        Stat("Drainage", $"{tile.DrainageBand:F2}");
        Stat("Forest Cover", $"{tile.ForestCover:F2}");
        Stat("Relief", $"{tile.Relief:F2}");
        Stat("Mountain Cover", $"{tile.MountainCover:F2}");
        Stat("Flow Acc", $"{tile.FlowAccumulation:F1}");
        Stat("Discharge", tile.HasRiver ? $"{tile.RiverDischarge:F2}" : "-");
        Stat("Order", tile.HasRiver ? tile.RiverOrder.ToString() : "-");
        Stat("River", tile.HasRiver ? tile.RiverEdges.ToString() : UiText.No);
        Stat("Road", tile.HasRoad ? tile.RoadEdges.ToString() : UiText.No);
        Stat("Faction", $"{tile.FactionPressure:F2}");
        DrillButton("View Region ->", () =>
        {
            SetMode(ViewMode.Region);
        });

        var historySnapshot = GetCurrentHistoryYear();
        StatHeader("HISTORY");
        Stat("History Enabled", _historyEnabled ? "Yes" : "No");
        Stat("Target Years", _historyEnabled ? _historyYears.ToString() : "-");
        var generatedYears = _historyTimeline?.Years.Count ?? 0;
        var targetYears = _historySession?.TargetYears ?? generatedYears;
        if (!_historyEnabled)
        {
            Stat("Status", "Disabled");
        }
        else if (_historySession is null)
        {
            Stat("Status", "No session");
        }
        else if (historySnapshot is null || generatedYears == 0)
        {
            Stat("Year", targetYears <= 0 ? "0/0" : $"0/{targetYears}");
            Stat("Generated", $"{generatedYears}/{targetYears}");
            Stat("Status", targetYears <= 0 ? "Completed" : "Not started");
        }
        else
        {
            var selectedCoord = new WorldCoord(_selectedWorldX, _selectedWorldY);
            var ownerLabel = "Unclaimed";
            if (historySnapshot.TryGetOwner(selectedCoord, out var ownerCivilizationId))
            {
                var ownerCivilization = historySnapshot.Civilizations.FirstOrDefault(civ =>
                    string.Equals(civ.CivilizationId, ownerCivilizationId, System.StringComparison.OrdinalIgnoreCase));
                ownerLabel = ownerCivilization is null
                    ? ownerCivilizationId
                    : $"{ownerCivilization.Name} ({ownerCivilizationId})";
            }

            var historyYears = _historyTimeline?.Years ?? Array.Empty<HistoryYearSnapshot>();
            var cumulativeEvents = 0;
            for (var i = 0; i <= _historyYearIndex && i < historyYears.Count; i++)
                cumulativeEvents += historyYears[i].Events.Count;

            var leadingCivilization = historySnapshot.Civilizations
                .OrderByDescending(civ => civ.TerritoryTiles)
                .ThenBy(civ => civ.CivilizationId, StringComparer.Ordinal)
                .FirstOrDefault();
            var leadingCivilizationLabel = leadingCivilization is null
                ? "-"
                : $"{leadingCivilization.Name} ({leadingCivilization.TerritoryTiles} tiles)";

            Stat("Year", $"{historySnapshot.Year}/{targetYears}");
            Stat("Generated", $"{generatedYears}/{targetYears}");
            Stat("Events (Year)", historySnapshot.Events.Count.ToString());
            Stat("Events (Total)", cumulativeEvents.ToString());
            Stat("Roads (Year)", historySnapshot.Roads.Count.ToString());
            Stat("Avg Prosperity", $"{historySnapshot.AverageProsperity:F2}");
            Stat("Avg Threat", $"{historySnapshot.AverageThreat:F2}");
            Stat("Tile Owner", ownerLabel);
            Stat("Leading Civ", leadingCivilizationLabel);
        }

        StatHeader("WORLD SUMMARY");
        var biomeCounts = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        var rivers = 0;
        var roads = 0;

        for (var y = 0; y < _worldHeight; y++)
        for (var x = 0; x < _worldWidth; x++)
        {
            var worldTile = _worldMap.GetTile(x, y);
            if (worldTile.HasRiver)
                rivers++;
            if (worldTile.HasRoad)
                roads++;

            biomeCounts.TryGetValue(worldTile.MacroBiomeId, out var count);
            biomeCounts[worldTile.MacroBiomeId] = count + 1;
        }

        foreach (var (biomeId, count) in biomeCounts.OrderByDescending(kvp => kvp.Value).ThenBy(kvp => kvp.Key))
            Stat(biomeId.Replace('_', ' '), count.ToString());
        Stat("River Tiles", rivers.ToString());
        Stat("Road Tiles", roads.ToString());
        Stat("River Continuity", CountWorldContinuityWarnings().ToString());
        Stat("Road Continuity", CountWorldRoadContinuityWarnings().ToString());

        StatHeader("PIPELINE DIAGNOSTICS");
        Stat("Status", _pipelineStatusLabel?.Text ?? "Pipeline: not run");
        Stat("Seed Samples", _pipelineSeedCount.ToString());
        if (_pipelineReport is null)
        {
            Stat("Report", "Run Pipeline Check to evaluate world/region/local coherence.");
            return;
        }

        Stat("Evaluated Seeds", _pipelineReport.EvaluatedSeedCount.ToString());
        Stat("Extra Seeds Used", _pipelineReport.AdditionalSeedsUsed.ToString());
        Stat("Coverage Requested", UiText.YesNo(_pipelineReport.BiomeCoverageRequested));
        var passedBudgets = _pipelineReport.Budgets.Count(b => b.Passed);
        Stat("Budgets", $"{passedBudgets}/{_pipelineReport.Budgets.Count}");
        Stat("Macro Alignment", $"{_pipelineReport.RegionParentMacroAlignmentRatio:F3}");
        Stat("Tropical Land Share", $"{_pipelineReport.TropicalLandShare:F3}");
        Stat("Arid Land Share", $"{_pipelineReport.AridLandShare:F3}");
        Stat("Cold Land Share", $"{_pipelineReport.ColdLandShare:F3}");
        Stat("Desert Land Share", $"{_pipelineReport.DesertLandShare:F3}");
        Stat("Forest->Region Veg", $"{_pipelineReport.WorldForestRegionVegetationCorrelation:F3}");
        Stat("Forest->Local Trees", $"{_pipelineReport.WorldForestLocalTreeDensityCorrelation:F3}");
        Stat("Mountain->Region Slope", $"{_pipelineReport.WorldMountainRegionSlopeCorrelation:F3}");
        Stat("Region River Mismatch", $"{_pipelineReport.RegionRiverEdgeMismatchRatio:F3}");
        Stat("Region Road Mismatch", $"{_pipelineReport.RegionRoadEdgeMismatchRatio:F3}");
        Stat("Local Boundary Samples", _pipelineReport.LocalBoundarySampleCount.ToString());
        Stat("Local Surface Mismatch", $"{_pipelineReport.LocalSurfaceBoundaryMismatchRatio:F3}");
        Stat("Local Water Mismatch", $"{_pipelineReport.LocalWaterBoundaryMismatchRatio:F3}");
        Stat("Local Ecology Mismatch", $"{_pipelineReport.LocalEcologyBoundaryMismatchRatio:F3}");
        Stat("Local Tree Mismatch", $"{_pipelineReport.LocalTreeBoundaryMismatchRatio:F3}");
        Stat("Local Band Samples", _pipelineReport.LocalBoundaryBandSampleCount.ToString());
        Stat("Local Band Surface", $"{_pipelineReport.LocalSurfaceBoundaryBandMismatchRatio:F3}");
        Stat("Local Band Water", $"{_pipelineReport.LocalWaterBoundaryBandMismatchRatio:F3}");
        Stat("Local Band Ecology", $"{_pipelineReport.LocalEcologyBoundaryBandMismatchRatio:F3}");
        Stat("Local Band Tree", $"{_pipelineReport.LocalTreeBoundaryBandMismatchRatio:F3}");
        Stat("Local Avg Tree Density", $"{_pipelineReport.AvgLocalTreeDensity:F3}");
        Stat("Dense Forest Canopy", $"{_pipelineReport.DenseForestMedianTreeDensity:F3} ({_pipelineReport.DenseForestSampleCount} samples)");
        Stat("Tropical Canopy", $"{_pipelineReport.TropicalMedianTreeDensity:F3} ({_pipelineReport.TropicalSampleCount} samples)");
        Stat("Arid Canopy", $"{_pipelineReport.AridMedianTreeDensity:F3} ({_pipelineReport.AridSampleCount} samples)");
        Stat("Dense Forest Patch", $"{_pipelineReport.DenseForestMedianLargestPatchRatio:F3}");
        Stat("Dense Coverage", UiText.YesNo(_pipelineReport.DenseForestCoverageAchieved));
        Stat("Tropical Coverage", UiText.YesNo(_pipelineReport.TropicalCoverageAchieved));
        Stat("Arid Coverage", UiText.YesNo(_pipelineReport.AridCoverageAchieved));

        var failedBudgets = _pipelineReport.Budgets.Where(b => !b.Passed).Select(b => b.Name).ToArray();
        if (failedBudgets.Length > 0)
            Stat("Failing Budgets", string.Join(", ", failedBudgets));
    }

    private void ShowRegionStats()
    {
        var tile = _regionMap!.GetTile(_selectedRegionX, _selectedRegionY);
        StatHeader("SELECTED REGION CELL");
        Stat("Overlay", GetOverlayLabel());
        Stat("Position", $"({_selectedRegionX}, {_selectedRegionY})");
        Stat("Variant", tile.BiomeVariantId.Replace('_', ' '));
        Stat("Surface", tile.SurfaceClassId.Replace('_', ' '));
        Stat("Geology", tile.GeologyProfileId.Replace('_', ' '));
        Stat("River", UiText.YesNo(tile.HasRiver));
        Stat("Discharge", tile.HasRiver ? $"{tile.RiverDischarge:F2}" : "-");
        Stat("Order", tile.HasRiver ? tile.RiverOrder.ToString() : "-");
        Stat("River Edges", tile.HasRiver ? tile.RiverEdges.ToString() : UiText.No);
        Stat("Lake", UiText.YesNo(tile.HasLake));
        Stat("Settlement", UiText.YesNo(tile.HasSettlement));
        Stat("Road", UiText.YesNo(tile.HasRoad));
        Stat("Road Edges", tile.HasRoad ? tile.RoadEdges.ToString() : UiText.No);
        Stat("Vegetation", $"{tile.VegetationDensity:F2}");
        Stat("Vegetation Suitability", $"{tile.VegetationSuitability:F2}");
        Stat("Temperature", $"{tile.TemperatureBand:F2}");
        Stat("Moisture", $"{tile.MoistureBand:F2}");
        Stat("Aridity", $"{ComputeRegionAridity(tile):F2}");
        Stat("Flow Accumulation", $"{tile.FlowAccumulationBand:F2}");
        Stat("Soil Depth", $"{tile.SoilDepth:F2}");
        Stat("Groundwater", $"{tile.Groundwater:F2}");
        Stat("Resources", $"{tile.ResourceRichness:F2}");
        Stat("Slope", tile.Slope.ToString());
        Stat("Parent Macro", _regionMap.ParentMacroBiomeId.Replace('_', ' '));
        Stat("Parent Forest", $"{_regionMap.ParentForestCover:F2}");
        Stat("Parent Mountain", $"{_regionMap.ParentMountainCover:F2}");
        Stat("Parent Relief", $"{_regionMap.ParentRelief:F2}");
        Stat("Parent Moisture", $"{_regionMap.ParentMoistureBand:F2}");
        Stat("Parent Temperature", $"{_regionMap.ParentTemperatureBand:F2}");
        Stat("Delta Tolerance", $"+/-{_parentDeltaTolerance:0.00}");
        var localForestSignal = ResolveRegionForestSignal(tile);
        var localMountainSignal = ResolveRegionMountainSignal(tile);
        Stat("Forest Delta", $"{(localForestSignal - _regionMap.ParentForestCover):+0.00;-0.00;0.00}");
        Stat("Mountain Delta", $"{(localMountainSignal - _regionMap.ParentMountainCover):+0.00;-0.00;0.00}");
        Stat("Moisture Delta", $"{(tile.MoistureBand - _regionMap.ParentMoistureBand):+0.00;-0.00;0.00}");
        Stat("Parent River", UiText.YesNo(_regionMap.ParentHasRiver));
        Stat("Parent River Order", _regionMap.ParentHasRiver ? _regionMap.ParentRiverOrder.ToString() : "-");
        Stat("Parent River Discharge", _regionMap.ParentHasRiver ? $"{_regionMap.ParentRiverDischarge:F2}" : "-");
        DrillButton("View Local Embark ->", () =>
        {
            SetMode(ViewMode.Local);
        });

        StatHeader("REGION SUMMARY");
        var rivers = 0;
        var lakes = 0;
        var settlements = 0;
        var roads = 0;
        var surfaceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var y = 0; y < _regionHeight; y++)
        for (var x = 0; x < _regionWidth; x++)
        {
            var regionTile = _regionMap.GetTile(x, y);
            if (regionTile.HasRiver) rivers++;
            if (regionTile.HasLake) lakes++;
            if (regionTile.HasSettlement) settlements++;
            if (regionTile.HasRoad) roads++;
            if (!surfaceCounts.TryAdd(regionTile.SurfaceClassId, 1))
                surfaceCounts[regionTile.SurfaceClassId]++;
        }

        Stat("River Cells", rivers.ToString());
        Stat("Lake Cells", lakes.ToString());
        Stat("Settlements", settlements.ToString());
        Stat("Road Cells", roads.ToString());
        var totalCells = Math.Max(1, _regionWidth * _regionHeight);
        foreach (var surface in RegionSurfaceClassIds.All)
        {
            var count = surfaceCounts.TryGetValue(surface, out var value) ? value : 0;
            if (count <= 0)
                continue;

            var ratio = count / (float)totalCells;
            Stat($"Surface {surface}", $"{count} ({ratio:P0})");
        }
        Stat("Continuity Warnings", CountRegionContinuityWarnings().ToString());
        Stat("River Continuity", CountRegionRiverContinuityWarnings().ToString());
        Stat("Road Continuity", CountRegionRoadContinuityWarnings().ToString());

        var forestHistogram = BuildRegionDeltaHistogram(ResolveRegionForestSignal, _regionMap.ParentForestCover);
        var mountainHistogram = BuildRegionDeltaHistogram(ResolveRegionMountainSignal, _regionMap.ParentMountainCover);
        var moistureHistogram = BuildRegionDeltaHistogram(static regionTile => regionTile.MoistureBand, _regionMap.ParentMoistureBand);

        StatHeader("PARENT DELTA HISTOGRAM");
        Stat("Tolerance (T)", $"+/-{_parentDeltaTolerance:0.00}");
        AppendDeltaHistogramStats("Forest", forestHistogram);
        AppendDeltaHistogramStats("Mountain", mountainHistogram);
        AppendDeltaHistogramStats("Moisture", moistureHistogram);

        StatHeader("DELTA OUTLIERS");
        AddRegionDeltaJumpButton("Forest", ResolveRegionForestSignal, _regionMap.ParentForestCover);
        AddRegionDeltaJumpButton("Mountain", ResolveRegionMountainSignal, _regionMap.ParentMountainCover);
        AddRegionDeltaJumpButton("Moisture", static regionTile => regionTile.MoistureBand, _regionMap.ParentMoistureBand);
    }

    private bool IsParentDeltaOverlay(OverlayMode mode)
        => mode is OverlayMode.ParentForestDelta or OverlayMode.ParentMountainDelta or OverlayMode.ParentMoistureDelta;

    private void UpdateParentDeltaControlsVisibility()
    {
        var visible = _mode == ViewMode.Region && IsParentDeltaOverlay(_overlayMode);
        if (_parentDeltaThresholdLabel is not null)
            _parentDeltaThresholdLabel.Visible = visible;
        if (_parentDeltaThresholdSlider is not null)
            _parentDeltaThresholdSlider.Visible = visible;
        if (_parentDeltaLegendLabel is not null)
            _parentDeltaLegendLabel.Visible = visible;
    }

    private void OnParentDeltaThresholdChanged(double value)
    {
        SetParentDeltaTolerance((float)value);
    }

    private void SetParentDeltaTolerance(float value)
    {
        var clamped = Mathf.Clamp(value, ParentDeltaToleranceMin, ParentDeltaToleranceMax);
        if (Mathf.IsEqualApprox(clamped, _parentDeltaTolerance))
            return;

        _parentDeltaTolerance = clamped;
        if (_parentDeltaThresholdSlider is not null && !Mathf.IsEqualApprox((float)_parentDeltaThresholdSlider.Value, clamped))
            _parentDeltaThresholdSlider.Value = clamped;

        SyncParentDeltaThresholdLabel();
        RefreshStats();
        QueueRedraw();
    }

    private void SyncParentDeltaThresholdLabel()
    {
        if (_parentDeltaThresholdLabel is not null)
            _parentDeltaThresholdLabel.Text = $"Delta tolerance: +/-{_parentDeltaTolerance:0.00}";
    }

    private DeltaHistogram BuildRegionDeltaHistogram(Func<GeneratedRegionTile, float> localSignalSelector, float parentSignal)
    {
        if (_regionMap is null)
            return default;

        var tolerance = Mathf.Clamp(_parentDeltaTolerance, ParentDeltaToleranceMin, ParentDeltaToleranceMax);
        var total = 0;
        var farBelow = 0;
        var below = 0;
        var aligned = 0;
        var above = 0;
        var farAbove = 0;
        var sumDelta = 0f;
        var sumAbsDelta = 0f;

        for (var y = 0; y < _regionHeight; y++)
        for (var x = 0; x < _regionWidth; x++)
        {
            var tile = _regionMap.GetTile(x, y);
            var delta = localSignalSelector(tile) - parentSignal;
            total++;
            sumDelta += delta;
            sumAbsDelta += MathF.Abs(delta);

            if (delta <= -(2f * tolerance))
                farBelow++;
            else if (delta < -tolerance)
                below++;
            else if (MathF.Abs(delta) <= tolerance)
                aligned++;
            else if (delta < 2f * tolerance)
                above++;
            else
                farAbove++;
        }

        if (total <= 0)
            return default;

        return new DeltaHistogram(
            Total: total,
            FarBelow: farBelow,
            Below: below,
            Aligned: aligned,
            Above: above,
            FarAbove: farAbove,
            MeanDelta: sumDelta / total,
            MeanAbsDelta: sumAbsDelta / total);
    }

    private void AppendDeltaHistogramStats(string label, DeltaHistogram histogram)
    {
        if (histogram.Total <= 0)
            return;

        Stat($"{label} Mean", $"{histogram.MeanDelta:+0.000;-0.000;0.000}");
        Stat($"{label} |d|", $"{histogram.MeanAbsDelta:0.000}");
        Stat($"{label} <=-2T", FormatDeltaBucket(histogram.FarBelow, histogram.Total));
        Stat($"{label} -2T..-T", FormatDeltaBucket(histogram.Below, histogram.Total));
        Stat($"{label} -T..+T", FormatDeltaBucket(histogram.Aligned, histogram.Total));
        Stat($"{label} +T..+2T", FormatDeltaBucket(histogram.Above, histogram.Total));
        Stat($"{label} >=+2T", FormatDeltaBucket(histogram.FarAbove, histogram.Total));
    }

    private static string FormatDeltaBucket(int count, int total)
    {
        var safeTotal = Math.Max(1, total);
        var ratio = count / (float)safeTotal;
        var barLength = Math.Clamp((int)MathF.Round(ratio * 8f), 0, 8);
        var bar = new string('#', barLength).PadRight(8, '.');
        return $"{bar} {count} ({ratio * 100f:0}%)";
    }

    private void AddRegionDeltaJumpButton(string label, Func<GeneratedRegionTile, float> localSignalSelector, float parentSignal)
    {
        if (!TryFindWorstRegionDeltaTile(localSignalSelector, parentSignal, out var x, out var y, out var delta))
            return;

        var safeLabel = string.IsNullOrWhiteSpace(label) ? "Signal" : label;
        var suffix = MathF.Abs(delta) <= _parentDeltaTolerance ? "aligned" : "outlier";
        DrillButton(
            $"Jump Worst {safeLabel} ({delta:+0.00;-0.00;0.00}, {suffix})",
            () => JumpToRegionCell(x, y));
    }

    private bool TryFindWorstRegionDeltaTile(
        Func<GeneratedRegionTile, float> localSignalSelector,
        float parentSignal,
        out int bestX,
        out int bestY,
        out float bestDelta)
    {
        bestX = -1;
        bestY = -1;
        bestDelta = 0f;
        if (_regionMap is null)
            return false;

        var bestScore = float.MinValue;
        var tolerance = Mathf.Clamp(_parentDeltaTolerance, ParentDeltaToleranceMin, ParentDeltaToleranceMax);
        for (var y = 0; y < _regionHeight; y++)
        for (var x = 0; x < _regionWidth; x++)
        {
            var tile = _regionMap.GetTile(x, y);
            var delta = localSignalSelector(tile) - parentSignal;
            var magnitude = MathF.Abs(delta);
            var score = magnitude;
            if (magnitude > tolerance)
                score += 10f;

            if (score < bestScore)
                continue;

            var tieBreak = score == bestScore && magnitude <= MathF.Abs(bestDelta);
            if (tieBreak)
                continue;

            bestScore = score;
            bestX = x;
            bestY = y;
            bestDelta = delta;
        }

        return bestX >= 0 && bestY >= 0;
    }

    private void JumpToRegionCell(int x, int y)
    {
        if (_regionMap is null)
            return;

        _selectedRegionX = Mathf.Clamp(x, 0, _regionWidth - 1);
        _selectedRegionY = Mathf.Clamp(y, 0, _regionHeight - 1);
        GenerateLocal();
        RefreshStats();
        QueueRedraw();
    }

    private void ShowLocalStats()
    {
        if (_localMap is null)
            return;

        var stageReport = _localMap.Diagnostics is null
            ? null
            : WorldGenAnalyzer.AnalyzeEmbarkStages(_localMap);

        StatHeader($"LOCAL MAP Z={_currentZ}");
        var passable = 0;
        var trees = 0;
        var water = 0;
        var magma = 0;
        var walls = 0;
        var aquifer = 0;
        var wildlifeAtCurrentZ = 0;
        var surfaceWildlife = 0;
        var caveWildlife = 0;
        var wildlifeSpecies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var y = 0; y < _localHeight; y++)
        for (var x = 0; x < _localWidth; x++)
        {
            var tile = _localMap.GetTile(x, y, _currentZ);
            if (tile.IsPassable) passable++;
            if (tile.TileDefId == GeneratedTileDefIds.Tree) trees++;
            if (tile.TileDefId == GeneratedTileDefIds.Water) water++;
            if (tile.TileDefId == GeneratedTileDefIds.Magma) magma++;
            if (!tile.IsPassable && tile.TileDefId != GeneratedTileDefIds.Empty) walls++;
            if (tile.IsAquifer) aquifer++;
        }

        for (var i = 0; i < _localMap.CreatureSpawns.Count; i++)
        {
            var spawn = _localMap.CreatureSpawns[i];
            if (spawn.Z == _currentZ)
                wildlifeAtCurrentZ++;
            if (spawn.Z == 0)
                surfaceWildlife++;
            else
                caveWildlife++;

            wildlifeSpecies.Add(spawn.CreatureDefId);
        }

        Stat("Passable", passable.ToString());
        Stat("Trees", trees.ToString());
        Stat("Water", water.ToString());
        Stat("Magma", magma.ToString());
        Stat("Walls", walls.ToString());
        Stat("Aquifer", aquifer.ToString());
        Stat("Wildlife (Z)", wildlifeAtCurrentZ.ToString());
        Stat("Wildlife (Surface)", surfaceWildlife.ToString());
        Stat("Wildlife (Caves)", caveWildlife.ToString());
        Stat("Wildlife Species", wildlifeSpecies.Count.ToString());
        Stat("Boundary Samples", CountLocalComparableBoundaryTiles().ToString());
        Stat("Surface Continuity", CountLocalContinuityWarnings(EmbarkBoundaryMismatchKind.SurfaceFamily).ToString());
        Stat("Water Continuity", CountLocalContinuityWarnings(EmbarkBoundaryMismatchKind.Water).ToString());
        Stat("Ecology Continuity", CountLocalContinuityWarnings(EmbarkBoundaryMismatchKind.Ecology | EmbarkBoundaryMismatchKind.Tree).ToString());
        if (_currentZ == 0)
            Stat("Unsafe Borders", EmbarkBoundaryContinuity.CountUnsafeBorderCells(_localMap, z: 0).ToString());

        if (stageReport is null)
            return;

        StatHeader("LOCAL PIPELINE");
        var passedBudgets = stageReport.Budgets.Count(b => b.Passed);
        Stat("Stage Seed", stageReport.Seed.ToString());
        Stat("Stage Budgets", $"{passedBudgets}/{stageReport.Budgets.Count}");
        Stat("Stage Status", stageReport.Passed ? "Pass" : "Fail");

        var failedBudgets = stageReport.Budgets.Where(b => !b.Passed).Select(b => b.Name).ToArray();
        if (failedBudgets.Length > 0)
            Stat("Failing Budgets", string.Join(", ", failedBudgets));

        foreach (var snapshot in stageReport.StageSnapshots)
            Stat(FormatEmbarkStageLabel(snapshot.StageId), FormatEmbarkStageSnapshot(snapshot));
    }

    private void ShowLocalTileInfo(int x, int y)
    {
        if (_localMap is null || _statsBox is null)
            return;

        foreach (var child in _statsBox.GetChildren())
            child.QueueFree();

        var tile = _localMap.GetTile(x, y, _currentZ);
        StatHeader($"TILE ({x}, {y}, {_currentZ})");
        Stat("Type", tile.TileDefId.Replace('_', ' '));
        Stat("Material", tile.MaterialId ?? "-");
        Stat("Species", tile.TreeSpeciesId ?? "-");
        Stat("Ore", tile.OreId ?? "-");
        Stat("Aquifer", UiText.YesNo(tile.IsAquifer));
        Stat("Passable", UiText.YesNo(tile.IsPassable));
        var wildlife = _localMap.CreatureSpawns
            .Where(spawn => spawn.X == x && spawn.Y == y && spawn.Z == _currentZ)
            .GroupBy(spawn => spawn.CreatureDefId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key} x{group.Count()}")
            .ToArray();
        Stat("Wildlife", wildlife.Length > 0 ? string.Join(", ", wildlife) : "-");

        if (tile.FluidType != GeneratedFluidType.None)
        {
            Stat("Fluid", tile.FluidType.ToString());
            Stat("Level", tile.FluidLevel.ToString());
        }
    }

    private void StatHeader(string text)
    {
        _statsBox!.AddChild(new HSeparator());
        _statsBox.AddChild(new Label
        {
            Text = text,
            Modulate = new Color(1f, 0.9f, 0.35f),
        });
    }

    private void Stat(string key, string value)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label
        {
            Text = key,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Modulate = new Color(0.74f, 0.74f, 0.74f),
        });
        row.AddChild(new Label { Text = value });
        _statsBox!.AddChild(row);
    }

    private void DrillButton(string label, System.Action onPressed)
    {
        var btn = new Button
        {
            Text = label,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        btn.Pressed += onPressed;
        _statsBox!.AddChild(new HSeparator());
        _statsBox.AddChild(btn);
    }

    private static string FormatEmbarkStageLabel(EmbarkGenerationStageId stageId)
        => stageId switch
        {
            EmbarkGenerationStageId.Inputs => "Stage Inputs",
            EmbarkGenerationStageId.SurfaceShape => "Stage Surface",
            EmbarkGenerationStageId.UndergroundStructure => "Stage Underground",
            EmbarkGenerationStageId.Hydrology => "Stage Hydrology",
            EmbarkGenerationStageId.Ecology => "Stage Ecology",
            EmbarkGenerationStageId.HydrologyPolish => "Stage Hydro Polish",
            EmbarkGenerationStageId.CivilizationOverlay => "Stage Overlay",
            EmbarkGenerationStageId.Playability => "Stage Playability",
            EmbarkGenerationStageId.Population => "Stage Population",
            _ => stageId.ToString(),
        };

    private static string FormatEmbarkStageSnapshot(EmbarkGenerationStageSnapshot snapshot)
        => $"P{snapshot.SurfacePassableTiles} W{snapshot.SurfaceWaterTiles} T{snapshot.SurfaceTreeTiles} Aq{snapshot.AquiferTiles} O{snapshot.OreTiles} M{snapshot.MagmaTiles} S{snapshot.CreatureSpawnCount}";
}

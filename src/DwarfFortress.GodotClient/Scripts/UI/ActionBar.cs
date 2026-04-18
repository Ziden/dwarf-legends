using System;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Systems;
using Godot;

namespace DwarfFortress.GodotClient.UI;


/// <summary>
/// Bottom action bar Ã¢â‚¬â€ the primary interaction surface.
/// Contains designation buttons, a build popup, and pause control.
/// Replaces the buried BuildMenu and the old time controls that were in TopBar.
/// </summary>
public partial class ActionBar : PanelContainer
{
    private const int ActionButtonSize = 46;
    private const float FixedSpeedMultiplier = 5f;

    // Ã¢â€â‚¬Ã¢â€â‚¬ Exposed to GameRoot Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
    public bool  IsPaused        => _paused;
    public float SpeedMultiplier => FixedSpeedMultiplier;

    // Ã¢â€â‚¬Ã¢â€â‚¬ Private state Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
    private InputController? _input;
    private PopupPanel?      _buildPopup;
    private ItemSelectionList? _buildList;
    private Button?          _pauseBtn;
    private Button?          _knowledgeBtn;
    private bool             _paused = false;
    private GameSimulation?  _simulation;
    private DiscoverySystem? _discovery;
    private bool             _buildListRefreshQueued;

    // Ã¢â€â‚¬Ã¢â€â‚¬ Knowledge panel callback Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
    public Action? OnKnowledgePressed { get; set; }

    public override void _Ready()
    {
        _pauseBtn = GetNode<Button>("%PauseBtn");
        _pauseBtn.Toggled += v => _paused = v;

        var clearBtn = GetNode<Button>("%ClearBtn");
        var cancelBtn = GetNode<Button>("%CancelBtn");
        var zoneBtn = GetNode<Button>("%ZoneBtn");
        var buildBtn = GetNode<Button>("%BuildBtn");
        _knowledgeBtn = GetNode<Button>("%KnowledgeBtn");

        clearBtn.Pressed    += () => _input?.SetMode(InputMode.DesignateClear);
        cancelBtn.Pressed   += () => _input?.SetMode(InputMode.DesignateCancel);
        zoneBtn.Pressed     += () => _input?.SetMode(InputMode.StockpileZone);
        buildBtn.Pressed    += ToggleBuildPopup;
        _knowledgeBtn.Pressed += () => OnKnowledgePressed?.Invoke();

        ConfigureActionButton(clearBtn, PixelArtFactory.GetUiIcon(UiIconIds.Pickaxe), "Harvest", "Harvest terrain  [M]\nDrag over walls and trees");
        ConfigureActionButton(cancelBtn, PixelArtFactory.GetUiIcon(UiIconIds.Cancel), "Cancel", "Cancel designations  [X]");
        ConfigureActionButton(zoneBtn, PixelArtFactory.GetUiIcon(UiIconIds.Zone), "Zone", "Create stockpile zone  [S]");
        ConfigureActionButton(buildBtn, PixelArtFactory.GetUiIcon(UiIconIds.Build), "Build", "Open build menu  [B]");
        ConfigureActionButton(_knowledgeBtn, PixelArtFactory.GetUiIcon(UiIconIds.Book), "Knowledge", "View discovered knowledge  [K]");
        ConfigureActionButton(_pauseBtn, PixelArtFactory.GetUiIcon(UiIconIds.Pause), "Pause", "Pause simulation  [Space]");

        // Build popup must be a child of the root Window to float freely above everything.
        _buildPopup = new PopupPanel();

        var pMargin = new MarginContainer();
        pMargin.AddThemeConstantOverride("margin_left",   8);
        pMargin.AddThemeConstantOverride("margin_right",  8);
        pMargin.AddThemeConstantOverride("margin_top",    8);
        pMargin.AddThemeConstantOverride("margin_bottom", 8);
        _buildPopup.AddChild(pMargin);

        var header = new Label { Text = UiText.ChooseBuilding };
        var scroll = new ItemSelectionList
        {
            CustomMinimumSize = new Vector2(0, 360),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _buildList = scroll;

        var pVbox = new VBoxContainer();
        pVbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        pVbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        pVbox.AddThemeConstantOverride("separation", 6);
        pVbox.AddChild(header);
        pVbox.AddChild(new HSeparator());
        pVbox.AddChild(scroll);
        pMargin.AddChild(pVbox);
    }

    public override void _ExitTree()
    {
        UnsubscribeFromSimulationEvents();
    }

    /// <summary>Called by GameRoot after the simulation is ready.</summary>
    public void Setup(InputController input, GameSimulation sim)
    {
        _input = input;
        if (!ReferenceEquals(_simulation, sim))
        {
            UnsubscribeFromSimulationEvents();
            _simulation = sim;
            SubscribeToSimulationEvents();
        }
        else
        {
            _simulation = sim;
        }

        _discovery = sim.Context.TryGet<DiscoverySystem>();
        if (_buildPopup is null || _buildList is null)
            throw new InvalidOperationException("ActionBar must finish _Ready before Setup.");

        // Popup must be a direct child of the root Window to float freely above everything.
        // Deferred so we don't hit the "parent busy" error during Ready chain.
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, _buildPopup);

        RefreshBuildList();
    }

    public void RefreshBuildList()
    {
        _buildListRefreshQueued = false;
        if (_buildList is null || _simulation is null)
            return;

        var dm = _simulation.Context.Get<DataManager>();
        var entries = _discovery is null
            ? dm.Buildings.All().Select(def => BuildEntry(def, null)).ToList()
            : _discovery.GetBuildingInfos()
                .Where(info => info.State != DiscoveryKnowledgeState.Hidden)
                .Select(info => BuildEntry(dm.Buildings.Get(info.Id), info))
                .ToList();

        if (entries.Count == 0)
        {
            _buildList.SetEntries(new[]
            {
                new ItemSelectionEntry(
                    Id: "none",
                    Title: _discovery is not null ? "No buildings discovered yet" : UiText.NoBuildingDefinitionsLoaded,
                    Subtitle: _discovery is not null ? "Gather materials to discover buildings." : string.Empty,
                    Details: string.Empty,
                    Status: string.Empty,
                    StatusColor: new Color(0.7f, 0.7f, 0.7f),
                    Icon: null,
                    ActionLabel: "Unavailable",
                    IsEnabled: false,
                    OnPressed: null)
            });
            return;
        }

        _buildList.SetEntries(entries);
    }

    private void SubscribeToSimulationEvents()
    {
        if (_simulation is null)
            return;

        _simulation.EventBus.On<ItemCreatedEvent>(OnBuildRelevantItemCreated);
        _simulation.EventBus.On<ItemDestroyedEvent>(OnBuildRelevantItemDestroyed);
        _simulation.EventBus.On<ItemPickedUpEvent>(OnBuildRelevantItemPickedUp);
        _simulation.EventBus.On<ItemDroppedEvent>(OnBuildRelevantItemDropped);
        _simulation.EventBus.On<ItemStoredEvent>(OnBuildRelevantItemStored);
        _simulation.EventBus.On<DiscoveryUnlockedEvent>(OnBuildRelevantDiscoveryUnlocked);
    }

    private void UnsubscribeFromSimulationEvents()
    {
        if (_simulation is null)
            return;

        _simulation.EventBus.Off<ItemCreatedEvent>(OnBuildRelevantItemCreated);
        _simulation.EventBus.Off<ItemDestroyedEvent>(OnBuildRelevantItemDestroyed);
        _simulation.EventBus.Off<ItemPickedUpEvent>(OnBuildRelevantItemPickedUp);
        _simulation.EventBus.Off<ItemDroppedEvent>(OnBuildRelevantItemDropped);
        _simulation.EventBus.Off<ItemStoredEvent>(OnBuildRelevantItemStored);
        _simulation.EventBus.Off<DiscoveryUnlockedEvent>(OnBuildRelevantDiscoveryUnlocked);
    }

    private void OnBuildRelevantItemCreated(ItemCreatedEvent _) => QueueBuildListRefresh();

    private void OnBuildRelevantItemDestroyed(ItemDestroyedEvent _) => QueueBuildListRefresh();

    private void OnBuildRelevantItemPickedUp(ItemPickedUpEvent _) => QueueBuildListRefresh();

    private void OnBuildRelevantItemDropped(ItemDroppedEvent _) => QueueBuildListRefresh();

    private void OnBuildRelevantItemStored(ItemStoredEvent _) => QueueBuildListRefresh();

    private void OnBuildRelevantDiscoveryUnlocked(DiscoveryUnlockedEvent e)
    {
        if (string.Equals(e.Kind, "building", StringComparison.OrdinalIgnoreCase))
            QueueBuildListRefresh();
    }

    private void QueueBuildListRefresh()
    {
        if (_buildListRefreshQueued || !IsInsideTree())
            return;

        _buildListRefreshQueued = true;
        CallDeferred(nameof(RefreshBuildList));
    }

    private void ToggleBuildPopup()
    {
        if (_buildPopup is null) return;
        if (_buildPopup.Visible) { _buildPopup.Hide(); return; }

        RefreshBuildList();

        const int popupW = 640;
        const int popupH = 460;
        var rect = GetGlobalRect();
        _buildPopup.Size = new Vector2I(popupW, popupH);
        _buildPopup.PopupOnParent(new Rect2I(
            (int)rect.Position.X,
            (int)rect.Position.Y - popupH - 4,
            popupW, popupH));
    }

    public void TogglePause()
    {
        if (_pauseBtn is null)
        {
            _paused = !_paused;
            return;
        }

        _pauseBtn.ButtonPressed = !_pauseBtn.ButtonPressed;
    }

    private ItemSelectionEntry BuildEntry(BuildingDef def, BuildingDiscoveryInfo? info)
    {
        var requirements = SelectionRequirementHelper.Analyze(_simulation!, def.ConstructionInputs);
        var state = info?.State ?? (requirements.CanFulfill
            ? DiscoveryKnowledgeState.BuildableNow
            : DiscoveryKnowledgeState.Unlocked);
        var canPlace = state == DiscoveryKnowledgeState.BuildableNow;
        var typeLabel = def.IsWorkshop ? "Workshop" : "Structure";
        var footprint = def.Footprint.Any()
            ? $"{def.Footprint.Max(tile => tile.Offset.X) + 1}x{def.Footprint.Max(tile => tile.Offset.Y) + 1}"
            : "1x1";

        return new ItemSelectionEntry(
            Id: def.Id,
            Title: def.DisplayName,
            Subtitle: $"{typeLabel} Ã¢â‚¬Â¢ footprint {footprint} Ã¢â‚¬Â¢ {FormatStateLabel(state)}",
            Details: def.ConstructionInputs.Count == 0
                ? $"Build time {def.ConstructionTime:0.#}"
                : $"Needs {requirements.NeededSummary}  |  Build {def.ConstructionTime:0.#}",
            Status: BuildStatusText(state, requirements, info),
            StatusColor: BuildStatusColor(state),
            Icon: PixelArtFactory.GetBuilding(def.Id),
            ActionLabel: canPlace ? "Place mode" : "Unavailable",
            IsEnabled: canPlace,
            OnPressed: canPlace ? () =>
            {
                if (_input is null) return;
                _input.PendingBuildingDefId = def.Id;
                _input.PendingBuildingRotation = BuildingRotation.None;
                _input.SetMode(InputMode.BuildingPreview);
                _buildPopup?.Hide();
            } : null);
    }

    private static Color BuildStatusColor(DiscoveryKnowledgeState state)
        => state switch
        {
            DiscoveryKnowledgeState.BuildableNow => new Color(0.44f, 0.85f, 0.48f),
            DiscoveryKnowledgeState.Unlocked => new Color(0.96f, 0.72f, 0.28f),
            DiscoveryKnowledgeState.Known => new Color(0.91f, 0.68f, 0.34f),
            _ => new Color(0.7f, 0.7f, 0.7f),
        };

    private static string FormatStateLabel(DiscoveryKnowledgeState state)
        => state switch
        {
            DiscoveryKnowledgeState.BuildableNow => "buildable",
            DiscoveryKnowledgeState.Unlocked => "known",
            DiscoveryKnowledgeState.Known => "partial knowledge",
            _ => "hidden",
        };

    private static string BuildStatusText(DiscoveryKnowledgeState state, RequirementAnalysis requirements, BuildingDiscoveryInfo? info)
        => state switch
        {
            DiscoveryKnowledgeState.BuildableNow => "Materials available now",
            DiscoveryKnowledgeState.Unlocked => string.IsNullOrWhiteSpace(requirements.MissingSummary)
                ? "Discovered"
                : $"Missing {requirements.MissingSummary}",
            DiscoveryKnowledgeState.Known => $"Need to discover {FormatMissingDiscovery(info)}",
            _ => "Not yet discovered",
        };

    private static string FormatMissingDiscovery(BuildingDiscoveryInfo? info)
    {
        if (info is null)
            return "more materials";

        var missing = info.Value.DiscoveryRequirements
            .Where(status => !status.IsEncountered)
            .Select(status => FormatRequirement(status.Input))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return missing.Count > 0
            ? string.Join(", ", missing)
            : "more materials";
    }

    private static string FormatRequirement(RecipeInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.ItemDefId))
            return ItemTextFormatter.Humanize(input.ItemDefId);

        if (!string.IsNullOrWhiteSpace(input.MaterialId))
            return ItemTextFormatter.Humanize(input.MaterialId);

        if (input.RequiredTags.Count > 0)
            return string.Join("/", input.RequiredTags.All.Select(ItemTextFormatter.Humanize));

        return "material";
    }

    private static void ConfigureActionButton(Button button, Texture2D icon, string text, string tooltip)
    {
        button.Icon = icon;
        button.Text = text;
        button.TooltipText = tooltip;
        button.CustomMinimumSize = new Vector2(ActionButtonSize, ActionButtonSize);
        button.ExpandIcon = true;
        button.IconAlignment = HorizontalAlignment.Left;
    }
}

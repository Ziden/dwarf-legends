using System;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Systems;
using Godot;

/// <summary>
/// Bottom action bar — the primary interaction surface.
/// Contains designation buttons, a build popup, and pause control.
/// Replaces the buried BuildMenu and the old time controls that were in TopBar.
/// </summary>
public partial class ActionBar : PanelContainer
{
    private const int ActionButtonSize = 46;
    private const float FixedSpeedMultiplier = 5f;

    // ── Exposed to GameRoot ────────────────────────────────────────────────
    public bool  IsPaused        => _paused;
    public float SpeedMultiplier => FixedSpeedMultiplier;

    // ── Private state ──────────────────────────────────────────────────────
    private InputController? _input;
    private PopupPanel?      _buildPopup;
    private ItemSelectionList? _buildList;
    private Button?          _pauseBtn;
    private Button?          _knowledgeBtn;
    private bool             _paused = false;
    private GameSimulation?  _simulation;
    private DiscoverySystem? _discovery;

    // ── Knowledge panel callback ───────────────────────────────────────────
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

        ConfigureActionButton(clearBtn, PixelArtFactory.GetUiIcon(UiIconIds.Pickaxe), "Clear", "Clear terrain  [M]\nDrag over walls and trees");
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

    /// <summary>Called by GameRoot after the simulation is ready.</summary>
    public void Setup(InputController input, GameSimulation sim)
    {
        _input = input;
        _simulation = sim;
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
        if (_buildList is null || _simulation is null) return;

        var dm = _simulation.Context.Get<DataManager>();
        var defs = dm.Buildings.All()
            .Where(b => _discovery is null || _discovery.IsBuildingUnlocked(b.Id))
            .ToList();

        if (defs.Count == 0)
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

        _buildList.SetEntries(defs.Select(BuildEntry).ToList());
    }

    private void ToggleBuildPopup()
    {
        if (_buildPopup is null) return;
        if (_buildPopup.Visible) { _buildPopup.Hide(); return; }

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

    private ItemSelectionEntry BuildEntry(BuildingDef def)
    {
        var requirements = SelectionRequirementHelper.Analyze(_simulation!, def.ConstructionInputs);
        bool canPlace = requirements.CanFulfill;
        var typeLabel = def.IsWorkshop ? "Workshop" : "Structure";
        var footprint = def.Footprint.Any()
            ? $"{def.Footprint.Max(tile => tile.Offset.X) + 1}x{def.Footprint.Max(tile => tile.Offset.Y) + 1}"
            : "1x1";

        return new ItemSelectionEntry(
            Id: def.Id,
            Title: def.DisplayName,
            Subtitle: $"{typeLabel} • footprint {footprint}",
            Details: def.ConstructionInputs.Count == 0
                ? $"Build time {def.ConstructionTime:0.#}"
                : $"Needs {requirements.NeededSummary}  |  Build {def.ConstructionTime:0.#}",
            Status: requirements.CanFulfill
                ? "Materials available now"
                : $"Missing {requirements.MissingSummary}",
            StatusColor: requirements.CanFulfill ? new Color(0.44f, 0.85f, 0.48f) : new Color(0.96f, 0.72f, 0.28f),
            Icon: PixelArtFactory.GetBuilding(def.Id),
            ActionLabel: canPlace ? "Place mode" : "Unavailable",
            IsEnabled: canPlace,
            OnPressed: canPlace ? () =>
            {
                if (_input is null) return;
                _input.PendingBuildingDefId = def.Id;
                _input.SetMode(InputMode.BuildingPreview);
                _buildPopup?.Hide();
            } : null);
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

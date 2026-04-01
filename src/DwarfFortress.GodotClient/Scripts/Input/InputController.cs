using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Systems;
using Godot;

public enum InputMode
{
    Select,
    DesignateClear,
    DesignateMine,
    DesignateCutTrees,
    DesignateCancel,
    StockpileZone,
    BuildingPreview,
}

public partial class InputController : Node2D
{
    // ── Public state read by GameRoot each frame ───────────────────────────
    public InputMode CurrentMode   { get; private set; } = InputMode.Select;
    public Vector2I  HoveredTile   { get; private set; }
    public Vector2I? SelectedTile   { get; private set; }
    public Vector2I? DragStart     { get; private set; }
    public Vector2I? DragCurrent   { get; private set; }
    public bool      IsDragging    { get; private set; }
    public int?      SelectedDwarfId    { get; private set; }
    public int?      SelectedCreatureId { get; private set; }
    public int?      SelectedBuildingId { get; private set; }

    // ── Set by BuildMenu/StockpileDialog ──────────────────────────────────
    public string?   PendingBuildingDefId  { get; set; }
    public string[]  PendingStockpileTags  { get; set; } = ["all"];

    // ── Private ────────────────────────────────────────────────────────────
    private GameSimulation?    _simulation;
    private WorldQuerySystem?  _query;
    private int                _currentZ;

    // ── Setup (called by GameRoot after simulation starts) ─────────────────
    public void Setup(GameSimulation sim)
    {
        _simulation = sim;
        _query = sim.Context.Get<WorldQuerySystem>();
    }

    public void SetCurrentZ(int z)   => _currentZ = z;

    public override void _Process(double delta)
    {
        // Update hover every frame so the ghost follows the mouse smoothly
        // without requiring any other input event to fire.
        var world = GetGlobalMousePosition();
        HoveredTile = new Vector2I(
            (int)Mathf.Floor(world.X / GameRoot.TileSize),
            (int)Mathf.Floor(world.Y / GameRoot.TileSize));

        if (IsDragging)
            DragCurrent = HoveredTile;
    }

    public void SetMode(InputMode mode)
    {
        CurrentMode = mode;
        DragStart   = null;
        DragCurrent = null;
        IsDragging  = false;
    }

    // ── Godot Input ────────────────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
            HandleMouseButton(mb);
        else if (@event is InputEventMouseMotion && IsDragging)
            GetViewport().SetInputAsHandled(); // just suppress so UI doesn't get stray hover events
        else if (@event is InputEventKey key && key.Pressed && !key.Echo)
            HandleKeyPress(key.Keycode);
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private void HandleMouseButton(InputEventMouseButton mb)
    {
        switch (mb.ButtonIndex)
        {
            case MouseButton.Left when mb.Pressed:
                if (IsPointerOverUi())
                    break;
                DragStart   = HoveredTile;
                DragCurrent = HoveredTile;
                IsDragging  = true;
                GetViewport().SetInputAsHandled();
                break;

            case MouseButton.Left when !mb.Pressed && IsDragging:
                DragCurrent = HoveredTile;
                IsDragging  = false;
                CommitAction();
                GetViewport().SetInputAsHandled();
                break;

            case MouseButton.Right when mb.Pressed:
                if (IsPointerOverUi())
                    break;
                CancelAction();
                SetMode(InputMode.Select);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void HandleKeyPress(Key key)
    {
        switch (key)
        {
            case Key.M:      SetMode(InputMode.DesignateClear);    break;
            case Key.T:      SetMode(InputMode.DesignateCutTrees); break;
            case Key.X:      SetMode(InputMode.DesignateCancel);   break;
            case Key.B:      SetMode(InputMode.BuildingPreview);   break;
            case Key.Escape: CancelAction(); SetMode(InputMode.Select); break;
        }
    }

    private void CommitAction()
    {
        if (_simulation is null || DragStart is null || DragCurrent is null) return;

        var from = new Vec3i(
            Mathf.Min(DragStart.Value.X, DragCurrent.Value.X),
            Mathf.Min(DragStart.Value.Y, DragCurrent.Value.Y),
            _currentZ);
        var to = new Vec3i(
            Mathf.Max(DragStart.Value.X, DragCurrent.Value.X),
            Mathf.Max(DragStart.Value.Y, DragCurrent.Value.Y),
            _currentZ);

        switch (CurrentMode)
        {
            case InputMode.DesignateClear:
                _simulation.Context.Commands.Dispatch(new DesignateMineCommand(from, to));
                _simulation.Context.Commands.Dispatch(new DesignateCutTreesCommand(from, to));
                break;

            case InputMode.DesignateMine:
                _simulation.Context.Commands.Dispatch(new DesignateMineCommand(from, to));
                break;

            case InputMode.DesignateCutTrees:
                _simulation.Context.Commands.Dispatch(new DesignateCutTreesCommand(from, to));
                break;

            case InputMode.DesignateCancel:
                _simulation.Context.Commands.Dispatch(new CancelDesignationCommand(from, to));
                break;

            case InputMode.StockpileZone:
                _simulation.Context.Commands.Dispatch(new CreateStockpileCommand(from, to, PendingStockpileTags));
                SetMode(InputMode.Select);
                break;

            case InputMode.BuildingPreview when PendingBuildingDefId is not null:
                _simulation.Context.Commands.Dispatch(new PlaceBuildingCommand(PendingBuildingDefId, from));
                SetMode(InputMode.Select);
                PendingBuildingDefId = null;
                break;

            case InputMode.Select:
                TrySelectAt(DragStart.Value.X, DragStart.Value.Y);
                break;
        }

        DragStart   = null;
        DragCurrent = null;
    }

    private void CancelAction()
    {
        DragStart          = null;
        DragCurrent        = null;
        IsDragging         = false;
        SelectedTile       = null;
        SelectedDwarfId    = null;
        SelectedCreatureId = null;
        SelectedBuildingId = null;
    }

    private void TrySelectAt(int x, int y)
    {
        if (_query is null)
        {
            SelectedTile       = null;
            SelectedDwarfId    = null;
            SelectedCreatureId = null;
            SelectedBuildingId = null;
            return;
        }

        var tile = _query.QueryTile(new Vec3i(x, y, _currentZ));

        var dwarf = tile.Dwarves.FirstOrDefault();
        if (dwarf is not null)
        {
            SelectedDwarfId    = dwarf.Id;
            SelectedCreatureId = null;
            SelectedBuildingId = null;
            SelectedTile       = null;
            return;
        }

        var creature = tile.Creatures.FirstOrDefault();
        if (creature is not null)
        {
            SelectedCreatureId = creature.Id;
            SelectedDwarfId    = null;
            SelectedBuildingId = null;
            SelectedTile       = null;
            return;
        }

        if (tile.Building is not null)
        {
            SelectedBuildingId = tile.Building.Id;
            SelectedDwarfId    = null;
            SelectedCreatureId = null;
            SelectedTile       = null;
            return;
        }

        // Nothing entity-like found — select the raw tile
        SelectedTile       = new Vector2I(x, y);
        SelectedDwarfId    = null;
        SelectedCreatureId = null;
        SelectedBuildingId = null;
    }

    private bool IsPointerOverUi()
    {
        var hovered = GetViewport().GuiGetHoveredControl();
        return hovered is not null && hovered.MouseFilter != Control.MouseFilterEnum.Ignore;
    }

    /// <summary>Returns the current selection rectangle in tile coords, or null if no drag is active.</summary>
    public (Vector2I from, Vector2I to)? GetSelectionRect()
    {
        if (DragStart is null || DragCurrent is null) return null;
        return (
            new Vector2I(
                Mathf.Min(DragStart.Value.X, DragCurrent.Value.X),
                Mathf.Min(DragStart.Value.Y, DragCurrent.Value.Y)),
            new Vector2I(
                Mathf.Max(DragStart.Value.X, DragCurrent.Value.X),
                Mathf.Max(DragStart.Value.Y, DragCurrent.Value.Y)));
    }
}

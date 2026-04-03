using System;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Systems;
using Godot;

namespace DwarfFortress.GodotClient.Input;


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

public enum HoverSelectionMode
{
    QueryTile,
    RawTile,
}

public partial class InputController : Node
{
    public event Action<Vec3i>? TileSelectionCommitted;
    public event Action<Vec3i, Vec3i>? AreaSelectionCommitted;

    // â”€â”€ Public state read by GameRoot each frame â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public InputMode CurrentMode   { get; private set; } = InputMode.Select;
    public Vector2I  HoveredTile   { get; private set; }
    public Vector2I? SelectedTile   { get; private set; }
    public (Vector2I from, Vector2I to)? SelectedArea { get; private set; }
    public Vector2I? DragStart     { get; private set; }
    public Vector2I? DragCurrent   { get; private set; }
    public bool      IsDragging    { get; private set; }
    public int?      SelectedDwarfId    { get; private set; }
    public int?      SelectedCreatureId { get; private set; }
    public int?      SelectedBuildingId { get; private set; }
    public int?      SelectedItemId     { get; private set; }

    // â”€â”€ Set by BuildMenu/StockpileDialog â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public string?   PendingBuildingDefId  { get; set; }
    public string[]  PendingStockpileTags  { get; set; } = ["all"];

    // â”€â”€ Private â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private GameSimulation?    _simulation;
    private WorldQuerySystem?  _query;
    private int                _currentZ;
    private HoverSelectionMode _externalHoverSelectionMode = HoverSelectionMode.QueryTile;

    // â”€â”€ Setup (called by GameRoot after simulation starts) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public void Setup(GameSimulation sim)
    {
        _simulation = sim;
        _query = sim.Context.Get<WorldQuerySystem>();
    }

    public void SelectItem(int itemId)
        => TrySelectItem(itemId);

    public bool TrySelectDwarf(int dwarfId)
    {
        var dwarf = _query?.GetDwarfView(dwarfId);
        if (dwarf is null)
            return false;

        ClearAreaSelection();
        SelectedDwarfId = dwarfId;
        SelectedCreatureId = null;
        SelectedBuildingId = null;
        SelectedItemId = null;
        SelectedTile = new Vector2I(dwarf.Position.X, dwarf.Position.Y);
        TileSelectionCommitted?.Invoke(dwarf.Position);
        return true;
    }

    public bool TrySelectCreature(int creatureId)
    {
        var creature = _query?.GetCreatureView(creatureId);
        if (creature is null)
            return false;

        ClearAreaSelection();
        SelectedCreatureId = creatureId;
        SelectedTile = new Vector2I(creature.Position.X, creature.Position.Y);
        SelectedDwarfId = null;
        SelectedBuildingId = null;
        SelectedItemId = null;
        TileSelectionCommitted?.Invoke(creature.Position);
        return true;
    }

    public bool TrySelectItem(int itemId)
    {
        var item = _query?.GetItemView(itemId);
        if (item is null)
            return false;

        ClearAreaSelection();
        SelectedItemId = itemId;
        SelectedTile = new Vector2I(item.Position.X, item.Position.Y);
        SelectedDwarfId = null;
        SelectedCreatureId = null;
        SelectedBuildingId = null;
        TileSelectionCommitted?.Invoke(item.Position);
        return true;
    }

    public void SelectArea(Vector2I from, Vector2I to)
    {
        var normalized = NormalizeRect(from, to);
        if (normalized.from == normalized.to)
        {
            SelectRawTile(normalized.from.X, normalized.from.Y);
            return;
        }

        SelectedArea = normalized;
        SelectedTile = null;
        SelectedDwarfId = null;
        SelectedCreatureId = null;
        SelectedBuildingId = null;
        SelectedItemId = null;
        AreaSelectionCommitted?.Invoke(
            new Vec3i(normalized.from.X, normalized.from.Y, _currentZ),
            new Vec3i(normalized.to.X, normalized.to.Y, _currentZ));
    }

    public void SetCurrentZ(int z)   => _currentZ = z;

    public void UseExternalHoveredTile(Vector2I hoveredTile, HoverSelectionMode selectionMode = HoverSelectionMode.QueryTile)
    {
        _externalHoverSelectionMode = selectionMode;
        HoveredTile = hoveredTile;
        if (IsDragging)
            DragCurrent = HoveredTile;
    }

    public override void _Process(double delta)
    {
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

    // â”€â”€ Godot Input â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
            HandleMouseButton(mb);
        else if (@event is InputEventMouseMotion && IsDragging)
            GetViewport().SetInputAsHandled(); // just suppress so UI doesn't get stray hover events
        else if (@event is InputEventKey key && key.Pressed && !key.Echo)
            HandleKeyPress(key.Keycode);
    }

    // â”€â”€ Private helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
                var selection = NormalizeRect(DragStart.Value, DragCurrent.Value);
                if (selection.from != selection.to)
                {
                    SelectArea(selection.from, selection.to);
                }
                else if (_externalHoverSelectionMode == HoverSelectionMode.RawTile)
                {
                    SelectRawTile(DragStart.Value.X, DragStart.Value.Y);
                }
                else
                {
                    TrySelectAt(DragStart.Value.X, DragStart.Value.Y);
                }
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
        ClearAreaSelection();
        SelectedTile       = null;
        SelectedDwarfId    = null;
        SelectedCreatureId = null;
        SelectedBuildingId = null;
        SelectedItemId     = null;
    }

    private void TrySelectAt(int x, int y)
    {
        if (_query is null)
        {
            ClearAreaSelection();
            SelectedTile       = null;
            SelectedDwarfId    = null;
            SelectedCreatureId = null;
            SelectedBuildingId = null;
            SelectedItemId     = null;
            return;
        }

        var tile = _query.QueryTile(new Vec3i(x, y, _currentZ));

        var dwarf = tile.Dwarves.FirstOrDefault();
        if (dwarf is not null)
        {
            ClearAreaSelection();
            SelectedDwarfId    = dwarf.Id;
            SelectedCreatureId = null;
            SelectedBuildingId = null;
            SelectedItemId     = null;
            SelectedTile       = new Vector2I(x, y);
            TileSelectionCommitted?.Invoke(new Vec3i(x, y, _currentZ));
            return;
        }

        var creature = tile.Creatures.FirstOrDefault();
        if (creature is not null)
        {
            ClearAreaSelection();
            SelectedCreatureId = creature.Id;
            SelectedDwarfId    = null;
            SelectedBuildingId = null;
            SelectedItemId     = null;
            SelectedTile       = new Vector2I(x, y);
            TileSelectionCommitted?.Invoke(new Vec3i(x, y, _currentZ));
            return;
        }

        if (tile.Building is not null)
        {
            ClearAreaSelection();
            SelectedBuildingId = tile.Building.Id;
            SelectedDwarfId    = null;
            SelectedCreatureId = null;
            SelectedTile       = new Vector2I(x, y);
            SelectedItemId     = null;
            TileSelectionCommitted?.Invoke(new Vec3i(x, y, _currentZ));
            return;
        }

        // Check for items on the ground
        var item = tile.Items.FirstOrDefault();
        if (item is not null)
        {
            ClearAreaSelection();
            SelectedItemId     = item.Id;
            SelectedDwarfId    = null;
            SelectedCreatureId = null;
            SelectedBuildingId = null;
            SelectedTile       = new Vector2I(x, y);
            TileSelectionCommitted?.Invoke(new Vec3i(x, y, _currentZ));
            return;
        }

        // Nothing entity-like found â€” select the raw tile
        ClearAreaSelection();
        SelectedTile       = new Vector2I(x, y);
        SelectedDwarfId    = null;
        SelectedCreatureId = null;
        SelectedBuildingId = null;
        SelectedItemId     = null;
        TileSelectionCommitted?.Invoke(new Vec3i(x, y, _currentZ));
    }

    private void SelectRawTile(int x, int y)
    {
        ClearAreaSelection();
        SelectedTile = new Vector2I(x, y);
        SelectedDwarfId = null;
        SelectedCreatureId = null;
        SelectedBuildingId = null;
        SelectedItemId = null;
        TileSelectionCommitted?.Invoke(new Vec3i(x, y, _currentZ));
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
        return NormalizeRect(DragStart.Value, DragCurrent.Value);
    }

    public (Vector2I from, Vector2I to)? GetSelectedAreaRect() => SelectedArea;

    private void ClearAreaSelection() => SelectedArea = null;

    private static (Vector2I from, Vector2I to) NormalizeRect(Vector2I a, Vector2I b)
    {
        return (
            new Vector2I(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y)),
            new Vector2I(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y)));
    }
}

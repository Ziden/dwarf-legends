using DwarfFortress.GameLogic.Systems;
using Godot;

namespace DwarfFortress.GodotClient.UI;


/// <summary>
/// Routes the current client selection to the appropriate right-side panel.
/// Kept separate from GameRoot so panel presentation logic can evolve without
/// pulling in simulation startup, rendering, or viewport concerns.
/// </summary>
public sealed class SelectionPanelRouter
{
    private readonly InputController? _input;
    private readonly WorldQuerySystem? _query;
    private readonly SelectionViewPanel? _selectionView;
    private readonly TileInfoPanel? _tileInfo;
    private readonly DwarfPanel? _dwarfPanel;
    private readonly WorkshopPanel? _workshopPanel;

    public SelectionPanelRouter(
        InputController? input,
        WorldQuerySystem? query,
        SelectionViewPanel? selectionView,
        TileInfoPanel? tileInfo,
        DwarfPanel? dwarfPanel,
        WorkshopPanel? workshopPanel)
    {
        _input = input;
        _query = query;
        _selectionView = selectionView;
        _tileInfo = tileInfo;
        _dwarfPanel = dwarfPanel;
        _workshopPanel = workshopPanel;
    }

    public void Refresh(int currentZ)
    {
        if (_input is null || _query is null)
            return;

        if (_input.GetSelectedAreaRect() is { } selectedArea && selectedArea.from != selectedArea.to)
        {
            _selectionView?.Refresh(selectedArea, currentZ);
            _selectionView?.Show();
            _tileInfo?.Hide();
            _dwarfPanel?.Hide();
            _workshopPanel?.Hide();
            return;
        }

        if (_input.SelectedDwarfId is int dwarfId)
        {
            var dwarf = _query.GetDwarfView(dwarfId);
            if (dwarf is not null && _dwarfPanel is not null)
            {
                _selectionView?.Hide();
                _tileInfo?.Hide();
                _dwarfPanel?.ShowDwarf(dwarf);
                _workshopPanel?.Hide();
                return;
            }
        }

        if (_input.SelectedCreatureId is int creatureId)
        {
            var creature = _query.GetCreatureView(creatureId);
            if (creature is not null && _dwarfPanel is not null)
            {
                _selectionView?.Hide();
                _tileInfo?.Hide();
                _dwarfPanel?.ShowCreature(creature);
                _workshopPanel?.Hide();
                return;
            }
        }

        if (_input.SelectedBuildingId is int buildingId)
        {
            var building = _query.GetBuildingView(buildingId);
            if (building is not null && building.IsWorkshop && _workshopPanel is not null)
            {
                _selectionView?.Hide();
                _tileInfo?.Hide();
                _workshopPanel?.ShowBuilding(building);
                _dwarfPanel?.Hide();
                return;
            }
        }

        if (_input.SelectedItemId is int itemId)
        {
            var item = _query.GetItemView(itemId);
            if (item is not null && _tileInfo is not null)
            {
                _selectionView?.Hide();
                _tileInfo.ShowItem(item);
                _tileInfo.Show();
                _dwarfPanel?.Hide();
                _workshopPanel?.Hide();
                return;
            }
        }

        if (_input.SelectedTile is Vector2I selectedTile)
        {
            ShowTileInfo(selectedTile, currentZ);
            return;
        }

        HideAllPanels();
    }

    private void ShowTileInfo(Vector2I selectedTile, int currentZ)
    {
        _selectionView?.Hide();
        _tileInfo?.Refresh(selectedTile, currentZ);
        _tileInfo?.Show();
        _dwarfPanel?.Hide();
        _workshopPanel?.Hide();
    }

    private void HideAllPanels()
    {
        _selectionView?.Hide();
        _tileInfo?.Hide();
        _dwarfPanel?.Hide();
        _workshopPanel?.Hide();
    }
}

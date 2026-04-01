using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Systems;
using Godot;

/// <summary>Right-side panel showing detailed info for the selected tile.</summary>
public partial class TileInfoPanel : PanelContainer
{
    private Label? _label;
    private WorldQuerySystem? _query;

    public override void _Ready()
    {
        _label = GetNode<Label>("%ContentLabel");
    }

    public void Setup(GameSimulation sim) => _query = sim.Context.Get<WorldQuerySystem>();

    public void Refresh(Vector2I selectedTile, int z)
    {
        if (_label is null || _query is null)
            return;

        _label.Text = TileInspectionFormatter.BuildDetailedText(_query, selectedTile, z);
    }
}

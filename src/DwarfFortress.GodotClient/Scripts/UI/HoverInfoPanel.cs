using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Systems;
using Godot;

namespace DwarfFortress.GodotClient.UI;


/// <summary>Compact overlay showing the hovered tile's basic composition (row 1) and any units on it (row 2).</summary>
public partial class HoverInfoPanel : PanelContainer
{
    private const float MinPanelWidth = 200f;
    private const float ViewportWidthPadding = 16f;

    private Label? _staticLabel;
    private Label? _unitsLabel;
    private WorldQuerySystem? _query;

    public override void _Ready()
    {
        _staticLabel = GetNode<Label>("%StaticLabel");
        _unitsLabel  = GetNode<Label>("%UnitsLabel");
    }

    public void Setup(GameSimulation sim) => _query = sim.Context.Get<WorldQuerySystem>();

    public void Refresh(Vector2I hovered, int z)
    {
        if (_staticLabel is null || _query is null) return;

        _staticLabel.Text = TileInspectionFormatter.BuildHoverSummary(_query, hovered, z);

        var units = TileInspectionFormatter.BuildHoverUnitsSummary(_query, hovered, z);
        if (_unitsLabel is not null)
        {
            _unitsLabel.Text    = units;
            _unitsLabel.Visible = units.Length > 0;
        }

        ResizeToFitContent();
    }

    private void ResizeToFitContent()
    {
        var minimumSize = GetCombinedMinimumSize();
        var viewportWidth = GetViewportRect().Size.X;
        var maxWidth = Mathf.Max(MinPanelWidth, viewportWidth - (OffsetLeft + ViewportWidthPadding));
        var width = Mathf.Clamp(minimumSize.X, MinPanelWidth, maxWidth);
        Size = new Vector2(width, minimumSize.Y);
    }
}

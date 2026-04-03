using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Godot;

namespace DwarfFortress.GodotClient.UI;

public partial class SelectionViewPanel : PanelContainer
{
    private Label? _summaryLabel;
    private ItemSelectionList? _selectionList;
    private GameSimulation? _simulation;
    private WorldMap? _map;
    private WorldQuerySystem? _query;
    private DataManager? _data;

    public string DebugSummaryText { get; private set; } = string.Empty;
    public string DebugEntriesText { get; private set; } = string.Empty;

    public override void _Ready()
    {
        _summaryLabel = GetNode<Label>("%SummaryLabel");
        _selectionList = GetNode<ItemSelectionList>("%SelectionList");
    }

    public void Setup(GameSimulation simulation)
    {
        _simulation = simulation;
        _map = simulation.Context.Get<WorldMap>();
        _query = simulation.Context.Get<WorldQuerySystem>();
        _data = simulation.Context.TryGet<DataManager>();
    }

    public void Refresh((Vector2I from, Vector2I to) selection, int z)
    {
        if (_summaryLabel is null || _selectionList is null || _simulation is null || _map is null || _query is null || _data is null)
            return;

        var view = SelectionResourceViewBuilder.BuildAreaView(_query, _map, _data, selection.from, selection.to, z);
        var tileCount = view.TotalTileCount;
        var groupCount = view.Groups.Length;
        _summaryLabel.Text = tileCount <= 0
            ? "Selection View\nNo visible tiles in this selection."
            : $"Selection View\n{tileCount} tile{(tileCount == 1 ? string.Empty : "s")} across {groupCount} type{(groupCount == 1 ? string.Empty : "s")}.";

        var handIcon = PixelArtFactory.GetUiIcon(UiIconIds.Hand);
        var entries = new List<ItemSelectionEntry>(view.Groups.Length);
        foreach (var group in view.Groups)
        {
            var actionable = group.ActionKind != SelectionResourceActionKind.None;
            Action? onPressed = actionable ? () => SelectionResourceViewBuilder.DispatchAction(_simulation!, group) : null;
            entries.Add(new ItemSelectionEntry(
                Id: $"selection-group:{group.Id}",
                Title: group.Title,
                Subtitle: string.Empty,
                Details: group.Details,
                Status: $"{group.TileCount} tile{(group.TileCount == 1 ? string.Empty : "s")}",
                StatusColor: actionable ? new Color(0.44f, 0.85f, 0.48f) : new Color(0.70f, 0.70f, 0.70f),
                Icon: group.Icon,
                ActionLabel: actionable ? SelectionResourceViewBuilder.ResolveActionLabel(group.ActionKind) : string.Empty,
                IsEnabled: actionable,
                OnPressed: onPressed,
                ActionIcon: actionable ? handIcon : null));
        }

        _selectionList.SetEntries(entries);
        DebugSummaryText = _summaryLabel.Text;
        DebugEntriesText = string.Join("\n", view.Groups.Select(group => $"{group.Title}|{group.TileCount}|{group.ActionKind}"));
    }
}

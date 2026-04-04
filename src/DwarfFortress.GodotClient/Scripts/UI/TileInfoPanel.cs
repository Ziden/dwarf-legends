using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Godot;

namespace DwarfFortress.GodotClient.UI;


/// <summary>Right-side panel showing detailed info for the selected tile.</summary>
public partial class TileInfoPanel : PanelContainer
{
    private static readonly Color ContainedStatusColor = new(0.78f, 0.78f, 0.78f, 1f);
    private static readonly Color EmptyStatusColor = new(0.70f, 0.70f, 0.70f, 1f);

    private CenterContainer? _selectedItemIconContainer;
    private TextureRect? _selectedItemIcon;
    private Label? _label;
    private HSeparator? _resourceActionsSeparator;
    private Label? _resourceActionsHeading;
    private ItemSelectionList? _resourceActionsList;
    private HSeparator? _containerContentsSeparator;
    private Label? _containerContentsHeading;
    private ItemSelectionList? _containerContentsList;
    private GameSimulation? _simulation;
    private WorldMap? _map;
    private WorldQuerySystem? _query;
    private DataManager? _data;
    private Action<int>? _showItemDetails;

    public string DebugActionSummaryText { get; private set; } = string.Empty;

    public override void _Ready()
    {
        _selectedItemIconContainer = GetNode<CenterContainer>("%SelectedItemIconContainer");
        _selectedItemIcon = GetNode<TextureRect>("%SelectedItemIcon");
        _label = GetNode<Label>("%ContentLabel");
        _resourceActionsSeparator = GetNode<HSeparator>("%ResourceActionsSeparator");
        _resourceActionsHeading = GetNode<Label>("%ResourceActionsHeading");
        _resourceActionsList = GetNode<ItemSelectionList>("%ResourceActionsList");
        _containerContentsSeparator = GetNode<HSeparator>("%ContainerContentsSeparator");
        _containerContentsHeading = GetNode<Label>("%ContainerContentsHeading");
        _containerContentsList = GetNode<ItemSelectionList>("%ContainerContentsList");

        if (_selectedItemIcon is not null)
        {
            _selectedItemIcon.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
            _selectedItemIcon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        }
    }

    public void Setup(GameSimulation sim)
    {
        _simulation = sim;
        _map = sim.Context.Get<WorldMap>();
        _query = sim.Context.Get<WorldQuerySystem>();
        _data = sim.Context.TryGet<DataManager>();
    }

    public void SetItemInspector(Action<int> showItemDetails) => _showItemDetails = showItemDetails;

    public void Refresh(Vector2I selectedTile, int z)
    {
        if (_label is null || _query is null)
            return;

        var result = _query.QueryTile(new Vec3i(selectedTile.X, selectedTile.Y, z));
        _label.Text = TileInspectionFormatter.BuildDetailedText(result);
        SetSelectedItemIcon(null);
        RefreshTileResourceActions(result);
        RefreshTileContainerContents(result);
    }

    public void ShowItem(ItemView item)
    {
        if (_label is null || _query is null)
            return;

        var tileResult = _query.QueryTile(item.Position);
        _label.Text = TileInspectionFormatter.BuildItemDetailedText(item, tileResult);
        SetSelectedItemIcon(PixelArtFactory.GetItem(item.DefId, item.MaterialId));
        SetResourceActionsVisible(false);
        DebugActionSummaryText = string.Empty;
        RefreshFocusedItemContents(item);
    }

    private void RefreshTileResourceActions(TileQueryResult tileResult)
    {
        if (_resourceActionsSeparator is null || _resourceActionsHeading is null || _resourceActionsList is null || _query is null || _map is null || _data is null || _simulation is null)
            return;

        var view = SelectionResourceViewBuilder.BuildSingleTileActionView(_query, _map, _data, tileResult);
        DebugActionSummaryText = string.Join("\n", view.Groups.Select(group => $"{group.Title}|{group.ActionKind}"));
        if (view.Groups.Length == 0)
        {
            _resourceActionsList.SetEntries(Array.Empty<ItemSelectionEntry>());
            SetResourceActionsVisible(false);
            return;
        }

        var handIcon = PixelArtFactory.GetUiIcon(UiIconIds.Hand);
        _resourceActionsHeading.Text = "Resource Actions";
        _resourceActionsList.SetEntries(view.Groups.Select(group => new ItemSelectionEntry(
            Id: $"resource-action:{group.Id}",
            Title: group.Title,
            Subtitle: group.CategoryLabel,
            Details: group.Details,
            Status: "Ready",
            StatusColor: new Color(0.44f, 0.85f, 0.48f),
            Icon: group.Icon,
            ActionLabel: SelectionResourceViewBuilder.ResolveActionLabel(group.ActionKind),
            IsEnabled: true,
            OnPressed: () => SelectionResourceViewBuilder.DispatchAction(_simulation, group),
            ActionIcon: handIcon)).ToArray());
        SetResourceActionsVisible(true);
    }

    private void RefreshTileContainerContents(TileQueryResult tileResult)
    {
        if (_containerContentsSeparator is null || _containerContentsHeading is null || _containerContentsList is null)
            return;

        var containerItems = tileResult.Items
            .Where(item => item.Storage is not null)
            .OrderBy(item => item.Id)
            .ToArray();
        var containerEntities = tileResult.Containers
            .OrderBy(container => container.Id)
            .ToArray();

        if (containerItems.Length == 0 && containerEntities.Length == 0)
        {
            _containerContentsList.SetEntries(Array.Empty<ItemSelectionEntry>());
            SetContainerContentsVisible(false);
            return;
        }

        var entries = new List<ItemSelectionEntry>();
        foreach (var containerItem in containerItems)
        {
            var containerName = ItemTextFormatter.GetDisplayName(containerItem, includeMaterialPrefix: true);
            var containedItems = containerItem.Storage!.Contents;
            if (containerItem.Storage.StoredItemCount == 0)
            {
                entries.Add(new ItemSelectionEntry(
                    Id: $"container-empty:{containerItem.Id}",
                    Title: containerName,
                    Subtitle: string.Empty,
                    Details: ItemTextFormatter.BuildWeightText(containerItem),
                    Status: "Empty",
                    StatusColor: EmptyStatusColor,
                    Icon: PixelArtFactory.GetItem(containerItem.DefId, containerItem.MaterialId),
                    ActionLabel: string.Empty,
                    IsEnabled: false,
                    OnPressed: null));
                continue;
            }

            foreach (var containedItem in containedItems)
                entries.Add(BuildContainedItemEntry(containerItem.Id, containerName, containedItem));
        }

        foreach (var container in containerEntities)
        {
            var containerName = GetContainerEntityDisplayName(container);
            var containedItems = container.Storage.Contents;
            if (container.Storage.StoredItemCount == 0)
            {
                entries.Add(new ItemSelectionEntry(
                    Id: $"container-entity-empty:{container.Id}",
                    Title: containerName,
                    Subtitle: string.Empty,
                    Details: string.Empty,
                    Status: "Empty",
                    StatusColor: EmptyStatusColor,
                    Icon: PixelArtFactory.GetItem(container.DefId),
                    ActionLabel: string.Empty,
                    IsEnabled: false,
                    OnPressed: null));
                continue;
            }

            foreach (var containedItem in containedItems)
                entries.Add(BuildContainedItemEntry(container.Id, containerName, containedItem));
        }

        _containerContentsHeading.Text = "Contents";
        _containerContentsList.SetEntries(entries);
        SetContainerContentsVisible(true);
    }

    private void RefreshFocusedItemContents(ItemView item)
    {
        if (_containerContentsSeparator is null || _containerContentsHeading is null || _containerContentsList is null)
            return;

        if (item.Storage is null)
        {
            _containerContentsList.SetEntries(Array.Empty<ItemSelectionEntry>());
            SetContainerContentsVisible(false);
            return;
        }

        var entries = new List<ItemSelectionEntry>();
        var containerName = ItemTextFormatter.GetDisplayName(item, includeMaterialPrefix: true);
        if (item.Storage.StoredItemCount == 0)
        {
            entries.Add(new ItemSelectionEntry(
                Id: $"item-empty:{item.Id}",
                Title: containerName,
                Subtitle: string.Empty,
                Details: ItemTextFormatter.BuildWeightText(item),
                Status: "Empty",
                StatusColor: EmptyStatusColor,
                Icon: PixelArtFactory.GetItem(item.DefId, item.MaterialId),
                ActionLabel: string.Empty,
                IsEnabled: false,
                OnPressed: null));
        }
        else
        {
            foreach (var containedItem in item.Storage.Contents)
                entries.Add(BuildContainedItemEntry(item.Id, containerName, containedItem));
        }

        _containerContentsHeading.Text = "Contents";
        _containerContentsList.SetEntries(entries);
        SetContainerContentsVisible(true);
    }

    private ItemSelectionEntry BuildContainedItemEntry(int containerItemId, string containerName, ItemView containedItem)
    {
        return new ItemSelectionEntry(
            Id: $"contained:{containerItemId}:{containedItem.Id}",
            Title: ItemTextFormatter.BuildContainedCardTitle(containedItem),
            Subtitle: containerName,
            Details: ItemTextFormatter.BuildContainedCardDetails(containedItem),
            Status: string.Empty,
            StatusColor: ContainedStatusColor,
            Icon: PixelArtFactory.GetItem(containedItem.DefId, containedItem.MaterialId),
            ActionLabel: "Inspect",
            IsEnabled: true,
            OnPressed: () => _showItemDetails?.Invoke(containedItem.Id));
    }

    private string GetContainerEntityDisplayName(ContainerEntityView container)
    {
        var itemDef = _data?.Items.GetOrNull(container.DefId);
        if (itemDef is not null)
            return itemDef.DisplayName;

        var buildingDef = _data?.Buildings.GetOrNull(container.DefId);
        if (buildingDef is not null)
            return buildingDef.DisplayName;

        return ItemTextFormatter.FormatToken(container.DefId);
    }

    private void SetContainerContentsVisible(bool visible)
    {
        _containerContentsSeparator!.Visible = visible;
        _containerContentsHeading!.Visible = visible;
        _containerContentsList!.Visible = visible;
    }

    private void SetResourceActionsVisible(bool visible)
    {
        _resourceActionsSeparator!.Visible = visible;
        _resourceActionsHeading!.Visible = visible;
        _resourceActionsList!.Visible = visible;
    }

    private void SetSelectedItemIcon(Texture2D? texture)
    {
        if (_selectedItemIconContainer is null || _selectedItemIcon is null)
            return;

        _selectedItemIcon.Texture = texture;
        _selectedItemIconContainer.Visible = texture is not null;
    }

}

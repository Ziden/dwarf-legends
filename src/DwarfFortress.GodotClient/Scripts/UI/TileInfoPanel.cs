using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    private static readonly Color InactiveTabColor = new(0.86f, 0.86f, 0.86f, 1f);

    private readonly ButtonGroup _targetTabGroup = new();
    private readonly Dictionary<string, Button> _targetTabButtons = new();
    private readonly List<InspectorTarget> _currentTargets = new();
    private List<string> _targetTabOrder = new();

    private ScrollContainer? _targetTabsScroll;
    private VBoxContainer? _targetTabs;
    private VSeparator? _targetTabsSeparator;
    private CenterContainer? _selectedItemIconContainer;
    private TextureRect? _selectedItemIcon;
    private Label? _label;
    private HSeparator? _occupantsSeparator;
    private Label? _occupantsHeading;
    private ItemSelectionList? _occupantsList;
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
    private Action<int>? _showDwarfDetails;
    private Action<int>? _showCreatureDetails;
    private Action<int>? _showBuildingDetails;
    private TileQueryResult? _currentTileResult;
    private string? _activeTargetKey;
    private string? _lastSelectionKey;

    public string DebugActionSummaryText { get; private set; } = string.Empty;
    public string DebugOccupantSummaryText { get; private set; } = string.Empty;
    public string DebugTargetSummaryText { get; private set; } = string.Empty;
    public string DebugSelectedTargetKey { get; private set; } = string.Empty;

    public override void _Ready()
    {
        _targetTabsScroll = GetNode<ScrollContainer>("%TargetTabsScroll");
        _targetTabs = GetNode<VBoxContainer>("%TargetTabs");
        _targetTabsSeparator = GetNode<VSeparator>("%TargetTabsSeparator");
        _selectedItemIconContainer = GetNode<CenterContainer>("%SelectedItemIconContainer");
        _selectedItemIcon = GetNode<TextureRect>("%SelectedItemIcon");
        _label = GetNode<Label>("%ContentLabel");
        _occupantsSeparator = GetNode<HSeparator>("%OccupantsSeparator");
        _occupantsHeading = GetNode<Label>("%OccupantsHeading");
        _occupantsList = GetNode<ItemSelectionList>("%OccupantsList");
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

    public void SetSelectionHandlers(
        Action<int> showItemDetails,
        Action<int> showDwarfDetails,
        Action<int> showCreatureDetails,
        Action<int> showBuildingDetails)
    {
        _showItemDetails = showItemDetails;
        _showDwarfDetails = showDwarfDetails;
        _showCreatureDetails = showCreatureDetails;
        _showBuildingDetails = showBuildingDetails;
    }

    public void Refresh(Vector2I selectedTile, int z)
        => ShowSelection(selectedTile, z);

    public void ShowItem(ItemView item)
        => ShowSelection(new Vector2I(item.Position.X, item.Position.Y), item.Position.Z, itemId: item.Id);

    public void ShowSelection(
        Vector2I selectedTile,
        int z,
        int? dwarfId = null,
        int? creatureId = null,
        int? buildingId = null,
        int? itemId = null)
    {
        if (_label is null || _query is null)
            return;

        var tileResult = _query.QueryTile(new Vec3i(selectedTile.X, selectedTile.Y, z));
        var selectedDwarf = dwarfId is int selectedDwarfId ? _query.GetDwarfView(selectedDwarfId) : null;
        var selectedCreature = creatureId is int selectedCreatureId ? _query.GetCreatureView(selectedCreatureId) : null;
        var selectedBuilding = buildingId is int selectedBuildingId ? _query.GetBuildingView(selectedBuildingId) : null;
        var selectedItem = itemId is int selectedItemId ? _query.GetItemView(selectedItemId) : null;
        var targets = BuildInspectorTargets(tileResult, selectedDwarf, selectedCreature, selectedBuilding, selectedItem);
        if (targets.Count == 0)
        {
            _label.Text = "-";
            SetSelectedItemIcon(null);
            SetTargetTabsVisible(false);
            SetOccupantsVisible(false);
            SetResourceActionsVisible(false);
            SetContainerContentsVisible(false);
            DebugActionSummaryText = string.Empty;
            DebugOccupantSummaryText = string.Empty;
            DebugTargetSummaryText = string.Empty;
            DebugSelectedTargetKey = string.Empty;
            return;
        }

        _currentTileResult = tileResult;
        _currentTargets.Clear();
        _currentTargets.AddRange(targets);

        var preferredTargetKey = BuildPreferredTargetKey(selectedDwarf, selectedCreature, selectedBuilding, selectedItem);
        var selectionKey = $"{selectedTile.X}:{selectedTile.Y}:{z}|{preferredTargetKey}";
        var activeTarget = ResolveActiveTarget(targets, preferredTargetKey, selectionKey);

        DebugTargetSummaryText = string.Join("\n", targets.Select(target => $"{target.Key}|{target.Title}|{target.Kind}"));
        DebugOccupantSummaryText = DebugTargetSummaryText;
        RefreshTargetTabs(targets, activeTarget.Key);
        RenderTarget(tileResult, activeTarget);
        _lastSelectionKey = selectionKey;
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

    private void RefreshTargetTabs(IReadOnlyList<InspectorTarget> targets, string activeTargetKey)
    {
        if (_targetTabs is null)
            return;

        var shouldShow = targets.Count > 1;
        SetTargetTabsVisible(shouldShow);
        if (!shouldShow)
            return;

        var newOrder = targets.Select(target => target.Key).ToList();
        bool rebuild = _targetTabOrder.Count != newOrder.Count || !_targetTabOrder.SequenceEqual(newOrder);
        if (rebuild)
        {
            foreach (Node child in _targetTabs.GetChildren())
                child.QueueFree();

            _targetTabButtons.Clear();
            _targetTabOrder = newOrder;
            foreach (var target in targets)
            {
                var button = CreateTargetTabButton(target);
                _targetTabButtons[target.Key] = button;
                _targetTabs.AddChild(button);
            }
        }

        foreach (var target in targets)
            UpdateTargetTabButton(_targetTabButtons[target.Key], target, target.Key == activeTargetKey);
    }

    private Button CreateTargetTabButton(InspectorTarget target)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(56, 56),
            FocusMode = FocusModeEnum.None,
            ToggleMode = true,
            ButtonGroup = _targetTabGroup,
            ExpandIcon = true,
            IconAlignment = HorizontalAlignment.Center,
            Text = string.Empty,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        var capturedKey = target.Key;
        button.Pressed += () => ShowTarget(capturedKey);
        return button;
    }

    private static void UpdateTargetTabButton(Button button, InspectorTarget target, bool isActive)
    {
        button.Icon = target.Icon;
        button.TooltipText = target.Tooltip;
        button.ButtonPressed = isActive;
        button.Modulate = isActive ? Colors.White : InactiveTabColor;
    }

    private void ShowTarget(string targetKey)
    {
        if (_currentTileResult is null)
            return;

        var target = _currentTargets.FirstOrDefault(candidate => candidate.Key == targetKey);
        if (target is null)
            return;

        RenderTarget(_currentTileResult, target);
        RefreshTargetTabs(_currentTargets, target.Key);
    }

    private void RenderTarget(TileQueryResult tileResult, InspectorTarget target)
    {
        if (_label is null)
            return;

        _activeTargetKey = target.Key;
        DebugSelectedTargetKey = target.Key;
        SetOccupantsVisible(false);

        switch (target.Kind)
        {
            case InspectorTargetKind.Tile:
                _label.Text = TileInspectionFormatter.BuildDetailedText(tileResult);
                SetSelectedItemIcon(target.Icon);
                RefreshTileResourceActions(tileResult);
                RefreshTileContainerContents(tileResult);
                break;

            case InspectorTargetKind.Dwarf:
                _label.Text = BuildDwarfDetailedText(target.Dwarf!);
                SetSelectedItemIcon(target.Icon);
                SetResourceActionsVisible(false);
                DebugActionSummaryText = string.Empty;
                SetContainerContentsVisible(false);
                break;

            case InspectorTargetKind.Creature:
                _label.Text = BuildCreatureDetailedText(target.Creature!);
                SetSelectedItemIcon(target.Icon);
                SetResourceActionsVisible(false);
                DebugActionSummaryText = string.Empty;
                SetContainerContentsVisible(false);
                break;

            case InspectorTargetKind.Building:
                _label.Text = BuildBuildingDetailedText(target.Building!);
                SetSelectedItemIcon(target.Icon);
                SetResourceActionsVisible(false);
                DebugActionSummaryText = string.Empty;
                SetContainerContentsVisible(false);
                break;

            case InspectorTargetKind.Item:
                _label.Text = TileInspectionFormatter.BuildItemDetailedText(target.Item!, tileResult);
                SetSelectedItemIcon(target.Icon);
                SetResourceActionsVisible(false);
                DebugActionSummaryText = string.Empty;
                RefreshFocusedItemContents(target.Item!);
                break;

            case InspectorTargetKind.Container:
                _label.Text = BuildContainerDetailedText(target.Container!);
                SetSelectedItemIcon(target.Icon);
                SetResourceActionsVisible(false);
                DebugActionSummaryText = string.Empty;
                RefreshFocusedContainerContents(target.Container!);
                break;
        }
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

    private void RefreshFocusedContainerContents(ContainerEntityView container)
    {
        if (_containerContentsSeparator is null || _containerContentsHeading is null || _containerContentsList is null)
            return;

        var entries = new List<ItemSelectionEntry>();
        var containerName = GetContainerEntityDisplayName(container);
        if (container.Storage.StoredItemCount == 0)
        {
            entries.Add(new ItemSelectionEntry(
                Id: $"container-focus-empty:{container.Id}",
                Title: containerName,
                Subtitle: string.Empty,
                Details: string.Empty,
                Status: "Empty",
                StatusColor: EmptyStatusColor,
                Icon: PixelArtFactory.GetItem(container.DefId),
                ActionLabel: string.Empty,
                IsEnabled: false,
                OnPressed: null));
        }
        else
        {
            foreach (var containedItem in container.Storage.Contents)
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

    private List<InspectorTarget> BuildInspectorTargets(
        TileQueryResult tileResult,
        DwarfView? selectedDwarf,
        CreatureView? selectedCreature,
        BuildingView? selectedBuilding,
        ItemView? selectedItem)
    {
        var targets = new List<InspectorTarget>();
        AddTarget(targets, CreateTileTarget(tileResult));

        foreach (var dwarf in tileResult.Dwarves.OrderBy(dwarf => dwarf.Name, StringComparer.Ordinal))
            AddTarget(targets, CreateDwarfTarget(dwarf));

        foreach (var creature in tileResult.Creatures.OrderBy(creature => creature.DefId, StringComparer.Ordinal).ThenBy(creature => creature.Id))
            AddTarget(targets, CreateCreatureTarget(creature));

        if (tileResult.Building is not null)
            AddTarget(targets, CreateBuildingTarget(tileResult.Building));

        foreach (var item in tileResult.Items
                     .OrderByDescending(item => item.Corpse is not null)
                     .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
                     .ThenBy(item => item.Id))
        {
            AddTarget(targets, CreateItemTarget(item));
        }

        foreach (var container in tileResult.Containers.OrderBy(container => container.Id))
            AddTarget(targets, CreateContainerTarget(container));

        if (selectedDwarf is not null)
            AddTarget(targets, CreateDwarfTarget(selectedDwarf));

        if (selectedCreature is not null)
            AddTarget(targets, CreateCreatureTarget(selectedCreature));

        if (selectedBuilding is not null)
            AddTarget(targets, CreateBuildingTarget(selectedBuilding));

        if (selectedItem is not null)
            AddTarget(targets, CreateItemTarget(selectedItem));

        return targets;
    }

    private static void AddTarget(List<InspectorTarget> targets, InspectorTarget target)
    {
        if (targets.Any(existing => existing.Key == target.Key))
            return;

        targets.Add(target);
    }

    private InspectorTarget ResolveActiveTarget(IReadOnlyList<InspectorTarget> targets, string preferredTargetKey, string selectionKey)
    {
        if (string.Equals(_lastSelectionKey, selectionKey, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(_activeTargetKey))
        {
            var preservedTarget = targets.FirstOrDefault(target => target.Key == _activeTargetKey);
            if (preservedTarget is not null)
                return preservedTarget;
        }

        var preferredTarget = targets.FirstOrDefault(target => target.Key == preferredTargetKey);
        return preferredTarget ?? targets[0];
    }

    private static string BuildPreferredTargetKey(
        DwarfView? selectedDwarf,
        CreatureView? selectedCreature,
        BuildingView? selectedBuilding,
        ItemView? selectedItem)
    {
        if (selectedDwarf is not null)
            return $"dwarf:{selectedDwarf.Id}";

        if (selectedCreature is not null)
            return $"creature:{selectedCreature.Id}";

        if (selectedBuilding is not null)
            return $"building:{selectedBuilding.Id}";

        if (selectedItem is not null)
            return $"item:{selectedItem.Id}";

        return "tile";
    }

    private InspectorTarget CreateTileTarget(TileQueryResult tileResult)
    {
        var tile = tileResult.Tile;
        var title = tile is null ? "Tile" : ItemTextFormatter.FormatToken(tile.TileDefId);
        var tooltip = tile is null ? "Tile" : $"Tile: {title}";
        var icon = tile is null ? null : PixelArtFactory.GetTile(tile.TileDefId, tile.MaterialId);
        return new InspectorTarget("tile", InspectorTargetKind.Tile, title, tooltip, icon, Tile: tile);
    }

    private static InspectorTarget CreateDwarfTarget(DwarfView dwarf)
        => new(
            $"dwarf:{dwarf.Id}",
            InspectorTargetKind.Dwarf,
            dwarf.Name,
            $"Dwarf: {dwarf.Name}",
            PixelArtFactory.GetDwarf(dwarf.Appearance),
            Dwarf: dwarf);

    private static InspectorTarget CreateCreatureTarget(CreatureView creature)
    {
        var title = ItemTextFormatter.FormatToken(creature.DefId);
        return new InspectorTarget(
            $"creature:{creature.Id}",
            InspectorTargetKind.Creature,
            title,
            $"Creature: {title}",
            PixelArtFactory.GetEntity(creature.DefId),
            Creature: creature);
    }

    private InspectorTarget CreateBuildingTarget(BuildingView building)
    {
        var title = _data?.Buildings.GetOrNull(building.BuildingDefId)?.DisplayName
            ?? ItemTextFormatter.FormatToken(building.BuildingDefId);
        return new InspectorTarget(
            $"building:{building.Id}",
            InspectorTargetKind.Building,
            title,
            $"Building: {title}",
            PixelArtFactory.GetBuilding(building.BuildingDefId),
            Building: building);
    }

    private static InspectorTarget CreateItemTarget(ItemView item)
    {
        var title = ItemTextFormatter.BuildContainedCardTitle(item);
        return new InspectorTarget(
            $"item:{item.Id}",
            InspectorTargetKind.Item,
            title,
            $"Item: {title}",
            PixelArtFactory.GetItem(item.DefId, item.MaterialId),
            Item: item);
    }

    private InspectorTarget CreateContainerTarget(ContainerEntityView container)
    {
        var title = GetContainerEntityDisplayName(container);
        return new InspectorTarget(
            $"container:{container.Id}",
            InspectorTargetKind.Container,
            title,
            $"Container: {title}",
            PixelArtFactory.GetItem(container.DefId),
            Container: container);
    }

    private string BuildDwarfDetailedText(DwarfView dwarf)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[{dwarf.Name}]");
        builder.AppendLine(FormatPosition(dwarf.Position));
        builder.AppendLine($"  {ItemTextFormatter.FormatToken(dwarf.ProfessionId)}");
        builder.AppendLine($"  Mood: {ItemTextFormatter.FormatToken(dwarf.Mood.ToString())}");
        builder.AppendLine($"  Happiness: {dwarf.Happiness:0}%");
        builder.AppendLine($"  Health: {dwarf.CurrentHealth:0.#}/{dwarf.MaxHealth:0.#}");
        if (dwarf.CurrentJob is not null)
            builder.AppendLine($"  Job: {ItemTextFormatter.FormatToken(dwarf.CurrentJob.JobDefId)} ({ItemTextFormatter.FormatToken(dwarf.CurrentJob.Status.ToString())})");
        if (dwarf.HauledItem is not null)
            builder.AppendLine($"  Hauling: {ItemTextFormatter.BuildContainedCardTitle(dwarf.HauledItem)}");
        if (dwarf.CarriedItems.Length > 0)
            builder.AppendLine($"  Carrying: {dwarf.CarriedItems.Length} item(s)");
        return builder.ToString().TrimEnd();
    }

    private static string BuildCreatureDetailedText(CreatureView creature)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[{ItemTextFormatter.FormatToken(creature.DefId)}]");
        builder.AppendLine(FormatPosition(creature.Position));
        builder.AppendLine($"  {(creature.IsHostile ? "Hostile creature" : "Creature")}");
        builder.AppendLine($"  Health: {creature.CurrentHealth:0.#}/{creature.MaxHealth:0.#}");
        builder.AppendLine($"  {(creature.IsConscious ? "Conscious" : "Unconscious")}");
        if (creature.HauledItem is not null)
            builder.AppendLine($"  Hauling: {ItemTextFormatter.BuildContainedCardTitle(creature.HauledItem)}");
        if (creature.CarriedItems.Length > 0)
            builder.AppendLine($"  Carrying: {creature.CarriedItems.Length} item(s)");
        return builder.ToString().TrimEnd();
    }

    private string BuildBuildingDetailedText(BuildingView building)
    {
        var title = _data?.Buildings.GetOrNull(building.BuildingDefId)?.DisplayName
            ?? ItemTextFormatter.FormatToken(building.BuildingDefId);
        var builder = new StringBuilder();
        builder.AppendLine($"[{title}]");
        builder.AppendLine(FormatPosition(building.Origin));
        builder.AppendLine($"  {(building.IsWorkshop ? "Workshop" : "Building")}");
        if (!string.IsNullOrWhiteSpace(building.MaterialId))
            builder.AppendLine($"  Material: {ItemTextFormatter.FormatToken(building.MaterialId!)}");
        if (building.StoredItemCount > 0)
            builder.AppendLine($"  Stored items: {building.StoredItemCount}");
        return builder.ToString().TrimEnd();
    }

    private string BuildContainerDetailedText(ContainerEntityView container)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[{GetContainerEntityDisplayName(container)}]");
        builder.AppendLine(FormatPosition(container.Position));
        if (container.Storage.Capacity is int capacity)
            builder.AppendLine($"  Storage: {container.Storage.StoredItemCount}/{capacity}");
        else
            builder.AppendLine($"  Storage: {container.Storage.StoredItemCount}");

        foreach (var line in BuildStorageSummary(container.Storage.Contents, maxEntries: 3))
            builder.AppendLine($"  {line}");

        return builder.ToString().TrimEnd();
    }

    private static IEnumerable<string> BuildStorageSummary(ItemView[] contents, int maxEntries)
    {
        if (contents.Length == 0)
        {
            yield return "Empty";
            yield break;
        }

        foreach (var group in contents
                     .GroupBy(ItemTextFormatter.BuildContainedCardTitle)
                     .OrderByDescending(group => group.Count())
                     .ThenBy(group => group.Key, StringComparer.Ordinal)
                     .Take(maxEntries))
        {
            yield return group.Count() > 1 ? $"{group.Count()}x {group.Key}" : group.Key;
        }

        if (contents.Length > maxEntries)
            yield return $"+{contents.Length - maxEntries} more";
    }

    private static string FormatPosition(Vec3i position)
        => $"({position.X}, {position.Y}, z:{position.Z})";

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

    private void SetTargetTabsVisible(bool visible)
    {
        _targetTabsScroll!.Visible = visible;
        _targetTabsSeparator!.Visible = visible;
    }

    private void SetOccupantsVisible(bool visible)
    {
        _occupantsSeparator!.Visible = visible;
        _occupantsHeading!.Visible = visible;
        _occupantsList!.Visible = visible;
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

    private enum InspectorTargetKind
    {
        Tile,
        Dwarf,
        Creature,
        Building,
        Item,
        Container,
    }

    private sealed record InspectorTarget(
        string Key,
        InspectorTargetKind Kind,
        string Title,
        string Tooltip,
        Texture2D? Icon,
        TileView? Tile = null,
        DwarfView? Dwarf = null,
        CreatureView? Creature = null,
        BuildingView? Building = null,
        ItemView? Item = null,
        ContainerEntityView? Container = null);

}

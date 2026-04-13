using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Systems;
using Godot;

namespace DwarfFortress.GodotClient.UI;


/// <summary>
/// Knowledge screen Ã¢â‚¬â€ displays building and recipe knowledge using the
/// shared GameLogic discovery state.
/// </summary>
public partial class KnowledgePanel : PanelContainer
{
    private GameSimulation? _simulation;
    private DiscoverySystem? _discovery;
    private DataManager? _data;
    private ItemSelectionList? _list;
    private Label? _detailTitle;
    private Label? _detailSubtitle;
    private Label? _detailDescription;
    private Label? _detailUnlocks;
    private readonly Dictionary<string, BuildingDiscoveryInfo> _buildingInfos = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecipeDiscoveryInfo> _recipeInfos = new(StringComparer.OrdinalIgnoreCase);

    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 8);
        margin.AddChild(vbox);

        var header = new Label { Text = "Knowledge" };
        header.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(header);
        vbox.AddChild(new HSeparator());

        var hSplit = new HSplitContainer();
        hSplit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hSplit.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        hSplit.SplitOffsets = new[] { 320 };
        vbox.AddChild(hSplit);

        var listVbox = new VBoxContainer();
        listVbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        listVbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        hSplit.AddChild(listVbox);

        var listHeader = new Label { Text = "Known And Hidden" };
        listVbox.AddChild(listHeader);

        _list = new ItemSelectionList
        {
            CustomMinimumSize = new Vector2(0, 300),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        listVbox.AddChild(_list);

        var detailVbox = new VBoxContainer();
        detailVbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        detailVbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        detailVbox.AddThemeConstantOverride("separation", 6);
        hSplit.AddChild(detailVbox);

        _detailTitle = new Label { Text = "Select an item" };
        _detailTitle.AddThemeFontSizeOverride("font_size", 16);
        detailVbox.AddChild(_detailTitle);

        _detailSubtitle = new Label();
        _detailSubtitle.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        detailVbox.AddChild(_detailSubtitle);

        detailVbox.AddChild(new HSeparator());

        _detailDescription = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _detailDescription.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        detailVbox.AddChild(_detailDescription);

        detailVbox.AddChild(new HSeparator());

        _detailUnlocks = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _detailUnlocks.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        detailVbox.AddChild(_detailUnlocks);

        var closeBtn = new Button { Text = "Close" };
        closeBtn.Pressed += () => Hide();
        vbox.AddChild(closeBtn);
    }

    public void Setup(GameSimulation sim)
    {
        _simulation = sim;
        _discovery = sim.Context.TryGet<DiscoverySystem>();
        _data = sim.Context.TryGet<DataManager>();
        Refresh();
    }

    public void Refresh()
    {
        if (_list is null || _data is null || _discovery is null)
            return;

        _buildingInfos.Clear();
        _recipeInfos.Clear();

        var entries = new List<ItemSelectionEntry>();

        foreach (var buildingInfo in _discovery.GetBuildingInfos()
                     .OrderByDescending(info => info.State)
                     .ThenBy(info => info.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            _buildingInfos[buildingInfo.Id] = buildingInfo;
            var building = _data.Buildings.GetOrNull(buildingInfo.Id);
            if (building is null)
                continue;

            entries.Add(new ItemSelectionEntry(
                Id: $"building:{buildingInfo.Id}",
                Title: building.DisplayName,
                Subtitle: $"Building Ã¢â‚¬â€ {FormatStateLabel(buildingInfo.State)}",
                Details: $"{(building.IsWorkshop ? "Workshop" : "Structure")} Ã¢â‚¬â€ needs {FormatRequirements(building.ConstructionInputs)}",
                Status: BuildBuildingStatus(buildingInfo),
                StatusColor: GetStateColor(buildingInfo.State),
                Icon: PixelArtFactory.GetBuilding(buildingInfo.Id),
                ActionLabel: "View details",
                IsEnabled: true,
                OnPressed: () => ShowBuildingDetail(buildingInfo.Id)));
        }

        foreach (var recipeInfo in _discovery.GetRecipeInfos()
                     .OrderByDescending(info => info.State)
                     .ThenBy(info => info.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            _recipeInfos[recipeInfo.Id] = recipeInfo;
            var recipe = _data.Recipes.GetOrNull(recipeInfo.Id);
            if (recipe is null)
                continue;

            entries.Add(new ItemSelectionEntry(
                Id: $"recipe:{recipeInfo.Id}",
                Title: recipe.DisplayName,
                Subtitle: $"Recipe Ã¢â‚¬â€ {FormatStateLabel(recipeInfo.State)}",
                Details: $"At {_data.Buildings.GetOrNull(recipe.WorkshopDefId)?.DisplayName ?? recipe.WorkshopDefId} Ã¢â‚¬â€ outputs {string.Join(", ", recipe.Outputs.Select(FormatRecipeOutput))}",
                Status: BuildRecipeStatus(recipeInfo),
                StatusColor: GetStateColor(recipeInfo.State),
                Icon: null,
                ActionLabel: "View details",
                IsEnabled: true,
                OnPressed: () => ShowRecipeDetail(recipeInfo.Id)));
        }

        if (entries.Count == 0)
        {
            entries.Add(new ItemSelectionEntry(
                Id: "none",
                Title: "Nothing discovered yet",
                Subtitle: "Gather materials like logs and stone to reveal buildings and recipes.",
                Details: string.Empty,
                Status: string.Empty,
                StatusColor: new Color(0.7f, 0.7f, 0.7f),
                Icon: null,
                ActionLabel: "Unavailable",
                IsEnabled: false,
                OnPressed: null));
        }

        _list.SetEntries(entries);
    }

    private void ShowBuildingDetail(string buildingId)
    {
        if (_detailTitle is null || _detailSubtitle is null || _detailDescription is null || _detailUnlocks is null)
            return;
        if (_data is null || !_buildingInfos.TryGetValue(buildingId, out var info))
            return;

        var building = _data.Buildings.GetOrNull(buildingId);
        if (building is null)
            return;

        _detailTitle.Text = building.DisplayName;
        _detailSubtitle.Text = $"{(building.IsWorkshop ? "Workshop" : "Structure")} Ã¢â‚¬â€ {FormatStateLabel(info.State)}";

        var triggerText = info.TriggerItemId is not null
            ? $"First unlocked by: {GetItemDisplayName(info.TriggerItemId)}\n"
            : string.Empty;

        _detailDescription.Text =
            $"{triggerText}" +
            $"Discovery requirements: {FormatRequirementStatuses(info.DiscoveryRequirements, showAvailability: false)}\n" +
            $"Construction requirements: {FormatRequirementStatuses(info.ConstructionRequirements, showAvailability: true)}";

        var recipes = _discovery!.GetRecipesForBuilding(buildingId)
            .Select(recipeId => _data.Recipes.GetOrNull(recipeId)?.DisplayName ?? recipeId)
            .ToList();
        _detailUnlocks.Text = recipes.Count > 0
            ? $"Associated recipes:\n{string.Join("\n", recipes.Select(name => $"Ã¢â‚¬Â¢ {name}"))}"
            : "No recipes are associated with this building yet.";
    }

    private void ShowRecipeDetail(string recipeId)
    {
        if (_detailTitle is null || _detailSubtitle is null || _detailDescription is null || _detailUnlocks is null)
            return;
        if (_data is null || !_recipeInfos.TryGetValue(recipeId, out var info))
            return;

        var recipe = _data.Recipes.GetOrNull(recipeId);
        if (recipe is null)
            return;

        _detailTitle.Text = recipe.DisplayName;
        _detailSubtitle.Text = $"Recipe at {_data.Buildings.GetOrNull(recipe.WorkshopDefId)?.DisplayName ?? recipe.WorkshopDefId} Ã¢â‚¬â€ {FormatStateLabel(info.State)}";

        var triggerText = info.TriggerItemId is not null
            ? $"First unlocked by: {GetItemDisplayName(info.TriggerItemId)}\n"
            : string.Empty;

        _detailDescription.Text =
            $"{triggerText}" +
            $"Discovery requirements: {FormatRequirementStatuses(info.DiscoveryRequirements, showAvailability: false)}\n" +
            $"Inputs: {FormatRequirementStatuses(info.ConstructionRequirements, showAvailability: true)}\n" +
            $"Outputs: {string.Join(", ", recipe.Outputs.Select(FormatRecipeOutput))}";

        _detailUnlocks.Text = info.State >= DiscoveryKnowledgeState.Unlocked
            ? "This recipe can contribute to later discoveries as you produce new goods."
            : "Gather the missing materials to reveal this recipe fully.";
    }

    private static Color GetStateColor(DiscoveryKnowledgeState state)
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
            DiscoveryKnowledgeState.BuildableNow => "buildable now",
            DiscoveryKnowledgeState.Unlocked => "discovered",
            DiscoveryKnowledgeState.Known => "partially understood",
            _ => "still hidden",
        };

    private static string BuildBuildingStatus(BuildingDiscoveryInfo info)
        => info.State switch
        {
            DiscoveryKnowledgeState.BuildableNow => "Materials available now",
            DiscoveryKnowledgeState.Unlocked => $"Discovered Ã¢â‚¬â€ {FormatMissingAvailable(info.ConstructionRequirements)}",
            DiscoveryKnowledgeState.Known => $"Missing discovery: {FormatMissingDiscovery(info.DiscoveryRequirements)}",
            _ => $"Hidden Ã¢â‚¬â€ {FormatMissingDiscovery(info.DiscoveryRequirements)}",
        };

    private static string BuildRecipeStatus(RecipeDiscoveryInfo info)
        => info.State switch
        {
            DiscoveryKnowledgeState.BuildableNow => "Inputs available now",
            DiscoveryKnowledgeState.Unlocked => $"Discovered Ã¢â‚¬â€ {FormatMissingAvailable(info.ConstructionRequirements)}",
            DiscoveryKnowledgeState.Known => $"Missing discovery: {FormatMissingDiscovery(info.DiscoveryRequirements)}",
            _ => $"Hidden Ã¢â‚¬â€ {FormatMissingDiscovery(info.DiscoveryRequirements)}",
        };

    private static string FormatMissingAvailable(IReadOnlyList<InputDiscoveryStatus> statuses)
    {
        var missing = statuses
            .Where(status => !status.CanFulfillNow)
            .Select(status => $"{status.Input.Quantity}x {FormatRequirement(status.Input)}")
            .ToList();

        return missing.Count > 0
            ? $"need {string.Join(", ", missing)}"
            : "available now";
    }

    private static string FormatMissingDiscovery(IReadOnlyList<InputDiscoveryStatus> statuses)
    {
        var missing = statuses
            .Where(status => !status.IsEncountered)
            .Select(status => FormatRequirement(status.Input))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return missing.Count > 0
            ? string.Join(", ", missing)
            : "more materials";
    }

    private static string FormatRequirementStatuses(IReadOnlyList<InputDiscoveryStatus> statuses, bool showAvailability)
    {
        if (statuses.Count == 0)
            return "None";

        return string.Join(", ", statuses.Select(status =>
        {
            var baseText = $"{status.Input.Quantity}x {FormatRequirement(status.Input)}";
            var suffix = showAvailability
                ? status.CanFulfillNow ? "available now" : status.IsEncountered ? "known, but not available now" : "not discovered"
                : status.IsEncountered ? "known" : "unknown";
            return $"{baseText} ({suffix})";
        }));
    }

    private static string FormatRequirements(IReadOnlyList<RecipeInput> inputs)
        => inputs.Count == 0
            ? "nothing"
            : string.Join(", ", inputs.Select(input => $"{input.Quantity}x {FormatRequirement(input)}"));

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

    private string GetItemDisplayName(string itemDefId)
    {
        if (_data is null)
            return itemDefId;

        var item = _data.Items.GetOrNull(itemDefId);
        return item?.DisplayName ?? ItemTextFormatter.Humanize(itemDefId);
    }

    private string FormatRecipeOutput(RecipeOutput output)
    {
        if (_data is null)
            return $"{output.Quantity}x {output.ItemDefId ?? output.FormRole ?? "item"}";

        var itemDefId = RecipeOutputQuery.ResolveItemDefIds(_data, output).FirstOrDefault();
        var itemDisplayName = !string.IsNullOrWhiteSpace(itemDefId)
            ? GetItemDisplayName(itemDefId)
            : output.ItemDefId ?? output.FormRole ?? "item";

        return $"{output.Quantity}x {itemDisplayName}";
    }
}

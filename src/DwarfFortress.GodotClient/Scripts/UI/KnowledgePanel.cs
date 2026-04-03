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
/// Knowledge screen â€” displays discovered buildings, recipes, and materials.
/// Shows what triggered each discovery and what it unlocks.
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

        // Header
        var header = new Label { Text = "Knowledge" };
        header.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(header);
        vbox.AddChild(new HSeparator());

        // Main content area â€” split into list and detail
        var hSplit = new HSplitContainer();
        hSplit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hSplit.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        hSplit.SplitOffsets = new[] { 320 };
        vbox.AddChild(hSplit);

        // Left: list of discoveries
        var listVbox = new VBoxContainer();
        listVbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        listVbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        hSplit.AddChild(listVbox);

        var listHeader = new Label { Text = "Discovered" };
        listVbox.AddChild(listHeader);

        _list = new ItemSelectionList
        {
            CustomMinimumSize = new Vector2(0, 300),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        listVbox.AddChild(_list);

        // Right: detail panel
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

        // Close button
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
        if (_list is null || _data is null || _discovery is null) return;

        var entries = new List<ItemSelectionEntry>();

        // Show unlocked buildings
        foreach (var buildingId in _discovery.GetUnlockedBuildings())
        {
            var building = _data.Buildings.GetOrNull(buildingId);
            if (building is null) continue;

            var triggerItem = _discovery.GetDiscoveredBy(buildingId);
            var triggerName = triggerItem is not null ? GetItemDisplayName(triggerItem) : "Unknown";
            var recipes = _discovery.GetRecipesForBuilding(buildingId).ToList();
            var unlocksText = recipes.Count > 0
                ? $"Unlocks recipes: {string.Join(", ", recipes.Select(r => _data.Recipes.GetOrNull(r)?.DisplayName ?? r))}"
                : "No recipes yet";

            entries.Add(new ItemSelectionEntry(
                Id: $"building:{buildingId}",
                Title: building.DisplayName,
                Subtitle: $"Building â€” discovered via {triggerName}",
                Details: building.IsWorkshop ? "Workshop" : "Structure",
                Status: unlocksText,
                StatusColor: new Color(0.44f, 0.85f, 0.48f),
                Icon: PixelArtFactory.GetBuilding(buildingId),
                ActionLabel: "View details",
                IsEnabled: true,
                OnPressed: () => ShowDetail("building", buildingId)));
        }

        // Show unlocked recipes
        foreach (var recipeId in _discovery.GetUnlockedRecipes())
        {
            var recipe = _data.Recipes.GetOrNull(recipeId);
            if (recipe is null) continue;

            var triggerItem = _discovery.GetDiscoveredBy(recipeId);
            var triggerName = triggerItem is not null ? GetItemDisplayName(triggerItem) : "Unknown";
            var outputs = string.Join(", ", recipe.Outputs.Select(o => FormatRecipeOutput(o)));

            entries.Add(new ItemSelectionEntry(
                Id: $"recipe:{recipeId}",
                Title: recipe.DisplayName,
                Subtitle: $"Recipe â€” discovered via {triggerName}",
                Details: $"Outputs: {outputs}",
                Status: $"At: {_data.Buildings.GetOrNull(recipe.WorkshopDefId)?.DisplayName ?? recipe.WorkshopDefId}",
                StatusColor: new Color(0.44f, 0.85f, 0.48f),
                Icon: null,
                ActionLabel: "View details",
                IsEnabled: true,
                OnPressed: () => ShowDetail("recipe", recipeId)));
        }

        if (entries.Count == 0)
        {
            entries.Add(new ItemSelectionEntry(
                Id: "none",
                Title: "Nothing discovered yet",
                Subtitle: "Gather materials like logs and stone to discover buildings and recipes.",
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

    private void OnItemSelected(int index)
    {
        // Detail is shown via OnPressed callback
    }

    private void ShowDetail(string kind, string id)
    {
        if (_detailTitle is null || _detailSubtitle is null || _detailDescription is null || _detailUnlocks is null) return;
        if (_data is null || _discovery is null) return;

        switch (kind)
        {
            case "building":
            {
                var building = _data.Buildings.GetOrNull(id);
                if (building is null) return;
                var triggerItem = _discovery.GetDiscoveredBy(id);
                _detailTitle.Text = building.DisplayName;
                _detailSubtitle.Text = $"Building ({(building.IsWorkshop ? "Workshop" : "Structure")})";
                _detailDescription.Text = $"Discovered by picking up: {GetItemDisplayName(triggerItem ?? "unknown")}\n" +
                    $"Construction needs: {string.Join(", ", building.ConstructionInputs.Select(i => $"{i.Quantity}x {string.Join(", ", i.RequiredTags.All)}"))}";
                var recipes = _discovery.GetRecipesForBuilding(id).ToList();
                _detailUnlocks.Text = recipes.Count > 0
                    ? $"Unlocks:\n{string.Join("\n", recipes.Select(r => $"â€¢ {_data.Recipes.GetOrNull(r)?.DisplayName ?? r}"))}"
                    : "No recipes unlocked yet.";
                break;
            }
            case "recipe":
            {
                var recipe = _data.Recipes.GetOrNull(id);
                if (recipe is null) return;
                var triggerItem = _discovery.GetDiscoveredBy(id);
                _detailTitle.Text = recipe.DisplayName;
                _detailSubtitle.Text = $"Recipe at {_data.Buildings.GetOrNull(recipe.WorkshopDefId)?.DisplayName ?? recipe.WorkshopDefId}";
                var inputs = string.Join(", ", recipe.Inputs.Select(i => $"{i.Quantity}x {string.Join(", ", i.RequiredTags.All)}"));
                var outputs = string.Join(", ", recipe.Outputs.Select(FormatRecipeOutput));
                _detailDescription.Text = $"Discovered by picking up: {GetItemDisplayName(triggerItem ?? "unknown")}\n" +
                    $"Inputs: {inputs}\nOutputs: {outputs}";
                _detailUnlocks.Text = "This recipe produces items that may unlock further discoveries.";
                break;
            }
            case "pending_building":
            {
                var building = _data.Buildings.GetOrNull(id);
                if (building is null) return;
                _detailTitle.Text = building.DisplayName;
                _detailSubtitle.Text = "Not yet discovered";
                var hasMaterials = building.ConstructionInputs
                    .SelectMany(i => i.RequiredTags.All)
                    .Where(tag => _discovery.GetDiscoveredTags().Contains(tag))
                    .ToList();
                var missingMaterials = building.ConstructionInputs
                    .SelectMany(i => i.RequiredTags.All)
                    .Where(tag => !_discovery.GetDiscoveredTags().Contains(tag))
                    .ToList();
                _detailDescription.Text = $"You have materials: {string.Join(", ", hasMaterials)}\n" +
                    $"Still need: {string.Join(", ", missingMaterials)}\n\n" +
                    $"Gather the missing materials to discover this building.";
                _detailUnlocks.Text = "Once discovered, this building will unlock its associated recipes.";
                break;
            }
        }
    }

    private string GetItemDisplayName(string itemDefId)
    {
        if (_data is null) return itemDefId;
        var item = _data.Items.GetOrNull(itemDefId);
        return item?.DisplayName ?? itemDefId;
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

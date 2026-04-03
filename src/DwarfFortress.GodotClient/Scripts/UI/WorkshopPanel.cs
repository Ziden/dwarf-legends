using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Systems;
using Godot;

namespace DwarfFortress.GodotClient.UI;


/// <summary>Right-side panel showing available recipes for a selected workshop.</summary>
public partial class WorkshopPanel : PanelContainer
{
    private Label?         _titleLabel;
    private VBoxContainer? _queueBox;
    private Button?        _clearQueueBtn;
    private Label?         _storageStatusLabel;
    private Label?         _queueStatusLabel;
    private ItemSelectionList? _recipeList;
    private ItemSelectionList? _inventoryList;
    private GameSimulation? _simulation;
    private BuildingView?   _building;
    private List<RecipeDef> _recipes = new();
    private int _lastQueueHash;

    public override void _Ready()
    {
        _titleLabel    = GetNode<Label>("%TitleLabel");
        _queueBox      = GetNode<VBoxContainer>("%QueueBox");
        _clearQueueBtn = GetNode<Button>("%ClearQueueBtn");
        _storageStatusLabel = GetNode<Label>("%StorageStatusLabel");
        _queueStatusLabel = GetNode<Label>("%QueueStatusLabel");
        _recipeList    = GetNode<ItemSelectionList>("%RecipeList");
        _inventoryList = GetNode<ItemSelectionList>("%InventoryList");

        _clearQueueBtn.Pressed += OnClearQueuePressed;
    }

    public void Setup(GameSimulation sim) => _simulation = sim;

    public void ShowBuilding(BuildingView building)
    {
        var changed = _building?.Id != building.Id;
        _building = building;
        Visible = true;

        if (changed)
        {
            _lastQueueHash = 0;
            Rebuild();
            return;
        }

        Refresh();
    }

    public new void Hide() { Visible = false; _building = null; }

    public void Refresh()
    {
        if (Visible && _building is not null)
        {
            if (_storageStatusLabel is not null)
                _storageStatusLabel.Text = FormatStorageStatus(_building.StoredItemCount);
            RebuildQueue();
            RefreshRecipeEntries();
            RefreshInventory();
        }
    }

    // â”€â”€ Private â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void Rebuild()
    {
        if (_building is null || _simulation is null) return;

        _titleLabel!.Text = $"{_building.BuildingDefId.Replace('_', ' ')}  (#{_building.Id})";
        if (_storageStatusLabel is not null)
            _storageStatusLabel.Text = FormatStorageStatus(_building.StoredItemCount);

        RebuildQueue();
        RebuildRecipes();
        RefreshInventory();
    }

    private void RebuildQueue()
    {
        foreach (Node child in _queueBox!.GetChildren()) child.QueueFree();

        var recipeSystem = _simulation!.Context.TryGet<RecipeSystem>();
        if (recipeSystem is null) return;

        var queue  = recipeSystem.GetOrCreateQueue(_building!.Id);
        var orders = queue.All.ToList();

        if (orders.Count == 0)
        {
            if (_clearQueueBtn is not null) _clearQueueBtn.Disabled = true;
            if (_queueStatusLabel is not null) _queueStatusLabel.Text = "No work orders queued.";
            _queueBox.AddChild(new Label { Text = "  (empty)", Modulate = new Color(0.7f, 0.7f, 0.7f) });
            _lastQueueHash = 0;
            return;
        }

        var dm = _simulation.Context.Get<DataManager>();
        if (_clearQueueBtn is not null) _clearQueueBtn.Disabled = false;
        if (_queueStatusLabel is not null) _queueStatusLabel.Text = $"{orders.Sum(order => order.Remaining)} work order(s) queued.";

        foreach (var order in orders)
        {
            var displayName = dm.Recipes.Contains(order.RecipeId)
                ? dm.Recipes.Get(order.RecipeId).DisplayName
                : order.RecipeId;

            _queueBox.AddChild(new Label { Text = $"  {displayName} Ã—{order.Remaining}" });
        }

        _lastQueueHash = ComputeQueueHash(orders);
    }

    private void RebuildRecipes()
    {
        var dataManager = _simulation!.Context.Get<DataManager>();

        // Only show recipes that belong to this workshop type
        _recipes = dataManager.Recipes.All()
            .Where(r => r.WorkshopDefId == _building!.BuildingDefId)
            .ToList();

        if (_recipes.Count == 0)
        {
            _recipeList!.SetEntries(new[]
            {
                new ItemSelectionEntry(
                    Id: "none",
                    Title: "No recipes available",
                    Subtitle: "This workshop has no craftable items yet.",
                    Details: "",
                    Status: "",
                    StatusColor: new Color(0.7f, 0.7f, 0.7f),
                    Icon: null,
                    ActionLabel: "Unavailable",
                    IsEnabled: false,
                    OnPressed: null)
            });
            return;
        }

        RefreshRecipeEntries();
    }

    private void RefreshRecipeEntries()
    {
        if (_simulation is null || _building is null || _recipeList is null) return;

        var recipeSystem = _simulation.Context.Get<RecipeSystem>();
        var queueCounts = recipeSystem.GetOrCreateQueue(_building.Id)
            .All
            .GroupBy(order => order.RecipeId)
            .ToDictionary(group => group.Key, group => group.Sum(order => order.Remaining));

        var entries = _recipes.Select(recipe => BuildRecipeEntry(recipe, queueCounts)).ToList();
        _recipeList.SetEntries(entries);
    }

    private ItemSelectionEntry BuildRecipeEntry(RecipeDef recipe, IReadOnlyDictionary<string, int> queueCounts)
    {
        var dataManager = _simulation!.Context.Get<DataManager>();
        var output = recipe.Outputs.FirstOrDefault();
        var outputItemDefIds = RecipeOutputQuery.ResolveItemDefIds(dataManager, recipe);
        var itemDefId = outputItemDefIds.FirstOrDefault() ?? output?.ItemDefId ?? ItemDefIds.Log;
        var outputLabel = output is null
            ? recipe.DisplayName
            : FormatOutputLabel(dataManager, output);
        var requirements = SelectionRequirementHelper.Analyze(_simulation!, recipe.Inputs);

        var queueText = queueCounts.TryGetValue(recipe.Id, out var queued) && queued > 0
            ? $"Queued x{queued}"
            : "Ready to queue";

        var status = requirements.CanFulfill
            ? $"{queueText} â€¢ materials available now"
            : $"{queueText} â€¢ missing {requirements.MissingSummary}";

        return new ItemSelectionEntry(
            Id: recipe.Id,
            Title: recipe.DisplayName,
            Subtitle: $"Produces: {outputLabel}",
            Details: $"Needs {requirements.NeededSummary}  |  Work {recipe.WorkTime:0.#}",
            Status: status,
            StatusColor: requirements.CanFulfill ? new Color(0.44f, 0.85f, 0.48f) : new Color(0.96f, 0.72f, 0.28f),
            Icon: PixelArtFactory.GetItem(itemDefId),
            ActionLabel: "Add to queue",
            IsEnabled: true,
            OnPressed: () => QueueRecipe(recipe.Id));
    }

    private void QueueRecipe(string recipeId)
    {
        if (_simulation is null || _building is null) return;

        _simulation.Context.Commands.Dispatch(new SetProductionOrderCommand(_building.Id, recipeId, 1));
        RebuildQueue();
        RefreshRecipeEntries();
    }

    private void OnClearQueuePressed()
    {
        if (_building is null || _simulation is null) return;
        var recipeSystem = _simulation.Context.TryGet<RecipeSystem>();
        var queueCount = recipeSystem?.GetOrCreateQueue(_building.Id).All.Count() ?? 0;
        for (int index = 0; index < queueCount; index++)
            _simulation.Context.Commands.Dispatch(new CancelProductionOrderCommand(_building.Id, 0));
        RebuildQueue();
        RefreshRecipeEntries();
    }

    private static string FormatDefId(string id)
        => id.Replace('_', ' ');

    private static string FormatOutputLabel(DataManager dataManager, RecipeOutput output)
    {
        var outputItemDefId = RecipeOutputQuery.ResolveItemDefIds(dataManager, output).FirstOrDefault();
        var labelId = !string.IsNullOrWhiteSpace(outputItemDefId)
            ? outputItemDefId
            : output.ItemDefId ?? output.FormRole ?? "item";

        return $"{output.Quantity}x {FormatDefId(labelId)}";
    }

    private static string FormatStorageStatus(int storedItemCount, float totalWeight = 0f)
    {
        if (storedItemCount <= 0)
            return "Workshop storage: empty";
        
        var weightText = totalWeight > 0f ? $" ({totalWeight:F1} kg)" : "";
        return $"Workshop storage: {storedItemCount} staged item(s){weightText}";
    }

    private void RefreshInventory()
    {
        if (_building is null || _simulation is null || _inventoryList is null) return;

        var itemSystem = _simulation.Context.TryGet<ItemSystem>();
        var dm = _simulation.Context.TryGet<DataManager>();
        if (itemSystem is null)
        {
            _inventoryList.SetEntries(new[]
            {
                new ItemSelectionEntry(
                    Id: "none",
                    Title: "No items",
                    Subtitle: "Workshop storage is empty",
                    Details: "",
                    Status: "",
                    StatusColor: new Color(0.7f, 0.7f, 0.7f),
                    Icon: null,
                    ActionLabel: "",
                    IsEnabled: false,
                    OnPressed: null)
            });
            if (_storageStatusLabel is not null)
                _storageStatusLabel.Text = FormatStorageStatus(0);
            return;
        }

        var items = itemSystem.GetItemsInBuilding(_building.Id).ToList();
        
        if (items.Count == 0)
        {
            _inventoryList.SetEntries(new[]
            {
                new ItemSelectionEntry(
                    Id: "empty",
                    Title: "No items stored",
                    Subtitle: "This workshop's storage is empty",
                    Details: "Items will appear here when hauled to the workshop",
                    Status: "",
                    StatusColor: new Color(0.7f, 0.7f, 0.7f),
                    Icon: null,
                    ActionLabel: "",
                    IsEnabled: false,
                    OnPressed: null)
            });
            if (_storageStatusLabel is not null)
                _storageStatusLabel.Text = FormatStorageStatus(0);
            return;
        }

        // Calculate total weight
        float totalWeight = 0f;
        foreach (var item in items)
        {
            var itemDef = dm?.Items.GetOrNull(item.DefId);
            var weight = itemDef?.Weight ?? 0f;
            if (item.StackSize > 1)
                weight *= item.StackSize;
            totalWeight += weight;
        }

        var entries = items.Select(item => BuildInventoryEntry(item)).ToList();
        _inventoryList.SetEntries(entries);

        // Update storage status with total weight
        if (_storageStatusLabel is not null)
            _storageStatusLabel.Text = FormatStorageStatus(items.Count, totalWeight);
    }

    private ItemSelectionEntry BuildInventoryEntry(Item item)
    {
        var dm = _simulation?.Context.TryGet<DataManager>();
        var itemDef = dm?.Items.GetOrNull(item.DefId);
        var weight = itemDef?.Weight ?? 0f;
        if (item.StackSize > 1)
            weight *= item.StackSize;

        var materialLabel = !string.IsNullOrEmpty(item.MaterialId) 
            ? $"{FormatDefId(item.MaterialId)} " 
            : "";
        var stackLabel = item.StackSize > 1 ? $" x{item.StackSize}" : "";
        var qualityLabel = item.Quality != ItemQuality.Ordinary 
            ? $" [{item.Quality}]" 
            : "";
        var weightLabel = weight > 0f ? $" [{weight:F1} kg]" : "";

        var corpse = item.Components.TryGet<CorpseComponent>();
        string title, subtitle;
        
        if (corpse is not null)
        {
            title = $"{corpse.DisplayName} corpse";
            var rot = item.Components.TryGet<RotComponent>();
            var rotStage = rot?.Stage ?? "fresh";
            subtitle = $"Died of {FormatDefId(corpse.DeathCause)} â€¢ {rotStage}";
        }
        else
        {
            title = $"{materialLabel}{FormatDefId(item.DefId)}{stackLabel}{qualityLabel}{weightLabel}";
            subtitle = $"Stored in workshop";
        }

        return new ItemSelectionEntry(
            Id: item.Id.ToString(),
            Title: title,
            Subtitle: subtitle,
            Details: $"Item #{item.Id} â€¢ Position: ({item.Position.Position.X}, {item.Position.Position.Y}, {item.Position.Position.Z})",
            Status: item.IsClaimed ? "Reserved" : "Available",
            StatusColor: item.IsClaimed ? new Color(0.96f, 0.72f, 0.28f) : new Color(0.44f, 0.85f, 0.48f),
            Icon: PixelArtFactory.GetItem(item.DefId, item.MaterialId),
            ActionLabel: "",
            IsEnabled: false,
            OnPressed: null);
    }

    private static int ComputeQueueHash(IReadOnlyList<ProductionQueue.Order> orders)
    {
        int hash = 17;
        foreach (var order in orders)
            hash = (hash * 31) ^ System.HashCode.Combine(order.RecipeId, order.Remaining);
        return hash;
    }
}

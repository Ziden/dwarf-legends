using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;
using Godot;

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

    // ── Private ────────────────────────────────────────────────────────────

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

            _queueBox.AddChild(new Label { Text = $"  {displayName} ×{order.Remaining}" });
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
        var output = recipe.Outputs.FirstOrDefault();
        var itemDefId = output?.ItemDefId ?? "log";
        var outputLabel = output is null
            ? recipe.DisplayName
            : $"{output.Quantity}x {FormatDefId(output.ItemDefId)}";
        var requirements = SelectionRequirementHelper.Analyze(_simulation!, recipe.Inputs);

        var queueText = queueCounts.TryGetValue(recipe.Id, out var queued) && queued > 0
            ? $"Queued x{queued}"
            : "Ready to queue";

        var status = requirements.CanFulfill
            ? $"{queueText} • materials available now"
            : $"{queueText} • missing {requirements.MissingSummary}";

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

    private static string FormatStorageStatus(int storedItemCount)
        => storedItemCount <= 0
            ? "Workshop storage: empty"
            : $"Workshop storage: {storedItemCount} staged item(s)";

    private void RefreshInventory()
    {
        if (_building is null || _simulation is null || _inventoryList is null) return;

        var itemSystem = _simulation.Context.TryGet<ItemSystem>();
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
            return;
        }

        var entries = items.Select(item => BuildInventoryEntry(item)).ToList();
        _inventoryList.SetEntries(entries);
    }

    private ItemSelectionEntry BuildInventoryEntry(Item item)
    {
        var materialLabel = !string.IsNullOrEmpty(item.MaterialId) 
            ? $"{FormatDefId(item.MaterialId)} " 
            : "";
        var stackLabel = item.StackSize > 1 ? $" x{item.StackSize}" : "";
        var qualityLabel = item.Quality != ItemQuality.Ordinary 
            ? $" [{item.Quality}]" 
            : "";

        var corpse = item.Components.TryGet<CorpseComponent>();
        string title, subtitle;
        
        if (corpse is not null)
        {
            title = $"{corpse.DisplayName} corpse";
            var rot = item.Components.TryGet<RotComponent>();
            var rotStage = rot?.Stage ?? "fresh";
            subtitle = $"Died of {FormatDefId(corpse.DeathCause)} • {rotStage}";
        }
        else
        {
            title = $"{materialLabel}{FormatDefId(item.DefId)}{stackLabel}{qualityLabel}";
            subtitle = $"Stored in workshop";
        }

        return new ItemSelectionEntry(
            Id: item.Id.ToString(),
            Title: title,
            Subtitle: subtitle,
            Details: $"Item #{item.Id} • Position: ({item.Position.Position.X}, {item.Position.Position.Y}, {item.Position.Position.Z})",
            Status: item.IsClaimed ? "Reserved" : "Available",
            StatusColor: item.IsClaimed ? new Color(0.96f, 0.72f, 0.28f) : new Color(0.44f, 0.85f, 0.48f),
            Icon: PixelArtFactory.GetItem(item.DefId),
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

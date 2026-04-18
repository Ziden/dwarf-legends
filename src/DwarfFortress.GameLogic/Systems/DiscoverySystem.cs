using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;

namespace DwarfFortress.GameLogic.Systems;

// â”€â”€ Events â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public record struct DiscoveryUnlockedEvent(
    string Kind,           // "building" or "recipe"
    string Id,
    string DisplayName,
    string TriggerItemId);

// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/// <summary>
/// Tracks encountered materials and derives a shared knowledge/buildability state
/// for buildings and recipes.
/// </summary>
public sealed class DiscoverySystem : IGameSystem
{
    public string SystemId    => SystemIds.DiscoverySystem;
    public int    UpdateOrder => 2; // After DataManager (0) and EntityRegistry (1)
    public bool   IsEnabled   { get; set; } = true;

    // Encountered selectors that can satisfy discovery requirements.
    private readonly HashSet<string> _discoveredTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _encounteredItemDefIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _encounteredMaterialIds = new(StringComparer.OrdinalIgnoreCase);

    // Fully unlocked buildings/recipes (all discovery requirements met at least once).
    private readonly HashSet<string> _unlockedBuildings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unlockedRecipes   = new(StringComparer.OrdinalIgnoreCase);

    // Items that have triggered discoveries (for Knowledge screen)
    private readonly Dictionary<string, string> _discoveredByItem = new(StringComparer.OrdinalIgnoreCase); // building/recipe id â†’ trigger item id

    private GameContext? _ctx;
    private DataManager? _data;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        _data = ctx.TryGet<DataManager>();
        ctx.EventBus.On<ItemCreatedEvent>(OnItemCreated);
        ctx.EventBus.On<ItemPickedUpEvent>(OnItemPickedUp);
    }

    public void Tick(float delta) { }

    private void OnItemCreated(ItemCreatedEvent e)
        => RegisterEncounteredItem(e.ItemId, e.ItemDefId);

    private void OnItemPickedUp(ItemPickedUpEvent e)
        => RegisterEncounteredItem(e.ItemId, e.ItemDefId);

    private void RegisterEncounteredItem(int itemId, string itemDefId)
    {
        if (_data is null || _ctx is null)
            return;

        var itemSystem = _ctx.TryGet<ItemSystem>();
        Item? item = null;
        itemSystem?.TryGetItem(itemId, out item);
        RegisterEncounteredSelectors(itemDefId, item?.MaterialId);

        EvaluateUnlocks(itemDefId);
    }

    private void RegisterEncounteredSelectors(string itemDefId, string? materialId)
    {
        if (_data is null)
            return;

        var itemDef = _data.Items.GetOrNull(itemDefId);
        if (itemDef is null)
            return;

        _encounteredItemDefIds.Add(itemDefId);
        foreach (var tag in itemDef.Tags.All)
            _discoveredTags.Add(tag);

        if (!string.IsNullOrWhiteSpace(materialId))
            _encounteredMaterialIds.Add(materialId);
    }

    private void SyncEncounteredSelectorsFromCurrentItems()
    {
        if (_ctx is null)
            return;

        var itemSystem = _ctx.TryGet<ItemSystem>();
        if (itemSystem is null)
            return;

        foreach (var item in itemSystem.GetAllItems())
            RegisterEncounteredSelectors(item.DefId, item.MaterialId);
    }

    private void EvaluateUnlocks(string triggerItemId)
    {
        if (_data is null || _ctx is null)
            return;

        SyncEncounteredSelectorsFromCurrentItems();

        foreach (var building in _data.Buildings.All())
        {
            if (_unlockedBuildings.Contains(building.Id))
                continue;

            if (!HasAllDiscoveryRequirements(GetDiscoveryInputs(building)))
                continue;

            _unlockedBuildings.Add(building.Id);
            _discoveredByItem[building.Id] = triggerItemId;
            _ctx.EventBus.Emit(new DiscoveryUnlockedEvent(
                "building", building.Id, building.DisplayName, triggerItemId));
        }

        foreach (var recipe in _data.Recipes.All())
        {
            if (_unlockedRecipes.Contains(recipe.Id))
                continue;

            if (!HasAllDiscoveryRequirements(GetDiscoveryInputs(recipe)))
                continue;

            _unlockedRecipes.Add(recipe.Id);
            _discoveredByItem[recipe.Id] = triggerItemId;
            _ctx.EventBus.Emit(new DiscoveryUnlockedEvent(
                "recipe", recipe.Id, recipe.DisplayName, triggerItemId));
        }
    }

    private bool HasAllDiscoveryRequirements(IReadOnlyList<RecipeInput> inputs)
    {
        foreach (var input in inputs)
        {
            if (!IsInputEncountered(input))
                return false;
        }

        return true;
    }

    private bool CanFulfillInputsNow(IReadOnlyList<RecipeInput> inputs)
    {
        if (_ctx is null)
            return false;

        var itemSystem = _ctx.TryGet<ItemSystem>();
        return itemSystem is not null && itemSystem.CanFulfillInputs(inputs, ItemAvailabilityScope.Owned);
    }

    private bool IsInputEncountered(RecipeInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.ItemDefId) &&
            !_encounteredItemDefIds.Contains(input.ItemDefId))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(input.MaterialId) &&
            !_encounteredMaterialIds.Contains(input.MaterialId))
        {
            return false;
        }

        foreach (var tag in input.RequiredTags.All)
        {
            if (!_discoveredTags.Contains(tag))
                return false;
        }

        return true;
    }

    private static IReadOnlyList<RecipeInput> GetDiscoveryInputs(BuildingDef building)
        => building.DiscoveryInputs is { Count: > 0 } ? building.DiscoveryInputs : building.ConstructionInputs;

    private static IReadOnlyList<RecipeInput> GetDiscoveryInputs(RecipeDef recipe)
        => recipe.DiscoveryInputs is { Count: > 0 } ? recipe.DiscoveryInputs : recipe.Inputs;

    private DiscoveryKnowledgeState DetermineState(
        string id,
        IReadOnlyList<RecipeInput> discoveryInputs,
        IReadOnlyList<RecipeInput> constructionInputs,
        IReadOnlySet<string> unlockedIds)
    {
        var canBuildNow = CanFulfillInputsNow(constructionInputs);
        if (unlockedIds.Contains(id))
            return canBuildNow ? DiscoveryKnowledgeState.BuildableNow : DiscoveryKnowledgeState.Unlocked;

        var anyEncountered = false;
        foreach (var input in discoveryInputs)
        {
            if (!IsInputEncountered(input))
                return anyEncountered ? DiscoveryKnowledgeState.Known : DiscoveryKnowledgeState.Hidden;

            anyEncountered = true;
        }

        return canBuildNow ? DiscoveryKnowledgeState.BuildableNow : DiscoveryKnowledgeState.Unlocked;
    }

    private List<InputDiscoveryStatus> BuildInputStatuses(IReadOnlyList<RecipeInput> inputs)
    {
        var statuses = new List<InputDiscoveryStatus>(inputs.Count);
        foreach (var input in inputs)
        {
            statuses.Add(new InputDiscoveryStatus(
                Input: input,
                IsEncountered: IsInputEncountered(input),
                CanFulfillNow: CanFulfillInputsNow([input])));
        }

        return statuses;
    }

    private BuildingDiscoveryInfo BuildBuildingInfo(BuildingDef building)
        => new(
            Id: building.Id,
            DisplayName: building.DisplayName,
            State: DetermineState(building.Id, GetDiscoveryInputs(building), building.ConstructionInputs, _unlockedBuildings),
            DiscoveryRequirements: BuildInputStatuses(GetDiscoveryInputs(building)),
            ConstructionRequirements: BuildInputStatuses(building.ConstructionInputs),
            TriggerItemId: GetDiscoveredBy(building.Id));

    private RecipeDiscoveryInfo BuildRecipeInfo(RecipeDef recipe)
        => new(
            Id: recipe.Id,
            DisplayName: recipe.DisplayName,
            WorkshopDefId: recipe.WorkshopDefId,
            State: DetermineState(recipe.Id, GetDiscoveryInputs(recipe), recipe.Inputs, _unlockedRecipes),
            DiscoveryRequirements: BuildInputStatuses(GetDiscoveryInputs(recipe)),
            ConstructionRequirements: BuildInputStatuses(recipe.Inputs),
            TriggerItemId: GetDiscoveredBy(recipe.Id));

    // â”€â”€ Public Query API â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public DiscoveryKnowledgeState GetBuildingState(string buildingId)
    {
        SyncEncounteredSelectorsFromCurrentItems();
        var building = _data?.Buildings.GetOrNull(buildingId);
        return building is null
            ? DiscoveryKnowledgeState.Hidden
            : DetermineState(building.Id, GetDiscoveryInputs(building), building.ConstructionInputs, _unlockedBuildings);
    }

    public DiscoveryKnowledgeState GetRecipeState(string recipeId)
    {
        SyncEncounteredSelectorsFromCurrentItems();
        var recipe = _data?.Recipes.GetOrNull(recipeId);
        return recipe is null
            ? DiscoveryKnowledgeState.Hidden
            : DetermineState(recipe.Id, GetDiscoveryInputs(recipe), recipe.Inputs, _unlockedRecipes);
    }

    public bool IsBuildingUnlocked(string buildingId)
        => GetBuildingState(buildingId) >= DiscoveryKnowledgeState.Unlocked;

    public bool IsRecipeUnlocked(string recipeId)
        => GetRecipeState(recipeId) >= DiscoveryKnowledgeState.Unlocked;

    public IEnumerable<string> GetUnlockedBuildings()
        => _data?.Buildings.All()
            .Where(building => _unlockedBuildings.Contains(building.Id))
            .Select(building => building.Id)
            ?? Enumerable.Empty<string>();

    public IEnumerable<string> GetUnlockedRecipes()
        => _data?.Recipes.All()
            .Where(recipe => _unlockedRecipes.Contains(recipe.Id))
            .Select(recipe => recipe.Id)
            ?? Enumerable.Empty<string>();

    public IReadOnlySet<string> GetDiscoveredTags() => _discoveredTags;

    /// <summary>
    /// Returns the item ID that triggered the discovery of a building or recipe.
    /// </summary>
    public string? GetDiscoveredBy(string id) => _discoveredByItem.GetValueOrDefault(id);

    /// <summary>
    /// Returns all building discovery states in stable data order.
    /// </summary>
    public IEnumerable<BuildingDiscoveryInfo> GetBuildingInfos()
    {
        if (_data is null)
            yield break;

        SyncEncounteredSelectorsFromCurrentItems();
        foreach (var building in _data.Buildings.All())
            yield return BuildBuildingInfo(building);
    }

    /// <summary>
    /// Returns building discovery states that are not yet unlocked.
    /// </summary>
    public IEnumerable<BuildingDiscoveryInfo> GetPendingBuildings()
    {
        foreach (var building in GetBuildingInfos())
            if (building.State < DiscoveryKnowledgeState.Unlocked)
                yield return building;
    }

    /// <summary>
    /// Returns all recipe discovery states in stable data order.
    /// </summary>
    public IEnumerable<RecipeDiscoveryInfo> GetRecipeInfos()
    {
        if (_data is null)
            yield break;

        SyncEncounteredSelectorsFromCurrentItems();
        foreach (var recipe in _data.Recipes.All())
            yield return BuildRecipeInfo(recipe);
    }

    /// <summary>
    /// Returns recipe discovery states that are not yet unlocked.
    /// </summary>
    public IEnumerable<RecipeDiscoveryInfo> GetPendingRecipes()
    {
        foreach (var recipe in GetRecipeInfos())
            if (recipe.State < DiscoveryKnowledgeState.Unlocked)
                yield return recipe;
    }

    /// <summary>
    /// Returns all recipes that use a given building (workshop).
    /// </summary>
    public IEnumerable<string> GetRecipesForBuilding(string buildingId)
    {
        if (_data is null)
            yield break;

        foreach (var recipe in _data.Recipes.All())
        {
            if (recipe.WorkshopDefId == buildingId)
                yield return recipe.Id;
        }
    }

    /// <summary>
    /// Returns all items that are outputs of unlocked recipes.
    /// </summary>
    public IEnumerable<string> GetCraftableItems()
    {
        if (_data is null)
            yield break;

        foreach (var recipe in _data.Recipes.All())
        {
            if (!_unlockedRecipes.Contains(recipe.Id))
                continue;

            foreach (var outputItemId in RecipeResolver.ResolveCraftableOutputItemIds(_data, recipe))
                yield return outputItemId;
        }
    }

    // â”€â”€ Save/Load â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public void OnSave(SaveWriter w)
    {
        w.Write("discoveredTags", _discoveredTags.ToList());
        w.Write("encounteredItemDefIds", _encounteredItemDefIds.ToList());
        w.Write("encounteredMaterialIds", _encounteredMaterialIds.ToList());
        w.Write("unlockedBuildings", _unlockedBuildings.ToList());
        w.Write("unlockedRecipes", _unlockedRecipes.ToList());
        w.Write("discoveredByItem", _discoveredByItem);
    }

    public void OnLoad(SaveReader r)
    {
        _discoveredTags.Clear();
        _encounteredItemDefIds.Clear();
        _encounteredMaterialIds.Clear();
        _unlockedBuildings.Clear();
        _unlockedRecipes.Clear();
        _discoveredByItem.Clear();

        var tags = r.TryRead<List<string>>("discoveredTags");
        if (tags is not null)
            foreach (var tag in tags)
                _discoveredTags.Add(tag);

        var encounteredItemDefIds = r.TryRead<List<string>>("encounteredItemDefIds");
        if (encounteredItemDefIds is not null)
            foreach (var itemDefId in encounteredItemDefIds)
                _encounteredItemDefIds.Add(itemDefId);

        var encounteredMaterialIds = r.TryRead<List<string>>("encounteredMaterialIds");
        if (encounteredMaterialIds is not null)
            foreach (var materialId in encounteredMaterialIds)
                _encounteredMaterialIds.Add(materialId);

        var buildings = r.TryRead<List<string>>("unlockedBuildings");
        if (buildings is not null)
            foreach (var buildingId in buildings)
                _unlockedBuildings.Add(buildingId);

        var recipes = r.TryRead<List<string>>("unlockedRecipes");
        if (recipes is not null)
            foreach (var recipeId in recipes)
                _unlockedRecipes.Add(recipeId);

        var byItem = r.TryRead<Dictionary<string, string>>("discoveredByItem");
        if (byItem is not null)
            foreach (var kv in byItem)
                _discoveredByItem[kv.Key] = kv.Value;
    }
}

// â”€â”€ Discovery Info Records â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public enum DiscoveryKnowledgeState
{
    Hidden = 0,
    Known = 1,
    Unlocked = 2,
    BuildableNow = 3,
}

public record struct InputDiscoveryStatus(
    RecipeInput Input,
    bool IsEncountered,
    bool CanFulfillNow);

public record struct BuildingDiscoveryInfo(
    string Id,
    string DisplayName,
    DiscoveryKnowledgeState State,
    List<InputDiscoveryStatus> DiscoveryRequirements,
    List<InputDiscoveryStatus> ConstructionRequirements,
    string? TriggerItemId);

public record struct RecipeDiscoveryInfo(
    string Id,
    string DisplayName,
    string WorkshopDefId,
    DiscoveryKnowledgeState State,
    List<InputDiscoveryStatus> DiscoveryRequirements,
    List<InputDiscoveryStatus> ConstructionRequirements,
    string? TriggerItemId);

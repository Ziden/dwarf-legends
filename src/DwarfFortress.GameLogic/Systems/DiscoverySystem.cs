using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct DiscoveryUnlockedEvent(
    string Kind,           // "building" or "recipe"
    string Id,
    string DisplayName,
    string TriggerItemId);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Tracks which materials the player has encountered and automatically unlocks
/// buildings and recipes when the player's current owned items can satisfy their
/// full input requirements.
/// </summary>
public sealed class DiscoverySystem : IGameSystem
{
    public string SystemId    => SystemIds.DiscoverySystem;
    public int    UpdateOrder => 2; // After DataManager (0) and EntityRegistry (1)
    public bool   IsEnabled   { get; set; } = true;

    // All item/material tags the player has ever encountered.
    private readonly HashSet<string> _discoveredTags = new();

    // Fully unlocked buildings/recipes (all requirements met at least once).
    private readonly HashSet<string> _unlockedBuildings = new();
    private readonly HashSet<string> _unlockedRecipes   = new();

    // Items that have triggered discoveries (for Knowledge screen)
    private readonly Dictionary<string, string> _discoveredByItem = new(); // building/recipe id → trigger item id

    private GameContext? _ctx;
    private DataManager? _data;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        _data = ctx.TryGet<DataManager>();
        ctx.EventBus.On<ItemPickedUpEvent>(OnItemPickedUp);
    }

    public void Tick(float delta) { }

    private void OnItemPickedUp(ItemPickedUpEvent e)
    {
        if (_data is null || _ctx is null) return;

        var itemDef = _data.Items.GetOrNull(e.ItemDefId);
        if (itemDef is null) return;

        foreach (var tag in itemDef.Tags.All)
            _discoveredTags.Add(tag);

        EvaluateUnlocks(e.ItemDefId);
    }

    private void EvaluateUnlocks(string triggerItemId)
    {
        if (_data is null || _ctx is null) return;

        foreach (var building in _data.Buildings.All())
        {
            if (_unlockedBuildings.Contains(building.Id)) continue;
            if (HasAllMaterials(building.ConstructionInputs))
            {
                _unlockedBuildings.Add(building.Id);
                _discoveredByItem[building.Id] = triggerItemId;
                _ctx.EventBus.Emit(new DiscoveryUnlockedEvent(
                    "building", building.Id, building.DisplayName, triggerItemId));
            }
        }

        foreach (var recipe in _data.Recipes.All())
        {
            if (_unlockedRecipes.Contains(recipe.Id)) continue;
            if (HasAllMaterials(recipe.Inputs))
            {
                _unlockedRecipes.Add(recipe.Id);
                _discoveredByItem[recipe.Id] = triggerItemId;
                _ctx.EventBus.Emit(new DiscoveryUnlockedEvent(
                    "recipe", recipe.Id, recipe.DisplayName, triggerItemId));
            }
        }
    }

    private bool HasAllMaterials(IReadOnlyList<RecipeInput> inputs)
    {
        if (_ctx is null || _data is null) return false;

        var itemSystem = _ctx.TryGet<ItemSystem>();
        if (itemSystem is null) return false;

        return TryMatchInputIds(inputs, itemSystem.GetAllItems().ToList(), out _);
    }

    private bool TryMatchInputIds(IReadOnlyList<RecipeInput> inputs, IReadOnlyList<Item> availableItems, out List<int> matchedIds)
    {
        var requiredTags = inputs
            .SelectMany(input => Enumerable.Repeat(input.RequiredTags, input.Quantity))
            .OrderByDescending(tags => tags.Count)
            .ToList();

        matchedIds = new List<int>();
        return TryMatchInputs(requiredTags, 0, availableItems, new HashSet<int>(), matchedIds);
    }

    private bool TryMatchInputs(
        IReadOnlyList<TagSet> requiredTags,
        int index,
        IReadOnlyList<Item> availableItems,
        HashSet<int> matchedIds,
        List<int> consumedIds)
    {
        if (index >= requiredTags.Count)
            return true;

        var tags = requiredTags[index];
        var candidates = availableItems
            .Where(item => !matchedIds.Contains(item.Id) && MatchesInput(item, tags))
            .ToList();

        foreach (var candidate in candidates)
        {
            matchedIds.Add(candidate.Id);
            consumedIds.Add(candidate.Id);

            if (TryMatchInputs(requiredTags, index + 1, availableItems, matchedIds, consumedIds))
                return true;

            consumedIds.RemoveAt(consumedIds.Count - 1);
            matchedIds.Remove(candidate.Id);
        }

        return false;
    }

    private bool MatchesInput(Item item, TagSet requiredTags)
    {
        if (_data is null) return false;
        var def = _data.Items.GetOrNull(item.DefId);
        return def?.Tags.HasAll(requiredTags.All.ToArray()) ?? false;
    }

    // ── Public Query API ────────────────────────────────────────────────────

    public bool IsBuildingUnlocked(string buildingId) => _unlockedBuildings.Contains(buildingId);
    public bool IsRecipeUnlocked(string recipeId)     => _unlockedRecipes.Contains(recipeId);

    public IEnumerable<string> GetUnlockedBuildings() => _unlockedBuildings;
    public IEnumerable<string> GetUnlockedRecipes()   => _unlockedRecipes;

    public IReadOnlySet<string> GetDiscoveredTags() => _discoveredTags;

    /// <summary>
    /// Returns the item ID that triggered the discovery of a building or recipe.
    /// </summary>
    public string? GetDiscoveredBy(string id) => _discoveredByItem.GetValueOrDefault(id);

    /// <summary>
    /// Returns all buildings that are NOT yet unlocked, along with which
    /// materials the player has and which are still missing.
    /// </summary>
    public IEnumerable<BuildingDiscoveryInfo> GetPendingBuildings()
    {
        if (_data is null) yield break;
        foreach (var building in _data.Buildings.All())
        {
            if (_unlockedBuildings.Contains(building.Id)) continue;
            yield return new BuildingDiscoveryInfo(
                Id: building.Id,
                DisplayName: building.DisplayName,
                HasMaterials: GetMaterialStatus(building.ConstructionInputs),
                TriggerTags: GetTriggerTags(building.ConstructionInputs));
        }
    }

    /// <summary>
    /// Returns all recipes that are NOT yet unlocked, along with which
    /// materials the player has and which are still missing.
    /// </summary>
    public IEnumerable<RecipeDiscoveryInfo> GetPendingRecipes()
    {
        if (_data is null) yield break;
        foreach (var recipe in _data.Recipes.All())
        {
            if (_unlockedRecipes.Contains(recipe.Id)) continue;
            yield return new RecipeDiscoveryInfo(
                Id: recipe.Id,
                DisplayName: recipe.DisplayName,
                HasMaterials: GetMaterialStatus(recipe.Inputs),
                TriggerTags: GetTriggerTags(recipe.Inputs));
        }
    }

    private List<MaterialStatus> GetMaterialStatus(IReadOnlyList<RecipeInput> inputs)
    {
        var status = new List<MaterialStatus>();
        foreach (var input in inputs)
        {
            var primaryTag = input.RequiredTags.All.FirstOrDefault() ?? "unknown";
            status.Add(new MaterialStatus(
                Tag: primaryTag,
                IsDiscovered: HasAllMaterials(new[] { input })));
        }
        return status;
    }

    private List<string> GetTriggerTags(IReadOnlyList<RecipeInput> inputs)
    {
        var tags = new List<string>();
        foreach (var input in inputs)
        {
            var primaryTag = input.RequiredTags.All.FirstOrDefault() ?? "unknown";
            if (!tags.Contains(primaryTag))
                tags.Add(primaryTag);
        }
        return tags;
    }

    /// <summary>
    /// Returns all recipes that use a given building (workshop).
    /// </summary>
    public IEnumerable<string> GetRecipesForBuilding(string buildingId)
    {
        if (_data is null) yield break;
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
        if (_data is null) yield break;
        foreach (var recipe in _data.Recipes.All())
        {
            if (!_unlockedRecipes.Contains(recipe.Id)) continue;
            foreach (var output in recipe.Outputs)
            {
                yield return output.ItemDefId;
            }
        }
    }

    // ── Save/Load ───────────────────────────────────────────────────────────

    public void OnSave(SaveWriter w)
    {
        w.Write("discoveredTags", _discoveredTags.ToList());
        w.Write("unlockedBuildings", _unlockedBuildings.ToList());
        w.Write("unlockedRecipes", _unlockedRecipes.ToList());
        w.Write("discoveredByItem", _discoveredByItem);
    }

    public void OnLoad(SaveReader r)
    {
        var tags = r.TryRead<List<string>>("discoveredTags");
        if (tags is not null) foreach (var t in tags) _discoveredTags.Add(t);

        var buildings = r.TryRead<List<string>>("unlockedBuildings");
        if (buildings is not null) foreach (var b in buildings) _unlockedBuildings.Add(b);

        var recipes = r.TryRead<List<string>>("unlockedRecipes");
        if (recipes is not null) foreach (var r2 in recipes) _unlockedRecipes.Add(r2);

        var byItem = r.TryRead<Dictionary<string, string>>("discoveredByItem");
        if (byItem is not null) foreach (var kv in byItem) _discoveredByItem[kv.Key] = kv.Value;
    }
}

// ── Discovery Info Records ──────────────────────────────────────────────────

public record struct MaterialStatus(string Tag, bool IsDiscovered);

public record struct BuildingDiscoveryInfo(
    string Id,
    string DisplayName,
    List<MaterialStatus> HasMaterials,
    List<string> TriggerTags);

public record struct RecipeDiscoveryInfo(
    string Id,
    string DisplayName,
    List<MaterialStatus> HasMaterials,
    List<string> TriggerTags);
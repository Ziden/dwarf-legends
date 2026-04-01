using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;

namespace DwarfFortress.GameLogic.Data;

/// <summary>
/// Loads all JSON definition files and exposes typed read-only registries.
/// Order 0 — must initialize before any other system.
/// </summary>
public sealed class DataManager : IGameSystem
{
    // ── IGameSystem ────────────────────────────────────────────────────────
    public string SystemId    => SystemIds.DataManager;
    public int    UpdateOrder => 0;
    public bool   IsEnabled   { get; set; } = true;

    // ── Registries ─────────────────────────────────────────────────────────
    public Registry<MaterialDef>   Materials   { get; } = new();
    public Registry<TileDef>       Tiles       { get; } = new();
    public Registry<ItemDef>       Items       { get; } = new();
    public Registry<PlantDef>      Plants      { get; } = new();
    public Registry<RecipeDef>     Recipes     { get; } = new();
    public Registry<JobDef>        Jobs        { get; } = new();
    public Registry<CreatureDef>   Creatures   { get; } = new();
    public Registry<ReactionDef>   Reactions   { get; } = new();
    public Registry<WorldEventDef> WorldEvents { get; } = new();
    public Registry<BuildingDef>   Buildings   { get; } = new();
    public Registry<DiscoveryDef>  Discoveries { get; } = new();
    public Registry<TraitDef>      Traits      { get; } = new();

    // ── IGameSystem.Initialize ─────────────────────────────────────────────

    public void Initialize(GameContext ctx)
    {
        var ds = ctx.DataSource;
        var log = ctx.Logger;

        LoadRegistryFromFile(ds, log, "data/ConfigBundle/materials.json",    ParseMaterial,   Materials);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/tiles.json",        ParseTile,       Tiles);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/items.json",        ParseItem,       Items);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/plants.json",       ParsePlant,      Plants);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/recipes.json",      ParseRecipe,     Recipes);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/jobs.json",         ParseJob,        Jobs);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/creatures.json",    ParseCreature,   Creatures);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/reactions.json",    ParseReaction,   Reactions);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/world_events.json", ParseWorldEvent, WorldEvents);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/buildings.json",    ParseBuilding,   Buildings);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/discoveries.json",  ParseDiscovery,  Discoveries);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/traits.json",       ParseTrait,      Traits);

        Materials.Seal(); Tiles.Seal();  Items.Seal();  Plants.Seal(); Recipes.Seal();
        Jobs.Seal();      Creatures.Seal(); Reactions.Seal(); WorldEvents.Seal();
        Buildings.Seal(); Discoveries.Seal(); Traits.Seal();

        log.Info($"[DataManager] Loaded: {Materials.Count} materials, {Tiles.Count} tiles, " +
             $"{Items.Count} items, {Plants.Count} plants, {Recipes.Count} recipes, {Jobs.Count} jobs, " +
                 $"{Creatures.Count} creatures, {Reactions.Count} reactions, " +
                 $"{WorldEvents.Count} world_events, {Buildings.Count} buildings, {Discoveries.Count} discoveries, {Traits.Count} traits.");
    }

    private static void LoadRegistryFromFile<T>(
        IDataSource       ds,
        ILogger           log,
        string            path,
        Func<JsonNode, T> parser,
        Registry<T>       registry) where T : class
    {
        if (!ds.Exists(path))
        {
            log.Warn($"[DataManager] File '{path}' not found — skipping.");
            return;
        }

        try
        {
            var text = ds.ReadText(path);
            var root = JsonNode.Parse(text, null, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip
            })!;

            // Support both a single object { } and an array [ { }, { } ]
            if (root is JsonArray arr)
            {
                foreach (var node in arr)
                    if (node is not null)
                    {
                        var def = parser(node);
                        registry.Add(GetId(node), def);
                    }
            }
            else
            {
                var def = parser(root);
                registry.Add(GetId(root), def);
            }
        }
        catch (Exception ex)
        {
            log.Error($"[DataManager] Failed to parse '{path}': {ex.Message}");
        }
    }

    private static string GetId(JsonNode node)
        => node["id"]?.GetValue<string>()
           ?? throw new InvalidOperationException("Definition missing required 'id' field.");

    // ── Parsers ────────────────────────────────────────────────────────────

    private static MaterialDef ParseMaterial(JsonNode n) => new(
        Id:           n["id"]!.GetValue<string>(),
        DisplayName:  n["displayName"]?.GetValue<string>() ?? n["id"]!.GetValue<string>(),
        Tags:         ParseTagSet(n["tags"]),
        Hardness:     n["hardness"]?.GetValue<float>()     ?? 1.0f,
        MeltingPoint: n["meltingPoint"]?.GetValue<float>() ?? float.MaxValue,
        Density:      n["density"]?.GetValue<float>()      ?? 1.0f,
        Value:        n["value"]?.GetValue<int>()          ?? 1,
        Color:        n["color"]?.GetValue<string>());

    private static TileDef ParseTile(JsonNode n) => new(
        Id:            n["id"]!.GetValue<string>(),
        DisplayName:   n["displayName"]?.GetValue<string>() ?? n["id"]!.GetValue<string>(),
        Tags:          ParseTagSet(n["tags"]),
        IsPassable:    n["isPassable"]?.GetValue<bool>()    ?? true,
        IsOpaque:      n["isOpaque"]?.GetValue<bool>()      ?? false,
        IsMineable:    n["isMineable"]?.GetValue<bool>()    ?? false,
        TilesetIndex:  n["tilesetIndex"]?.GetValue<int>()   ?? 0,
        Color:         n["color"]?.GetValue<string>(),
        DropItemDefId: n["dropItemDefId"]?.GetValue<string>());

    private static ItemDef ParseItem(JsonNode n) => new(
        Id:          n["id"]!.GetValue<string>(),
        DisplayName: n["displayName"]?.GetValue<string>() ?? n["id"]!.GetValue<string>(),
        Tags:        ParseTagSet(n["tags"]),
        Stackable:   n["stackable"]?.GetValue<bool>()  ?? false,
        MaxStack:    n["maxStack"]?.GetValue<int>()    ?? 1,
        Weight:      n["weight"]?.GetValue<float>()   ?? 1.0f,
        BaseValue:   n["baseValue"]?.GetValue<int>()  ?? 1,
        UseEffects:  ParseUseEffects(n["useEffects"]),
        Nutrition:   ParseNutrition(n["nutrition"]));

    private static NutritionProfile? ParseNutrition(JsonNode? node)
    {
        if (node is null) return null;
        return new NutritionProfile(
            Carbs:    node["carbs"]?.GetValue<float>()    ?? 0.4f,
            Protein:  node["protein"]?.GetValue<float>()  ?? 0.4f,
            Fat:      node["fat"]?.GetValue<float>()      ?? 0.3f,
            Vitamins: node["vitamins"]?.GetValue<float>() ?? 0.4f);
    }

    private static IReadOnlyList<EffectBlock>? ParseUseEffects(JsonNode? node)
    {
        if (node is not JsonArray arr) return null;
        var effects = new List<EffectBlock>();
        foreach (var e in arr)
        {
            if (e is not null)
                effects.Add(new EffectBlock(
                    e["op"]!.GetValue<string>(),
                    ParseStringDict(e["params"])));
        }
        return effects;
    }

    private static PlantDef ParsePlant(JsonNode n) => new(
        Id:                    n["id"]!.GetValue<string>(),
        DisplayName:           n["displayName"]?.GetValue<string>() ?? n["id"]!.GetValue<string>(),
        HostKind:              ParsePlantHostKind(n["hostKind"]?.GetValue<string>()),
        AllowedBiomeIds:       ParseStringList(n["allowedBiomes"]),
        AllowedGroundTileDefIds: ParseStringList(n["allowedGroundTiles"]),
        SupportedTreeSpeciesIds: ParseStringList(n["supportedTreeSpecies"]),
        MinMoisture:           n["minMoisture"]?.GetValue<float>() ?? 0f,
        MaxMoisture:           n["maxMoisture"]?.GetValue<float>() ?? 1f,
        MinTerrain:            n["minTerrain"]?.GetValue<float>() ?? 0f,
        MaxTerrain:            n["maxTerrain"]?.GetValue<float>() ?? 1f,
        PrefersNearWater:      n["prefersNearWater"]?.GetValue<bool>() ?? false,
        PrefersFarFromWater:   n["prefersFarFromWater"]?.GetValue<bool>() ?? false,
        MaxGrowthStage:        n["maxGrowthStage"]?.GetValue<byte>() ?? 3,
        SecondsPerStage:       n["secondsPerStage"]?.GetValue<float>() ?? 240f,
        FruitingCycleSeconds:  n["fruitingCycleSeconds"]?.GetValue<float>() ?? 420f,
        SeedSpreadChance:      n["seedSpreadChance"]?.GetValue<float>() ?? 0.08f,
        SeedSpreadRadiusMin:   n["seedSpreadRadiusMin"]?.GetValue<int>() ?? 2,
        SeedSpreadRadiusMax:   n["seedSpreadRadiusMax"]?.GetValue<int>() ?? 5,
        Energy:                n["nutrition"]?["energy"]?.GetValue<float>() ?? 0f,
        Protein:               n["nutrition"]?["protein"]?.GetValue<float>() ?? 0f,
        Vitamins:              n["nutrition"]?["vitamins"]?.GetValue<float>() ?? 0f,
        Minerals:              n["nutrition"]?["minerals"]?.GetValue<float>() ?? 0f,
        HarvestItemDefId:      n["harvestItemDefId"]?.GetValue<string>(),
        SeedItemDefId:         n["seedItemDefId"]?.GetValue<string>(),
        FruitItemDefId:        n["fruitItemDefId"]?.GetValue<string>());

    private static RecipeDef ParseRecipe(JsonNode n)
    {
        var inputs  = new List<RecipeInput>();
        var outputs = new List<RecipeOutput>();

        if (n["inputs"] is JsonArray inp)
            foreach (var i in inp)
                if (i is not null)
                    inputs.Add(new RecipeInput(ParseTagSet(i["tags"]), i["qty"]?.GetValue<int>() ?? 1));

        if (n["outputs"] is JsonArray out_)
            foreach (var o in out_)
                if (o is not null)
                    outputs.Add(new RecipeOutput(
                        o["itemId"]!.GetValue<string>(),
                        o["qty"]?.GetValue<int>() ?? 1,
                        o["materialFrom"]?.GetValue<string>()));

        return new RecipeDef(
            Id:               n["id"]!.GetValue<string>(),
            DisplayName:      n["displayName"]?.GetValue<string>() ?? n["id"]!.GetValue<string>(),
            WorkshopDefId:    n["workshop"]!.GetValue<string>(),
            RequiredLaborId:  n["labor"]?.GetValue<string>() ?? "crafting",
            Inputs:           inputs,
            Outputs:          outputs,
            WorkTime:         n["workTime"]?.GetValue<float>() ?? 100f,
            SkillXp:          n["skillXp"]?.GetValue<int>()   ?? 10);
    }

    private static JobDef ParseJob(JsonNode n) => new(
        Id:             n["id"]!.GetValue<string>(),
        DisplayName:    n["displayName"]?.GetValue<string>() ?? n["id"]!.GetValue<string>(),
        RequiredLaborId:n["labor"]?.GetValue<string>()       ?? "misc",
        WorkTime:       n["workTime"]?.GetValue<float>()     ?? 50f,
        Priority:       n["priority"]?.GetValue<int>()       ?? 0,
        Tags:           ParseTagSet(n["tags"]));

    private static CreatureDef ParseCreature(JsonNode n) => new(
        Id:            n["id"]!.GetValue<string>(),
        DisplayName:   n["displayName"]?.GetValue<string>() ?? n["id"]!.GetValue<string>(),
        Tags:          ParseTagSet(n["tags"]),
        BaseSpeed:     n["speed"]?.GetValue<float>()      ?? 1.0f,
        BaseStrength:  n["strength"]?.GetValue<float>()   ?? 1.0f,
        BaseToughness: n["toughness"]?.GetValue<float>()  ?? 1.0f,
        MaxHealth:     n["maxHealth"]?.GetValue<float>()  ?? 100f,
        IsPlayable:    n["isPlayable"]?.GetValue<bool>()  ?? false,
        IsSapient:     n["isSapient"]?.GetValue<bool>()   ?? false);

    private static ReactionDef ParseReaction(JsonNode n)
    {
        var triggers = new List<ReactionTrigger>();
        var effects  = new List<EffectBlock>();

        if (n["triggers"] is JsonArray trg)
            foreach (var t in trg)
                if (t is not null)
                    triggers.Add(new ReactionTrigger(
                        t["type"]!.GetValue<string>(),
                        ParseStringDict(t["params"])));

        if (n["effects"] is JsonArray eff)
            foreach (var e in eff)
                if (e is not null)
                    effects.Add(new EffectBlock(
                        e["op"]!.GetValue<string>(),
                        ParseStringDict(e["params"])));

        return new ReactionDef(
            Id:          n["id"]!.GetValue<string>(),
            Triggers:    triggers,
            Effects:     effects,
            Probability: n["probability"]?.GetValue<float>() ?? 1.0f);
    }

    private static WorldEventDef ParseWorldEvent(JsonNode n)
    {
        var triggers = new List<EventTrigger>();
        var effects  = new List<EffectBlock>();

        if (n["triggers"] is JsonArray trg)
            foreach (var t in trg)
                if (t is not null)
                    triggers.Add(new EventTrigger(
                        t["type"]!.GetValue<string>(),
                        ParseStringDict(t["params"])));

        if (n["effects"] is JsonArray eff)
            foreach (var e in eff)
                if (e is not null)
                    effects.Add(new EffectBlock(
                        e["op"]!.GetValue<string>(),
                        ParseStringDict(e["params"])));

        return new WorldEventDef(
            Id:          n["id"]!.GetValue<string>(),
            DisplayName: n["displayName"]?.GetValue<string>() ?? n["id"]!.GetValue<string>(),
            Triggers:    triggers,
            Effects:     effects,
            Probability: n["probability"]?.GetValue<float>() ?? 1.0f,
            Cooldown:    n["cooldown"]?.GetValue<float>()    ?? 0f,
            Repeatable:  n["repeatable"]?.GetValue<bool>()  ?? true);
    }

    private static DiscoveryDef ParseDiscovery(JsonNode n) => new(
        Id:           n["id"]!.GetValue<string>(),
        DisplayName:  n["displayName"]?.GetValue<string>() ?? n["id"]!.GetValue<string>(),
        Description:  n["description"]?.GetValue<string>(),
        Category:     n["category"]?.GetValue<string>());

    private static TraitDef ParseTrait(JsonNode n) => new(
        Id:           n["id"]!.GetValue<string>(),
        DisplayName:  n["displayName"]?.GetValue<string>() ?? n["id"]!.GetValue<string>(),
        Description:  n["description"]?.GetValue<string>() ?? "",
        Category:     n["category"]?.GetValue<string>() ?? "physical",
        Tags:         ParseTagSet(n["tags"]));

    private static BuildingDef ParseBuilding(JsonNode n)
    {
        var footprint = new List<BuildingTile>();
        var inputs    = new List<RecipeInput>();

        if (n["footprint"] is JsonArray fp)
            foreach (var t in fp)
                if (t is not null)
                    footprint.Add(new BuildingTile(
                        new Core.Vec2i(t["x"]?.GetValue<int>() ?? 0, t["y"]?.GetValue<int>() ?? 0),
                        t["tile"]!.GetValue<string>()));

        if (n["constructionInputs"] is JsonArray ci)
            foreach (var i in ci)
                if (i is not null)
                    inputs.Add(new RecipeInput(ParseTagSet(i["tags"]), i["qty"]?.GetValue<int>() ?? 1));

        return new BuildingDef(
            Id:                 n["id"]!.GetValue<string>(),
            DisplayName:        n["displayName"]?.GetValue<string>() ?? n["id"]!.GetValue<string>(),
            Tags:               ParseTagSet(n["tags"]),
            Footprint:          footprint,
            ConstructionInputs: inputs,
            ConstructionTime:   n["constructionTime"]?.GetValue<float>() ?? 50f,
            IsWorkshop:         n["isWorkshop"]?.GetValue<bool>() ?? false);
    }

    // ── Shared helpers ─────────────────────────────────────────────────────

    private static TagSet ParseTagSet(JsonNode? node)
    {
        if (node is not JsonArray arr) return TagSet.Empty;
        var tags = new List<string>();
        foreach (var t in arr)
            if (t?.GetValue<string>() is string s)
                tags.Add(s);
        return TagSet.From(tags);
    }

    private static IReadOnlyList<string> ParseStringList(JsonNode? node)
    {
        if (node is not JsonArray arr)
            return System.Array.Empty<string>();

        var values = new List<string>();
        foreach (var value in arr)
        {
            if (value?.GetValue<string>() is string text && !string.IsNullOrWhiteSpace(text))
                values.Add(text);
        }

        return values;
    }

    private static PlantHostKind ParsePlantHostKind(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "tree" => PlantHostKind.Tree,
            _ => PlantHostKind.Ground,
        };

    private static IReadOnlyDictionary<string, string> ParseStringDict(JsonNode? node)
    {
        var dict = new Dictionary<string, string>();
        if (node is JsonObject obj)
            foreach (var kv in obj)
                if (kv.Value?.GetValue<string>() is string v)
                    dict[kv.Key] = v;
        return dict;
    }

    // ── IGameSystem (noop for Data layer) ──────────────────────────────────
    public void Tick(float delta)    { }
    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }
}

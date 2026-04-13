using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.WorldGen.Content;

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
    public Registry<MaterialDef>      Materials      { get; } = new();
    public Registry<TileDef>          Tiles          { get; } = new();
    public Registry<ItemDef>          Items          { get; } = new();
    public Registry<PlantDef>         Plants         { get; } = new();
    public Registry<RecipeDef>        Recipes        { get; } = new();
    public Registry<JobDef>           Jobs           { get; } = new();
    public Registry<CreatureDef>      Creatures      { get; } = new();
    public Registry<ReactionDef>      Reactions      { get; } = new();
    public Registry<WorldEventDef>    WorldEvents    { get; } = new();
    public Registry<BuildingDef>      Buildings      { get; } = new();
    public Registry<DiscoveryDef>     Discoveries    { get; } = new();
    public Registry<DwarfAttributeDef> Attributes    { get; } = new();
    public SharedContentCatalog?      SharedContent  { get; private set; }
    public ContentQueryService?       ContentQueries { get; private set; }

    // ── IGameSystem.Initialize ─────────────────────────────────────────────

    public void Initialize(GameContext ctx)
    {
        var ds = ctx.DataSource;
        var log = ctx.Logger;

        SharedContent = SharedContentCatalogLoader.Load(new DataSourceContentFileSource(ds));
        ContentQueries = new ContentQueryService(SharedContent);

        LoadMaterialsFromCatalog(SharedContent, Materials);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/tiles.json",        ParseTile,       Tiles);
        LoadItemsFromCatalog(SharedContent, Items);
        LoadPlantsFromCatalog(SharedContent, Plants);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/recipes.json",      ParseRecipe,     Recipes);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/jobs.json",         ParseJob,        Jobs);
        LoadCreaturesFromCatalog(SharedContent, Creatures);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/reactions.json",    ParseReaction,   Reactions);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/world_events.json", ParseWorldEvent, WorldEvents);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/buildings.json",       ParseBuilding,      Buildings);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/discoveries.json",     ParseDiscovery,     Discoveries);
        LoadRegistryFromFile(ds, log, "data/ConfigBundle/dwarf_attributes.json", ParseDwarfAttribute, Attributes);

        Materials.Seal(); Tiles.Seal();   Items.Seal();   Plants.Seal();   Recipes.Seal();
        Jobs.Seal();      Creatures.Seal(); Reactions.Seal(); WorldEvents.Seal();
        Buildings.Seal(); Discoveries.Seal(); Attributes.Seal();

        log.Info($"[DataManager] Loaded: {Materials.Count} materials, {Tiles.Count} tiles, " +
             $"{Items.Count} items, {Plants.Count} plants, {Recipes.Count} recipes, {Jobs.Count} jobs, " +
                 $"{Creatures.Count} creatures, {Reactions.Count} reactions, " +
                 $"{WorldEvents.Count} world_events, {Buildings.Count} buildings, {Discoveries.Count} discoveries, {Attributes.Count} attributes.");
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
            else if (root is JsonObject obj && LooksLikeDefinitionMap(obj))
            {
                foreach (var (key, value) in obj)
                {
                    if (value is not JsonObject entry)
                        continue;

                    entry["id"] ??= key;
                    var def = parser(entry);
                    registry.Add(GetId(entry), def);
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

    private static void LoadMaterialsFromCatalog(SharedContentCatalog catalog, Registry<MaterialDef> registry)
    {
        foreach (var material in catalog.Materials.Values.OrderBy(material => material.Id, StringComparer.OrdinalIgnoreCase))
        {
            registry.Add(material.Id, new MaterialDef(
                Id: material.Id,
                DisplayName: material.DisplayName,
                Tags: TagSet.From(material.Tags),
                Hardness: material.Hardness,
                MeltingPoint: material.MeltingPoint,
                Density: material.Density,
                Value: material.Value,
                Color: material.Color));
        }
    }

    private static void LoadItemsFromCatalog(SharedContentCatalog catalog, Registry<ItemDef> registry)
    {
        foreach (var item in catalog.Items.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            registry.Add(item.Id, new ItemDef(
                Id: item.Id,
                DisplayName: item.DisplayName,
                Tags: TagSet.From(item.Tags),
                Stackable: item.Stackable,
                MaxStack: item.MaxStack,
                Weight: item.Weight,
                BaseValue: item.BaseValue,
                Nutrition: item.Nutrition is null
                    ? null
                    : new NutritionProfile(
                        item.Nutrition.Carbs,
                        item.Nutrition.Protein,
                        item.Nutrition.Fat,
                        item.Nutrition.Vitamins)));
        }
    }

    private static void LoadPlantsFromCatalog(SharedContentCatalog catalog, Registry<PlantDef> registry)
    {
        foreach (var plant in catalog.Plants.Values.OrderBy(plant => plant.Id, StringComparer.OrdinalIgnoreCase))
        {
            registry.Add(plant.Id, new PlantDef(
                Id: plant.Id,
                DisplayName: plant.DisplayName,
                HostKind: plant.HostKind == PlantContentHostKind.Tree ? PlantHostKind.Tree : PlantHostKind.Ground,
                AllowedBiomeIds: plant.AllowedBiomeIds,
                AllowedGroundTileDefIds: plant.AllowedGroundTileDefIds,
                SupportedTreeSpeciesIds: plant.SupportedTreeSpeciesIds,
                MinMoisture: plant.MinMoisture,
                MaxMoisture: plant.MaxMoisture,
                MinTerrain: plant.MinTerrain,
                MaxTerrain: plant.MaxTerrain,
                PrefersNearWater: plant.PrefersNearWater,
                PrefersFarFromWater: plant.PrefersFarFromWater,
                MaxGrowthStage: plant.MaxGrowthStage,
                SecondsPerStage: plant.SecondsPerStage,
                FruitingCycleSeconds: plant.FruitingCycleSeconds,
                SeedSpreadChance: plant.SeedSpreadChance,
                SeedSpreadRadiusMin: plant.SeedSpreadRadiusMin,
                SeedSpreadRadiusMax: plant.SeedSpreadRadiusMax,
                Energy: plant.Energy,
                Protein: plant.Protein,
                Vitamins: plant.Vitamins,
                Minerals: plant.Minerals,
                HarvestItemDefId: plant.HarvestItemDefId,
                SeedItemDefId: plant.SeedItemDefId,
                FruitItemDefId: plant.FruitItemDefId,
                DropYieldOnHostRemoval: plant.DropYieldOnHostRemoval));
        }
    }

    private static void LoadCreaturesFromCatalog(SharedContentCatalog catalog, Registry<CreatureDef> registry)
    {
        foreach (var creature in catalog.Creatures.Values.OrderBy(creature => creature.Id, StringComparer.OrdinalIgnoreCase))
        {
            registry.Add(creature.Id, new CreatureDef(
                Id: creature.Id,
                DisplayName: creature.DisplayName,
                Tags: TagSet.From(creature.Tags),
                BaseSpeed: creature.BaseSpeed,
                BaseStrength: creature.BaseStrength,
                BaseToughness: creature.BaseToughness,
                MaxHealth: creature.MaxHealth,
                IsPlayable: creature.IsPlayable,
                IsSapient: creature.IsSapient,
                AuthoredIsHostile: creature.IsHostile,
                AuthoredCanGroom: creature.CanGroom,
                AuthoredDiet: ParseCreatureDiet(creature.DietId),
                AuthoredMovementMode: ParseCreatureMovementMode(creature.MovementModeId),
                BodyParts: creature.BodyParts?
                    .Select(part => new BodyPartDef(part.Id, part.DisplayName, part.HitWeight, part.IsVital))
                    .ToArray(),
                NaturalLabors: creature.NaturalLabors,
                DeathDrops: creature.DeathDrops?
                    .Select(drop => new CreatureDeathDropDef(drop.ItemDefId, drop.Quantity, drop.MaterialId))
                    .ToArray(),
                FactionRoles: creature.Society?.FactionRoles?
                    .Select(role => new CreatureFactionRoleDef(role.Id, role.Weight))
                    .ToArray()));
        }
    }

    private static string GetId(JsonNode node)
        => node["id"]?.GetValue<string>()
           ?? throw new InvalidOperationException("Definition missing required 'id' field.");

    private static bool LooksLikeDefinitionMap(JsonObject obj)
        => !obj.ContainsKey("id") && obj.Count > 0 && obj.All(entry => entry.Value is JsonObject);

    private static CreatureDiet? ParseCreatureDiet(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            ContentCreatureDietIds.Herbivore => CreatureDiet.Herbivore,
            ContentCreatureDietIds.Carnivore => CreatureDiet.Carnivore,
            ContentCreatureDietIds.Omnivore => CreatureDiet.Omnivore,
            ContentCreatureDietIds.AquaticGrazer => CreatureDiet.AquaticGrazer,
            _ => null,
        };
    }

    private static CreatureMovementMode? ParseCreatureMovementMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            ContentCreatureMovementModeIds.Land => CreatureMovementMode.Land,
            ContentCreatureMovementModeIds.Swimmer => CreatureMovementMode.Swimmer,
            ContentCreatureMovementModeIds.Aquatic => CreatureMovementMode.Aquatic,
            _ => null,
        };
    }

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
        FruitItemDefId:        n["fruitItemDefId"]?.GetValue<string>(),
        DropYieldOnHostRemoval: n["dropYieldOnHostRemoval"]?.GetValue<bool>() ?? false);

    private static RecipeDef ParseRecipe(JsonNode n)
    {
        var inputs = ParseRecipeInputs(n["inputs"]);
        var discoveryInputs = ParseRecipeInputs(n["discoveryInputs"]);
        var outputs = new List<RecipeOutput>();

        if (n["outputs"] is JsonArray out_)
            foreach (var o in out_)
                if (o is not null)
                    outputs.Add(new RecipeOutput(
                        o["itemId"]?.GetValue<string>(),
                        o["qty"]?.GetValue<int>() ?? 1,
                        o["materialFrom"]?.GetValue<string>(),
                        o["formRole"]?.GetValue<string>()));

        return new RecipeDef(
            Id:               n["id"]!.GetValue<string>(),
            DisplayName:      n["displayName"]?.GetValue<string>() ?? n["id"]!.GetValue<string>(),
            WorkshopDefId:    n["workshop"]!.GetValue<string>(),
            RequiredLaborId:  n["labor"]?.GetValue<string>() ?? "crafting",
            Inputs:           inputs,
            DiscoveryInputs:  discoveryInputs,
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

    private static DwarfAttributeDef ParseDwarfAttribute(JsonNode n) => new(
        Id:           n["id"]!.GetValue<string>(),
        DisplayName:  n["displayName"]?.GetValue<string>() ?? n["id"]!.GetValue<string>(),
        Description:  n["description"]?.GetValue<string>() ?? "",
        Category:     n["category"]?.GetValue<string>() ?? "physiological",
        Tags:         ParseTagSet(n["tags"]),
        EffectCurves: ParseEffectCurves(n["effectCurves"]));

    private static IReadOnlyDictionary<string, AttributeEffectCurve> ParseEffectCurves(JsonNode? node)
    {
        if (node is not JsonObject obj) return new Dictionary<string, AttributeEffectCurve>();
        var curves = new Dictionary<string, AttributeEffectCurve>();
        foreach (var kv in obj)
        {
            if (kv.Value is not JsonObject entry) continue;
            var effects = new Dictionary<string, float>();
            if (entry["effects"] is JsonObject eff)
            {
                foreach (var e in eff)
                    if (TryReadFloat(e.Value, out var effectValue))
                        effects[e.Key] = effectValue;
            }
            curves[kv.Key] = new AttributeEffectCurve(effects);
        }
        return curves;
    }

    private static bool TryReadFloat(JsonNode? node, out float value)
    {
        value = 0f;
        if (node is not JsonValue jsonValue)
            return false;

        if (jsonValue.TryGetValue<float>(out value))
            return true;

        if (jsonValue.TryGetValue<double>(out var doubleValue))
        {
            value = (float)doubleValue;
            return true;
        }

        if (jsonValue.TryGetValue<int>(out var intValue))
        {
            value = intValue;
            return true;
        }

        if (jsonValue.TryGetValue<long>(out var longValue))
        {
            value = longValue;
            return true;
        }

        if (jsonValue.TryGetValue<string>(out var stringValue) && float.TryParse(stringValue, out value))
            return true;

        return false;
    }

    private static BuildingDef ParseBuilding(JsonNode n)
    {
        var footprint = new List<BuildingTile>();
        var inputs = ParseRecipeInputs(n["constructionInputs"]);
        var discoveryInputs = ParseRecipeInputs(n["discoveryInputs"]);
        var entryOffsets = new List<Core.Vec2i>();

        if (n["footprint"] is JsonArray fp)
            foreach (var t in fp)
                if (t is not null)
                    footprint.Add(new BuildingTile(
                        new Core.Vec2i(t["x"]?.GetValue<int>() ?? 0, t["y"]?.GetValue<int>() ?? 0),
                        t["tile"]!.GetValue<string>()));

        if (n["entryOffsets"] is JsonArray eo)
            foreach (var entry in eo)
                if (entry is not null)
                    entryOffsets.Add(new Core.Vec2i(
                        entry["x"]?.GetValue<int>() ?? 0,
                        entry["y"]?.GetValue<int>() ?? 0));

        return new BuildingDef(
            Id:                 n["id"]!.GetValue<string>(),
            DisplayName:        n["displayName"]?.GetValue<string>() ?? n["id"]!.GetValue<string>(),
            Tags:               ParseTagSet(n["tags"]),
            Footprint:          footprint,
            ConstructionInputs: inputs,
            DiscoveryInputs:    discoveryInputs,
            ConstructionTime:   n["constructionTime"]?.GetValue<float>() ?? 50f,
            IsWorkshop:         n["isWorkshop"]?.GetValue<bool>() ?? false,
            ProducedSmokeId:    n["producedSmokeId"]?.GetValue<string>(),
            ResidenceCapacity:  n["residenceCapacity"]?.GetValue<int>() ?? 0,
            EntryOffsets:       entryOffsets,
            AutoStockpileAcceptedTags: ParseStringList(n["autoStockpileAcceptedTags"]),
            StructureVisualId:  n["structureVisualId"]?.GetValue<string>());
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

    private static List<RecipeInput> ParseRecipeInputs(JsonNode? node)
    {
        var inputs = new List<RecipeInput>();
        if (node is not JsonArray arr)
            return inputs;

        foreach (var input in arr)
        {
            if (input is null)
                continue;

            inputs.Add(new RecipeInput(
                ParseTagSet(input["tags"]),
                input["qty"]?.GetValue<int>() ?? 1,
                input["itemId"]?.GetValue<string>(),
                input["materialId"]?.GetValue<string>()));
        }

        return inputs;
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

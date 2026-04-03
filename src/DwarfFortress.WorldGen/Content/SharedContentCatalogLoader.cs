using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using DwarfFortress.WorldGen.Config;

namespace DwarfFortress.WorldGen.Content;

public static class SharedContentCatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static SharedContentCatalog Load(IContentFileSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var report = new ContentLoadReport();
        var materials = new Dictionary<string, MaterialContentDefinition>(StringComparer.OrdinalIgnoreCase);
        var items = new Dictionary<string, ContentItemDefinition>(StringComparer.OrdinalIgnoreCase);
        var plants = new Dictionary<string, PlantContentDefinition>(StringComparer.OrdinalIgnoreCase);
        var treeSpecies = new Dictionary<string, TreeSpeciesContentDefinition>(StringComparer.OrdinalIgnoreCase);
        var creatures = new Dictionary<string, CreatureContentDefinition>(StringComparer.OrdinalIgnoreCase);

        LoadLegacyMaterials(source, materials);
        LoadLegacyItems(source, items);
        LoadLegacyPlants(source, plants);
        foreach (var record in DiscoverEntries(source, ContentRoots.Core, "data/Content/Core"))
            ApplyRecord(record, materials, items, plants, treeSpecies, creatures, report);

        foreach (var record in DiscoverEntries(source, ContentRoots.Game, "data/Content/Game"))
            ApplyRecord(record, materials, items, plants, treeSpecies, creatures, report);

        ValidateTreeSpeciesMaterials(materials, treeSpecies);
        ValidatePlantTreeSpecies(plants, treeSpecies);
        ValidateCreatures(creatures, items);

        return new SharedContentCatalog(materials, items, plants, treeSpecies, creatures, report);
    }

    public static SharedContentCatalog LoadDefaultOrFallback()
    {
        foreach (var dataPath in WorldGenConfigPathResolver.EnumerateCandidatePaths("data"))
        {
            if (!Directory.Exists(dataPath))
                continue;

            var repoRoot = Directory.GetParent(dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(repoRoot))
                continue;

            return Load(new DirectoryContentFileSource(repoRoot));
        }

        return new SharedContentCatalog(
            new Dictionary<string, MaterialContentDefinition>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, ContentItemDefinition>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, PlantContentDefinition>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, TreeSpeciesContentDefinition>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, CreatureContentDefinition>(StringComparer.OrdinalIgnoreCase),
            new ContentLoadReport());
    }

    private static IEnumerable<DiscoveredContentEntry> DiscoverEntries(IContentFileSource source, string rootName, string rootPath)
    {
        if (!source.Exists(rootPath))
            return Array.Empty<DiscoveredContentEntry>();

        var records = new List<DiscoveredContentEntry>();
        DiscoverFamily(source, rootName, rootPath, ContentFamilies.Materials, records);
        DiscoverFamily(source, rootName, rootPath, ContentFamilies.TreeSpecies, records);
        DiscoverFamily(source, rootName, rootPath, ContentFamilies.Plants, records);
        DiscoverFamily(source, rootName, rootPath, ContentFamilies.Creatures, records);
        return records;
    }

    private static void DiscoverFamily(
        IContentFileSource source,
        string rootName,
        string rootPath,
        string family,
        ICollection<DiscoveredContentEntry> target)
    {
        var directory = $"{rootPath}/{family}";
        if (!source.Exists(directory))
            return;

        foreach (var path in source.ListFiles(directory, recursive: true).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            target.Add(new DiscoveredContentEntry(rootName, family, path, source.ReadText(path)));
    }

    private static void ApplyRecord(
        DiscoveredContentEntry record,
        IDictionary<string, MaterialContentDefinition> materials,
        IDictionary<string, ContentItemDefinition> items,
        IDictionary<string, PlantContentDefinition> plants,
        IDictionary<string, TreeSpeciesContentDefinition> treeSpecies,
        IDictionary<string, CreatureContentDefinition> creatures,
        ContentLoadReport report)
    {
        switch (record.Family)
        {
            case ContentFamilies.Materials:
            {
                var material = ParseMaterial(record);
                MergeCurated(material.Id, record, materials, material, report);
                if (material.Forms is not null)
                {
                    foreach (var form in material.Forms.Values)
                        MergeBundledItem(form.Item, record, items, report);
                }

                break;
            }

            case ContentFamilies.TreeSpecies:
            {
                var species = ParseTreeSpecies(record);
                MergeCurated(species.Id, record, treeSpecies, species, report);
                break;
            }

            case ContentFamilies.Plants:
            {
                var plant = ParsePlant(record);
                MergeBundle(plant.Id, record, plants, plant, report);
                MergePlantItems(plant, record, items, report);
                break;
            }

            case ContentFamilies.Creatures:
            {
                var creature = ParseCreature(record);
                MergeBundle(creature.Id, record, creatures, creature, report);
                break;
            }
        }
    }

    private static void MergePlantItems(
        PlantContentDefinition plant,
        DiscoveredContentEntry record,
        IDictionary<string, ContentItemDefinition> items,
        ContentLoadReport report)
    {
        if (plant.HarvestItem is not null)
            MergeBundledItem(plant.HarvestItem, record, items, report);
        if (plant.SeedItem is not null)
            MergeBundledItem(plant.SeedItem, record, items, report);
        if (plant.FruitItem is not null)
            MergeBundledItem(plant.FruitItem, record, items, report);
    }

    private static void MergeBundledItem(
        ContentItemDefinition item,
        DiscoveredContentEntry record,
        IDictionary<string, ContentItemDefinition> items,
        ContentLoadReport report)
    {
        if (items.TryGetValue(item.Id, out var existing))
        {
            report.ShadowedEntries.Add(new ContentShadowRecord(
                "items",
                item.Id,
                existing.SourceRoot,
                record.RootName,
                existing.SourcePath,
                record.Path));
        }

        items[item.Id] = item with
        {
            SourceRoot = record.RootName,
            SourcePath = record.Path,
            SourceFamily = record.Family,
        };
    }

    private static void MergeBundle<T>(
        string id,
        DiscoveredContentEntry record,
        IDictionary<string, T> catalog,
        T entry,
        ContentLoadReport report) where T : notnull
    {
        if (catalog.TryGetValue(id, out var existing) && TryGetSource(existing, out var existingRoot, out var existingPath))
        {
            report.ShadowedEntries.Add(new ContentShadowRecord(
                record.Family,
                id,
                existingRoot,
                record.RootName,
                existingPath,
                record.Path));
        }

        catalog[id] = entry;
    }

    private static void MergeCurated<T>(
        string id,
        DiscoveredContentEntry record,
        IDictionary<string, T> catalog,
        T entry,
        ContentLoadReport report) where T : notnull
    {
        if (catalog.TryGetValue(id, out var existing) &&
            TryGetSource(existing, out var existingRoot, out var existingPath) &&
            !string.Equals(existingRoot, ContentRoots.Legacy, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Duplicate curated content id '{id}' in family '{record.Family}' from '{existingPath}' and '{record.Path}'.");
        }

        if (catalog.TryGetValue(id, out existing) && TryGetSource(existing, out existingRoot, out existingPath))
        {
            report.ShadowedEntries.Add(new ContentShadowRecord(
                record.Family,
                id,
                existingRoot,
                record.RootName,
                existingPath,
                record.Path));
        }

        catalog[id] = entry;
    }

    private static bool TryGetSource<T>(T entry, out string root, out string path)
    {
        switch (entry)
        {
            case ContentItemDefinition item:
                root = item.SourceRoot;
                path = item.SourcePath;
                return true;
            case MaterialContentDefinition material:
                root = material.SourceRoot;
                path = material.SourcePath;
                return true;
            case PlantContentDefinition plant:
                root = plant.SourceRoot;
                path = plant.SourcePath;
                return true;
            case TreeSpeciesContentDefinition species:
                root = species.SourceRoot;
                path = species.SourcePath;
                return true;
            case CreatureContentDefinition creature:
                root = creature.SourceRoot;
                path = creature.SourcePath;
                return true;
            default:
                root = string.Empty;
                path = string.Empty;
                return false;
        }
    }

    private static void ValidatePlantTreeSpecies(
        IReadOnlyDictionary<string, PlantContentDefinition> plants,
        IReadOnlyDictionary<string, TreeSpeciesContentDefinition> treeSpecies)
    {
        foreach (var plant in plants.Values)
        {
            foreach (var speciesId in plant.SupportedTreeSpeciesIds)
            {
                if (treeSpecies.ContainsKey(speciesId))
                    continue;

                throw new InvalidOperationException($"Plant '{plant.Id}' references unknown tree species '{speciesId}'.");
            }
        }
    }

    private static void ValidateTreeSpeciesMaterials(
        IReadOnlyDictionary<string, MaterialContentDefinition> materials,
        IReadOnlyDictionary<string, TreeSpeciesContentDefinition> treeSpecies)
    {
        foreach (var species in treeSpecies.Values)
        {
            if (string.IsNullOrWhiteSpace(species.WoodMaterialId))
                continue;

            if (materials.ContainsKey(species.WoodMaterialId))
                continue;

            throw new InvalidOperationException($"Tree species '{species.Id}' references unknown wood material '{species.WoodMaterialId}'.");
        }
    }

    private static void ValidateCreatures(
        IReadOnlyDictionary<string, CreatureContentDefinition> creatures,
        IReadOnlyDictionary<string, ContentItemDefinition> items)
    {
        foreach (var creature in creatures.Values)
        {
            if (creature.BodyParts is not null)
            {
                var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var bodyPart in creature.BodyParts)
                {
                    if (!seenIds.Add(bodyPart.Id))
                        throw new InvalidOperationException($"Creature '{creature.Id}' defines duplicate body part '{bodyPart.Id}'.");

                    if (bodyPart.HitWeight <= 0f)
                        throw new InvalidOperationException($"Creature '{creature.Id}' has non-positive hit weight for body part '{bodyPart.Id}'.");
                }
            }

            if (!string.IsNullOrWhiteSpace(creature.DietId) && !IsKnownCreatureDiet(creature.DietId))
                throw new InvalidOperationException($"Creature '{creature.Id}' has unknown diet '{creature.DietId}'.");
            if (!string.IsNullOrWhiteSpace(creature.MovementModeId) && !IsKnownCreatureMovementMode(creature.MovementModeId))
                throw new InvalidOperationException($"Creature '{creature.Id}' has unknown movement mode '{creature.MovementModeId}'.");
            if (!string.IsNullOrWhiteSpace(creature.Visuals?.ProceduralProfileId) && !IsKnownCreatureVisualProfile(creature.Visuals.ProceduralProfileId))
                throw new InvalidOperationException($"Creature '{creature.Id}' has unknown visual profile '{creature.Visuals.ProceduralProfileId}'.");
            if (!string.IsNullOrWhiteSpace(creature.Visuals?.WaterEffectStyleId) && !IsKnownCreatureWaterEffectStyle(creature.Visuals.WaterEffectStyleId))
                throw new InvalidOperationException($"Creature '{creature.Id}' has unknown water effect style '{creature.Visuals.WaterEffectStyleId}'.");
            if (!string.IsNullOrWhiteSpace(creature.Visuals?.SpriteSheet) &&
                (creature.Visuals.SpriteColumn is null || creature.Visuals.SpriteRow is null))
            {
                throw new InvalidOperationException($"Creature '{creature.Id}' visual sprite mapping requires spriteColumn and spriteRow.");
            }

            if (creature.Ecology is not null)
            {
                foreach (var surfaceRule in creature.Ecology.SurfaceWildlife ?? Array.Empty<CreatureSurfaceEcologyContentDefinition>())
                {
                    if (surfaceRule.BiomeIds.Count == 0)
                        throw new InvalidOperationException($"Creature '{creature.Id}' has a surface ecology rule without any biome ids.");
                    if (surfaceRule.Weight <= 0f)
                        throw new InvalidOperationException($"Creature '{creature.Id}' has a non-positive surface ecology weight.");
                    if (surfaceRule.MinGroup <= 0 || surfaceRule.MaxGroup < surfaceRule.MinGroup)
                        throw new InvalidOperationException($"Creature '{creature.Id}' has an invalid surface ecology group range.");
                }

                foreach (var caveRule in creature.Ecology.CaveWildlife ?? Array.Empty<CreatureCaveEcologyContentDefinition>())
                {
                    if (caveRule.Layers.Count == 0 || caveRule.Layers.Any(layer => layer <= 0))
                        throw new InvalidOperationException($"Creature '{creature.Id}' has a cave ecology rule without valid positive layers.");
                    if (caveRule.Weight <= 0f)
                        throw new InvalidOperationException($"Creature '{creature.Id}' has a non-positive cave ecology weight.");
                    if (caveRule.MinGroup <= 0 || caveRule.MaxGroup < caveRule.MinGroup)
                        throw new InvalidOperationException($"Creature '{creature.Id}' has an invalid cave ecology group range.");
                }
            }

            if (creature.History is not null &&
                creature.History.FigureNamePool is { Count: > 0 } &&
                creature.History.FigureNamePool.Any(name => string.IsNullOrWhiteSpace(name)))
            {
                throw new InvalidOperationException($"Creature '{creature.Id}' has blank entries in its history figure name pool.");
            }

            foreach (var rule in creature.History?.ProfessionRules ?? Array.Empty<CreatureHistoryProfessionRuleContentDefinition>())
            {
                if (rule.ProfessionIds is not { Count: > 0 })
                    throw new InvalidOperationException($"Creature '{creature.Id}' has a history profession rule without profession ids.");
            }

            foreach (var drop in creature.DeathDrops ?? Array.Empty<CreatureDeathDropContentDefinition>())
            {
                if (string.IsNullOrWhiteSpace(drop.ItemDefId))
                    throw new InvalidOperationException($"Creature '{creature.Id}' has a death drop with an empty item id.");
                if (drop.Quantity <= 0)
                    throw new InvalidOperationException($"Creature '{creature.Id}' has a death drop with non-positive quantity for '{drop.ItemDefId}'.");
                if (!items.ContainsKey(drop.ItemDefId))
                    throw new InvalidOperationException($"Creature '{creature.Id}' references unknown death drop item '{drop.ItemDefId}'.");
            }

            foreach (var role in creature.Society?.FactionRoles ?? Array.Empty<CreatureFactionRoleContentDefinition>())
            {
                if (string.IsNullOrWhiteSpace(role.Id))
                    throw new InvalidOperationException($"Creature '{creature.Id}' has a society faction role with an empty id.");
                if (role.Weight <= 0f)
                    throw new InvalidOperationException($"Creature '{creature.Id}' has a non-positive society faction role weight for '{role.Id}'.");
            }
        }
    }

    private static void LoadLegacyMaterials(IContentFileSource source, IDictionary<string, MaterialContentDefinition> materials)
    {
        const string path = "data/ConfigBundle/materials.json";
        if (!source.Exists(path))
            return;

        var root = JsonNode.Parse(source.ReadText(path), null, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
        });
        if (root is not JsonArray array)
            return;

        foreach (var node in array.OfType<JsonObject>())
        {
            var id = node["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id))
                continue;

            materials[id] = new MaterialContentDefinition(
                Id: id,
                DisplayName: node["displayName"]?.GetValue<string>() ?? id,
                Tags: ParseStringList(node["tags"]),
                Hardness: node["hardness"]?.GetValue<float>() ?? 1.0f,
                MeltingPoint: node["meltingPoint"]?.GetValue<float>() ?? float.MaxValue,
                Density: node["density"]?.GetValue<float>() ?? 1.0f,
                Value: node["value"]?.GetValue<int>() ?? 1,
                Color: node["color"]?.GetValue<string>(),
                Forms: BuildLegacyMaterialForms(id, node["displayName"]?.GetValue<string>() ?? id, ParseStringList(node["tags"])),
                SourceRoot: ContentRoots.Legacy,
                SourcePath: path);
        }
    }

    private static void LoadLegacyItems(IContentFileSource source, IDictionary<string, ContentItemDefinition> items)
    {
        const string path = "data/ConfigBundle/items.json";
        if (!source.Exists(path))
            return;

        var root = JsonNode.Parse(source.ReadText(path), null, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
        });
        if (root is not JsonArray array)
            return;

        foreach (var node in array.OfType<JsonObject>())
        {
            var item = ParseItem(node, ContentFamilies.Materials, ContentRoots.Legacy, path);
            items[item.Id] = item;
        }
    }

    private static void LoadLegacyPlants(IContentFileSource source, IDictionary<string, PlantContentDefinition> plants)
    {
        const string path = "data/ConfigBundle/plants.json";
        if (!source.Exists(path))
            return;

        var root = JsonNode.Parse(source.ReadText(path), null, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
        });
        if (root is not JsonArray array)
            return;

        foreach (var node in array.OfType<JsonObject>())
        {
            var id = node["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id))
                continue;

            plants[id] = new PlantContentDefinition(
                Id: id,
                DisplayName: node["displayName"]?.GetValue<string>() ?? id,
                HostKind: ParsePlantHostKind(node["hostKind"]?.GetValue<string>()),
                AllowedBiomeIds: ParseStringList(node["allowedBiomes"]),
                AllowedGroundTileDefIds: ParseStringList(node["allowedGroundTiles"]),
                SupportedTreeSpeciesIds: ParseStringList(node["supportedTreeSpecies"]),
                MinMoisture: node["minMoisture"]?.GetValue<float>() ?? 0f,
                MaxMoisture: node["maxMoisture"]?.GetValue<float>() ?? 1f,
                MinTerrain: node["minTerrain"]?.GetValue<float>() ?? 0f,
                MaxTerrain: node["maxTerrain"]?.GetValue<float>() ?? 1f,
                PrefersNearWater: node["prefersNearWater"]?.GetValue<bool>() ?? false,
                PrefersFarFromWater: node["prefersFarFromWater"]?.GetValue<bool>() ?? false,
                MaxGrowthStage: node["maxGrowthStage"]?.GetValue<byte>() ?? 3,
                SecondsPerStage: node["secondsPerStage"]?.GetValue<float>() ?? 240f,
                FruitingCycleSeconds: node["fruitingCycleSeconds"]?.GetValue<float>() ?? 420f,
                SeedSpreadChance: node["seedSpreadChance"]?.GetValue<float>() ?? 0.08f,
                SeedSpreadRadiusMin: node["seedSpreadRadiusMin"]?.GetValue<int>() ?? 2,
                SeedSpreadRadiusMax: node["seedSpreadRadiusMax"]?.GetValue<int>() ?? 5,
                Energy: node["nutrition"]?["energy"]?.GetValue<float>() ?? 0f,
                Protein: node["nutrition"]?["protein"]?.GetValue<float>() ?? 0f,
                Vitamins: node["nutrition"]?["vitamins"]?.GetValue<float>() ?? 0f,
                Minerals: node["nutrition"]?["minerals"]?.GetValue<float>() ?? 0f,
                HarvestItemDefId: node["harvestItemDefId"]?.GetValue<string>(),
                SeedItemDefId: node["seedItemDefId"]?.GetValue<string>(),
                FruitItemDefId: node["fruitItemDefId"]?.GetValue<string>(),
                SourceRoot: ContentRoots.Legacy,
                SourcePath: path);
        }
    }

    private static MaterialContentDefinition ParseMaterial(DiscoveredContentEntry record)
    {
        var parsed = JsonSerializer.Deserialize<MaterialFileModel>(record.Json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse material file '{record.Path}'.");

        if (string.IsNullOrWhiteSpace(parsed.Id))
            throw new InvalidOperationException($"Material file '{record.Path}' is missing required 'id'.");

        var forms = parsed.Forms?
            .Where(form => !string.IsNullOrWhiteSpace(form.Role) && form.Item is not null && !string.IsNullOrWhiteSpace(form.Item.Id))
            .Select(form => new MaterialFormDefinition(
                form.Role!,
                ToItemDefinition(form.Item!, record.Family, record.RootName, record.Path)))
            .ToDictionary(form => form.Role, StringComparer.OrdinalIgnoreCase);

        return new MaterialContentDefinition(
            Id: parsed.Id,
            DisplayName: parsed.DisplayName ?? parsed.Id,
            Tags: parsed.Tags?.ToArray() ?? Array.Empty<string>(),
            Hardness: parsed.Hardness ?? 1.0f,
            MeltingPoint: parsed.MeltingPoint ?? float.MaxValue,
            Density: parsed.Density ?? 1.0f,
            Value: parsed.Value ?? 1,
            Color: parsed.Color,
            Forms: forms,
            SourceRoot: record.RootName,
            SourcePath: record.Path);
    }

    private static TreeSpeciesContentDefinition ParseTreeSpecies(DiscoveredContentEntry record)
    {
        var parsed = JsonSerializer.Deserialize<TreeSpeciesFileModel>(record.Json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse tree species file '{record.Path}'.");

        if (string.IsNullOrWhiteSpace(parsed.Id))
            throw new InvalidOperationException($"Tree species file '{record.Path}' is missing required 'id'.");

        return new TreeSpeciesContentDefinition(
            Id: parsed.Id,
            DisplayName: parsed.DisplayName ?? parsed.Id,
            WoodMaterialId: parsed.WoodMaterialId,
            Tags: parsed.Tags?.ToArray() ?? Array.Empty<string>(),
            SourceRoot: record.RootName,
            SourcePath: record.Path);
    }

    private static PlantContentDefinition ParsePlant(DiscoveredContentEntry record)
    {
        var parsed = JsonSerializer.Deserialize<PlantFileModel>(record.Json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse plant file '{record.Path}'.");

        if (string.IsNullOrWhiteSpace(parsed.Id))
            throw new InvalidOperationException($"Plant file '{record.Path}' is missing required 'id'.");

        var harvestItem = ResolvePlantItem(parsed.HarvestItem, parsed.HarvestItemDefId, record, "harvestItem");
        var seedItem = ResolvePlantItem(parsed.SeedItem, parsed.SeedItemDefId, record, "seedItem");
        var fruitItem = ResolvePlantItem(parsed.FruitItem, parsed.FruitItemDefId, record, "fruitItem");

        return new PlantContentDefinition(
            Id: parsed.Id,
            DisplayName: parsed.DisplayName ?? parsed.Id,
            HostKind: ParsePlantHostKind(parsed.HostKind),
            AllowedBiomeIds: parsed.AllowedBiomes?.ToArray() ?? Array.Empty<string>(),
            AllowedGroundTileDefIds: parsed.AllowedGroundTiles?.ToArray() ?? Array.Empty<string>(),
            SupportedTreeSpeciesIds: parsed.SupportedTreeSpecies?.ToArray() ?? Array.Empty<string>(),
            MinMoisture: parsed.MinMoisture ?? 0f,
            MaxMoisture: parsed.MaxMoisture ?? 1f,
            MinTerrain: parsed.MinTerrain ?? 0f,
            MaxTerrain: parsed.MaxTerrain ?? 1f,
            PrefersNearWater: parsed.PrefersNearWater ?? false,
            PrefersFarFromWater: parsed.PrefersFarFromWater ?? false,
            MaxGrowthStage: parsed.MaxGrowthStage ?? 3,
            SecondsPerStage: parsed.SecondsPerStage ?? 240f,
            FruitingCycleSeconds: parsed.FruitingCycleSeconds ?? 420f,
            SeedSpreadChance: parsed.SeedSpreadChance ?? 0.08f,
            SeedSpreadRadiusMin: parsed.SeedSpreadRadiusMin ?? 2,
            SeedSpreadRadiusMax: parsed.SeedSpreadRadiusMax ?? 5,
            Energy: parsed.Nutrition?.Energy ?? 0f,
            Protein: parsed.Nutrition?.Protein ?? 0f,
            Vitamins: parsed.Nutrition?.Vitamins ?? 0f,
            Minerals: parsed.Nutrition?.Minerals ?? 0f,
            HarvestItemDefId: ResolveItemId(parsed.HarvestItemDefId, harvestItem),
            SeedItemDefId: ResolveItemId(parsed.SeedItemDefId, seedItem),
            FruitItemDefId: ResolveItemId(parsed.FruitItemDefId, fruitItem),
            HarvestItem: harvestItem,
            SeedItem: seedItem,
            FruitItem: fruitItem,
            SourceRoot: record.RootName,
            SourcePath: record.Path);
    }

    private static CreatureContentDefinition ParseCreature(DiscoveredContentEntry record)
    {
        var parsed = JsonSerializer.Deserialize<CreatureFileModel>(record.Json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse creature file '{record.Path}'.");

        if (string.IsNullOrWhiteSpace(parsed.Id))
            throw new InvalidOperationException($"Creature file '{record.Path}' is missing required 'id'.");

        return new CreatureContentDefinition(
            Id: parsed.Id,
            DisplayName: parsed.DisplayName ?? parsed.Id,
            Tags: parsed.Tags?.ToArray() ?? Array.Empty<string>(),
            BaseSpeed: parsed.Speed ?? 1.0f,
            BaseStrength: parsed.Strength ?? 10.0f,
            BaseToughness: parsed.Toughness ?? 10.0f,
            MaxHealth: parsed.MaxHealth ?? 100f,
            IsPlayable: parsed.IsPlayable ?? false,
            IsSapient: parsed.IsSapient ?? false,
            IsHostile: parsed.IsHostile ?? false,
            CanGroom: parsed.CanGroom,
            DietId: parsed.Diet,
            MovementModeId: parsed.MovementMode,
            BodyParts: parsed.BodyParts?
                .Where(part => !string.IsNullOrWhiteSpace(part.Id))
                .Select(part => new BodyPartContentDefinition(
                    part.Id!,
                    part.DisplayName ?? part.Id!,
                    part.HitWeight ?? 1.0f,
                    part.IsVital ?? false))
                .ToArray(),
            NaturalLabors: parsed.NaturalLabors?
                .Where(labor => !string.IsNullOrWhiteSpace(labor))
                .Cast<string>()
                .ToArray(),
            Ecology: ParseCreatureEcology(parsed.Ecology),
            History: ParseCreatureHistory(parsed.History),
            DeathDrops: ParseCreatureDeathDrops(parsed.DeathDrops),
            Society: ParseCreatureSociety(parsed.Society),
            Visuals: ParseCreatureVisuals(parsed.Visuals),
            SourceRoot: record.RootName,
            SourcePath: record.Path);
    }

    private static ContentItemDefinition? ResolvePlantItem(
        ItemFileModel? item,
        string? explicitId,
        DiscoveredContentEntry record,
        string itemName)
    {
        if (item is null)
            return null;

        if (!string.IsNullOrWhiteSpace(explicitId) && !string.Equals(explicitId, item.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Plant file '{record.Path}' has mismatched '{itemName}.id' and explicit item id.");

        return ToItemDefinition(item, record.Family, record.RootName, record.Path);
    }

    private static string? ResolveItemId(string? explicitId, ContentItemDefinition? item)
        => !string.IsNullOrWhiteSpace(explicitId) ? explicitId : item?.Id;

    private static ContentItemDefinition ParseItem(JsonObject node, string family, string root, string path)
        => new(
            Id: node["id"]?.GetValue<string>() ?? throw new InvalidOperationException($"Item entry in '{path}' is missing required 'id'."),
            DisplayName: node["displayName"]?.GetValue<string>() ?? node["id"]!.GetValue<string>(),
            Tags: ParseStringList(node["tags"]),
            Stackable: node["stackable"]?.GetValue<bool>() ?? false,
            MaxStack: node["maxStack"]?.GetValue<int>() ?? 1,
            Weight: node["weight"]?.GetValue<float>() ?? 1.0f,
            BaseValue: node["baseValue"]?.GetValue<int>() ?? 1,
            Nutrition: ParseItemNutrition(node["nutrition"]),
            SourceFamily: family,
            SourceRoot: root,
            SourcePath: path);

    private static ContentItemDefinition ToItemDefinition(ItemFileModel item, string family, string root, string path)
        => new(
            Id: item.Id ?? throw new InvalidOperationException($"Embedded item in '{path}' is missing required 'id'."),
            DisplayName: item.DisplayName ?? item.Id!,
            Tags: item.Tags?.ToArray() ?? Array.Empty<string>(),
            Stackable: item.Stackable ?? false,
            MaxStack: item.MaxStack ?? 1,
            Weight: item.Weight ?? 1.0f,
            BaseValue: item.BaseValue ?? 1,
            Nutrition: item.Nutrition is null ? null : new NutritionContent(
                item.Nutrition.Carbs ?? 0.4f,
                item.Nutrition.Protein ?? 0.4f,
                item.Nutrition.Fat ?? 0.3f,
                item.Nutrition.Vitamins ?? 0.4f),
            SourceFamily: family,
            SourceRoot: root,
            SourcePath: path);

    private static NutritionContent? ParseItemNutrition(JsonNode? node)
    {
        if (node is not JsonObject nutrition)
            return null;

        return new NutritionContent(
            nutrition["carbs"]?.GetValue<float>() ?? 0.4f,
            nutrition["protein"]?.GetValue<float>() ?? 0.4f,
            nutrition["fat"]?.GetValue<float>() ?? 0.3f,
            nutrition["vitamins"]?.GetValue<float>() ?? 0.4f);
    }

    private static IReadOnlyDictionary<string, MaterialFormDefinition>? BuildLegacyMaterialForms(
        string materialId,
        string displayName,
        IReadOnlyList<string> tags)
    {
        var forms = new Dictionary<string, MaterialFormDefinition>(StringComparer.OrdinalIgnoreCase);
        var boulderId = $"{materialId}_boulder";
        var oreId = $"{materialId}_ore";
        var barId = $"{materialId}_bar";
        var isWoodMaterial =
            string.Equals(materialId, "wood", StringComparison.OrdinalIgnoreCase) ||
            materialId.EndsWith("_wood", StringComparison.OrdinalIgnoreCase) ||
            tags.Any(tag => string.Equals(tag, "wood", StringComparison.OrdinalIgnoreCase));

        if (materialId is "granite" or "limestone" or "sandstone" or "basalt" or "shale" or "slate" or "marble")
        {
            forms[ContentFormRoles.Boulder] = new MaterialFormDefinition(
                ContentFormRoles.Boulder,
                new ContentItemDefinition(
                    boulderId,
                    $"{displayName} Boulder",
                    new[] { "stone", "boulder" },
                    Weight: 20.0f,
                    SourceFamily: ContentFamilies.Materials,
                    SourceRoot: ContentRoots.Legacy,
                    SourcePath: "data/ConfigBundle/materials.json"));
        }

        if (materialId is "iron" or "copper" or "tin" or "silver" or "gold" or "coal")
        {
            forms[ContentFormRoles.Ore] = new MaterialFormDefinition(
                ContentFormRoles.Ore,
                new ContentItemDefinition(
                    oreId,
                    $"{displayName} Ore",
                    new[] { "ore", "stone" },
                    Weight: 15.0f,
                    SourceFamily: ContentFamilies.Materials,
                    SourceRoot: ContentRoots.Legacy,
                    SourcePath: "data/ConfigBundle/materials.json"));
        }

        if (materialId is "iron" or "copper" or "tin" or "silver" or "gold")
        {
            forms[ContentFormRoles.Bar] = new MaterialFormDefinition(
                ContentFormRoles.Bar,
                new ContentItemDefinition(
                    barId,
                    $"{displayName} Bar",
                    new[] { "metal", "bar" },
                    Weight: 8.0f,
                    SourceFamily: ContentFamilies.Materials,
                    SourceRoot: ContentRoots.Legacy,
                    SourcePath: "data/ConfigBundle/materials.json"));
        }

        if (isWoodMaterial)
        {
            forms[ContentFormRoles.Log] = new MaterialFormDefinition(
                ContentFormRoles.Log,
                new ContentItemDefinition(
                    "log",
                    "Log",
                    new[] { "wood", "log" },
                    Weight: 10.0f,
                    SourceFamily: ContentFamilies.Materials,
                    SourceRoot: ContentRoots.Legacy,
                    SourcePath: "data/ConfigBundle/materials.json"));

            forms[ContentFormRoles.Plank] = new MaterialFormDefinition(
                ContentFormRoles.Plank,
                new ContentItemDefinition(
                    "plank",
                    "Plank",
                    new[] { "wood", "plank" },
                    Weight: 5.0f,
                    SourceFamily: ContentFamilies.Materials,
                    SourceRoot: ContentRoots.Legacy,
                    SourcePath: "data/ConfigBundle/materials.json"));
        }

        return forms.Count == 0 ? null : forms;
    }

    private static IReadOnlyList<string> ParseStringList(JsonNode? node)
    {
        if (node is not JsonArray array)
            return Array.Empty<string>();

        return array
            .Select(entry => entry?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
    }

    private static IReadOnlyList<string>? ParseStringListOrNull(JsonNode? node)
    {
        var values = ParseStringList(node);
        return values.Count == 0 ? null : values;
    }

    private static IReadOnlyList<BodyPartContentDefinition>? ParseBodyParts(JsonNode? node)
    {
        if (node is not JsonArray array)
            return null;

        var parts = new List<BodyPartContentDefinition>();
        foreach (var partNode in array.OfType<JsonObject>())
        {
            var id = partNode["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id))
                continue;

            parts.Add(new BodyPartContentDefinition(
                Id: id,
                DisplayName: partNode["displayName"]?.GetValue<string>() ?? id,
                HitWeight: partNode["hitWeight"]?.GetValue<float>() ?? 1.0f,
                IsVital: partNode["isVital"]?.GetValue<bool>() ?? false));
        }

        return parts.Count == 0 ? null : parts;
    }

    private static CreatureEcologyContentDefinition? ParseCreatureEcology(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return null;

        var surfaceWildlife = ParseSurfaceEcology(obj["surfaceWildlife"]);
        var caveWildlife = ParseCaveEcology(obj["caveWildlife"]);
        return surfaceWildlife is null && caveWildlife is null
            ? null
            : new CreatureEcologyContentDefinition(surfaceWildlife, caveWildlife);
    }

    private static CreatureEcologyContentDefinition? ParseCreatureEcology(CreatureEcologyFileModel? ecology)
    {
        if (ecology is null)
            return null;

        var surfaceWildlife = ecology.SurfaceWildlife?
            .Where(rule => rule.Biomes is not null)
            .Select(rule => new CreatureSurfaceEcologyContentDefinition(
                BiomeIds: rule.Biomes?
                    .Where(biomeId => !string.IsNullOrWhiteSpace(biomeId))
                    .Cast<string>()
                    .ToArray() ?? Array.Empty<string>(),
                Weight: rule.Weight ?? 1.0f,
                MinGroup: rule.MinGroup ?? 1,
                MaxGroup: rule.MaxGroup ?? Math.Max(1, rule.MinGroup ?? 1),
                RequiresWater: rule.RequiresWater ?? false,
                AvoidEmbarkCenter: rule.AvoidEmbarkCenter ?? true))
            .ToArray();

        var caveWildlife = ecology.CaveWildlife?
            .Where(rule => rule.Layers is not null)
            .Select(rule => new CreatureCaveEcologyContentDefinition(
                Layers: rule.Layers?
                    .Where(layer => layer > 0)
                    .ToArray() ?? Array.Empty<int>(),
                Weight: rule.Weight ?? 1.0f,
                MinGroup: rule.MinGroup ?? 1,
                MaxGroup: rule.MaxGroup ?? Math.Max(1, rule.MinGroup ?? 1),
                RequiresWater: rule.RequiresWater ?? false,
                AvoidEmbarkCenter: rule.AvoidEmbarkCenter ?? true))
            .ToArray();

        return (surfaceWildlife is null || surfaceWildlife.Length == 0) &&
               (caveWildlife is null || caveWildlife.Length == 0)
            ? null
            : new CreatureEcologyContentDefinition(surfaceWildlife, caveWildlife);
    }

    private static CreatureHistoryContentDefinition? ParseCreatureHistory(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return null;

        var figureNamePool = ParseRawStringListOrNull(obj["figureNamePool"]);
        var defaultProfessionIds = ParseStringListOrNull(obj["defaultProfessionIds"]);
        var professionRules = ParseCreatureHistoryRules(obj["professionRules"]);

        return figureNamePool is null && defaultProfessionIds is null && professionRules is null
            ? null
            : new CreatureHistoryContentDefinition(figureNamePool, defaultProfessionIds, professionRules);
    }

    private static CreatureHistoryContentDefinition? ParseCreatureHistory(CreatureHistoryFileModel? history)
    {
        if (history is null)
            return null;

        var figureNamePool = history.FigureNamePool?.ToArray();
        var defaultProfessionIds = history.DefaultProfessionIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToArray();
        var professionRules = history.ProfessionRules?
            .Select(rule => new CreatureHistoryProfessionRuleContentDefinition(
                SiteKindContains: string.IsNullOrWhiteSpace(rule.SiteKindContains) ? null : rule.SiteKindContains,
                MemberIndex: rule.MemberIndex,
                FounderBias: rule.FounderBias,
                ProfessionIds: rule.ProfessionIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Cast<string>()
                    .ToArray()))
            .ToArray();

        return (figureNamePool is null || figureNamePool.Length == 0) &&
               (defaultProfessionIds is null || defaultProfessionIds.Length == 0) &&
               (professionRules is null || professionRules.Length == 0)
            ? null
            : new CreatureHistoryContentDefinition(figureNamePool, defaultProfessionIds, professionRules);
    }

    private static IReadOnlyList<CreatureDeathDropContentDefinition>? ParseCreatureDeathDrops(JsonNode? node)
    {
        if (node is not JsonArray array)
            return null;

        var drops = new List<CreatureDeathDropContentDefinition>();
        foreach (var dropNode in array.OfType<JsonObject>())
        {
            drops.Add(new CreatureDeathDropContentDefinition(
                ItemDefId: dropNode["itemDefId"]?.GetValue<string>() ?? string.Empty,
                Quantity: dropNode["quantity"]?.GetValue<int>() ?? 1,
                MaterialId: dropNode["materialId"]?.GetValue<string>()));
        }

        return drops.Count == 0 ? null : drops;
    }

    private static IReadOnlyList<CreatureDeathDropContentDefinition>? ParseCreatureDeathDrops(List<CreatureDeathDropFileModel>? drops)
    {
        if (drops is not { Count: > 0 })
            return null;

        var parsed = drops
            .Select(drop => new CreatureDeathDropContentDefinition(
                ItemDefId: drop.ItemDefId ?? string.Empty,
                Quantity: drop.Quantity ?? 1,
                MaterialId: drop.MaterialId))
            .ToArray();

        return parsed.Length == 0 ? null : parsed;
    }

    private static CreatureSocietyContentDefinition? ParseCreatureSociety(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return null;

        var factionRoles = ParseFactionRoles(obj["factionRoles"]);
        return factionRoles is null
            ? null
            : new CreatureSocietyContentDefinition(factionRoles);
    }

    private static CreatureSocietyContentDefinition? ParseCreatureSociety(CreatureSocietyFileModel? society)
    {
        if (society?.FactionRoles is not { Count: > 0 })
            return null;

        var factionRoles = society.FactionRoles
            .Select(role => new CreatureFactionRoleContentDefinition(
                role.Id ?? string.Empty,
                role.Weight ?? 1.0f))
            .ToArray();

        return factionRoles.Length == 0 ? null : new CreatureSocietyContentDefinition(factionRoles);
    }

    private static CreatureVisualContentDefinition? ParseCreatureVisuals(CreatureVisualsFileModel? visuals)
    {
        if (visuals is null)
            return null;

        if (string.IsNullOrWhiteSpace(visuals.ProceduralProfile) &&
            string.IsNullOrWhiteSpace(visuals.WaterEffectStyle) &&
            string.IsNullOrWhiteSpace(visuals.ViewerColor) &&
            string.IsNullOrWhiteSpace(visuals.SpriteSheet) &&
            visuals.SpriteColumn is null &&
            visuals.SpriteRow is null)
        {
            return null;
        }

        return new CreatureVisualContentDefinition(
            ProceduralProfileId: string.IsNullOrWhiteSpace(visuals.ProceduralProfile) ? null : visuals.ProceduralProfile,
            WaterEffectStyleId: string.IsNullOrWhiteSpace(visuals.WaterEffectStyle) ? null : visuals.WaterEffectStyle,
            ViewerColor: string.IsNullOrWhiteSpace(visuals.ViewerColor) ? null : visuals.ViewerColor,
            SpriteSheet: string.IsNullOrWhiteSpace(visuals.SpriteSheet) ? null : visuals.SpriteSheet,
            SpriteColumn: visuals.SpriteColumn,
            SpriteRow: visuals.SpriteRow);
    }

    private static IReadOnlyList<CreatureFactionRoleContentDefinition>? ParseFactionRoles(JsonNode? node)
    {
        if (node is not JsonArray array)
            return null;

        var roles = new List<CreatureFactionRoleContentDefinition>();
        foreach (var roleNode in array.OfType<JsonObject>())
        {
            roles.Add(new CreatureFactionRoleContentDefinition(
                Id: roleNode["id"]?.GetValue<string>() ?? string.Empty,
                Weight: roleNode["weight"]?.GetValue<float>() ?? 1.0f));
        }

        return roles.Count == 0 ? null : roles;
    }

    private static IReadOnlyList<CreatureHistoryProfessionRuleContentDefinition>? ParseCreatureHistoryRules(JsonNode? node)
    {
        if (node is not JsonArray array)
            return null;

        var rules = new List<CreatureHistoryProfessionRuleContentDefinition>();
        foreach (var ruleNode in array.OfType<JsonObject>())
        {
            rules.Add(new CreatureHistoryProfessionRuleContentDefinition(
                SiteKindContains: ruleNode["siteKindContains"]?.GetValue<string>(),
                MemberIndex: ruleNode["memberIndex"]?.GetValue<int>(),
                FounderBias: ruleNode["founderBias"]?.GetValue<bool>(),
                ProfessionIds: ParseStringListOrNull(ruleNode["professionIds"])));
        }

        return rules.Count == 0 ? null : rules;
    }

    private static IReadOnlyList<CreatureSurfaceEcologyContentDefinition>? ParseSurfaceEcology(JsonNode? node)
    {
        if (node is not JsonArray array)
            return null;

        var rules = new List<CreatureSurfaceEcologyContentDefinition>();
        foreach (var ruleNode in array.OfType<JsonObject>())
        {
            var biomeIds = ParseStringList(ruleNode["biomes"]);
            rules.Add(new CreatureSurfaceEcologyContentDefinition(
                BiomeIds: biomeIds,
                Weight: ruleNode["weight"]?.GetValue<float>() ?? 1.0f,
                MinGroup: ruleNode["minGroup"]?.GetValue<int>() ?? 1,
                MaxGroup: ruleNode["maxGroup"]?.GetValue<int>() ?? Math.Max(1, ruleNode["minGroup"]?.GetValue<int>() ?? 1),
                RequiresWater: ruleNode["requiresWater"]?.GetValue<bool>() ?? false,
                AvoidEmbarkCenter: ruleNode["avoidEmbarkCenter"]?.GetValue<bool>() ?? true));
        }

        return rules.Count == 0 ? null : rules;
    }

    private static IReadOnlyList<CreatureCaveEcologyContentDefinition>? ParseCaveEcology(JsonNode? node)
    {
        if (node is not JsonArray array)
            return null;

        var rules = new List<CreatureCaveEcologyContentDefinition>();
        foreach (var ruleNode in array.OfType<JsonObject>())
        {
            var layers = ParseIntList(ruleNode["layers"]);
            rules.Add(new CreatureCaveEcologyContentDefinition(
                Layers: layers,
                Weight: ruleNode["weight"]?.GetValue<float>() ?? 1.0f,
                MinGroup: ruleNode["minGroup"]?.GetValue<int>() ?? 1,
                MaxGroup: ruleNode["maxGroup"]?.GetValue<int>() ?? Math.Max(1, ruleNode["minGroup"]?.GetValue<int>() ?? 1),
                RequiresWater: ruleNode["requiresWater"]?.GetValue<bool>() ?? false,
                AvoidEmbarkCenter: ruleNode["avoidEmbarkCenter"]?.GetValue<bool>() ?? true));
        }

        return rules.Count == 0 ? null : rules;
    }

    private static IReadOnlyList<int> ParseIntList(JsonNode? node)
    {
        if (node is not JsonArray array)
            return Array.Empty<int>();

        return array
            .Select(entry => entry is JsonValue value && value.TryGetValue<int>(out var parsed) ? parsed : 0)
            .Where(value => value != 0)
            .ToArray();
    }

    private static IReadOnlyList<string>? ParseRawStringListOrNull(JsonNode? node)
    {
        if (node is not JsonArray array)
            return null;

        var values = array
            .Select(entry => entry?.GetValue<string>() ?? string.Empty)
            .ToArray();

        return values.Length == 0 ? null : values;
    }

    private static PlantContentHostKind ParsePlantHostKind(string? value)
        => string.Equals(value, "tree", StringComparison.OrdinalIgnoreCase)
            ? PlantContentHostKind.Tree
            : PlantContentHostKind.Ground;

    private static bool IsKnownCreatureDiet(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            ContentCreatureDietIds.Herbivore => true,
            ContentCreatureDietIds.Carnivore => true,
            ContentCreatureDietIds.Omnivore => true,
            ContentCreatureDietIds.AquaticGrazer => true,
            _ => false,
        };
    }

    private static bool IsKnownCreatureMovementMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            ContentCreatureMovementModeIds.Land => true,
            ContentCreatureMovementModeIds.Swimmer => true,
            ContentCreatureMovementModeIds.Aquatic => true,
            _ => false,
        };
    }

    private static bool IsKnownCreatureVisualProfile(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            ContentCreatureVisualProfileIds.Dwarf => true,
            ContentCreatureVisualProfileIds.Goblin => true,
            ContentCreatureVisualProfileIds.Troll => true,
            ContentCreatureVisualProfileIds.Elk => true,
            ContentCreatureVisualProfileIds.GiantCarp => true,
            ContentCreatureVisualProfileIds.Cat => true,
            ContentCreatureVisualProfileIds.Dog => true,
            _ => false,
        };
    }

    private static bool IsKnownCreatureWaterEffectStyle(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            ContentCreatureWaterEffectStyleIds.Default => true,
            ContentCreatureWaterEffectStyleIds.Large => true,
            ContentCreatureWaterEffectStyleIds.Pet => true,
            ContentCreatureWaterEffectStyleIds.Aquatic => true,
            _ => false,
        };
    }

    private readonly record struct DiscoveredContentEntry(string RootName, string Family, string Path, string Json);

    private sealed class MaterialFileModel
    {
        public string? Id { get; init; }
        public string? DisplayName { get; init; }
        public List<string>? Tags { get; init; }
        public float? Hardness { get; init; }
        public float? MeltingPoint { get; init; }
        public float? Density { get; init; }
        public int? Value { get; init; }
        public string? Color { get; init; }
        public List<MaterialFormFileModel>? Forms { get; init; }
    }

    private sealed class MaterialFormFileModel
    {
        public string? Role { get; init; }
        public ItemFileModel? Item { get; init; }
    }

    private sealed class TreeSpeciesFileModel
    {
        public string? Id { get; init; }
        public string? DisplayName { get; init; }
        public string? WoodMaterialId { get; init; }
        public List<string>? Tags { get; init; }
    }

    private sealed class PlantFileModel
    {
        public string? Id { get; init; }
        public string? DisplayName { get; init; }
        public string? HostKind { get; init; }
        public List<string>? AllowedBiomes { get; init; }
        public List<string>? AllowedGroundTiles { get; init; }
        public List<string>? SupportedTreeSpecies { get; init; }
        public float? MinMoisture { get; init; }
        public float? MaxMoisture { get; init; }
        public float? MinTerrain { get; init; }
        public float? MaxTerrain { get; init; }
        public bool? PrefersNearWater { get; init; }
        public bool? PrefersFarFromWater { get; init; }
        public byte? MaxGrowthStage { get; init; }
        public float? SecondsPerStage { get; init; }
        public float? FruitingCycleSeconds { get; init; }
        public float? SeedSpreadChance { get; init; }
        public int? SeedSpreadRadiusMin { get; init; }
        public int? SeedSpreadRadiusMax { get; init; }
        public PlantNutritionFileModel? Nutrition { get; init; }
        public string? HarvestItemDefId { get; init; }
        public string? SeedItemDefId { get; init; }
        public string? FruitItemDefId { get; init; }
        public ItemFileModel? HarvestItem { get; init; }
        public ItemFileModel? SeedItem { get; init; }
        public ItemFileModel? FruitItem { get; init; }
    }

    private sealed class CreatureFileModel
    {
        public string? Id { get; init; }
        public string? DisplayName { get; init; }
        public List<string>? Tags { get; init; }
        public float? Speed { get; init; }
        public float? Strength { get; init; }
        public float? Toughness { get; init; }
        public float? MaxHealth { get; init; }
        public bool? IsPlayable { get; init; }
        public bool? IsSapient { get; init; }
        public bool? IsHostile { get; init; }
        public bool? CanGroom { get; init; }
        public string? Diet { get; init; }
        public string? MovementMode { get; init; }
        public List<BodyPartFileModel>? BodyParts { get; init; }
        public List<string>? NaturalLabors { get; init; }
        public CreatureEcologyFileModel? Ecology { get; init; }
        public CreatureHistoryFileModel? History { get; init; }
        public List<CreatureDeathDropFileModel>? DeathDrops { get; init; }
        public CreatureSocietyFileModel? Society { get; init; }
        public CreatureVisualsFileModel? Visuals { get; init; }
    }

    private sealed class BodyPartFileModel
    {
        public string? Id { get; init; }
        public string? DisplayName { get; init; }
        public float? HitWeight { get; init; }
        public bool? IsVital { get; init; }
    }

    private sealed class CreatureEcologyFileModel
    {
        public List<CreatureSurfaceEcologyFileModel>? SurfaceWildlife { get; init; }
        public List<CreatureCaveEcologyFileModel>? CaveWildlife { get; init; }
    }

    private sealed class CreatureSurfaceEcologyFileModel
    {
        public List<string>? Biomes { get; init; }
        public float? Weight { get; init; }
        public int? MinGroup { get; init; }
        public int? MaxGroup { get; init; }
        public bool? RequiresWater { get; init; }
        public bool? AvoidEmbarkCenter { get; init; }
    }

    private sealed class CreatureCaveEcologyFileModel
    {
        public List<int>? Layers { get; init; }
        public float? Weight { get; init; }
        public int? MinGroup { get; init; }
        public int? MaxGroup { get; init; }
        public bool? RequiresWater { get; init; }
        public bool? AvoidEmbarkCenter { get; init; }
    }

    private sealed class CreatureHistoryFileModel
    {
        public List<string>? FigureNamePool { get; init; }
        public List<string>? DefaultProfessionIds { get; init; }
        public List<CreatureHistoryProfessionRuleFileModel>? ProfessionRules { get; init; }
    }

    private sealed class CreatureHistoryProfessionRuleFileModel
    {
        public string? SiteKindContains { get; init; }
        public int? MemberIndex { get; init; }
        public bool? FounderBias { get; init; }
        public List<string>? ProfessionIds { get; init; }
    }

    private sealed class CreatureDeathDropFileModel
    {
        public string? ItemDefId { get; init; }
        public int? Quantity { get; init; }
        public string? MaterialId { get; init; }
    }

    private sealed class CreatureSocietyFileModel
    {
        public List<CreatureFactionRoleFileModel>? FactionRoles { get; init; }
    }

    private sealed class CreatureFactionRoleFileModel
    {
        public string? Id { get; init; }
        public float? Weight { get; init; }
    }

    private sealed class CreatureVisualsFileModel
    {
        public string? ProceduralProfile { get; init; }
        public string? WaterEffectStyle { get; init; }
        public string? ViewerColor { get; init; }
        public string? SpriteSheet { get; init; }
        public int? SpriteColumn { get; init; }
        public int? SpriteRow { get; init; }
    }

    private sealed class PlantNutritionFileModel
    {
        public float? Energy { get; init; }
        public float? Protein { get; init; }
        public float? Vitamins { get; init; }
        public float? Minerals { get; init; }
    }

    private sealed class ItemFileModel
    {
        public string? Id { get; init; }
        public string? DisplayName { get; init; }
        public List<string>? Tags { get; init; }
        public bool? Stackable { get; init; }
        public int? MaxStack { get; init; }
        public float? Weight { get; init; }
        public int? BaseValue { get; init; }
        public ItemNutritionFileModel? Nutrition { get; init; }
    }

    private sealed class ItemNutritionFileModel
    {
        public float? Carbs { get; init; }
        public float? Protein { get; init; }
        public float? Fat { get; init; }
        public float? Vitamins { get; init; }
    }
}

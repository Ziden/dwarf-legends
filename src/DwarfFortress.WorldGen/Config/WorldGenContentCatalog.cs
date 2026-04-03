using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.WorldGen.Content;
using DwarfFortress.WorldGen.Creatures;
using DwarfFortress.WorldGen.Geology;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Story;

namespace DwarfFortress.WorldGen.Config;

public sealed class WorldGenContentCatalog
{
    private readonly IReadOnlyDictionary<string, StrataProfile> _strataProfiles;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<MineralVeinDef>> _mineralVeinsByProfile;
    private readonly IReadOnlyDictionary<string, TreeBiomeProfile> _treeProfiles;
    private readonly IReadOnlyList<BiomeGenerationProfile> _biomePresetOrder;
    private readonly IReadOnlyDictionary<string, BiomeGenerationProfile> _biomePresets;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<SpawnEntry>> _surfaceWildlife;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<SpawnEntry>> _caveWildlifeByLayer;
    private readonly HistoryFigureGenerationCatalog _historyFigureGeneration;
    private readonly ContentQueryService _contentQueries;
    private readonly HashSet<string> _oreCompatibilityKeys;
    private readonly StrataProfile _fallbackStrataProfile;
    private readonly BiomeGenerationProfile _fallbackBiomePreset;
    private readonly IReadOnlyList<SpawnEntry> _fallbackSurfaceWildlife;
    private readonly IReadOnlyList<SpawnEntry> _fallbackCaveWildlife;

    private WorldGenContentCatalog(
        IReadOnlyDictionary<string, StrataProfile> strataProfiles,
        IReadOnlyDictionary<string, IReadOnlyList<MineralVeinDef>> mineralVeinsByProfile,
        IReadOnlyDictionary<string, TreeBiomeProfile> treeProfiles,
        IReadOnlyList<BiomeGenerationProfile> biomePresetOrder,
        IReadOnlyDictionary<string, BiomeGenerationProfile> biomePresets,
        IReadOnlyDictionary<string, IReadOnlyList<SpawnEntry>> surfaceWildlife,
        IReadOnlyDictionary<int, IReadOnlyList<SpawnEntry>> caveWildlifeByLayer,
        HistoryFigureGenerationCatalog historyFigureGeneration,
        ContentQueryService contentQueries,
        HashSet<string> oreCompatibilityKeys,
        StrataProfile fallbackStrataProfile,
        BiomeGenerationProfile fallbackBiomePreset,
        IReadOnlyList<SpawnEntry> fallbackSurfaceWildlife,
        IReadOnlyList<SpawnEntry> fallbackCaveWildlife)
    {
        _strataProfiles = strataProfiles;
        _mineralVeinsByProfile = mineralVeinsByProfile;
        _treeProfiles = treeProfiles;
        _biomePresetOrder = biomePresetOrder;
        _biomePresets = biomePresets;
        _surfaceWildlife = surfaceWildlife;
        _caveWildlifeByLayer = caveWildlifeByLayer;
        _historyFigureGeneration = historyFigureGeneration;
        _contentQueries = contentQueries;
        _oreCompatibilityKeys = oreCompatibilityKeys;
        _fallbackStrataProfile = fallbackStrataProfile;
        _fallbackBiomePreset = fallbackBiomePreset;
        _fallbackSurfaceWildlife = fallbackSurfaceWildlife;
        _fallbackCaveWildlife = fallbackCaveWildlife;
    }

    public static WorldGenContentCatalog FromConfig(WorldGenContentConfig config)
        => FromConfig(config, SharedContentCatalogLoader.LoadDefaultOrFallback());

    public static WorldGenContentCatalog FromConfig(WorldGenContentConfig config, SharedContentCatalog sharedContent)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(sharedContent);

        var strataProfiles = new Dictionary<string, StrataProfile>(StringComparer.OrdinalIgnoreCase);
        var mineralVeinsByProfile = new Dictionary<string, IReadOnlyList<MineralVeinDef>>(StringComparer.OrdinalIgnoreCase);
        var treeProfiles = new Dictionary<string, TreeBiomeProfile>(StringComparer.OrdinalIgnoreCase);
        var legacyBiomePresets = BuildLegacyBiomePresets();
        var biomePresetOrder = new List<BiomeGenerationProfile>(legacyBiomePresets);
        var biomePresets = legacyBiomePresets.ToDictionary(profile => profile.Id, StringComparer.OrdinalIgnoreCase);
        var surfaceWildlife = new Dictionary<string, IReadOnlyList<SpawnEntry>>(StringComparer.OrdinalIgnoreCase);
        var caveWildlifeByLayer = new Dictionary<int, IReadOnlyList<SpawnEntry>>();
        var historyFigureGeneration = HistoryFigureGenerationCatalog.Create(config.HistoryFigures, sharedContent);
        var oreCompatibilityKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contentQueries = new ContentQueryService(sharedContent);

        foreach (var profileConfig in config.GeologyProfiles)
        {
            if (string.IsNullOrWhiteSpace(profileConfig.Id) || profileConfig.Layers.Count == 0)
                continue;

            var layers = profileConfig.Layers
                .Where(layer => !string.IsNullOrWhiteSpace(layer.RockTypeId))
                .Select(layer => new StrataLayer(
                    RockTypeId: ResolveWorldgenMaterialId(sharedContent, layer.RockTypeId, $"geology layer in profile '{profileConfig.Id}'"),
                    ThicknessMin: Math.Max(1, layer.ThicknessMin),
                    ThicknessMax: Math.Max(Math.Max(1, layer.ThicknessMin), layer.ThicknessMax)))
                .ToArray();
            if (layers.Length == 0)
                continue;

            strataProfiles[profileConfig.Id] = new StrataProfile(
                GeologyProfileId: profileConfig.Id,
                SeedSalt: profileConfig.SeedSalt,
                Layers: layers,
                AquiferDepthFraction: Math.Clamp(profileConfig.AquiferDepthFraction, 0f, 1f));

            var veins = new List<MineralVeinDef>(profileConfig.MineralVeins.Count);
            foreach (var veinConfig in profileConfig.MineralVeins)
            {
                if (string.IsNullOrWhiteSpace(veinConfig.RequiredRockTypeId))
                    continue;

                var def = ResolveMineralVeinDef(profileConfig.Id, veinConfig, sharedContent, contentQueries);
                veins.Add(def);
                oreCompatibilityKeys.Add(BuildOreCompatibilityKey(def.OreId, def.RequiredRockType));
            }

            mineralVeinsByProfile[profileConfig.Id] = veins;
        }

        foreach (var treeProfileConfig in config.TreeProfiles)
        {
            if (string.IsNullOrWhiteSpace(treeProfileConfig.BiomeId))
                continue;

            var defaultSpecies = NormalizeSpeciesWeights(sharedContent, treeProfileConfig.DefaultSpecies, $"tree profile '{treeProfileConfig.BiomeId}' defaults");
            if (defaultSpecies.Count == 0)
                continue;

            var rules = treeProfileConfig.Rules
                .Select(rule => new TreeSpeciesRule(
                    MinMoisture: rule.MinMoisture,
                    MaxMoisture: rule.MaxMoisture,
                    MinTerrain: rule.MinTerrain,
                    MaxTerrain: rule.MaxTerrain,
                    MinRiparianBoost: rule.MinRiparianBoost,
                    MaxRiparianBoost: rule.MaxRiparianBoost,
                    Chance: rule.Chance,
                    Species: NormalizeSpeciesWeights(sharedContent, rule.Species, $"tree profile '{treeProfileConfig.BiomeId}' rule")))
                .Where(rule => rule.Species.Count > 0)
                .ToArray();

            treeProfiles[treeProfileConfig.BiomeId] = new TreeBiomeProfile(
                treeProfileConfig.BiomeId,
                string.IsNullOrWhiteSpace(treeProfileConfig.SubsurfaceMaterialId)
                    ? ResolveLegacyTreeSubsurfaceMaterialId(treeProfileConfig.BiomeId)
                    : ResolveWorldgenMaterialId(sharedContent, treeProfileConfig.SubsurfaceMaterialId, $"tree profile '{treeProfileConfig.BiomeId}'"),
                rules,
                defaultSpecies);
        }

        foreach (var biomeProfileConfig in config.BiomeProfiles)
        {
            if (string.IsNullOrWhiteSpace(biomeProfileConfig.Id))
                continue;

            var profile = new BiomeGenerationProfile(
                Id: biomeProfileConfig.Id,
                GroundPlantDensity: biomeProfileConfig.GroundPlantDensity ?? ResolveLegacyGroundPlantDensity(biomeProfileConfig.Id),
                TerrainRuggedness: Math.Clamp(biomeProfileConfig.TerrainRuggedness ?? ResolveLegacyTerrainRuggedness(biomeProfileConfig.Id), 0f, 1f),
                BaseMoisture: Math.Clamp(biomeProfileConfig.BaseMoisture ?? ResolveLegacyBaseMoisture(biomeProfileConfig.Id), 0f, 1f),
                TreeCoverageBoost: Math.Clamp(biomeProfileConfig.TreeCoverageBoost ?? ResolveLegacyTreeCoverageBoost(biomeProfileConfig.Id), -1f, 1f),
                TreeSuitabilityFloor: Math.Clamp(biomeProfileConfig.TreeSuitabilityFloor ?? ResolveLegacyTreeSuitabilityFloor(biomeProfileConfig.Id), 0f, 1f),
                DenseForest: biomeProfileConfig.DenseForest ?? ResolveLegacyDenseForest(biomeProfileConfig.Id),
                SurfaceCreatureGroupBias: biomeProfileConfig.SurfaceCreatureGroupBias ?? ResolveLegacySurfaceCreatureGroupBias(biomeProfileConfig.Id),
                TreeCoverMin: Math.Clamp(biomeProfileConfig.TreeCoverMin, 0f, 0.95f),
                TreeCoverMax: Math.Clamp(biomeProfileConfig.TreeCoverMax, Math.Clamp(biomeProfileConfig.TreeCoverMin, 0f, 0.95f), 0.95f),
                OutcropMin: Math.Max(0, biomeProfileConfig.OutcropMin),
                OutcropMax: Math.Max(Math.Max(0, biomeProfileConfig.OutcropMin), biomeProfileConfig.OutcropMax),
                StreamBands: Math.Max(0, biomeProfileConfig.StreamBands),
                MarshPoolCount: Math.Max(0, biomeProfileConfig.MarshPoolCount),
                StoneSurface: biomeProfileConfig.StoneSurface);

            if (!biomePresets.ContainsKey(profile.Id))
                biomePresetOrder.Add(profile);

            biomePresets[profile.Id] = profile;
            surfaceWildlife[profile.Id] = NormalizeSpawnEntries(biomeProfileConfig.SurfaceWildlife);
        }

        foreach (var caveLayerConfig in config.CaveWildlifeLayers)
        {
            if (caveLayerConfig.Layer <= 0)
                continue;

            caveWildlifeByLayer[caveLayerConfig.Layer] = NormalizeSpawnEntries(caveLayerConfig.Spawns);
        }

        MergeCreatureEcologySurfaceWildlife(surfaceWildlife, biomePresets, contentQueries);
        MergeCreatureEcologyCaveWildlife(caveWildlifeByLayer, contentQueries);

        if (strataProfiles.Count == 0)
            throw new InvalidOperationException("Worldgen content config did not define any valid geology profiles.");
        if (biomePresetOrder.Count == 0)
            throw new InvalidOperationException("Worldgen content catalog requires at least one biome profile.");

        var fallbackStrataProfile = strataProfiles.TryGetValue(GeologyProfileIds.MixedBedrock, out var mixedBedrock)
            ? mixedBedrock
            : strataProfiles.Values.First();
        var fallbackBiomePreset = biomePresets.TryGetValue(MacroBiomeIds.TemperatePlains, out var temperatePreset)
            ? temperatePreset
            : biomePresetOrder[0];
        var fallbackSurfaceWildlife = surfaceWildlife.TryGetValue(fallbackBiomePreset.Id, out var temperateSurfaceWildlife)
            ? temperateSurfaceWildlife
            : Array.Empty<SpawnEntry>();
        var fallbackCaveWildlife = caveWildlifeByLayer.TryGetValue(1, out var caveLayerOne)
            ? caveLayerOne
            : Array.Empty<SpawnEntry>();

        return new WorldGenContentCatalog(
            strataProfiles,
            mineralVeinsByProfile,
            treeProfiles,
            biomePresetOrder,
            biomePresets,
            surfaceWildlife,
            caveWildlifeByLayer,
            historyFigureGeneration,
            contentQueries,
            oreCompatibilityKeys,
            fallbackStrataProfile,
            fallbackBiomePreset,
            fallbackSurfaceWildlife,
            fallbackCaveWildlife);
    }

    public BiomeGenerationProfile ResolveBiomePreset(string? biomeId, int seed)
    {
        if (!string.IsNullOrWhiteSpace(biomeId) && _biomePresets.TryGetValue(biomeId, out var profile))
            return profile;

        if (_biomePresetOrder.Count == 0)
            return _fallbackBiomePreset;

        var idx = (int)((seed & int.MaxValue) % _biomePresetOrder.Count);
        return _biomePresetOrder[idx];
    }

    public IReadOnlyList<SpawnEntry> ResolveSurfaceWildlife(string? biomeId, int seed)
    {
        var resolvedBiomeId = ResolveBiomePreset(biomeId, seed).Id;
        return _surfaceWildlife.TryGetValue(resolvedBiomeId, out var spawns)
            ? spawns
            : _fallbackSurfaceWildlife;
    }

    public IReadOnlyList<SpawnEntry> ResolveCaveWildlife(int caveLayer)
    {
        return _caveWildlifeByLayer.TryGetValue(caveLayer, out var spawns)
            ? spawns
            : _fallbackCaveWildlife;
    }

    public HistoryProfessionProfile ResolveHistoryFigureProfession(
        string? speciesDefId,
        string? siteKind,
        int memberIndex,
        bool founderBias,
        Random rng)
        => _historyFigureGeneration.ResolveProfession(speciesDefId, siteKind, memberIndex, founderBias, rng);

    public string ResolveHistoryFigureName(string? speciesDefId, Random rng)
        => _historyFigureGeneration.ResolveFigureName(speciesDefId, rng);

    public string ResolveFactionPrimaryUnit(FactionTemplateConfig template, Random rng)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(rng);

        var chooseAlternate = !string.IsNullOrWhiteSpace(template.AlternatePrimaryUnitDefId) ||
                              !string.IsNullOrWhiteSpace(template.AlternatePrimaryUnitRole);
        if (chooseAlternate && rng.NextDouble() < Clamp01(template.AlternatePrimaryChance))
        {
            var alternate = ResolveCreatureDefId(template.AlternatePrimaryUnitRole, template.AlternatePrimaryUnitDefId, rng);
            if (!string.IsNullOrWhiteSpace(alternate))
                return alternate;
        }

        var primary = ResolveCreatureDefId(template.PrimaryUnitRole, template.PrimaryUnitDefId, rng);
        if (!string.IsNullOrWhiteSpace(primary))
            return primary;

        return ResolveDefaultCivilizationPrimaryUnit(template.IsHostile, rng);
    }

    public string ResolveDefaultCivilizationPrimaryUnit(bool hostile, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        var role = hostile ? FactionUnitRoleIds.HostilePrimary : FactionUnitRoleIds.CivilizedPrimary;
        var resolved = _contentQueries.ResolveFactionCreatureDefId(role, rng);
        if (!string.IsNullOrWhiteSpace(resolved))
            return resolved;

        var fallback = hostile
            ? _contentQueries.ResolveDefaultHostileCreatureDefId()
            : _contentQueries.ResolveDefaultPlayableCreatureDefId();
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;

        var anyCreature = _contentQueries.Catalog.Creatures.Values
            .OrderBy(creature => creature.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?.Id;
        if (!string.IsNullOrWhiteSpace(anyCreature))
            return anyCreature;

        throw new InvalidOperationException("Worldgen requires at least one creature definition.");
    }

    public float ResolveGroundPlantDensity(string? biomeId)
        => ResolveBiomePreset(biomeId, seed: 0).GroundPlantDensity;

    public int ResolveSurfaceCreatureGroupBias(string? biomeId)
        => ResolveBiomePreset(biomeId, seed: 0).SurfaceCreatureGroupBias;

    public StrataProfile ResolveStrataProfile(string? geologyProfileId)
    {
        if (!string.IsNullOrWhiteSpace(geologyProfileId) && _strataProfiles.TryGetValue(geologyProfileId, out var profile))
            return profile;

        return _fallbackStrataProfile;
    }

    public IReadOnlyList<MineralVeinDef> ResolveMineralVeins(string? geologyProfileId)
    {
        var resolvedProfileId = ResolveStrataProfile(geologyProfileId).GeologyProfileId;
        return _mineralVeinsByProfile.TryGetValue(resolvedProfileId, out var veins)
            ? veins
            : Array.Empty<MineralVeinDef>();
    }

    public bool IsOreCompatible(string oreId, string? rockTypeId)
    {
        if (string.IsNullOrWhiteSpace(oreId) || string.IsNullOrWhiteSpace(rockTypeId))
            return false;

        return _oreCompatibilityKeys.Contains(BuildOreCompatibilityKey(oreId, rockTypeId));
    }

    public string ResolveTreeSpeciesId(string biomeId, float moisture, float terrain, float riparianBoost, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        if (_treeProfiles.TryGetValue(biomeId, out var profile))
        {
            foreach (var rule in profile.Rules)
            {
                if (!rule.Matches(moisture, terrain, riparianBoost))
                    continue;
                if (rule.Chance is float chance && rng.NextDouble() > chance)
                    continue;

                var ruleSpeciesId = SelectWeightedSpecies(rule.Species, rng);
                if (!string.IsNullOrWhiteSpace(ruleSpeciesId))
                    return ruleSpeciesId;
            }

            var defaultSpeciesId = SelectWeightedSpecies(profile.DefaultSpecies, rng);
            if (!string.IsNullOrWhiteSpace(defaultSpeciesId))
                return defaultSpeciesId;
        }

        return ResolveLegacyTreeSpeciesId(biomeId, moisture, terrain, riparianBoost, rng);
    }

    public string ResolveTreeSubsurfaceMaterialId(string biomeId)
    {
        if (_treeProfiles.TryGetValue(biomeId, out var profile) && !string.IsNullOrWhiteSpace(profile.SubsurfaceMaterialId))
            return profile.SubsurfaceMaterialId;

        return ResolveLegacyTreeSubsurfaceMaterialId(biomeId);
    }

    private static MineralVeinDef ResolveMineralVeinDef(
        string geologyProfileId,
        MineralVeinContentConfig veinConfig,
        SharedContentCatalog sharedContent,
        ContentQueryService contentQueries)
    {
        var formRole = ResolveVeinFormRole(veinConfig);
        var requiredRockTypeId = ResolveWorldgenMaterialId(sharedContent, veinConfig.RequiredRockTypeId, $"mineral vein in profile '{geologyProfileId}'");
        var shape = ParseVeinShape(veinConfig.Shape);

        var materialId = ResolveVeinMaterialId(veinConfig, contentQueries, sharedContent);
        var resourceItemDefId = ResolveVeinResourceItemDefId(veinConfig, materialId, formRole, contentQueries);

        return new MineralVeinDef(
            MaterialId: materialId,
            ResourceItemDefId: resourceItemDefId,
            ResourceFormRole: formRole,
            Shape: shape,
            Frequency: Math.Max(0f, veinConfig.Frequency),
            RequiredRockType: requiredRockTypeId,
            SizeMin: Math.Max(1, veinConfig.SizeMin),
            SizeMax: Math.Max(Math.Max(1, veinConfig.SizeMin), veinConfig.SizeMax));
    }

    private static string ResolveVeinFormRole(MineralVeinContentConfig veinConfig)
    {
        if (!string.IsNullOrWhiteSpace(veinConfig.ResourceFormRole))
            return veinConfig.ResourceFormRole.Trim().ToLowerInvariant();

        return ContentFormRoles.Ore;
    }

    private static string ResolveVeinMaterialId(
        MineralVeinContentConfig veinConfig,
        ContentQueryService contentQueries,
        SharedContentCatalog sharedContent)
    {
        if (!string.IsNullOrWhiteSpace(veinConfig.MaterialId))
            return ResolveWorldgenMaterialId(sharedContent, veinConfig.MaterialId, "mineral vein material");

        if (!string.IsNullOrWhiteSpace(veinConfig.OreId))
        {
            var resolvedMaterialId = contentQueries.ResolveMaterialIdForFormItemDefId(veinConfig.OreId, ResolveVeinFormRole(veinConfig));
            if (!string.IsNullOrWhiteSpace(resolvedMaterialId))
                return resolvedMaterialId;
        }

        throw new InvalidOperationException("Mineral vein is missing a resolvable 'materialId' or legacy 'oreId'.");
    }

    private static string ResolveVeinResourceItemDefId(
        MineralVeinContentConfig veinConfig,
        string materialId,
        string formRole,
        ContentQueryService contentQueries)
    {
        if (!string.IsNullOrWhiteSpace(veinConfig.OreId))
        {
            var explicitItemId = veinConfig.OreId.Trim();
            var resolvedFromMaterial = contentQueries.ResolveMaterialFormItemDefId(materialId, formRole);
            if (!string.IsNullOrWhiteSpace(resolvedFromMaterial) &&
                !string.Equals(explicitItemId, resolvedFromMaterial, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Mineral vein item '{explicitItemId}' does not match material '{materialId}' form '{formRole}' ('{resolvedFromMaterial}').");
            }

            return explicitItemId;
        }

        var itemDefId = contentQueries.ResolveMaterialFormItemDefId(materialId, formRole);
        if (!string.IsNullOrWhiteSpace(itemDefId))
            return itemDefId;

        throw new InvalidOperationException($"Material '{materialId}' does not define a '{formRole}' resource form for mineral vein generation.");
    }

    private static string ResolveWorldgenMaterialId(SharedContentCatalog sharedContent, string? materialId, string context)
    {
        if (string.IsNullOrWhiteSpace(materialId))
            throw new InvalidOperationException($"Worldgen {context} is missing a required material id.");

        var normalized = materialId.Trim();
        if (sharedContent.Materials.ContainsKey(normalized))
            return normalized;

        throw new InvalidOperationException($"Worldgen {context} references unknown material '{normalized}'.");
    }

    private static string BuildOreCompatibilityKey(string oreId, string rockTypeId)
        => string.Concat(oreId.Trim(), "|", rockTypeId.Trim());

    private static VeinShape ParseVeinShape(string? shape)
    {
        if (Enum.TryParse<VeinShape>(shape, ignoreCase: true, out var parsed))
            return parsed;

        return VeinShape.Cluster;
    }

    private static IReadOnlyList<WeightedTreeSpecies> NormalizeSpeciesWeights(
        SharedContentCatalog sharedContent,
        IEnumerable<WeightedTreeSpeciesContentConfig> source,
        string context)
    {
        return source
            .Where(species => !string.IsNullOrWhiteSpace(species.SpeciesId))
            .Select(species => new WeightedTreeSpecies(
                ResolveTreeSpeciesId(sharedContent, species.SpeciesId, context),
                Math.Max(0.001f, species.Weight)))
            .ToArray();
    }

    private static string ResolveTreeSpeciesId(SharedContentCatalog sharedContent, string? speciesId, string context)
    {
        if (string.IsNullOrWhiteSpace(speciesId))
            throw new InvalidOperationException($"Worldgen {context} is missing a required tree species id.");

        var normalized = speciesId.Trim();
        if (sharedContent.TreeSpecies.ContainsKey(normalized))
            return normalized;

        throw new InvalidOperationException($"Worldgen {context} references unknown tree species '{normalized}'.");
    }

    private static IReadOnlyList<SpawnEntry> NormalizeSpawnEntries(IEnumerable<CreatureSpawnContentConfig> source)
    {
        return source
            .Where(spawn => !string.IsNullOrWhiteSpace(spawn.CreatureDefId))
            .Select(spawn => new SpawnEntry(
                CreatureDefId: spawn.CreatureDefId,
                Weight: Math.Max(0.001f, spawn.Weight),
                MinGroup: Math.Max(1, spawn.MinGroup),
                MaxGroup: Math.Max(Math.Max(1, spawn.MinGroup), spawn.MaxGroup),
                RequiresWater: spawn.RequiresWater,
                AvoidEmbarkCenter: spawn.AvoidEmbarkCenter))
            .ToArray();
    }

    private static void MergeCreatureEcologySurfaceWildlife(
        IDictionary<string, IReadOnlyList<SpawnEntry>> surfaceWildlife,
        IReadOnlyDictionary<string, BiomeGenerationProfile> biomePresets,
        ContentQueryService contentQueries)
    {
        foreach (var biomeId in biomePresets.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var derivedEntries = contentQueries.ResolveSurfaceWildlifeForBiome(biomeId);
            if (derivedEntries.Count == 0)
                continue;

            surfaceWildlife[biomeId] = MergeSpawnEntries(TryGetSpawnEntries(surfaceWildlife, biomeId), derivedEntries);
        }

        foreach (var creature in contentQueries.Catalog.Creatures.Values.OrderBy(creature => creature.Id, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var rule in creature.Ecology?.SurfaceWildlife ?? Array.Empty<CreatureSurfaceEcologyContentDefinition>())
            {
                foreach (var biomeId in rule.BiomeIds)
                {
                    if (biomePresets.ContainsKey(biomeId))
                        continue;

                    throw new InvalidOperationException($"Creature '{creature.Id}' ecology references unknown biome '{biomeId}'.");
                }
            }
        }
    }

    private static void MergeCreatureEcologyCaveWildlife(
        IDictionary<int, IReadOnlyList<SpawnEntry>> caveWildlifeByLayer,
        ContentQueryService contentQueries)
    {
        var derivedLayers = contentQueries.Catalog.Creatures.Values
            .SelectMany(creature => creature.Ecology?.CaveWildlife ?? Array.Empty<CreatureCaveEcologyContentDefinition>())
            .SelectMany(rule => rule.Layers)
            .Distinct()
            .OrderBy(layer => layer)
            .ToArray();

        foreach (var layer in derivedLayers)
        {
            var derivedEntries = contentQueries.ResolveCaveWildlifeForLayer(layer);
            if (derivedEntries.Count == 0)
                continue;

            caveWildlifeByLayer[layer] = MergeSpawnEntries(TryGetSpawnEntries(caveWildlifeByLayer, layer), derivedEntries);
        }
    }

    private static IReadOnlyList<SpawnEntry> MergeSpawnEntries(
        IReadOnlyList<SpawnEntry>? explicitEntries,
        IReadOnlyList<SpawnEntry> derivedEntries)
    {
        if ((explicitEntries?.Count ?? 0) == 0)
            return derivedEntries;

        var merged = explicitEntries!.ToList();
        var existingCreatureIds = new HashSet<string>(
            merged.Select(entry => entry.CreatureDefId),
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in derivedEntries)
        {
            if (!existingCreatureIds.Add(entry.CreatureDefId))
                continue;

            merged.Add(entry);
        }

        return merged;
    }

    private static IReadOnlyList<SpawnEntry>? TryGetSpawnEntries<TKey>(
        IDictionary<TKey, IReadOnlyList<SpawnEntry>> source,
        TKey key) where TKey : notnull
        => source.TryGetValue(key, out var entries) ? entries : null;

    private string? ResolveCreatureDefId(string? role, string? explicitDefId, Random rng)
    {
        var resolvedFromRole = _contentQueries.ResolveFactionCreatureDefId(role, rng);
        if (!string.IsNullOrWhiteSpace(resolvedFromRole))
            return resolvedFromRole;

        return string.IsNullOrWhiteSpace(explicitDefId) ? null : explicitDefId;
    }

    private static float Clamp01(float value)
        => Math.Clamp(value, 0f, 1f);

    private static string? SelectWeightedSpecies(IReadOnlyList<WeightedTreeSpecies> candidates, Random rng)
    {
        if (candidates.Count == 0)
            return null;

        var totalWeight = 0f;
        for (var i = 0; i < candidates.Count; i++)
            totalWeight += candidates[i].Weight;

        if (totalWeight <= 0f)
            return candidates[0].SpeciesId;

        var roll = (float)rng.NextDouble() * totalWeight;
        for (var i = 0; i < candidates.Count; i++)
        {
            roll -= candidates[i].Weight;
            if (roll <= 0f)
                return candidates[i].SpeciesId;
        }

        return candidates[^1].SpeciesId;
    }

    private static string ResolveLegacyTreeSpeciesId(string biomeId, float moisture, float terrain, float riparianBoost, Random rng)
    {
        if (string.Equals(biomeId, MacroBiomeIds.ConiferForest, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.BorealForest, StringComparison.OrdinalIgnoreCase))
        {
            return rng.NextDouble() < 0.54 ? TreeSpeciesIds.Spruce : TreeSpeciesIds.Pine;
        }

        if (string.Equals(biomeId, MacroBiomeIds.MistyMarsh, StringComparison.OrdinalIgnoreCase))
            return TreeSpeciesIds.Willow;

        if (string.Equals(biomeId, MacroBiomeIds.TropicalRainforest, StringComparison.OrdinalIgnoreCase))
            return rng.NextDouble() < 0.62 ? TreeSpeciesIds.Palm : TreeSpeciesIds.Baobab;

        if (string.Equals(biomeId, MacroBiomeIds.Savanna, StringComparison.OrdinalIgnoreCase))
            return rng.NextDouble() < 0.70 ? TreeSpeciesIds.Baobab : TreeSpeciesIds.Palm;

        if (string.Equals(biomeId, MacroBiomeIds.Highland, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.Tundra, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase))
        {
            return TreeSpeciesIds.Deadwood;
        }

        if (riparianBoost >= 0.85f && string.Equals(biomeId, MacroBiomeIds.TemperatePlains, StringComparison.OrdinalIgnoreCase))
            return rng.NextDouble() < 0.58 ? TreeSpeciesIds.Willow : TreeSpeciesIds.Birch;

        if (string.Equals(biomeId, MacroBiomeIds.WindsweptSteppe, StringComparison.OrdinalIgnoreCase))
        {
            if (riparianBoost >= 0.80f)
                return TreeSpeciesIds.Birch;
            return moisture >= 0.45f && terrain <= 0.55f ? TreeSpeciesIds.Birch : TreeSpeciesIds.Deadwood;
        }

        if (string.Equals(biomeId, MacroBiomeIds.TemperatePlains, StringComparison.OrdinalIgnoreCase) &&
            moisture >= 0.26f && moisture <= 0.72f && terrain <= 0.62f && rng.NextDouble() < 0.18)
        {
            return TreeSpeciesIds.Apple;
        }

        return rng.NextDouble() < 0.64 ? TreeSpeciesIds.Oak : TreeSpeciesIds.Birch;
    }

    private static string ResolveLegacyTreeSubsurfaceMaterialId(string biomeId)
    {
        if (string.Equals(biomeId, MacroBiomeIds.MistyMarsh, StringComparison.OrdinalIgnoreCase))
            return "mud";

        if (string.Equals(biomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.Savanna, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.WindsweptSteppe, StringComparison.OrdinalIgnoreCase))
        {
            return "sand";
        }

        return "soil";
    }

    private static List<BiomeGenerationProfile> BuildLegacyBiomePresets()
    {
        return
        [
            new(MacroBiomeIds.TemperatePlains, 0.20f, 0.48f, 0.56f, 0.04f, 0.21f, false, 0, 0.09f, 0.19f, 0, 2, 1, 0, false),
            new(MacroBiomeIds.ConiferForest, 0.12f, 0.54f, 0.70f, 0.12f, 0.13f, true, 2, 0.22f, 0.38f, 0, 2, 1, 0, false),
            new(MacroBiomeIds.Highland, 0.16f, 1.00f, 0.44f, 0.00f, 0.32f, false, -1, 0.02f, 0.08f, 10, 20, 1, 0, true),
            new(MacroBiomeIds.MistyMarsh, 0.24f, 0.42f, 0.90f, 0.08f, 0.16f, false, 1, 0.06f, 0.14f, 0, 1, 2, 10, false),
            new(MacroBiomeIds.WindsweptSteppe, 0.16f, 0.66f, 0.20f, 0.00f, 0.28f, false, 0, 0.01f, 0.05f, 1, 4, 0, 0, false),
            new(MacroBiomeIds.TropicalRainforest, 0.28f, 0.48f, 0.82f, 0.16f, 0.08f, true, 3, 0.32f, 0.52f, 0, 2, 2, 4, false),
            new(MacroBiomeIds.Savanna, 0.16f, 0.56f, 0.34f, 0.00f, 0.24f, false, 1, 0.03f, 0.09f, 1, 5, 1, 0, false),
            new(MacroBiomeIds.Desert, 0.08f, 0.74f, 0.08f, 0.00f, 0.40f, false, -2, 0.00f, 0.01f, 3, 9, 0, 0, false),
            new(MacroBiomeIds.Tundra, 0.12f, 0.78f, 0.24f, 0.00f, 0.32f, false, -2, 0.00f, 0.02f, 2, 8, 0, 0, false),
            new(MacroBiomeIds.BorealForest, 0.20f, 0.58f, 0.66f, 0.14f, 0.11f, true, 2, 0.25f, 0.42f, 1, 4, 1, 1, false),
            new(MacroBiomeIds.IcePlains, 0.08f, 0.72f, 0.16f, 0.00f, 0.40f, false, -3, 0.00f, 0.00f, 2, 7, 0, 0, false),
            new(MacroBiomeIds.OceanShallow, 0.00f, 0.22f, 0.98f, 0.00f, 1.00f, false, 3, 0.00f, 0.00f, 0, 1, 0, 0, false),
            new(MacroBiomeIds.OceanDeep, 0.00f, 0.18f, 1.00f, 0.00f, 1.00f, false, 4, 0.00f, 0.00f, 0, 0, 0, 0, false),
        ];
    }

    private static float ResolveLegacyGroundPlantDensity(string biomeId)
    {
        if (string.Equals(biomeId, MacroBiomeIds.TropicalRainforest, StringComparison.OrdinalIgnoreCase))
            return 0.28f;
        if (string.Equals(biomeId, MacroBiomeIds.MistyMarsh, StringComparison.OrdinalIgnoreCase))
            return 0.24f;
        if (string.Equals(biomeId, MacroBiomeIds.TemperatePlains, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.BorealForest, StringComparison.OrdinalIgnoreCase))
        {
            return 0.20f;
        }
        if (string.Equals(biomeId, MacroBiomeIds.Savanna, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.WindsweptSteppe, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.Highland, StringComparison.OrdinalIgnoreCase))
        {
            return 0.16f;
        }
        if (string.Equals(biomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase))
        {
            return 0.08f;
        }

        return 0.12f;
    }

    private static float ResolveLegacyTerrainRuggedness(string biomeId)
    {
        return biomeId switch
        {
            MacroBiomeIds.Highland => 1.00f,
            MacroBiomeIds.Tundra => 0.78f,
            MacroBiomeIds.IcePlains => 0.72f,
            MacroBiomeIds.Desert => 0.74f,
            MacroBiomeIds.WindsweptSteppe => 0.66f,
            MacroBiomeIds.MistyMarsh => 0.42f,
            MacroBiomeIds.TropicalRainforest => 0.48f,
            MacroBiomeIds.Savanna => 0.56f,
            MacroBiomeIds.BorealForest => 0.58f,
            MacroBiomeIds.ConiferForest => 0.54f,
            MacroBiomeIds.OceanShallow => 0.22f,
            MacroBiomeIds.OceanDeep => 0.18f,
            _ => 0.48f,
        };
    }

    private static float ResolveLegacyBaseMoisture(string biomeId)
    {
        return biomeId switch
        {
            MacroBiomeIds.MistyMarsh => 0.90f,
            MacroBiomeIds.TropicalRainforest => 0.82f,
            MacroBiomeIds.ConiferForest => 0.70f,
            MacroBiomeIds.BorealForest => 0.66f,
            MacroBiomeIds.TemperatePlains => 0.56f,
            MacroBiomeIds.Highland => 0.44f,
            MacroBiomeIds.Savanna => 0.34f,
            MacroBiomeIds.WindsweptSteppe => 0.20f,
            MacroBiomeIds.Tundra => 0.24f,
            MacroBiomeIds.IcePlains => 0.16f,
            MacroBiomeIds.Desert => 0.08f,
            MacroBiomeIds.OceanShallow => 0.98f,
            MacroBiomeIds.OceanDeep => 1.00f,
            _ => 0.50f,
        };
    }

    private static float ResolveLegacyTreeCoverageBoost(string biomeId)
    {
        if (string.Equals(biomeId, MacroBiomeIds.TropicalRainforest, StringComparison.OrdinalIgnoreCase))
            return 0.16f;
        if (string.Equals(biomeId, MacroBiomeIds.ConiferForest, StringComparison.OrdinalIgnoreCase))
            return 0.12f;
        if (string.Equals(biomeId, MacroBiomeIds.BorealForest, StringComparison.OrdinalIgnoreCase))
            return 0.14f;
        if (string.Equals(biomeId, MacroBiomeIds.MistyMarsh, StringComparison.OrdinalIgnoreCase))
            return 0.08f;
        if (string.Equals(biomeId, MacroBiomeIds.TemperatePlains, StringComparison.OrdinalIgnoreCase))
            return 0.04f;

        return 0f;
    }

    private static float ResolveLegacyTreeSuitabilityFloor(string biomeId)
    {
        if (string.Equals(biomeId, MacroBiomeIds.TropicalRainforest, StringComparison.OrdinalIgnoreCase))
            return 0.08f;
        if (string.Equals(biomeId, MacroBiomeIds.BorealForest, StringComparison.OrdinalIgnoreCase))
            return 0.11f;
        if (string.Equals(biomeId, MacroBiomeIds.ConiferForest, StringComparison.OrdinalIgnoreCase))
            return 0.13f;
        if (string.Equals(biomeId, MacroBiomeIds.MistyMarsh, StringComparison.OrdinalIgnoreCase))
            return 0.16f;
        if (string.Equals(biomeId, MacroBiomeIds.TemperatePlains, StringComparison.OrdinalIgnoreCase))
            return 0.21f;
        if (string.Equals(biomeId, MacroBiomeIds.Savanna, StringComparison.OrdinalIgnoreCase))
            return 0.24f;
        if (string.Equals(biomeId, MacroBiomeIds.WindsweptSteppe, StringComparison.OrdinalIgnoreCase))
            return 0.28f;
        if (string.Equals(biomeId, MacroBiomeIds.Highland, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.Tundra, StringComparison.OrdinalIgnoreCase))
        {
            return 0.32f;
        }
        if (string.Equals(biomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase))
        {
            return 0.40f;
        }

        return 0.24f;
    }

    private static bool ResolveLegacyDenseForest(string biomeId)
    {
        return string.Equals(biomeId, MacroBiomeIds.ConiferForest, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(biomeId, MacroBiomeIds.BorealForest, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(biomeId, MacroBiomeIds.TropicalRainforest, StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveLegacySurfaceCreatureGroupBias(string biomeId)
    {
        return biomeId switch
        {
            MacroBiomeIds.TropicalRainforest => 3,
            MacroBiomeIds.ConiferForest => 2,
            MacroBiomeIds.BorealForest => 2,
            MacroBiomeIds.MistyMarsh => 1,
            MacroBiomeIds.OceanShallow => 3,
            MacroBiomeIds.OceanDeep => 4,
            MacroBiomeIds.Savanna => 1,
            MacroBiomeIds.Desert => -2,
            MacroBiomeIds.Tundra => -2,
            MacroBiomeIds.IcePlains => -3,
            MacroBiomeIds.Highland => -1,
            _ => 0,
        };
    }

    private sealed record TreeBiomeProfile(
        string BiomeId,
        string SubsurfaceMaterialId,
        IReadOnlyList<TreeSpeciesRule> Rules,
        IReadOnlyList<WeightedTreeSpecies> DefaultSpecies);

    private sealed record TreeSpeciesRule(
        float? MinMoisture,
        float? MaxMoisture,
        float? MinTerrain,
        float? MaxTerrain,
        float? MinRiparianBoost,
        float? MaxRiparianBoost,
        float? Chance,
        IReadOnlyList<WeightedTreeSpecies> Species)
    {
        public bool Matches(float moisture, float terrain, float riparianBoost)
        {
            if (MinMoisture is float minMoisture && moisture < minMoisture)
                return false;
            if (MaxMoisture is float maxMoisture && moisture > maxMoisture)
                return false;
            if (MinTerrain is float minTerrain && terrain < minTerrain)
                return false;
            if (MaxTerrain is float maxTerrain && terrain > maxTerrain)
                return false;
            if (MinRiparianBoost is float minRiparianBoost && riparianBoost < minRiparianBoost)
                return false;
            if (MaxRiparianBoost is float maxRiparianBoost && riparianBoost > maxRiparianBoost)
                return false;

            return true;
        }
    }

    private sealed record WeightedTreeSpecies(string SpeciesId, float Weight);
}

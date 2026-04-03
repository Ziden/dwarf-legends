using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.WorldGen.Creatures;
using DwarfFortress.WorldGen.Ids;

namespace DwarfFortress.WorldGen.Content;

public sealed class ContentQueryService
{
    public ContentQueryService(SharedContentCatalog catalog)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public SharedContentCatalog Catalog { get; }

    public string? ResolveSeedItemDefId(string plantDefId)
        => Catalog.Plants.TryGetValue(plantDefId, out var plant) ? plant.SeedItemDefId : null;

    public string? ResolveHarvestItemDefId(string plantDefId)
    {
        if (!Catalog.Plants.TryGetValue(plantDefId, out var plant))
            return null;

        return !string.IsNullOrWhiteSpace(plant.HarvestItemDefId)
            ? plant.HarvestItemDefId
            : plant.FruitItemDefId;
    }

    public IReadOnlyList<string> ResolveFoodProductsForPlant(string plantDefId)
    {
        if (!Catalog.Plants.TryGetValue(plantDefId, out var plant))
            return Array.Empty<string>();

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(plant.HarvestItemDefId))
            results.Add(plant.HarvestItemDefId);
        if (!string.IsNullOrWhiteSpace(plant.FruitItemDefId))
            results.Add(plant.FruitItemDefId);

        return results.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string? ResolveMineableBoulderForm(string? materialId)
        => ResolveMaterialFormItemDefId(materialId, ContentFormRoles.Boulder);

    public string? ResolveOreItemDefId(string? materialId)
        => ResolveMaterialFormItemDefId(materialId, ContentFormRoles.Ore);

    public string? ResolveBarItemDefId(string? materialId)
        => ResolveMaterialFormItemDefId(materialId, ContentFormRoles.Bar);

    public string? ResolveLogItemDefId(string? materialId)
        => ResolveMaterialFormItemDefId(materialId, ContentFormRoles.Log);

    public string? ResolvePlankItemDefId(string? materialId)
        => ResolveMaterialFormItemDefId(materialId, ContentFormRoles.Plank);

    public string? ResolveMaterialFormItemDefId(string? materialId, string? role)
    {
        if (string.IsNullOrWhiteSpace(materialId) || string.IsNullOrWhiteSpace(role))
            return null;

        if (Catalog.Materials.TryGetValue(materialId, out var material) &&
            material.Forms is not null &&
            material.Forms.TryGetValue(role, out var form))
        {
            return form.Item.Id;
        }

        var conventionId = $"{materialId.Trim()}_{role.Trim()}";
        if (Catalog.Items.ContainsKey(conventionId))
            return conventionId;

        return TryResolveGenericWoodFormItemDefId(materialId, role, out var genericWoodItemDefId)
            ? genericWoodItemDefId
            : null;
    }

    public string? ResolveMaterialIdForFormItemDefId(string? itemDefId, string? role = null)
        => ResolveMaterialIdsForFormItemDefId(itemDefId, role).FirstOrDefault();

    public IReadOnlyList<string> ResolveMaterialIdsForFormItemDefId(string? itemDefId, string? role = null)
    {
        if (string.IsNullOrWhiteSpace(itemDefId))
            return Array.Empty<string>();

        var matches = new List<string>();

        foreach (var material in Catalog.Materials.Values)
        {
            if (material.Forms is null)
                continue;

            foreach (var form in material.Forms)
            {
                if (!string.Equals(form.Value.Item.Id, itemDefId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrWhiteSpace(role) || string.Equals(form.Key, role, StringComparison.OrdinalIgnoreCase))
                    matches.Add(material.Id);
            }
        }

        if (matches.Count > 0)
            return matches
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (!string.IsNullOrWhiteSpace(role) &&
            Catalog.Items.ContainsKey(itemDefId.Trim()) &&
            IsGenericWoodFormItem(itemDefId, role))
        {
            return Catalog.Materials.Values
                .Where(IsWoodLikeMaterial)
                .Select(material => material.Id)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            var normalizedItemId = itemDefId.Trim();
            var suffix = "_" + role.Trim();
            if (normalizedItemId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var materialId = normalizedItemId[..^suffix.Length];
                return Catalog.Materials.ContainsKey(materialId)
                    ? new[] { materialId }
                    : Array.Empty<string>();
            }
        }

        return Array.Empty<string>();
    }

    private bool TryResolveGenericWoodFormItemDefId(string materialId, string role, out string itemDefId)
    {
        itemDefId = string.Empty;
        if (!Catalog.Materials.TryGetValue(materialId, out var material) || !IsWoodLikeMaterial(material))
            return false;

        itemDefId = role.Trim().ToLowerInvariant();
        return Catalog.Items.ContainsKey(itemDefId);
    }

    private static bool IsWoodLikeMaterial(MaterialContentDefinition material)
        => string.Equals(material.Id, "wood", StringComparison.OrdinalIgnoreCase)
        || material.Id.EndsWith("_wood", StringComparison.OrdinalIgnoreCase)
        || material.Tags.Any(tag => string.Equals(tag, "wood", StringComparison.OrdinalIgnoreCase));

    private static bool IsGenericWoodFormItem(string itemDefId, string role)
        => (string.Equals(itemDefId, "log", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(role, ContentFormRoles.Log, StringComparison.OrdinalIgnoreCase)) ||
           (string.Equals(itemDefId, "plank", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(role, ContentFormRoles.Plank, StringComparison.OrdinalIgnoreCase));

    public string? ResolveTreeWoodMaterialId(string? treeSpeciesId)
    {
        if (string.IsNullOrWhiteSpace(treeSpeciesId))
            return null;

        if (Catalog.TreeSpecies.TryGetValue(treeSpeciesId, out var species) &&
            !string.IsNullOrWhiteSpace(species.WoodMaterialId))
        {
            return species.WoodMaterialId;
        }

        var normalizedSpeciesId = treeSpeciesId.Trim().ToLowerInvariant();
        var conventionMaterialId = $"{normalizedSpeciesId}_wood";
        if (Catalog.Materials.ContainsKey(conventionMaterialId))
            return conventionMaterialId;

        return Catalog.Materials.ContainsKey(normalizedSpeciesId) ? normalizedSpeciesId : null;
    }

    public string ResolveTileKind(string tileDefId, string? materialId = null)
    {
        if (string.Equals(tileDefId, "stone_wall", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(materialId) &&
            Catalog.Materials.TryGetValue(materialId, out var material) &&
            material.Tags.Any(tag => string.Equals(tag, "stone", StringComparison.OrdinalIgnoreCase)))
        {
            return "natural_stone_wall";
        }

        return tileDefId.Trim().ToLowerInvariant();
    }

    public IReadOnlyList<SpawnEntry> ResolveSurfaceWildlifeForBiome(string? biomeId)
    {
        if (string.IsNullOrWhiteSpace(biomeId))
            return Array.Empty<SpawnEntry>();

        return Catalog.Creatures.Values
            .OrderBy(creature => creature.Id, StringComparer.OrdinalIgnoreCase)
            .SelectMany(creature => (creature.Ecology?.SurfaceWildlife ?? Array.Empty<CreatureSurfaceEcologyContentDefinition>())
                .Where(rule => rule.BiomeIds.Any(candidate => string.Equals(candidate, biomeId, StringComparison.OrdinalIgnoreCase)))
                .Select(rule => new SpawnEntry(
                    CreatureDefId: creature.Id,
                    Weight: Math.Max(0.001f, rule.Weight),
                    MinGroup: Math.Max(1, rule.MinGroup),
                    MaxGroup: Math.Max(Math.Max(1, rule.MinGroup), rule.MaxGroup),
                    RequiresWater: rule.RequiresWater,
                    AvoidEmbarkCenter: rule.AvoidEmbarkCenter)))
            .ToArray();
    }

    public IReadOnlyList<SpawnEntry> ResolveCaveWildlifeForLayer(int caveLayer)
    {
        if (caveLayer <= 0)
            return Array.Empty<SpawnEntry>();

        return Catalog.Creatures.Values
            .OrderBy(creature => creature.Id, StringComparer.OrdinalIgnoreCase)
            .SelectMany(creature => (creature.Ecology?.CaveWildlife ?? Array.Empty<CreatureCaveEcologyContentDefinition>())
                .Where(rule => rule.Layers.Contains(caveLayer))
                .Select(rule => new SpawnEntry(
                    CreatureDefId: creature.Id,
                    Weight: Math.Max(0.001f, rule.Weight),
                    MinGroup: Math.Max(1, rule.MinGroup),
                    MaxGroup: Math.Max(Math.Max(1, rule.MinGroup), rule.MaxGroup),
                    RequiresWater: rule.RequiresWater,
                    AvoidEmbarkCenter: rule.AvoidEmbarkCenter)))
            .ToArray();
    }

    public IReadOnlyList<string> ResolveCreatureDefIdsForFactionRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return Array.Empty<string>();

        return Catalog.Creatures.Values
            .Where(creature => (creature.Society?.FactionRoles ?? Array.Empty<CreatureFactionRoleContentDefinition>())
                .Any(candidate => string.Equals(candidate.Id, role, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(creature => creature.Id, StringComparer.OrdinalIgnoreCase)
            .Select(creature => creature.Id)
            .ToArray();
    }

    public IReadOnlyList<string> ResolveCreatureDefIdsByTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return Array.Empty<string>();

        return Catalog.Creatures.Values
            .Where(creature => creature.Tags.Any(existingTag => string.Equals(existingTag, tag, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(creature => creature.Id, StringComparer.OrdinalIgnoreCase)
            .Select(creature => creature.Id)
            .ToArray();
    }

    public string? ResolveDefaultPlayableCreatureDefId()
    {
        return Catalog.Creatures.Values
            .OrderByDescending(creature => HasFactionRole(creature, FactionUnitRoleIds.CivilizedPrimary) ? 1 : 0)
            .ThenBy(creature => creature.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(creature => creature.IsPlayable)
            ?.Id;
    }

    public string? ResolveDefaultHostileCreatureDefId()
    {
        return Catalog.Creatures.Values
            .OrderByDescending(creature => HasFactionRole(creature, FactionUnitRoleIds.HostilePrimary) ? 1 : 0)
            .ThenBy(creature => creature.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(creature => creature.IsHostile || HasFactionRole(creature, FactionUnitRoleIds.HostilePrimary))
            ?.Id;
    }

    public CreatureVisualContentDefinition? ResolveCreatureVisuals(string? creatureDefId)
    {
        if (string.IsNullOrWhiteSpace(creatureDefId))
            return null;

        return Catalog.Creatures.TryGetValue(creatureDefId.Trim(), out var creature)
            ? creature.Visuals
            : null;
    }

    public string? ResolveFactionCreatureDefId(string? role, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        if (string.IsNullOrWhiteSpace(role))
            return null;

        var candidates = Catalog.Creatures.Values
            .Select(creature => new
            {
                CreatureId = creature.Id,
                Role = (creature.Society?.FactionRoles ?? Array.Empty<CreatureFactionRoleContentDefinition>())
                    .FirstOrDefault(candidate => string.Equals(candidate.Id, role, StringComparison.OrdinalIgnoreCase))
            })
            .Where(candidate => candidate.Role is not null)
            .OrderBy(candidate => candidate.CreatureId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
            return null;

        var totalWeight = candidates.Sum(candidate => Math.Max(0.001f, candidate.Role!.Weight));
        var roll = (float)rng.NextDouble() * totalWeight;
        foreach (var candidate in candidates)
        {
            roll -= Math.Max(0.001f, candidate.Role!.Weight);
            if (roll <= 0f)
                return candidate.CreatureId;
        }

        return candidates[^1].CreatureId;
    }

    private static bool HasFactionRole(CreatureContentDefinition creature, string roleId)
        => (creature.Society?.FactionRoles ?? Array.Empty<CreatureFactionRoleContentDefinition>())
            .Any(candidate => string.Equals(candidate.Id, roleId, StringComparison.OrdinalIgnoreCase));
}

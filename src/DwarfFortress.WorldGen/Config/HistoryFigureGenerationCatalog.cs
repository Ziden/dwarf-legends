using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.WorldGen.Content;

namespace DwarfFortress.WorldGen.Config;

public sealed class HistoryFigureGenerationContentConfig
{
    public List<HistoryProfessionProfileContentConfig> ProfessionProfiles { get; init; } = [];
    public List<HistoryProfessionSelectionRuleContentConfig> ProfessionSelectionRules { get; init; } = [];
    public List<string> DefaultProfessionIds { get; init; } = [];
    public List<string> DefaultNonDwarfProfessionIds { get; init; } = [];
    public List<SpeciesNamePoolContentConfig> SpeciesNamePools { get; init; } = [];
    public List<string> DefaultNamePool { get; init; } = [];
}

public sealed class HistoryProfessionProfileContentConfig
{
    public string Id { get; init; } = string.Empty;
    public List<string> LaborIds { get; init; } = [];
    public Dictionary<string, int> SkillLevels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> AttributeLevels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? LikedFoodId { get; init; }
    public string? DislikedFoodId { get; init; }
}

public sealed class HistoryProfessionSelectionRuleContentConfig
{
    public string? SpeciesDefId { get; init; }
    public string? SiteKindContains { get; init; }
    public int? MemberIndex { get; init; }
    public bool? FounderBias { get; init; }
    public List<string> ProfessionIds { get; init; } = [];
}

public sealed class SpeciesNamePoolContentConfig
{
    public string SpeciesDefId { get; init; } = string.Empty;
    public List<string> Names { get; init; } = [];
}

public static class HistoryFigureAttributeIds
{
    public const string Appetite = "appetite";
    public const string Stamina = "stamina";
    public const string Strength = "strength";
    public const string Focus = "focus";
    public const string Courage = "courage";
}

public sealed record HistoryProfessionProfile(
    string ProfessionId,
    IReadOnlyList<string> LaborIds,
    IReadOnlyDictionary<string, int> SkillLevels,
    IReadOnlyDictionary<string, int> AttributeLevels,
    string? LikedFoodId,
    string? DislikedFoodId);

public sealed class HistoryFigureGenerationCatalog
{
    private const string MilitiaProfessionId = "militia";

    private readonly IReadOnlyDictionary<string, HistoryProfessionProfile> _profilesById;
    private readonly IReadOnlyList<HistoryProfessionSelectionRule> _selectionRules;
    private readonly IReadOnlyList<string> _defaultProfessionIds;
    private readonly IReadOnlyList<string> _defaultNonDwarfProfessionIds;
    private readonly string? _defaultPlayableSpeciesDefId;
    private readonly bool _hasExplicitDefaultProfessionIds;
    private readonly bool _hasExplicitDefaultNonDwarfProfessionIds;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _speciesDefaultProfessionIds;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _speciesNamePools;
    private readonly IReadOnlyList<string> _defaultNamePool;

    private HistoryFigureGenerationCatalog(
        IReadOnlyDictionary<string, HistoryProfessionProfile> profilesById,
        IReadOnlyList<HistoryProfessionSelectionRule> selectionRules,
        IReadOnlyList<string> defaultProfessionIds,
        IReadOnlyList<string> defaultNonDwarfProfessionIds,
        string? defaultPlayableSpeciesDefId,
        bool hasExplicitDefaultProfessionIds,
        bool hasExplicitDefaultNonDwarfProfessionIds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> speciesDefaultProfessionIds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> speciesNamePools,
        IReadOnlyList<string> defaultNamePool)
    {
        _profilesById = profilesById;
        _selectionRules = selectionRules;
        _defaultProfessionIds = defaultProfessionIds;
        _defaultNonDwarfProfessionIds = defaultNonDwarfProfessionIds;
        _defaultPlayableSpeciesDefId = defaultPlayableSpeciesDefId;
        _hasExplicitDefaultProfessionIds = hasExplicitDefaultProfessionIds;
        _hasExplicitDefaultNonDwarfProfessionIds = hasExplicitDefaultNonDwarfProfessionIds;
        _speciesDefaultProfessionIds = speciesDefaultProfessionIds;
        _speciesNamePools = speciesNamePools;
        _defaultNamePool = defaultNamePool;
    }

    public static HistoryFigureGenerationCatalog Create(HistoryFigureGenerationContentConfig? config, SharedContentCatalog? sharedContent = null)
    {
        var profilesById = CreateDefaultProfiles();
        if (config is not null)
        {
            foreach (var profileConfig in config.ProfessionProfiles)
            {
                if (string.IsNullOrWhiteSpace(profileConfig.Id))
                    throw new InvalidOperationException("History profession profile is missing an id.");

                profilesById[profileConfig.Id] = NormalizeProfile(profileConfig);
            }
        }

        if (profilesById.Count == 0)
            throw new InvalidOperationException("History figure generation requires at least one profession profile.");

        var hasExplicitDefaultProfessionIds = config?.DefaultProfessionIds.Count > 0;
        var defaultProfessionIds = NormalizeProfessionIds(
            config?.DefaultProfessionIds,
            profilesById,
            configPath: "historyFigures.defaultProfessionIds");
        if (defaultProfessionIds.Count == 0)
            defaultProfessionIds = profilesById.Values.Select(profile => profile.ProfessionId).ToArray();

        var hasExplicitDefaultNonDwarfProfessionIds = config?.DefaultNonDwarfProfessionIds.Count > 0;
        var defaultNonDwarfProfessionIds = NormalizeProfessionIds(
            config?.DefaultNonDwarfProfessionIds,
            profilesById,
            configPath: "historyFigures.defaultNonDwarfProfessionIds");
        if (defaultNonDwarfProfessionIds.Count == 0)
        {
            defaultNonDwarfProfessionIds = profilesById.ContainsKey(MilitiaProfessionId)
                ? [MilitiaProfessionId]
                : defaultProfessionIds;
        }

        var defaultPlayableSpeciesDefId = sharedContent is null
            ? null
            : new ContentQueryService(sharedContent).ResolveDefaultPlayableCreatureDefId();
        var speciesDefaultProfessionIds = CreateSpeciesDefaultProfessionIds(sharedContent, profilesById);
        var creatureSelectionRules = CreateCreatureSelectionRules(sharedContent, profilesById);
        var selectionRules = new List<HistoryProfessionSelectionRule>();
        if (config?.ProfessionSelectionRules is { Count: > 0 })
        {
            selectionRules.AddRange(NormalizeSelectionRules(config.ProfessionSelectionRules, profilesById));
            selectionRules.AddRange(creatureSelectionRules);
        }
        else
        {
            selectionRules.AddRange(creatureSelectionRules);
            selectionRules.AddRange(CreateDefaultSelectionRules(profilesById, defaultPlayableSpeciesDefId));
        }

        var speciesNamePools = CreateDefaultNamePools();
        foreach (var (speciesDefId, names) in CreateSpeciesNamePools(sharedContent))
            speciesNamePools[speciesDefId] = names;
        if (config is not null)
        {
            foreach (var poolConfig in config.SpeciesNamePools)
            {
                if (string.IsNullOrWhiteSpace(poolConfig.SpeciesDefId))
                    throw new InvalidOperationException("History species name pool is missing a speciesDefId.");

                var names = NormalizeNamePool(poolConfig.Names, $"historyFigures.speciesNamePools[{poolConfig.SpeciesDefId}]");
                speciesNamePools[poolConfig.SpeciesDefId] = names;
            }
        }

        var defaultNamePool = config is not null && config.DefaultNamePool.Count > 0
            ? NormalizeNamePool(config.DefaultNamePool, "historyFigures.defaultNamePool")
            : GetDefaultNamePool(speciesNamePools, defaultPlayableSpeciesDefId);

        return new HistoryFigureGenerationCatalog(
            profilesById,
            selectionRules,
            defaultProfessionIds,
            defaultNonDwarfProfessionIds,
            defaultPlayableSpeciesDefId,
            hasExplicitDefaultProfessionIds,
            hasExplicitDefaultNonDwarfProfessionIds,
            speciesDefaultProfessionIds,
            speciesNamePools,
            defaultNamePool);
    }

    public HistoryProfessionProfile ResolveProfession(string? speciesDefId, string? siteKind, int memberIndex, bool founderBias, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        var professionIds = SelectProfessionIds(speciesDefId, siteKind, memberIndex, founderBias);
        var chosenId = professionIds[rng.Next(professionIds.Count)];
        return _profilesById[chosenId];
    }

    public string ResolveFigureName(string? speciesDefId, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        if (!string.IsNullOrWhiteSpace(speciesDefId) &&
            _speciesNamePools.TryGetValue(speciesDefId, out var speciesNames) &&
            speciesNames.Count > 0)
        {
            return speciesNames[rng.Next(speciesNames.Count)];
        }

        return _defaultNamePool[rng.Next(_defaultNamePool.Count)];
    }

    private IReadOnlyList<string> SelectProfessionIds(string? speciesDefId, string? siteKind, int memberIndex, bool founderBias)
    {
        foreach (var rule in _selectionRules)
        {
            if (!rule.Matches(speciesDefId, siteKind, memberIndex, founderBias))
                continue;

            return rule.ProfessionIds;
        }

        var isDefaultPlayableSpecies =
            !string.IsNullOrWhiteSpace(_defaultPlayableSpeciesDefId) &&
            string.Equals(speciesDefId, _defaultPlayableSpeciesDefId, StringComparison.OrdinalIgnoreCase);

        if (isDefaultPlayableSpecies && _hasExplicitDefaultProfessionIds)
            return _defaultProfessionIds;

        if (!isDefaultPlayableSpecies && _hasExplicitDefaultNonDwarfProfessionIds && _defaultNonDwarfProfessionIds.Count > 0)
            return _defaultNonDwarfProfessionIds;

        if (!string.IsNullOrWhiteSpace(speciesDefId) &&
            _speciesDefaultProfessionIds.TryGetValue(speciesDefId, out var speciesProfessionIds) &&
            speciesProfessionIds.Count > 0)
        {
            return speciesProfessionIds;
        }

        if (!isDefaultPlayableSpecies &&
            _defaultNonDwarfProfessionIds.Count > 0)
        {
            return _defaultNonDwarfProfessionIds;
        }

        return _defaultProfessionIds;
    }

    private static Dictionary<string, HistoryProfessionProfile> CreateDefaultProfiles()
    {
        return new Dictionary<string, HistoryProfessionProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["miner"] = new(
                "miner",
                ["mining", "hauling"],
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["mining"] = 3 },
                CreateAttributeLevels(
                    (HistoryFigureAttributeIds.Strength, 5),
                    (HistoryFigureAttributeIds.Focus, 4)),
                LikedFoodId: "meal",
                DislikedFoodId: "drink"),
            ["woodworker"] = new(
                "woodworker",
                ["wood_cutting", "carpentry", "hauling"],
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["wood_cutting"] = 2,
                    ["carpentry"] = 2,
                },
                CreateAttributeLevels((HistoryFigureAttributeIds.Stamina, 4)),
                LikedFoodId: "apple",
                DislikedFoodId: "stone_tuber"),
            ["crafter"] = new(
                "crafter",
                ["crafting", "hauling"],
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["crafting"] = 3 },
                CreateAttributeLevels((HistoryFigureAttributeIds.Focus, 4)),
                LikedFoodId: "fig",
                DislikedFoodId: "drink"),
            ["mason"] = new(
                "mason",
                ["construction", "masonry", "hauling"],
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["construction"] = 2,
                    ["masonry"] = 2,
                },
                CreateAttributeLevels((HistoryFigureAttributeIds.Strength, 4)),
                LikedFoodId: "meal",
                DislikedFoodId: "apple"),
            ["farmer"] = new(
                "farmer",
                ["farming", "hauling"],
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["farming"] = 3 },
                CreateAttributeLevels(
                    (HistoryFigureAttributeIds.Stamina, 4),
                    (HistoryFigureAttributeIds.Appetite, 2)),
                LikedFoodId: "sunroot_bulb",
                DislikedFoodId: "drink"),
            ["brewer"] = new(
                "brewer",
                ["brewing", "hauling"],
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["brewing"] = 3 },
                CreateAttributeLevels((HistoryFigureAttributeIds.Stamina, 4)),
                LikedFoodId: "drink",
                DislikedFoodId: "marsh_reed_shoot"),
            ["cook"] = new(
                "cook",
                ["cooking", "hauling"],
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["cooking"] = 3 },
                CreateAttributeLevels((HistoryFigureAttributeIds.Appetite, 5)),
                LikedFoodId: "meal",
                DislikedFoodId: "stone_tuber"),
            ["hauler"] = new(
                "hauler",
                ["hauling"],
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["hauling"] = 2 },
                CreateAttributeLevels(
                    (HistoryFigureAttributeIds.Strength, 4),
                    (HistoryFigureAttributeIds.Stamina, 4)),
                LikedFoodId: "meal",
                DislikedFoodId: "marsh_reed_shoot"),
            ["militia"] = new(
                "militia",
                ["military", "hauling"],
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["military"] = 3 },
                CreateAttributeLevels(
                    (HistoryFigureAttributeIds.Strength, 4),
                    (HistoryFigureAttributeIds.Courage, 2)),
                LikedFoodId: "drink",
                DislikedFoodId: "apple"),
            ["peasant"] = new(
                "peasant",
                ["hauling", "misc"],
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                CreateAttributeLevels(),
                LikedFoodId: "meal",
                DislikedFoodId: "drink"),
        };
    }

    private static Dictionary<string, int> CreateAttributeLevels(params (string AttributeId, int Level)[] levels)
    {
        var attributeLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (attributeId, level) in levels)
            attributeLevels[attributeId] = Math.Clamp(level, 1, 5);

        return attributeLevels;
    }

    private static IReadOnlyList<HistoryProfessionSelectionRule> CreateDefaultSelectionRules(
        IReadOnlyDictionary<string, HistoryProfessionProfile> profilesById,
        string? defaultPlayableSpeciesDefId)
    {
        var speciesDefId = string.IsNullOrWhiteSpace(defaultPlayableSpeciesDefId)
            ? null
            : defaultPlayableSpeciesDefId;

        return
        [
            CreateRule(
                speciesDefId: speciesDefId,
                siteKindContains: null,
                memberIndex: 0,
                founderBias: true,
                professionIds: ["miner", "woodworker", "crafter", "mason"],
                profilesById),
            CreateRule(
                speciesDefId: speciesDefId,
                siteKindContains: "watch",
                memberIndex: 0,
                founderBias: null,
                professionIds: [MilitiaProfessionId],
                profilesById),
            CreateRule(
                speciesDefId: speciesDefId,
                siteKindContains: "shrine",
                memberIndex: 0,
                founderBias: null,
                professionIds: ["brewer"],
                profilesById),
            CreateRule(
                speciesDefId: speciesDefId,
                siteKindContains: "hamlet",
                memberIndex: 0,
                founderBias: null,
                professionIds: ["farmer"],
                profilesById),
        ];
    }

    private static Dictionary<string, IReadOnlyList<string>> CreateDefaultNamePools()
    {
        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["goblin"] = ["Snaga", "Ghash", "Urgat", "Muzg", "Ronk", "Bagul", "Krosh", "Zuglar"],
            ["troll"] = ["Thrag", "Mog", "Bruk", "Gor", "Drosh"],
        };
    }

    private static string[] GetDefaultNamePool(
        IReadOnlyDictionary<string, IReadOnlyList<string>> speciesNamePools,
        string? defaultPlayableSpeciesDefId)
    {
        if (!string.IsNullOrWhiteSpace(defaultPlayableSpeciesDefId) &&
            speciesNamePools.TryGetValue(defaultPlayableSpeciesDefId, out var names) &&
            names.Count > 0)
        {
            return [.. names];
        }

        return ["Urist", "Bomrek", "Domas", "Mistem", "Atir", "Stukos", "Kadol", "Mosus", "Meng", "Rigoth", "Sodel", "Vucar"];
    }

    private static HistoryProfessionProfile NormalizeProfile(HistoryProfessionProfileContentConfig config)
    {
        var laborIds = config.LaborIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var skillLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (skillId, level) in config.SkillLevels)
        {
            if (string.IsNullOrWhiteSpace(skillId))
                continue;

            skillLevels[skillId] = Math.Max(0, level);
        }

        var attributeLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (attributeId, level) in config.AttributeLevels)
        {
            if (string.IsNullOrWhiteSpace(attributeId))
                continue;

            attributeLevels[attributeId] = Math.Clamp(level, 1, 5);
        }

        return new HistoryProfessionProfile(
            config.Id,
            laborIds,
            skillLevels,
            attributeLevels,
            string.IsNullOrWhiteSpace(config.LikedFoodId) ? null : config.LikedFoodId,
            string.IsNullOrWhiteSpace(config.DislikedFoodId) ? null : config.DislikedFoodId);
    }

    private static IReadOnlyList<string> NormalizeProfessionIds(
        IEnumerable<string>? professionIds,
        IReadOnlyDictionary<string, HistoryProfessionProfile> profilesById,
        string configPath)
    {
        if (professionIds is null)
            return Array.Empty<string>();

        var normalized = professionIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var professionId in normalized)
        {
            if (!profilesById.ContainsKey(professionId))
                throw new InvalidOperationException($"{configPath} references unknown history profession '{professionId}'.");
        }

        return normalized;
    }

    private static IReadOnlyList<HistoryProfessionSelectionRule> NormalizeSelectionRules(
        IEnumerable<HistoryProfessionSelectionRuleContentConfig> rules,
        IReadOnlyDictionary<string, HistoryProfessionProfile> profilesById)
    {
        var normalized = new List<HistoryProfessionSelectionRule>();
        var index = 0;
        foreach (var rule in rules)
        {
            normalized.Add(CreateRule(
                rule.SpeciesDefId,
                rule.SiteKindContains,
                rule.MemberIndex,
                rule.FounderBias,
                rule.ProfessionIds,
                profilesById,
                $"historyFigures.professionSelectionRules[{index}]"));
            index++;
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> CreateSpeciesDefaultProfessionIds(
        SharedContentCatalog? sharedContent,
        IReadOnlyDictionary<string, HistoryProfessionProfile> profilesById)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (sharedContent is null)
            return result;

        foreach (var creature in sharedContent.Creatures.Values)
        {
            var defaultProfessionIds = NormalizeProfessionIds(
                creature.History?.DefaultProfessionIds,
                profilesById,
                $"creatures[{creature.Id}].history.defaultProfessionIds");
            if (defaultProfessionIds.Count == 0)
                continue;

            result[creature.Id] = defaultProfessionIds;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> CreateSpeciesNamePools(SharedContentCatalog? sharedContent)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (sharedContent is null)
            return result;

        foreach (var creature in sharedContent.Creatures.Values)
        {
            if (creature.History?.FigureNamePool is not { Count: > 0 })
                continue;

            result[creature.Id] = NormalizeNamePool(
                creature.History.FigureNamePool,
                $"creatures[{creature.Id}].history.figureNamePool");
        }

        return result;
    }

    private static IReadOnlyList<HistoryProfessionSelectionRule> CreateCreatureSelectionRules(
        SharedContentCatalog? sharedContent,
        IReadOnlyDictionary<string, HistoryProfessionProfile> profilesById)
    {
        if (sharedContent is null)
            return Array.Empty<HistoryProfessionSelectionRule>();

        var rules = new List<HistoryProfessionSelectionRule>();
        foreach (var creature in sharedContent.Creatures.Values.OrderBy(creature => creature.Id, StringComparer.OrdinalIgnoreCase))
        {
            var historyRules = creature.History?.ProfessionRules;
            if (historyRules is not { Count: > 0 })
                continue;

            for (var i = 0; i < historyRules.Count; i++)
            {
                var rule = historyRules[i];
                rules.Add(CreateRule(
                    creature.Id,
                    rule.SiteKindContains,
                    rule.MemberIndex,
                    rule.FounderBias,
                    rule.ProfessionIds ?? Array.Empty<string>(),
                    profilesById,
                    $"creatures[{creature.Id}].history.professionRules[{i}]"));
            }
        }

        return rules;
    }

    private static HistoryProfessionSelectionRule CreateRule(
        string? speciesDefId,
        string? siteKindContains,
        int? memberIndex,
        bool? founderBias,
        IEnumerable<string> professionIds,
        IReadOnlyDictionary<string, HistoryProfessionProfile> profilesById,
        string? configPath = null)
    {
        var normalizedProfessionIds = NormalizeProfessionIds(
            professionIds,
            profilesById,
            configPath ?? "historyFigures.professionSelectionRules");

        if (normalizedProfessionIds.Count == 0)
            throw new InvalidOperationException($"{configPath ?? "historyFigures.professionSelectionRules"} must reference at least one history profession.");

        return new HistoryProfessionSelectionRule(
            string.IsNullOrWhiteSpace(speciesDefId) ? null : speciesDefId,
            string.IsNullOrWhiteSpace(siteKindContains) ? null : siteKindContains,
            memberIndex,
            founderBias,
            normalizedProfessionIds);
    }

    private static string[] NormalizeNamePool(IEnumerable<string> names, string configPath)
    {
        var normalized = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
            throw new InvalidOperationException($"{configPath} must contain at least one non-empty name.");

        return normalized;
    }

    private sealed record HistoryProfessionSelectionRule(
        string? SpeciesDefId,
        string? SiteKindContains,
        int? MemberIndex,
        bool? FounderBias,
        IReadOnlyList<string> ProfessionIds)
    {
        public bool Matches(string? speciesDefId, string? siteKind, int memberIndex, bool founderBias)
        {
            if (!string.IsNullOrWhiteSpace(SpeciesDefId) &&
                !string.Equals(speciesDefId, SpeciesDefId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(SiteKindContains) &&
                (string.IsNullOrWhiteSpace(siteKind) ||
                 siteKind.IndexOf(SiteKindContains, StringComparison.OrdinalIgnoreCase) < 0))
            {
                return false;
            }

            if (MemberIndex.HasValue && MemberIndex.Value != memberIndex)
                return false;

            if (FounderBias.HasValue && FounderBias.Value != founderBias)
                return false;

            return true;
        }
    }
}

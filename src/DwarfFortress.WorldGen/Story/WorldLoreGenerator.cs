using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.WorldGen.Content;
using DwarfFortress.WorldGen.Ids;

namespace DwarfFortress.WorldGen.Story;

public static class WorldLoreGenerator
{
    public static WorldLoreState Generate(int seed, int width, int height, int depth, WorldLoreConfig? config = null, SharedContentCatalog? sharedContent = null)
    {
        var cfg = WorldLoreConfig.WithDefaults(config);
        var relationsCfg = cfg.Relations!;
        var evolutionCfg = cfg.SiteEvolution!;
        var contentQueries = new ContentQueryService(sharedContent ?? SharedContentCatalogLoader.LoadDefaultOrFallback());

        var rng = new Random(Hash(seed, width, height, depth));

        var state = new WorldLoreState
        {
            Seed = seed,
            Width = width,
            Height = height,
            Depth = depth,
            BiomeId = Pick(rng, cfg.Biomes),
        };

        state.RegionName = $"{Pick(rng, cfg.NameLeft)}{Pick(rng, cfg.NameRight)}";
        state.Factions = GenerateFactions(rng, cfg, contentQueries);
        state.FactionRelations = GenerateFactionRelations(rng, relationsCfg, state.Factions);
        state.Sites = GenerateSites(rng, cfg, state.Factions, width, height);
        InitializeSiteState(rng, evolutionCfg, state);

        SimulateHistory(rng, cfg.History!, relationsCfg, evolutionCfg, state);
        state.Threat = Math.Clamp(state.Threat, 0f, 1f);
        state.Prosperity = Math.Clamp(state.Prosperity, 0f, 1f);
        return state;
    }

    private static int Hash(int seed, int width, int height, int depth)
    {
        unchecked
        {
            var h = seed;
            h = (h * 397) ^ width;
            h = (h * 397) ^ height;
            h = (h * 397) ^ depth;
            return h;
        }
    }

    private static T Pick<T>(Random rng, IReadOnlyList<T> values)
        => values[rng.Next(values.Count)];

    private static List<FactionLoreState> GenerateFactions(Random rng, WorldLoreConfig config, ContentQueryService contentQueries)
    {
        var factions = new List<FactionLoreState>();

        foreach (var template in config.FactionTemplates)
        {
            var spawnChance = Clamp01(template.SpawnChance);
            if (spawnChance < 1f && rng.NextDouble() > spawnChance)
                continue;

            factions.Add(BuildFaction(rng, config, template, contentQueries));
        }

        if (factions.Count == 0 && config.FactionTemplates.Count > 0)
            factions.Add(BuildFaction(rng, config, config.FactionTemplates[0], contentQueries));

        return factions;
    }

    private static FactionLoreState BuildFaction(Random rng, WorldLoreConfig config, FactionTemplateConfig template, ContentQueryService contentQueries)
    {
        var primaryUnit = ResolvePrimaryUnit(rng, template, contentQueries);

        var motto = string.IsNullOrWhiteSpace(template.Motto)
            ? Pick(rng, config.MottoFragments)
            : template.Motto!;

        return new FactionLoreState
        {
            Id = template.Id,
            Name = ResolveName(rng, template.NamePattern, config),
            IsHostile = template.IsHostile,
            PrimaryUnitDefId = primaryUnit,
            Influence = RandomInRange(rng, template.InfluenceMin, template.InfluenceMax),
            Militarism = RandomInRange(rng, template.MilitarismMin, template.MilitarismMax),
            TradeFocus = RandomInRange(rng, template.TradeFocusMin, template.TradeFocusMax),
            Motto = motto,
        };
    }

    private static string ResolvePrimaryUnit(Random rng, FactionTemplateConfig template, ContentQueryService contentQueries)
    {
        var chooseAlternate = !string.IsNullOrWhiteSpace(template.AlternatePrimaryUnitDefId) ||
                              !string.IsNullOrWhiteSpace(template.AlternatePrimaryUnitRole);
        if (chooseAlternate && rng.NextDouble() < Clamp01(template.AlternatePrimaryChance))
        {
            var alternate = ResolveCreatureDefId(contentQueries, template.AlternatePrimaryUnitRole, template.AlternatePrimaryUnitDefId, rng);
            if (!string.IsNullOrWhiteSpace(alternate))
                return alternate;
        }

        var primary = ResolveCreatureDefId(contentQueries, template.PrimaryUnitRole, template.PrimaryUnitDefId, rng);
        if (!string.IsNullOrWhiteSpace(primary))
            return primary;

        var defaultRole = template.IsHostile ? FactionUnitRoleIds.HostilePrimary : FactionUnitRoleIds.CivilizedPrimary;
        var roleFallback = ResolveCreatureDefId(contentQueries, defaultRole, null, rng);
        if (!string.IsNullOrWhiteSpace(roleFallback))
            return roleFallback;

        var defaultCreature = template.IsHostile
            ? contentQueries.ResolveDefaultHostileCreatureDefId()
            : contentQueries.ResolveDefaultPlayableCreatureDefId();
        if (!string.IsNullOrWhiteSpace(defaultCreature))
            return defaultCreature;

        var anyCreature = contentQueries.Catalog.Creatures.Values
            .OrderBy(creature => creature.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?.Id;
        if (!string.IsNullOrWhiteSpace(anyCreature))
            return anyCreature;

        throw new InvalidOperationException("World lore generation requires at least one creature definition.");
    }

    private static string? ResolveCreatureDefId(ContentQueryService contentQueries, string? role, string? explicitCreatureDefId, Random rng)
    {
        var resolvedFromRole = contentQueries.ResolveFactionCreatureDefId(role, rng);
        if (!string.IsNullOrWhiteSpace(resolvedFromRole))
            return resolvedFromRole;

        return string.IsNullOrWhiteSpace(explicitCreatureDefId) ? null : explicitCreatureDefId;
    }

    private static string ResolveName(Random rng, string pattern, WorldLoreConfig config)
        => (string.IsNullOrWhiteSpace(pattern) ? "{left} faction" : pattern)
            .Replace("{left}", Pick(rng, config.NameLeft), StringComparison.Ordinal)
            .Replace("{right}", Pick(rng, config.NameRight), StringComparison.Ordinal);

    private static List<FactionRelationLoreState> GenerateFactionRelations(
        Random rng,
        FactionRelationConfig config,
        IReadOnlyList<FactionLoreState> factions)
    {
        var relations = new List<FactionRelationLoreState>();
        for (var i = 0; i < factions.Count; i++)
        {
            for (var j = i + 1; j < factions.Count; j++)
            {
                var a = factions[i];
                var b = factions[j];

                float score;
                if (a.IsHostile && b.IsHostile)
                {
                    score = RandomInRange(rng, config.InitialMutualHostileMin, config.InitialMutualHostileMax);
                }
                else if (a.IsHostile || b.IsHostile)
                {
                    score = RandomInRange(rng, config.InitialHostileMin, config.InitialHostileMax);
                }
                else
                {
                    score = RandomInRange(rng, config.InitialFriendlyMin, config.InitialFriendlyMax);
                }

                score = Math.Clamp(score, -1f, 1f);
                relations.Add(new FactionRelationLoreState
                {
                    FactionAId = a.Id,
                    FactionBId = b.Id,
                    Score = score,
                    Stance = ResolveStance(score, config),
                });
            }
        }

        return relations;
    }

    private static List<SiteLoreState> GenerateSites(
        Random rng,
        WorldLoreConfig config,
        IReadOnlyList<FactionLoreState> factions,
        int width,
        int height)
    {
        var sites = new List<SiteLoreState>();
        var count = 5 + rng.Next(0, 4); // 5..8

        for (var i = 0; i < count; i++)
        {
            var kind = Pick(rng, config.SiteKinds);
            var owner = ResolveSiteOwner(rng, kind, factions);

            var name = $"{Pick(rng, config.NameLeft)} {kind.Id}";
            sites.Add(new SiteLoreState
            {
                Id = $"site_{i:00}",
                Name = name,
                Kind = kind.Id,
                OwnerFactionId = owner.Id,
                X = rng.Next(2, Math.Max(3, width - 2)),
                Y = rng.Next(2, Math.Max(3, height - 2)),
                Z = 0,
                Summary = kind.Summary,
            });
        }

        return sites;
    }

    private static FactionLoreState ResolveSiteOwner(
        Random rng,
        SiteKindConfig siteKind,
        IReadOnlyList<FactionLoreState> factions)
    {
        if (factions.Count == 0)
            throw new InvalidOperationException("Cannot assign site owner because no factions were generated.");

        var rule = siteKind.OwnerRule?.Trim().ToLowerInvariant() ?? SiteOwnerRules.TradeFocused;
        return rule switch
        {
            SiteOwnerRules.Hostile => factions.FirstOrDefault(f => f.IsHostile) ?? factions[0],
            SiteOwnerRules.Random => factions[rng.Next(factions.Count)],
            _ => factions.OrderByDescending(f => f.Influence + f.TradeFocus).First(),
        };
    }

    private static void InitializeSiteState(Random rng, SiteEvolutionConfig config, WorldLoreState state)
    {
        foreach (var site in state.Sites)
        {
            var owner = state.Factions.FirstOrDefault(f => f.Id == site.OwnerFactionId);
            var development = RandomInRange(rng, config.InitialDevelopmentMin, config.InitialDevelopmentMax);
            var security = RandomInRange(rng, config.InitialSecurityMin, config.InitialSecurityMax);

            if (owner is not null)
            {
                development += owner.TradeFocus * 0.08f + owner.Influence * 0.04f;
                security += owner.Militarism * 0.10f;
                if (owner.IsHostile)
                    development -= 0.03f;
            }

            site.Development = Clamp01(development);
            site.Security = Clamp01(security);
            site.Status = ResolveSiteStatus(site.Development, site.Security, 0f, config);
        }
    }

    private static void SimulateHistory(
        Random rng,
        LoreHistoryConfig history,
        FactionRelationConfig relationsConfig,
        SiteEvolutionConfig siteConfig,
        WorldLoreState state)
    {
        state.Threat = RandomInRange(rng, history.BaseThreatMin, history.BaseThreatMax);
        state.Prosperity = RandomInRange(rng, history.BaseProsperityMin, history.BaseProsperityMax);
        state.SimulatedYears = NextInclusive(rng, history.SimulatedYearsMin, history.SimulatedYearsMax);

        var relationIndex = BuildRelationIndex(state.FactionRelations);

        var tradeYears = 0;
        var conflictYears = 0;

        var treatyWeight = Math.Max(0f, history.EventWeightTreaty);
        var raidWeight = Math.Max(0f, history.EventWeightRaid);
        var foundingWeight = Math.Max(0f, history.EventWeightFounding);
        var skirmishWeight = Math.Max(0f, history.EventWeightSkirmish);
        var crisisWeight = Math.Max(0f, history.EventWeightCrisis);
        var totalWeight = treatyWeight + raidWeight + foundingWeight + skirmishWeight + crisisWeight;
        if (totalWeight <= 0f)
            totalWeight = 1f;

        for (var year = 1; year <= state.SimulatedYears; year++)
        {
            var eventsThisYear = NextInclusive(rng, history.EventsPerYearMin, history.EventsPerYearMax);
            for (var i = 0; i < eventsThisYear; i++)
            {
                var roll = (float)rng.NextDouble() * totalWeight;

                if (roll < treatyWeight)
                {
                    if (AddTreatyEvent(rng, state, year, history, relationIndex, relationsConfig))
                        tradeYears++;
                    continue;
                }

                roll -= treatyWeight;
                if (roll < raidWeight)
                {
                    if (AddRaidEvent(rng, state, year, history, relationIndex, relationsConfig))
                        conflictYears++;
                    continue;
                }

                roll -= raidWeight;
                if (roll < foundingWeight)
                {
                    AddFoundingEvent(rng, state, year, history);
                    continue;
                }

                roll -= foundingWeight;
                if (roll < skirmishWeight)
                {
                    if (AddSkirmishEvent(rng, state, year, history, relationIndex, relationsConfig))
                        conflictYears++;
                    continue;
                }

                AddCrisisEvent(rng, state, year, history);
            }

            EvolveSitesForYear(rng, year, state, relationIndex, relationsConfig, siteConfig);
        }

        state.Threat += conflictYears * history.ThreatPerConflictYear;
        state.Prosperity += tradeYears * history.ProsperityPerTradeYear;
    }

    private static bool AddTreatyEvent(
        Random rng,
        WorldLoreState state,
        int year,
        LoreHistoryConfig history,
        Dictionary<string, FactionRelationLoreState> relations,
        FactionRelationConfig relationConfig)
    {
        var sides = state.Factions.OrderBy(_ => rng.Next()).Take(2).ToArray();
        if (sides.Length < 2) return false;

        state.Prosperity += history.TreatyProsperityDelta;
        state.Threat += history.TreatyThreatDelta;
        ShiftRelation(relations, sides[0].Id, sides[1].Id, relationConfig, relationConfig.TreatyDelta);

        state.History.Add(new HistoricalEventLoreState
        {
            Year = year,
            Type = HistoricalEventTypeIds.Treaty,
            Summary = $"{sides[0].Name} and {sides[1].Name} signed a border accord.",
            FactionAId = sides[0].Id,
            FactionBId = sides[1].Id,
        });
        return true;
    }

    private static bool AddRaidEvent(
        Random rng,
        WorldLoreState state,
        int year,
        LoreHistoryConfig history,
        Dictionary<string, FactionRelationLoreState> relations,
        FactionRelationConfig relationConfig)
    {
        var attacker = state.Factions
            .Where(f => f.IsHostile)
            .OrderByDescending(f => f.Militarism + rng.NextSingle() * 0.2f)
            .FirstOrDefault();
        var target = PickRaidTarget(rng, state, attacker, relations);
        if (attacker is null || target is null) return false;

        state.Threat += history.RaidThreatDelta;
        state.Prosperity += history.RaidProsperityDelta;

        if (!string.Equals(target.OwnerFactionId, attacker.Id, StringComparison.Ordinal))
            ShiftRelation(relations, attacker.Id, target.OwnerFactionId, relationConfig, relationConfig.RaidDelta);

        state.History.Add(new HistoricalEventLoreState
        {
            Year = year,
            Type = HistoricalEventTypeIds.Raid,
            Summary = $"{attacker.Name} raided {target.Name}.",
            FactionAId = attacker.Id,
            SiteId = target.Id,
        });
        return true;
    }

    private static SiteLoreState? PickRaidTarget(
        Random rng,
        WorldLoreState state,
        FactionLoreState? attacker,
        Dictionary<string, FactionRelationLoreState> relations)
    {
        if (attacker is null || state.Sites.Count == 0)
            return null;

        var candidates = state.Sites
            .Where(site => !string.Equals(site.OwnerFactionId, attacker.Id, StringComparison.Ordinal))
            .ToArray();
        if (candidates.Length == 0)
            return state.Sites[rng.Next(state.Sites.Count)];

        var weighted = new List<(SiteLoreState Site, float Weight)>(candidates.Length);
        foreach (var site in candidates)
        {
            var relationScore = GetRelationScore(relations, attacker.Id, site.OwnerFactionId);
            var hostility = (1f - relationScore) * 0.5f; // -1 => 1, +1 => 0
            var weight = 0.15f
                         + hostility * 0.65f
                         + site.Development * 0.20f
                         + (1f - site.Security) * 0.10f;

            if (site.Status == SiteStatusIds.Fortified)
                weight *= 0.85f;
            else if (site.Status == SiteStatusIds.Ruined)
                weight *= 1.10f;

            weighted.Add((site, Math.Max(0.01f, weight)));
        }

        return WeightedPick(rng, weighted);
    }

    private static void AddFoundingEvent(Random rng, WorldLoreState state, int year, LoreHistoryConfig history)
    {
        var founder = state.Factions
            .OrderByDescending(f => f.TradeFocus + f.Influence + rng.NextSingle() * 0.3f)
            .First();

        state.Prosperity += history.FoundingProsperityDelta;

        state.History.Add(new HistoricalEventLoreState
        {
            Year = year,
            Type = HistoricalEventTypeIds.Founding,
            Summary = $"{founder.Name} established a new trade outpost.",
            FactionAId = founder.Id,
        });
    }

    private static bool AddSkirmishEvent(
        Random rng,
        WorldLoreState state,
        int year,
        LoreHistoryConfig history,
        Dictionary<string, FactionRelationLoreState> relations,
        FactionRelationConfig relationConfig)
    {
        var sides = state.Factions.OrderBy(_ => rng.Next()).Take(2).ToArray();
        if (sides.Length < 2) return false;

        state.Threat += history.SkirmishThreatDelta;
        state.Prosperity += history.SkirmishProsperityDelta;
        ShiftRelation(relations, sides[0].Id, sides[1].Id, relationConfig, relationConfig.SkirmishDelta);

        state.History.Add(new HistoricalEventLoreState
        {
            Year = year,
            Type = HistoricalEventTypeIds.Skirmish,
            Summary = $"{sides[0].Name} clashed with {sides[1].Name} over caravan rights.",
            FactionAId = sides[0].Id,
            FactionBId = sides[1].Id,
        });
        return true;
    }

    private static void AddCrisisEvent(Random rng, WorldLoreState state, int year, LoreHistoryConfig history)
    {
        var site = state.Sites.OrderBy(_ => rng.Next()).FirstOrDefault();
        if (site is null) return;

        state.Threat += history.CrisisThreatDelta;
        state.Prosperity += history.CrisisProsperityDelta;

        var text = rng.NextDouble() < 0.5
            ? $"{site.Name} suffered a famine season."
            : $"{site.Name} was struck by a mysterious blight.";

        state.History.Add(new HistoricalEventLoreState
        {
            Year = year,
            Type = HistoricalEventTypeIds.Crisis,
            Summary = text,
            SiteId = site.Id,
        });
    }

    private static void EvolveSitesForYear(
        Random rng,
        int year,
        WorldLoreState state,
        Dictionary<string, FactionRelationLoreState> relations,
        FactionRelationConfig relationConfig,
        SiteEvolutionConfig config)
    {
        foreach (var site in state.Sites)
        {
            var owner = state.Factions.FirstOrDefault(f => f.Id == site.OwnerFactionId);
            var hostilePressure = ComputeHostilePressure(owner, state.Factions, relations, relationConfig);

            var growthSignal =
                state.Prosperity * config.GrowthFromProsperityWeight
                + (owner?.TradeFocus ?? 0.4f) * config.GrowthFromTradeWeight
                + (owner?.Influence ?? 0.4f) * config.GrowthFromInfluenceWeight
                + state.Threat * config.GrowthFromThreatWeight
                + hostilePressure * config.GrowthFromHostilePressureWeight;

            var securitySignal =
                (owner?.Militarism ?? 0.35f) * config.SecurityFromMilitarismWeight
                + state.Prosperity * config.SecurityFromProsperityWeight
                + state.Threat * config.SecurityFromThreatWeight
                + hostilePressure * config.SecurityFromHostilePressureWeight;

            var oldStatus = site.Status;
            var devNoise = (rng.NextSingle() - 0.5f) * config.DevelopmentNoisePerYear;
            var secNoise = (rng.NextSingle() - 0.5f) * config.SecurityNoisePerYear;

            site.Development = Clamp01(site.Development + growthSignal * config.DevelopmentDriftPerYear + devNoise);
            site.Security = Clamp01(site.Security + securitySignal * config.SecurityDriftPerYear + secNoise);
            site.Status = ResolveSiteStatus(site.Development, site.Security, growthSignal, config);

            // Low-frequency site-state event to reflect long-form world changes.
            if (!string.Equals(oldStatus, site.Status, StringComparison.Ordinal) && year % 12 == 0)
            {
                state.History.Add(new HistoricalEventLoreState
                {
                    Year = year,
                    Type = "site_shift",
                    Summary = $"{site.Name} shifted from {oldStatus} to {site.Status}.",
                    SiteId = site.Id,
                });
            }
        }
    }

    private static float ComputeHostilePressure(
        FactionLoreState? owner,
        IReadOnlyList<FactionLoreState> factions,
        Dictionary<string, FactionRelationLoreState> relations,
        FactionRelationConfig relationConfig)
    {
        if (owner is null || factions.Count == 0)
            return 0f;

        float total = 0f;
        var count = 0;
        foreach (var faction in factions)
        {
            if (string.Equals(faction.Id, owner.Id, StringComparison.Ordinal))
                continue;
            if (!faction.IsHostile)
                continue;

            var relation = GetRelationScore(relations, owner.Id, faction.Id);
            var hostility = relation <= relationConfig.HostileThreshold
                ? 1f
                : (1f - relation) * 0.5f;
            var weight = 0.45f + faction.Militarism * 0.35f + faction.Influence * 0.20f;
            total += hostility * weight;
            count++;
        }

        if (count == 0)
            return 0f;
        return Clamp01(total / count);
    }

    private static string ResolveSiteStatus(
        float development,
        float security,
        float growthSignal,
        SiteEvolutionConfig config)
    {
        if (development <= config.RuinedDevelopmentThreshold && security <= config.RuinedSecurityThreshold)
            return SiteStatusIds.Ruined;
        if (development >= config.FortifiedDevelopmentThreshold && security >= config.FortifiedSecurityThreshold)
            return SiteStatusIds.Fortified;
        if (growthSignal >= config.GrowingThreshold)
            return SiteStatusIds.Growing;
        if (growthSignal <= config.DecliningThreshold)
            return SiteStatusIds.Declining;
        return SiteStatusIds.Stable;
    }

    private static Dictionary<string, FactionRelationLoreState> BuildRelationIndex(
        IEnumerable<FactionRelationLoreState> relations)
    {
        var index = new Dictionary<string, FactionRelationLoreState>(StringComparer.Ordinal);
        foreach (var relation in relations)
            index[RelationKey(relation.FactionAId, relation.FactionBId)] = relation;
        return index;
    }

    private static float GetRelationScore(
        IReadOnlyDictionary<string, FactionRelationLoreState> relations,
        string factionAId,
        string factionBId)
    {
        if (string.Equals(factionAId, factionBId, StringComparison.Ordinal))
            return 1f;

        var key = RelationKey(factionAId, factionBId);
        return relations.TryGetValue(key, out var relation) ? relation.Score : 0f;
    }

    private static void ShiftRelation(
        IDictionary<string, FactionRelationLoreState> relations,
        string factionAId,
        string factionBId,
        FactionRelationConfig config,
        float delta)
    {
        if (string.Equals(factionAId, factionBId, StringComparison.Ordinal))
            return;

        var key = RelationKey(factionAId, factionBId);
        if (!relations.TryGetValue(key, out var relation))
            return;

        relation.Score = Math.Clamp(relation.Score + delta, -1f, 1f);
        relation.Stance = ResolveStance(relation.Score, config);
    }

    private static string ResolveStance(float score, FactionRelationConfig config)
    {
        if (score >= config.AllyThreshold)
            return RelationStanceIds.Ally;
        if (score <= config.HostileThreshold)
            return RelationStanceIds.Hostile;
        return RelationStanceIds.Neutral;
    }

    private static string RelationKey(string factionAId, string factionBId)
        => string.CompareOrdinal(factionAId, factionBId) <= 0
            ? $"{factionAId}|{factionBId}"
            : $"{factionBId}|{factionAId}";

    private static SiteLoreState WeightedPick(Random rng, IReadOnlyList<(SiteLoreState Site, float Weight)> candidates)
    {
        var total = 0f;
        foreach (var (_, weight) in candidates)
            total += Math.Max(0.01f, weight);

        var roll = (float)rng.NextDouble() * total;
        foreach (var (site, weight) in candidates)
        {
            roll -= Math.Max(0.01f, weight);
            if (roll <= 0f)
                return site;
        }

        return candidates[^1].Site;
    }

    private static int NextInclusive(Random rng, int min, int max)
    {
        if (min > max) (min, max) = (max, min);
        return rng.Next(min, max + 1);
    }

    private static float RandomInRange(Random rng, float min, float max)
    {
        if (min > max) (min, max) = (max, min);
        return min + ((float)rng.NextDouble() * (max - min));
    }

    private static float Clamp01(float value)
        => value < 0f ? 0f : value > 1f ? 1f : value;
}

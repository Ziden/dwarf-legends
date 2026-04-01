using System.Collections.Generic;
using System.Linq;
using DwarfFortress.WorldGen.Ids;

namespace DwarfFortress.WorldGen.Story;

public sealed class WorldLoreConfig
{
    public string[] Biomes { get; set; } = [];
    public string[] NameLeft { get; set; } = [];
    public string[] NameRight { get; set; } = [];
    public string[] MottoFragments { get; set; } = [];

    public List<SiteKindConfig> SiteKinds { get; set; } = [];
    public List<FactionTemplateConfig> FactionTemplates { get; set; } = [];
    public LoreHistoryConfig? History { get; set; }
    public FactionRelationConfig? Relations { get; set; }
    public SiteEvolutionConfig? SiteEvolution { get; set; }

    public static WorldLoreConfig CreateDefault()
        => new()
        {
            Biomes =
            [
                MacroBiomeIds.TemperatePlains,
                MacroBiomeIds.ConiferForest,
                MacroBiomeIds.Highland,
                MacroBiomeIds.MistyMarsh,
                MacroBiomeIds.WindsweptSteppe,
            ],
            NameLeft =
            [
                "Ash", "Stone", "Iron", "Copper", "River", "Moss", "Cold", "Oak",
                "Rime", "Star", "Black", "Silver", "Red", "Deep", "Green"
            ],
            NameRight =
            [
                "vale", "reach", "march", "hollow", "heights", "fen", "ford", "ridge",
                "field", "basin", "expanse", "frontier", "wilds", "downs", "depths"
            ],
            MottoFragments =
            [
                "from stone, strength",
                "trade binds the valleys",
                "honor above hunger",
                "fear is a sharpened blade",
                "memory outlives steel",
                "all debts are remembered",
            ],
            SiteKinds =
            [
                new()
                {
                    Id = "fortress",
                    OwnerRule = SiteOwnerRules.TradeFocused,
                    Summary = "A fortified settlement controlling nearby routes.",
                },
                new()
                {
                    Id = "hamlet",
                    OwnerRule = SiteOwnerRules.TradeFocused,
                    Summary = "A small settlement surviving through local trade.",
                },
                new()
                {
                    Id = "ruin",
                    OwnerRule = SiteOwnerRules.Random,
                    Summary = "A collapsed site where old records are still recoverable.",
                },
                new()
                {
                    Id = "shrine",
                    OwnerRule = SiteOwnerRules.TradeFocused,
                    Summary = "A sacred place tied to old oaths and taboos.",
                },
                new()
                {
                    Id = "cave",
                    OwnerRule = SiteOwnerRules.Hostile,
                    Summary = "A dangerous site whose tunnels are only partly mapped.",
                },
                new()
                {
                    Id = "watchtower",
                    OwnerRule = SiteOwnerRules.TradeFocused,
                    Summary = "A military outpost surveying the frontier.",
                },
            ],
            FactionTemplates =
            [
                new()
                {
                    Id = "faction_dwarven_hold",
                    NamePattern = "{left}hold Compact",
                    IsHostile = false,
                    PrimaryUnitDefId = "dwarf",
                    InfluenceMin = 0.65f,
                    InfluenceMax = 0.65f,
                    MilitarismMin = 0.45f,
                    MilitarismMax = 0.65f,
                    TradeFocusMin = 0.55f,
                    TradeFocusMax = 0.85f,
                    SpawnChance = 1.0f,
                },
                new()
                {
                    Id = "faction_goblin_clan",
                    NamePattern = "Black Banner of {left}",
                    IsHostile = true,
                    PrimaryUnitDefId = "goblin",
                    AlternatePrimaryUnitDefId = "troll",
                    AlternatePrimaryChance = 0.20f,
                    InfluenceMin = 0.45f,
                    InfluenceMax = 0.75f,
                    MilitarismMin = 0.65f,
                    MilitarismMax = 0.95f,
                    TradeFocusMin = 0.10f,
                    TradeFocusMax = 0.25f,
                    SpawnChance = 1.0f,
                },
                new()
                {
                    Id = "faction_lowland_league",
                    NamePattern = "{left} Valley League",
                    IsHostile = false,
                    PrimaryUnitDefId = "dwarf",
                    InfluenceMin = 0.35f,
                    InfluenceMax = 0.60f,
                    MilitarismMin = 0.25f,
                    MilitarismMax = 0.45f,
                    TradeFocusMin = 0.55f,
                    TradeFocusMax = 0.90f,
                    SpawnChance = 1.0f,
                },
                new()
                {
                    Id = "faction_ashen_host",
                    NamePattern = "Ashen Host of {left}",
                    IsHostile = true,
                    PrimaryUnitDefId = "goblin",
                    InfluenceMin = 0.20f,
                    InfluenceMax = 0.45f,
                    MilitarismMin = 0.70f,
                    MilitarismMax = 0.90f,
                    TradeFocusMin = 0.05f,
                    TradeFocusMax = 0.20f,
                    SpawnChance = 0.45f,
                },
            ],
            History = new LoreHistoryConfig(),
            Relations = new FactionRelationConfig(),
            SiteEvolution = new SiteEvolutionConfig(),
        };

    public static WorldLoreConfig WithDefaults(WorldLoreConfig? source)
    {
        var defaults = CreateDefault();
        if (source is null)
            return defaults;

        var siteKinds = (source.SiteKinds is { Count: > 0 } ? source.SiteKinds : defaults.SiteKinds)
            .Where(site => !string.IsNullOrWhiteSpace(site.Id))
            .Select(CloneSiteKind)
            .ToList();
        if (siteKinds.Count == 0)
            siteKinds = defaults.SiteKinds.Select(CloneSiteKind).ToList();

        var factionTemplates = (source.FactionTemplates is { Count: > 0 } ? source.FactionTemplates : defaults.FactionTemplates)
            .Where(template => !string.IsNullOrWhiteSpace(template.Id))
            .Select(CloneTemplate)
            .ToList();
        if (factionTemplates.Count == 0)
            factionTemplates = defaults.FactionTemplates.Select(CloneTemplate).ToList();

        return new WorldLoreConfig
        {
            Biomes = PickOrDefault(source.Biomes, defaults.Biomes),
            NameLeft = PickOrDefault(source.NameLeft, defaults.NameLeft),
            NameRight = PickOrDefault(source.NameRight, defaults.NameRight),
            MottoFragments = PickOrDefault(source.MottoFragments, defaults.MottoFragments),
            SiteKinds = siteKinds,
            FactionTemplates = factionTemplates,
            History = LoreHistoryConfig.WithDefaults(source.History),
            Relations = FactionRelationConfig.WithDefaults(source.Relations),
            SiteEvolution = SiteEvolutionConfig.WithDefaults(source.SiteEvolution),
        };
    }

    private static string[] PickOrDefault(string[]? values, string[] fallback)
        => values is { Length: > 0 } ? values : fallback;

    private static SiteKindConfig CloneSiteKind(SiteKindConfig site)
        => new()
        {
            Id = site.Id,
            OwnerRule = string.IsNullOrWhiteSpace(site.OwnerRule) ? SiteOwnerRules.TradeFocused : site.OwnerRule,
            Summary = site.Summary,
        };

    private static FactionTemplateConfig CloneTemplate(FactionTemplateConfig template)
        => new()
        {
            Id = template.Id,
            NamePattern = template.NamePattern,
            IsHostile = template.IsHostile,
            PrimaryUnitDefId = string.IsNullOrWhiteSpace(template.PrimaryUnitDefId) ? "goblin" : template.PrimaryUnitDefId,
            AlternatePrimaryUnitDefId = template.AlternatePrimaryUnitDefId,
            AlternatePrimaryChance = template.AlternatePrimaryChance,
            InfluenceMin = template.InfluenceMin,
            InfluenceMax = template.InfluenceMax,
            MilitarismMin = template.MilitarismMin,
            MilitarismMax = template.MilitarismMax,
            TradeFocusMin = template.TradeFocusMin,
            TradeFocusMax = template.TradeFocusMax,
            SpawnChance = template.SpawnChance,
            Motto = template.Motto,
        };
}

public static class SiteOwnerRules
{
    public const string TradeFocused = "trade_focused";
    public const string Hostile = "hostile";
    public const string Random = "random";
}

public sealed class SiteKindConfig
{
    public string Id { get; set; } = "";
    public string OwnerRule { get; set; } = SiteOwnerRules.TradeFocused;
    public string Summary { get; set; } = "";
}

public sealed class FactionTemplateConfig
{
    public string Id { get; set; } = "";
    public string NamePattern { get; set; } = "{left} faction";
    public bool IsHostile { get; set; }
    public string PrimaryUnitDefId { get; set; } = "goblin";
    public string? AlternatePrimaryUnitDefId { get; set; }
    public float AlternatePrimaryChance { get; set; }
    public float InfluenceMin { get; set; }
    public float InfluenceMax { get; set; }
    public float MilitarismMin { get; set; }
    public float MilitarismMax { get; set; }
    public float TradeFocusMin { get; set; }
    public float TradeFocusMax { get; set; }
    public float SpawnChance { get; set; } = 1.0f;
    public string? Motto { get; set; }
}

public sealed class LoreHistoryConfig
{
    public int SimulatedYearsMin { get; set; } = 120;
    public int SimulatedYearsMax { get; set; } = 260;

    public int EventsPerYearMin { get; set; } = 1;
    public int EventsPerYearMax { get; set; } = 2;

    public float BaseThreatMin { get; set; } = 0.32f;
    public float BaseThreatMax { get; set; } = 0.47f;

    public float BaseProsperityMin { get; set; } = 0.36f;
    public float BaseProsperityMax { get; set; } = 0.58f;

    public float EventWeightTreaty { get; set; } = 0.22f;
    public float EventWeightRaid { get; set; } = 0.25f;
    public float EventWeightFounding { get; set; } = 0.19f;
    public float EventWeightSkirmish { get; set; } = 0.17f;
    public float EventWeightCrisis { get; set; } = 0.17f;

    public float TreatyProsperityDelta { get; set; } = 0.02f;
    public float TreatyThreatDelta { get; set; } = -0.01f;

    public float RaidThreatDelta { get; set; } = 0.035f;
    public float RaidProsperityDelta { get; set; } = -0.015f;

    public float FoundingProsperityDelta { get; set; } = 0.012f;

    public float SkirmishThreatDelta { get; set; } = 0.02f;
    public float SkirmishProsperityDelta { get; set; } = -0.008f;

    public float CrisisThreatDelta { get; set; } = 0.012f;
    public float CrisisProsperityDelta { get; set; } = -0.01f;

    public float ThreatPerConflictYear { get; set; } = 0.0025f;
    public float ProsperityPerTradeYear { get; set; } = 0.002f;

    public static LoreHistoryConfig WithDefaults(LoreHistoryConfig? source)
        => source is null
            ? new LoreHistoryConfig()
            : new LoreHistoryConfig
            {
                SimulatedYearsMin = source.SimulatedYearsMin,
                SimulatedYearsMax = source.SimulatedYearsMax,
                EventsPerYearMin = source.EventsPerYearMin,
                EventsPerYearMax = source.EventsPerYearMax,
                BaseThreatMin = source.BaseThreatMin,
                BaseThreatMax = source.BaseThreatMax,
                BaseProsperityMin = source.BaseProsperityMin,
                BaseProsperityMax = source.BaseProsperityMax,
                EventWeightTreaty = source.EventWeightTreaty,
                EventWeightRaid = source.EventWeightRaid,
                EventWeightFounding = source.EventWeightFounding,
                EventWeightSkirmish = source.EventWeightSkirmish,
                EventWeightCrisis = source.EventWeightCrisis,
                TreatyProsperityDelta = source.TreatyProsperityDelta,
                TreatyThreatDelta = source.TreatyThreatDelta,
                RaidThreatDelta = source.RaidThreatDelta,
                RaidProsperityDelta = source.RaidProsperityDelta,
                FoundingProsperityDelta = source.FoundingProsperityDelta,
                SkirmishThreatDelta = source.SkirmishThreatDelta,
                SkirmishProsperityDelta = source.SkirmishProsperityDelta,
                CrisisThreatDelta = source.CrisisThreatDelta,
                CrisisProsperityDelta = source.CrisisProsperityDelta,
                ThreatPerConflictYear = source.ThreatPerConflictYear,
                ProsperityPerTradeYear = source.ProsperityPerTradeYear,
            };
}

public sealed class FactionRelationConfig
{
    public float InitialFriendlyMin { get; set; } = 0.05f;
    public float InitialFriendlyMax { get; set; } = 0.45f;

    public float InitialHostileMin { get; set; } = -0.75f;
    public float InitialHostileMax { get; set; } = -0.25f;

    public float InitialMutualHostileMin { get; set; } = -0.45f;
    public float InitialMutualHostileMax { get; set; } = 0.15f;

    public float TreatyDelta { get; set; } = 0.22f;
    public float SkirmishDelta { get; set; } = -0.20f;
    public float RaidDelta { get; set; } = -0.28f;

    public float AllyThreshold { get; set; } = 0.35f;
    public float HostileThreshold { get; set; } = -0.35f;

    public static FactionRelationConfig WithDefaults(FactionRelationConfig? source)
        => source is null
            ? new FactionRelationConfig()
            : new FactionRelationConfig
            {
                InitialFriendlyMin = source.InitialFriendlyMin,
                InitialFriendlyMax = source.InitialFriendlyMax,
                InitialHostileMin = source.InitialHostileMin,
                InitialHostileMax = source.InitialHostileMax,
                InitialMutualHostileMin = source.InitialMutualHostileMin,
                InitialMutualHostileMax = source.InitialMutualHostileMax,
                TreatyDelta = source.TreatyDelta,
                SkirmishDelta = source.SkirmishDelta,
                RaidDelta = source.RaidDelta,
                AllyThreshold = source.AllyThreshold,
                HostileThreshold = source.HostileThreshold,
            };
}

public sealed class SiteEvolutionConfig
{
    public float InitialDevelopmentMin { get; set; } = 0.35f;
    public float InitialDevelopmentMax { get; set; } = 0.65f;
    public float InitialSecurityMin { get; set; } = 0.30f;
    public float InitialSecurityMax { get; set; } = 0.65f;

    public float DevelopmentDriftPerYear { get; set; } = 0.015f;
    public float SecurityDriftPerYear { get; set; } = 0.012f;
    public float DevelopmentNoisePerYear { get; set; } = 0.01f;
    public float SecurityNoisePerYear { get; set; } = 0.01f;

    public float GrowthFromProsperityWeight { get; set; } = 0.45f;
    public float GrowthFromTradeWeight { get; set; } = 0.20f;
    public float GrowthFromInfluenceWeight { get; set; } = 0.15f;
    public float GrowthFromThreatWeight { get; set; } = -0.30f;
    public float GrowthFromHostilePressureWeight { get; set; } = -0.20f;

    public float SecurityFromMilitarismWeight { get; set; } = 0.25f;
    public float SecurityFromProsperityWeight { get; set; } = 0.10f;
    public float SecurityFromThreatWeight { get; set; } = -0.12f;
    public float SecurityFromHostilePressureWeight { get; set; } = -0.15f;

    public float GrowingThreshold { get; set; } = 0.12f;
    public float DecliningThreshold { get; set; } = -0.12f;

    public float FortifiedDevelopmentThreshold { get; set; } = 0.72f;
    public float FortifiedSecurityThreshold { get; set; } = 0.55f;
    public float RuinedDevelopmentThreshold { get; set; } = 0.20f;
    public float RuinedSecurityThreshold { get; set; } = 0.25f;

    public static SiteEvolutionConfig WithDefaults(SiteEvolutionConfig? source)
        => source is null
            ? new SiteEvolutionConfig()
            : new SiteEvolutionConfig
            {
                InitialDevelopmentMin = source.InitialDevelopmentMin,
                InitialDevelopmentMax = source.InitialDevelopmentMax,
                InitialSecurityMin = source.InitialSecurityMin,
                InitialSecurityMax = source.InitialSecurityMax,
                DevelopmentDriftPerYear = source.DevelopmentDriftPerYear,
                SecurityDriftPerYear = source.SecurityDriftPerYear,
                DevelopmentNoisePerYear = source.DevelopmentNoisePerYear,
                SecurityNoisePerYear = source.SecurityNoisePerYear,
                GrowthFromProsperityWeight = source.GrowthFromProsperityWeight,
                GrowthFromTradeWeight = source.GrowthFromTradeWeight,
                GrowthFromInfluenceWeight = source.GrowthFromInfluenceWeight,
                GrowthFromThreatWeight = source.GrowthFromThreatWeight,
                GrowthFromHostilePressureWeight = source.GrowthFromHostilePressureWeight,
                SecurityFromMilitarismWeight = source.SecurityFromMilitarismWeight,
                SecurityFromProsperityWeight = source.SecurityFromProsperityWeight,
                SecurityFromThreatWeight = source.SecurityFromThreatWeight,
                SecurityFromHostilePressureWeight = source.SecurityFromHostilePressureWeight,
                GrowingThreshold = source.GrowingThreshold,
                DecliningThreshold = source.DecliningThreshold,
                FortifiedDevelopmentThreshold = source.FortifiedDevelopmentThreshold,
                FortifiedSecurityThreshold = source.FortifiedSecurityThreshold,
                RuinedDevelopmentThreshold = source.RuinedDevelopmentThreshold,
                RuinedSecurityThreshold = source.RuinedSecurityThreshold,
            };
}

using System.Collections.Generic;

namespace DwarfFortress.WorldGen.Content;

public static class ContentFamilies
{
    public const string Plants = "plants";
    public const string TreeSpecies = "tree_species";
    public const string Materials = "materials";
    public const string Creatures = "creatures";
}

public static class ContentFormRoles
{
    public const string Boulder = "boulder";
    public const string Ore = "ore";
    public const string Bar = "bar";
    public const string Log = "log";
    public const string Plank = "plank";
}

public static class ContentCreatureDietIds
{
    public const string Herbivore = "herbivore";
    public const string Carnivore = "carnivore";
    public const string Omnivore = "omnivore";
    public const string AquaticGrazer = "aquatic_grazer";
}

public static class ContentCreatureMovementModeIds
{
    public const string Land = "land";
    public const string Swimmer = "swimmer";
    public const string Aquatic = "aquatic";
}

public static class ContentCreatureVisualProfileIds
{
    public const string Dwarf = "dwarf";
    public const string Goblin = "goblin";
    public const string Troll = "troll";
    public const string Elk = "elk";
    public const string GiantCarp = "giant_carp";
    public const string Cat = "cat";
    public const string Dog = "dog";
}

public static class ContentCreatureWaterEffectStyleIds
{
    public const string Default = "default";
    public const string Large = "large";
    public const string Pet = "pet";
    public const string Aquatic = "aquatic";
}

public static class ContentRoots
{
    public const string Legacy = "Legacy";
    public const string Core = "Core";
    public const string Game = "Game";
}

public sealed record ContentShadowRecord(
    string Family,
    string Id,
    string PreviousRoot,
    string NewRoot,
    string PreviousPath,
    string NewPath);

public sealed class ContentLoadReport
{
    public List<ContentShadowRecord> ShadowedEntries { get; } = [];
}

public sealed record NutritionContent(
    float Carbs = 0.4f,
    float Protein = 0.4f,
    float Fat = 0.3f,
    float Vitamins = 0.4f);

public sealed record ContentItemDefinition(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Tags,
    bool Stackable = false,
    int MaxStack = 1,
    float Weight = 1.0f,
    int BaseValue = 1,
    NutritionContent? Nutrition = null,
    string SourceFamily = "",
    string SourceRoot = "",
    string SourcePath = "");

public sealed record MaterialFormDefinition(
    string Role,
    ContentItemDefinition Item);

public sealed record MaterialContentDefinition(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Tags,
    float Hardness = 1.0f,
    float MeltingPoint = float.MaxValue,
    float Density = 1.0f,
    int Value = 1,
    string? Color = null,
    IReadOnlyDictionary<string, MaterialFormDefinition>? Forms = null,
    string SourceRoot = "",
    string SourcePath = "");

public sealed record TreeSpeciesContentDefinition(
    string Id,
    string DisplayName,
    string? WoodMaterialId = null,
    IReadOnlyList<string>? Tags = null,
    string SourceRoot = "",
    string SourcePath = "");

public enum PlantContentHostKind : byte
{
    Ground = 0,
    Tree = 1,
}

public sealed record PlantContentDefinition(
    string Id,
    string DisplayName,
    PlantContentHostKind HostKind,
    IReadOnlyList<string> AllowedBiomeIds,
    IReadOnlyList<string> AllowedGroundTileDefIds,
    IReadOnlyList<string> SupportedTreeSpeciesIds,
    float MinMoisture,
    float MaxMoisture,
    float MinTerrain,
    float MaxTerrain,
    bool PrefersNearWater,
    bool PrefersFarFromWater,
    byte MaxGrowthStage,
    float SecondsPerStage,
    float FruitingCycleSeconds,
    float SeedSpreadChance,
    int SeedSpreadRadiusMin,
    int SeedSpreadRadiusMax,
    float Energy,
    float Protein,
    float Vitamins,
    float Minerals,
    string? HarvestItemDefId = null,
    string? SeedItemDefId = null,
    string? FruitItemDefId = null,
    ContentItemDefinition? HarvestItem = null,
    ContentItemDefinition? SeedItem = null,
    ContentItemDefinition? FruitItem = null,
    string SourceRoot = "",
    string SourcePath = "");

public sealed record BodyPartContentDefinition(
    string Id,
    string DisplayName,
    float HitWeight = 1.0f,
    bool IsVital = false);

public sealed record CreatureSurfaceEcologyContentDefinition(
    IReadOnlyList<string> BiomeIds,
    float Weight = 1.0f,
    int MinGroup = 1,
    int MaxGroup = 1,
    bool RequiresWater = false,
    bool AvoidEmbarkCenter = true);

public sealed record CreatureCaveEcologyContentDefinition(
    IReadOnlyList<int> Layers,
    float Weight = 1.0f,
    int MinGroup = 1,
    int MaxGroup = 1,
    bool RequiresWater = false,
    bool AvoidEmbarkCenter = true);

public sealed record CreatureEcologyContentDefinition(
    IReadOnlyList<CreatureSurfaceEcologyContentDefinition>? SurfaceWildlife = null,
    IReadOnlyList<CreatureCaveEcologyContentDefinition>? CaveWildlife = null);

public sealed record CreatureHistoryProfessionRuleContentDefinition(
    string? SiteKindContains = null,
    int? MemberIndex = null,
    bool? FounderBias = null,
    IReadOnlyList<string>? ProfessionIds = null);

public sealed record CreatureHistoryContentDefinition(
    IReadOnlyList<string>? FigureNamePool = null,
    IReadOnlyList<string>? DefaultProfessionIds = null,
    IReadOnlyList<CreatureHistoryProfessionRuleContentDefinition>? ProfessionRules = null);

public sealed record CreatureFactionRoleContentDefinition(
    string Id,
    float Weight = 1.0f);

public sealed record CreatureSocietyContentDefinition(
    IReadOnlyList<CreatureFactionRoleContentDefinition>? FactionRoles = null);

public sealed record CreatureDeathDropContentDefinition(
    string ItemDefId,
    int Quantity = 1,
    string? MaterialId = null);

public sealed record CreatureVisualContentDefinition(
    string? ProceduralProfileId = null,
    string? WaterEffectStyleId = null,
    string? ViewerColor = null,
    string? SpriteSheet = null,
    int? SpriteColumn = null,
    int? SpriteRow = null);

public sealed record CreatureContentDefinition(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Tags,
    float BaseSpeed = 1.0f,
    float BaseStrength = 10.0f,
    float BaseToughness = 10.0f,
    float MaxHealth = 100f,
    bool IsPlayable = false,
    bool IsSapient = false,
    bool IsHostile = false,
    bool? CanGroom = null,
    string? DietId = null,
    string? MovementModeId = null,
    IReadOnlyList<BodyPartContentDefinition>? BodyParts = null,
    IReadOnlyList<string>? NaturalLabors = null,
    CreatureEcologyContentDefinition? Ecology = null,
    CreatureHistoryContentDefinition? History = null,
    IReadOnlyList<CreatureDeathDropContentDefinition>? DeathDrops = null,
    CreatureSocietyContentDefinition? Society = null,
    CreatureVisualContentDefinition? Visuals = null,
    string SourceRoot = "",
    string SourcePath = "");

public sealed class SharedContentCatalog
{
    public SharedContentCatalog(
        IReadOnlyDictionary<string, MaterialContentDefinition> materials,
        IReadOnlyDictionary<string, ContentItemDefinition> items,
        IReadOnlyDictionary<string, PlantContentDefinition> plants,
        IReadOnlyDictionary<string, TreeSpeciesContentDefinition> treeSpecies,
        IReadOnlyDictionary<string, CreatureContentDefinition> creatures,
        ContentLoadReport report)
    {
        Materials = materials;
        Items = items;
        Plants = plants;
        TreeSpecies = treeSpecies;
        Creatures = creatures;
        Report = report;
    }

    public IReadOnlyDictionary<string, MaterialContentDefinition> Materials { get; }
    public IReadOnlyDictionary<string, ContentItemDefinition> Items { get; }
    public IReadOnlyDictionary<string, PlantContentDefinition> Plants { get; }
    public IReadOnlyDictionary<string, TreeSpeciesContentDefinition> TreeSpecies { get; }
    public IReadOnlyDictionary<string, CreatureContentDefinition> Creatures { get; }
    public ContentLoadReport Report { get; }
}

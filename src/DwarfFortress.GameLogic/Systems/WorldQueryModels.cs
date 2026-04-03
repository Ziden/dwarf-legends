using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Jobs;

namespace DwarfFortress.GameLogic.Systems;

public sealed record GameTimeView(int Year, int Month, int Day, int Hour, string Season);
public sealed record TileView(
    int X,
    int Y,
    int Z,
    string TileDefId,
    string? MaterialId,
    string? TreeSpeciesId,
    bool IsPassable,
    bool IsDesignated,
    bool IsUnderConstruction,
    byte FluidLevel,
    string? FluidMaterialId,
    string? CoatingMaterialId,
    float CoatingAmount,
    bool IsAquifer,
    bool IsDamp,
    bool IsWarm,
    string? OreItemDefId,
    string? PlantDefId,
    byte PlantGrowthStage,
    byte PlantYieldLevel,
    byte PlantSeedLevel,
    bool IsVisible);
public sealed record NeedView(string Id, float Level);
public sealed record StatView(string Id, float Value);
public sealed record SkillView(string Id, int Level, float Xp, float XpForNextLevel);
public sealed record ThoughtView(string Id, string Description, float HappinessMod, float TimeLeft);
public enum EventLogLinkType
{
    Item,
    Entity,
}

public sealed record EventLogLinkTarget(int Id, EventLogLinkType Type, string DefId, string DisplayName, string? MaterialId = null);
public sealed record EventLogEntryView(string Message, Vec3i Position, string TimeLabel, EventLogLinkTarget? LinkedTarget = null);
public sealed record FortressAnnouncementView(int Sequence, FortressAnnouncementKind Kind, string Message, Vec3i Position, bool HasLocation, FortressAnnouncementSeverity Severity, string TimeLabel, int RepeatCount);
public sealed record WoundView(string BodyPartId, string Severity, bool IsBleeding);
public sealed record SubstanceView(string Id, float Concentration);
public sealed record JobView(int Id, string JobDefId, JobStatus Status, Vec3i TargetPos, float WorkProgress, string CurrentStep);
public sealed record DwarfAppearanceView(string HairType, string HairColor, string BeardType, string BeardColor, string EyeType, string NoseType, string MouthType, string FaceType);
public sealed record DwarfAttributeView(string Id, string DisplayName, int Level, string Label, string Category);
public sealed record DwarfProvenanceView(
    int WorldSeed,
    string? FigureId,
    string? FigureName,
    string? HouseholdId,
    string? HouseholdName,
    string? CivilizationId,
    string? CivilizationName,
    string? OriginSiteId,
    string? OriginSiteName,
    string? OriginSiteKind,
    string? BirthSiteId,
    string? BirthSiteName,
    string? MigrationWaveId,
    int? WorldX,
    int? WorldY,
    int? RegionX,
    int? RegionY);
public sealed record DwarfView(
    int Id,
    string Name,
    Vec3i Position,
    string ProfessionId,
    Mood Mood,
    float Happiness,
    float CurrentHealth,
    float MaxHealth,
    bool IsConscious,
    NeedView[] Needs,
    StatView[] Stats,
    SkillView[] Skills,
    ThoughtView[] Thoughts,
    WoundView[] Wounds,
    SubstanceView[] Substances,
    JobView? CurrentJob,
    ItemView[] CarriedItems,
    string[] EnabledLabors,
    DwarfAppearanceView Appearance,
    DwarfAttributeView[] Attributes,
    EventLogEntryView[] EventLog,
    DwarfProvenanceView? Provenance,
    ItemView? HauledItem = null);
public sealed record CreatureView(
    int Id,
    string DefId,
    Vec3i Position,
    float CurrentHealth,
    float MaxHealth,
    bool IsConscious,
    bool IsHostile,
    NeedView[] Needs,
    StatView[] Stats,
    WoundView[] Wounds,
    SubstanceView[] Substances,
    ItemView[] CarriedItems,
    EventLogEntryView[] EventLog,
    ItemView? HauledItem = null);
public sealed record StoredItemsView(int StoredItemCount, ItemView[] Contents, int? Capacity = null);
public sealed record CorpseView(int FormerEntityId, string FormerDefId, string DisplayName, string DeathCause, float RotProgress, string RotStage);
public sealed record ItemView(
    int Id,
    string DefId,
    string? MaterialId,
    Vec3i Position,
    int StackSize,
    int StockpileId,
    int ContainerBuildingId,
    int ContainerItemId,
    int CarriedByEntityId,
    CorpseView? Corpse,
    StoredItemsView? Storage,
    string DisplayName,
    float Weight = 0f,
    ItemCarryMode CarryMode = ItemCarryMode.None);
public sealed record ContainerEntityView(int Id, string DefId, Vec3i Position, StoredItemsView Storage);
public sealed record BuildingView(int Id, string BuildingDefId, Vec3i Origin, bool IsWorkshop, int StoredItemCount, string? MaterialId);
public sealed record StockpileView(int Id, Vec3i From, Vec3i To, string[] AcceptedTags);
public sealed record WorldLoreSummaryView(
    string RegionName,
    string BiomeId,
    float Threat,
    float Prosperity,
    int SimulatedYears,
    string[] RecentEvents,
    bool UsesCanonicalHistory,
    string? OwnerCivilizationId,
    string? OwnerCivilizationName,
    string? PrimarySiteId,
    string? PrimarySiteName,
    int? PrimarySitePopulation,
    int? PrimarySiteHouseholdCount,
    int? PrimarySiteMilitaryCount);
public sealed record TileQueryResult(
    Vec3i Position,
    TileView? Tile,
    IReadOnlyList<DwarfView> Dwarves,
    IReadOnlyList<CreatureView> Creatures,
    IReadOnlyList<ItemView> Items,
    IReadOnlyList<ContainerEntityView> Containers,
    BuildingView? Building,
    StockpileView? Stockpile);

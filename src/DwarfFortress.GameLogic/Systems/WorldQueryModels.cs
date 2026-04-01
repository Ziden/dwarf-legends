using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
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
public sealed record EventLogEntryView(string Message, Vec3i Position, string TimeLabel);
public sealed record FortressAnnouncementView(int Sequence, string Message, Vec3i Position, bool HasLocation, FortressAnnouncementSeverity Severity, string TimeLabel, int RepeatCount);
public sealed record WoundView(string BodyPartId, string Severity, bool IsBleeding);
public sealed record SubstanceView(string Id, float Concentration);
public sealed record JobView(int Id, string JobDefId, JobStatus Status, Vec3i TargetPos, float WorkProgress, string CurrentStep);
public sealed record DwarfAppearanceView(string HairType, string HairColor, string BeardType, string BeardColor, string EyeType, string NoseType, string MouthType, string FaceType);
public sealed record TraitView(string Id, string DisplayName, string Description, string Category);
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
    TraitView[] Traits,
    EventLogEntryView[] EventLog);
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
    EventLogEntryView[] EventLog);
public sealed record CorpseView(int FormerEntityId, string FormerDefId, string DisplayName, string DeathCause, float RotProgress, string RotStage, ItemView[] Contents);
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
    float Weight = 0f);
public sealed record BuildingView(int Id, string BuildingDefId, Vec3i Origin, bool IsWorkshop, int StoredItemCount);
public sealed record StockpileView(int Id, Vec3i From, Vec3i To, string[] AcceptedTags);
public sealed record WorldLoreSummaryView(
    string RegionName,
    string BiomeId,
    float Threat,
    float Prosperity,
    int SimulatedYears,
    string[] RecentEvents);
public sealed record TileQueryResult(
    Vec3i Position,
    TileView? Tile,
    IReadOnlyList<DwarfView> Dwarves,
    IReadOnlyList<CreatureView> Creatures,
    IReadOnlyList<ItemView> Items,
    BuildingView? Building,
    StockpileView? Stockpile);

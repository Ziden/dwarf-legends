using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

/// <summary>
/// Live gameplay query layer for the client. Unlike SaveGameSystem, this does not rebuild a
/// whole-world read model; it answers targeted queries by entity ID or tile.
/// </summary>
public sealed class WorldQuerySystem : IGameSystem
{
    public string SystemId => SystemIds.WorldQuerySystem;
    public int UpdateOrder => 97;
    public bool IsEnabled { get; set; } = true;

    private GameContext? _ctx;

    public void Initialize(GameContext ctx) => _ctx = ctx;
    public void Tick(float delta) { }
    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    public GameTimeView GetTimeView()
    {
        var time = _ctx!.Get<TimeSystem>();
        return new GameTimeView(time.Year, time.Month, time.Day, time.Hour, time.CurrentSeason.ToString());
    }

    public WorldLoreSummaryView? GetLoreSummary()
    {
        var lore = _ctx!.TryGet<WorldLoreSystem>()?.Current;
        if (lore is null)
            return null;

        return new WorldLoreSummaryView(
            lore.RegionName,
            lore.BiomeId,
            lore.Threat,
            lore.Prosperity,
            lore.SimulatedYears,
            lore.History
                .OrderByDescending(e => e.Year)
                .Take(8)
                .Select(e => $"Y{e.Year}: {e.Summary}")
                .ToArray());
    }

    public FortressAnnouncementView[] GetFortressAnnouncements(int maxEntries = 24)
    {
        var announcements = _ctx!.TryGet<FortressAnnouncementSystem>();
        return announcements?.GetEntries(maxEntries).Select(ToFortressAnnouncementView).ToArray()
               ?? Array.Empty<FortressAnnouncementView>();
    }

    public TileView? GetTileView(Vec3i pos)
    {
        var map = _ctx!.Get<WorldMap>();
        if (pos.X < 0 || pos.Y < 0 || pos.Z < 0 || pos.X >= map.Width || pos.Y >= map.Height || pos.Z >= map.Depth)
            return null;

        var tile = map.GetTile(pos);
        if (tile.TileDefId == TileDefIds.Empty)
            return null;
        var isVisible = MiningLineOfSight.IsTileVisible(map, pos);
        var oreItemDefId = tile.OreItemDefId;
        if (!string.IsNullOrWhiteSpace(oreItemDefId) && !MiningLineOfSight.IsOreVisible(map, pos))
            oreItemDefId = null;

        return new TileView(
            pos.X,
            pos.Y,
            pos.Z,
            tile.TileDefId,
            tile.MaterialId,
            tile.TreeSpeciesId,
            tile.IsPassable,
            tile.IsDesignated,
            tile.IsUnderConstruction,
            tile.FluidLevel,
            tile.FluidMaterialId,
            tile.CoatingMaterialId,
            tile.CoatingAmount,
            tile.IsAquifer,
            oreItemDefId,
                tile.PlantDefId,
                tile.PlantGrowthStage,
                tile.PlantYieldLevel,
                tile.PlantSeedLevel,
            isVisible);
    }

    public DwarfView? GetDwarfView(int id)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        if (!registry.TryGetById<Dwarf>(id, out var dwarf) || dwarf is null)
            return null;

        var items = _ctx.TryGet<ItemSystem>();
        var jobSystem = _ctx.TryGet<Jobs.JobSystem>();
        var eventLogSystem = _ctx.TryGet<EntityEventLogSystem>();
        var needs = dwarf.Components.Get<NeedsComponent>();
        var stats = dwarf.Components.Get<StatComponent>();
        var skills = dwarf.Components.Get<SkillComponent>();
        var thoughts = dwarf.Components.Get<ThoughtComponent>();
        var labors = dwarf.Components.Get<LaborComponent>();
        var inventory = dwarf.Components.Get<InventoryComponent>();
        var appearance = dwarf.Appearance;
        var traits = dwarf.Traits;
        var currentJob = jobSystem?.GetAssignedJob(dwarf.Id);
        var carriedItems = items is null
            ? Array.Empty<ItemView>()
            : inventory.CarriedItemIds
                .Select(itemId => items.TryGetItem(itemId, out var item) ? item : null)
                .Where(item => item is not null)
                .Select(item => ToItemView(item!))
                .ToArray();

        return new DwarfView(
            dwarf.Id,
            dwarf.FirstName,
            dwarf.Position.Position,
            dwarf.ProfessionId,
            dwarf.Mood.Current,
            dwarf.Mood.Happiness,
            dwarf.Health.CurrentHealth,
            dwarf.Health.MaxHealth,
            dwarf.Health.IsConscious,
            [
                new NeedView(NeedIds.Hunger, needs.Hunger.Level),
                new NeedView(NeedIds.Thirst, needs.Thirst.Level),
                new NeedView(NeedIds.Sleep, needs.Sleep.Level),
                new NeedView(NeedIds.Social, needs.Social.Level),
                new NeedView(NeedIds.Recreation, needs.Recreation.Level),
            ],
            [
                new StatView(StatNames.Speed, stats.Speed.Value),
                new StatView(StatNames.Strength, stats.Strength.Value),
                new StatView(StatNames.Toughness, stats.Toughness.Value),
                new StatView(StatNames.Agility, stats.Agility.Value),
                new StatView(StatNames.Focus, stats.Focus.Value),
            ],
            skills.All.Values
                .OrderByDescending(skill => skill.Level)
                .ThenBy(skill => skill.Name)
                .Select(skill => new SkillView(skill.Name, skill.Level, skill.Xp, skill.XpForNextLevel))
                .ToArray(),
            thoughts.Active
                .OrderByDescending(thought => Math.Abs(thought.HappinessMod))
                .ThenBy(thought => thought.Description)
                .Select(thought => new ThoughtView(thought.Id, thought.Description, thought.HappinessMod, thought.TimeLeft))
                .ToArray(),
            dwarf.Health.Wounds
                .Select(wound => new WoundView(wound.BodyPartId, wound.Severity.ToString(), wound.IsBleeding))
                .ToArray(),
            BuildSubstancesView(dwarf),
            currentJob is null
                ? null
                : new JobView(
                    currentJob.Id,
                    currentJob.JobDefId,
                    currentJob.Status,
                    currentJob.TargetPos,
                    currentJob.WorkProgress,
                    jobSystem?.DescribeCurrentStep(currentJob.Id) ?? "working"),
            carriedItems,
            labors.EnabledLabors.ToArray(),
            new DwarfAppearanceView(
                appearance.HairType.ToString(),
                appearance.HairColor.ToString(),
                appearance.BeardType.ToString(),
                appearance.BeardColor.ToString(),
                appearance.EyeType.ToString(),
                appearance.NoseType.ToString(),
                appearance.MouthType.ToString(),
                appearance.FaceType.ToString()),
            BuildTraitViews(traits),
            eventLogSystem?.GetEntries(dwarf.Id).Select(ToEventLogView).ToArray() ?? Array.Empty<EventLogEntryView>());
    }

    public CreatureView? GetCreatureView(int id)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        if (!registry.TryGetById<Creature>(id, out var creature) || creature is null)
            return null;

        var eventLogSystem = _ctx.TryGet<EntityEventLogSystem>();

        var creatureItems = _ctx.TryGet<ItemSystem>();
        var creatureCarried = creature.Inventory.CarriedItemIds
            .Select(id => creatureItems?.TryGetItem(id, out var itm) == true ? itm : null)
            .Where(itm => itm is not null)
            .Select(itm => ToItemView(itm!))
            .ToArray();

        return new CreatureView(
            creature.Id,
            creature.DefId,
            creature.Position.Position,
            creature.Health.CurrentHealth,
            creature.Health.MaxHealth,
            creature.Health.IsConscious,
            creature.IsHostile,
            [
                new NeedView(NeedIds.Hunger, creature.Needs.Hunger.Level),
                new NeedView(NeedIds.Thirst, creature.Needs.Thirst.Level),
            ],
            [
                new StatView(StatNames.Speed, creature.Stats.Speed.Value),
                new StatView(StatNames.Strength, creature.Stats.Strength.Value),
                new StatView(StatNames.Toughness, creature.Stats.Toughness.Value),
                new StatView(StatNames.Agility, creature.Stats.Agility.Value),
                new StatView(StatNames.Focus, creature.Stats.Focus.Value),
            ],
            creature.Health.Wounds
                .Select(wound => new WoundView(wound.BodyPartId, wound.Severity.ToString(), wound.IsBleeding))
                .ToArray(),
            creature.BodyChemistry.All
                .OrderByDescending(pair => pair.Value)
                .Select(pair => new SubstanceView(pair.Key, pair.Value))
                .ToArray(),
            creatureCarried,
            eventLogSystem?.GetEntries(creature.Id).Select(ToEventLogView).ToArray() ?? Array.Empty<EventLogEntryView>());
    }

    public ItemView? GetItemView(int id)
    {
        var items = _ctx!.TryGet<ItemSystem>();
        if (items is null || !items.TryGetItem(id, out var item) || item is null)
            return null;

        return ToItemView(item);
    }

    public BuildingView? GetBuildingView(int id)
    {
        var building = _ctx!.TryGet<BuildingSystem>()?.GetById(id);
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        return building is null
            ? null
            : new BuildingView(
                building.Id,
                building.BuildingDefId,
                building.Origin,
                building.IsWorkshop,
                itemSystem?.GetItemsInBuilding(building.Id).Count() ?? 0);
    }

    public TileQueryResult QueryTile(Vec3i pos)
    {
        var spatial = _ctx!.Get<SpatialIndexSystem>();
        var stockpile = _ctx.TryGet<StockpileManager>()?.GetAll()
            .FirstOrDefault(s =>
                pos.X >= Math.Min(s.From.X, s.To.X) && pos.X <= Math.Max(s.From.X, s.To.X) &&
                pos.Y >= Math.Min(s.From.Y, s.To.Y) && pos.Y <= Math.Max(s.From.Y, s.To.Y) &&
                pos.Z >= Math.Min(s.From.Z, s.To.Z) && pos.Z <= Math.Max(s.From.Z, s.To.Z));

        return new TileQueryResult(
            pos,
            GetTileView(pos),
            spatial.GetDwarvesAt(pos).Select(id => GetDwarfView(id)).Where(v => v is not null).Select(v => v!).ToArray(),
            spatial.GetCreaturesAt(pos).Select(id => GetCreatureView(id)).Where(v => v is not null).Select(v => v!).ToArray(),
            (_ctx!.TryGet<ItemSystem>()?.GetItemsAt(pos) ?? Enumerable.Empty<Item>()).Select(ToItemView).ToArray(),
            spatial.GetBuildingAt(pos) is int buildingId ? GetBuildingView(buildingId) : null,
            stockpile is null ? null : new StockpileView(stockpile.Id, stockpile.From, stockpile.To, stockpile.AcceptedTags));
    }

    private ItemView ToItemView(Item item)
    {
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        var dm = _ctx!.TryGet<DataManager>();
        var corpse = item.Components.TryGet<CorpseComponent>();
        var rot = item.Components.TryGet<RotComponent>();
        CorpseView? corpseView = null;
        if (corpse is not null)
        {
            var contents = itemSystem is null
                ? Array.Empty<ItemView>()
                : itemSystem.GetItemsInItem(item.Id).Select(ToItemView).ToArray();
            corpseView = new CorpseView(
                corpse.FormerEntityId,
                corpse.FormerDefId,
                corpse.DisplayName,
                corpse.DeathCause,
                rot?.Progress ?? 0f,
                rot?.Stage ?? "fresh",
                contents);
        }

        // Get item weight from definition
        var itemDef = dm?.Items.GetOrNull(item.DefId);
        var weight = itemDef?.Weight ?? 0f;
        if (item.StackSize > 1)
            weight *= item.StackSize;

        return new ItemView(
            item.Id,
            item.DefId,
            item.MaterialId,
            item.Position.Position,
            item.StackSize,
            item.StockpileId,
            item.ContainerBuildingId,
            item.ContainerItemId,
            item.CarriedByEntityId,
            corpseView,
            weight);
    }

    private static EventLogEntryView ToEventLogView(EntityEventLogEntry entry)
        => new(entry.Message, entry.Position, FormatTimeLabel(entry));

    private static FortressAnnouncementView ToFortressAnnouncementView(FortressAnnouncementEntry entry)
        => new(entry.Sequence, entry.Message, entry.Position, entry.HasLocation, entry.Severity,
            FormatTimeLabel(entry.Year, entry.Month, entry.Day, entry.Hour), entry.RepeatCount);

    private static SubstanceView[] BuildSubstancesView(Entities.Dwarf dwarf)
    {
        var chemistry = dwarf.BodyChemistry.All
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new SubstanceView(pair.Key, pair.Value));

        var fx = dwarf.Components.TryGet<Entities.Components.StatusEffectComponent>();
        if (fx is not null)
        {
            var statusViews = fx.All.Select(e =>
                new SubstanceView(e.Id, e.RemainingSeconds));
            return chemistry.Concat(statusViews).ToArray();
        }

        return chemistry.ToArray();
    }

    private static string FormatTimeLabel(EntityEventLogEntry entry)
    {
        if (entry.Year <= 0)
            return "Time unknown";

        return $"Y{entry.Year} M{entry.Month} D{entry.Day} {entry.Hour:00}:00";
    }

    private static string FormatTimeLabel(int year, int month, int day, int hour)
    {
        if (year <= 0)
            return "Time unknown";

        return $"Y{year} M{month} D{day} {hour:00}:00";
    }

    private TraitView[] BuildTraitViews(TraitComponent traits)
    {
        var data = _ctx!.TryGet<DataManager>();
        if (data is null) return Array.Empty<TraitView>();

        return traits.TraitIds
            .Select(id => data.Traits.GetOrNull(id))
            .Where(def => def is not null)
            .Select(def => new TraitView(def!.Id, def!.DisplayName, def!.Description, def!.Category))
            .ToArray();
    }
}

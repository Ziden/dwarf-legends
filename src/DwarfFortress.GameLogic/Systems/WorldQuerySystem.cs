using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
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
        var runtimeHistory = _ctx!.TryGet<WorldHistoryRuntimeService>();
        var macroState = _ctx.TryGet<WorldMacroStateService>();
        if (runtimeHistory?.CurrentSummary is not { } canonicalSummary || macroState?.Current is not { } macro)
            return null;

        return new WorldLoreSummaryView(
            canonicalSummary.RegionName,
            canonicalSummary.BiomeId,
            macro.Threat,
            macro.Prosperity,
            canonicalSummary.SimulatedYears,
            canonicalSummary.RecentEvents,
            true,
            canonicalSummary.OwnerCivilizationId,
            canonicalSummary.OwnerCivilizationName,
            canonicalSummary.PrimarySiteId,
            canonicalSummary.PrimarySiteName,
            canonicalSummary.PrimarySitePopulation,
            canonicalSummary.PrimarySiteHouseholdCount,
            canonicalSummary.PrimarySiteMilitaryCount);
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
        var isDamp = MiningHazardAnalysis.IsDampWall(map, pos);
        var isWarm = MiningHazardAnalysis.IsWarmWall(map, pos);
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
            isDamp,
            isWarm,
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
        var attributes = dwarf.Attributes;
        var currentJob = jobSystem?.GetAssignedJob(dwarf.Id);
        var hauledItem = items?.TryGetHauledItem(dwarf.Id, out var hauled) == true && hauled is not null
            ? ToItemView(hauled)
            : null;
        var carriedItems = items is null
            ? Array.Empty<ItemView>()
            : inventory.CarriedItemIds
                .Select(itemId => items.TryGetItem(itemId, out var item) ? item : null)
                .Where(item => item is not null)
                .OrderBy(item => item!.Id)
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
            BuildAttributeViews(attributes),
            eventLogSystem?.GetEntries(dwarf.Id).Select(ToEventLogView).ToArray() ?? Array.Empty<EventLogEntryView>(),
            BuildDwarfProvenanceView(dwarf),
            hauledItem);
    }

    private DwarfProvenanceView? BuildDwarfProvenanceView(Dwarf dwarf)
    {
        var provenance = dwarf.Components.TryGet<DwarfProvenanceComponent>();
        if (provenance is null || !provenance.HasKnownOrigin)
            return null;

        var historyRuntime = _ctx!.TryGet<WorldHistoryRuntimeService>();
        var figure = historyRuntime?.GetFigure(provenance.FigureId);
        var household = historyRuntime?.GetHousehold(provenance.HouseholdId);
        var civilization = historyRuntime?.GetCivilization(provenance.CivilizationId);
        var originSite = historyRuntime?.GetSite(provenance.OriginSiteId);
        var birthSite = historyRuntime?.GetSite(provenance.BirthSiteId);

        return new DwarfProvenanceView(
            provenance.WorldSeed,
            provenance.FigureId,
            figure?.Name,
            provenance.HouseholdId,
            household?.Name,
            provenance.CivilizationId,
            civilization?.Name,
            provenance.OriginSiteId,
            originSite?.Name,
            originSite?.Kind,
            provenance.BirthSiteId,
            birthSite?.Name,
            provenance.MigrationWaveId,
            provenance.WorldX,
            provenance.WorldY,
            provenance.RegionX,
            provenance.RegionY);
    }

    public CreatureView? GetCreatureView(int id)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        if (!registry.TryGetById<Creature>(id, out var creature) || creature is null)
            return null;

        var eventLogSystem = _ctx.TryGet<EntityEventLogSystem>();

        var creatureItems = _ctx.TryGet<ItemSystem>();
        var hauledItem = creatureItems?.TryGetHauledItem(creature.Id, out var hauled) == true && hauled is not null
            ? ToItemView(hauled)
            : null;
        var creatureCarried = creature.Inventory.CarriedItemIds
            .Select(id => creatureItems?.TryGetItem(id, out var itm) == true ? itm : null)
            .Where(itm => itm is not null)
            .OrderBy(itm => itm!.Id)
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
            eventLogSystem?.GetEntries(creature.Id).Select(ToEventLogView).ToArray() ?? Array.Empty<EventLogEntryView>(),
            hauledItem);
    }

    public ItemView? GetItemView(int id)
    {
        var items = _ctx!.TryGet<ItemSystem>();
        if (items is null || !items.TryGetItem(id, out var item) || item is null)
            return null;

        return ToItemView(item);
    }

    public ItemView[] GetContainedItemViews(int containerItemId)
    {
        var items = _ctx!.TryGet<ItemSystem>();
        if (items is null)
            return Array.Empty<ItemView>();

        var registry = _ctx.TryGet<EntityRegistry>();
        if (registry?.TryGetById(containerItemId) is { } entity && entity is not Item)
        {
            var container = entity.Components.TryGet<ContainerComponent>();
            if (container is not null)
            {
                return container.StoredItemIds
                    .Select(itemId => items.TryGetItem(itemId, out var item) ? item : null)
                    .Where(item => item is not null)
                    .OrderBy(item => item!.Id)
                    .Select(item => ToItemView(item!))
                    .ToArray();
            }
        }

        return items.GetItemsInItem(containerItemId)
            .OrderBy(item => item.Id)
            .Select(ToItemView)
            .ToArray();
    }

    public BuildingView? GetBuildingView(int id)
    {
        var building = _ctx!.TryGet<BuildingSystem>()?.GetById(id);
        var dataManager = _ctx.TryGet<DataManager>();
        var housingSystem = _ctx.TryGet<HousingSystem>();
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        var stockpileManager = _ctx.TryGet<StockpileManager>();
        if (building is null)
            return null;

        var definition = dataManager?.Buildings.GetOrNull(building.BuildingDefId);
        var residents = housingSystem?.GetResidents(building.Id) ?? [];
        var linkedStockpileId = building.LinkedStockpileId >= 0
            ? building.LinkedStockpileId
            : stockpileManager?.GetByOwnerBuilding(building.Id)?.Id ?? -1;
        var storedItemCount = itemSystem?.GetItemsInBuilding(building.Id).Count() ?? 0;
        var stockpileItemCount = linkedStockpileId >= 0 && itemSystem is not null
            ? itemSystem.GetAllItems().Count(item => item.StockpileId == linkedStockpileId)
            : 0;

        return building is null
            ? null
            : new BuildingView(
                building.Id,
                building.BuildingDefId,
                building.Origin,
                building.IsWorkshop,
                storedItemCount,
                building.MaterialId,
                building.Rotation,
                definition?.ResidenceCapacity ?? 0,
                residents.Select(dwarf => dwarf.Id).ToArray(),
                residents.Select(dwarf => dwarf.FirstName).ToArray(),
                linkedStockpileId,
                storedItemCount + stockpileItemCount);
    }

    public ContainerEntityView? GetContainerEntityView(int id)
    {
        var registry = _ctx!.TryGet<EntityRegistry>();
        var entity = registry?.TryGetById(id);
        if (entity is null || entity is Item)
            return null;

        var position = entity.Components.TryGet<PositionComponent>();
        var container = entity.Components.TryGet<ContainerComponent>();
        if (position is null || container is null)
            return null;

        var itemSystem = _ctx.TryGet<ItemSystem>();
        var contents = container.StoredItemIds
            .Select(itemId => itemSystem?.TryGetItem(itemId, out var item) == true ? item : null)
            .Where(item => item is not null)
            .OrderBy(item => item!.Id)
            .Select(item => ToItemView(item!))
            .ToArray();

        return new ContainerEntityView(
            entity.Id,
            entity.DefId,
            position.Position,
            new StoredItemsView(contents.Length, contents, container.Capacity));
    }

    public TileQueryResult QueryTile(Vec3i pos)
    {
        var spatial = _ctx!.Get<SpatialIndexSystem>();
        var itemSystem = _ctx.TryGet<ItemSystem>();
        var stockpile = _ctx.TryGet<StockpileManager>()?.GetContaining(pos);

        return new TileQueryResult(
            pos,
            GetTileView(pos),
            spatial.GetDwarvesAt(pos).Select(id => GetDwarfView(id)).Where(v => v is not null).Select(v => v!).ToArray(),
            spatial.GetCreaturesAt(pos).Select(id => GetCreatureView(id)).Where(v => v is not null).Select(v => v!).ToArray(),
            (itemSystem?.GetItemsAt(pos) ?? Enumerable.Empty<Item>())
                .Where(item => item.CarriedByEntityId < 0 && item.ContainerItemId < 0)
                .Select(ToItemView)
                .ToArray(),
            spatial.GetContainersAt(pos).Select(id => GetContainerEntityView(id)).Where(v => v is not null).Select(v => v!).ToArray(),
            spatial.GetBuildingAt(pos) is int buildingId ? GetBuildingView(buildingId) : null,
            stockpile is null ? null : new StockpileView(stockpile.Id, stockpile.From, stockpile.To, stockpile.AcceptedTags));
    }

    private ItemView ToItemView(Item item)
    {
        var dm = _ctx!.TryGet<DataManager>();
        var corpse = item.Components.TryGet<CorpseComponent>();
        var rot = item.Components.TryGet<RotComponent>();
        var itemDef = dm?.Items.GetOrNull(item.DefId);
        var storage = BuildItemStorageView(item, corpse is not null || itemDef?.Tags.Contains(TagIds.Container) == true);
        CorpseView? corpseView = null;
        if (corpse is not null)
        {
            corpseView = new CorpseView(
                corpse.FormerEntityId,
                corpse.FormerDefId,
                corpse.DisplayName,
                corpse.DeathCause,
                rot?.Progress ?? 0f,
                rot?.Stage ?? "fresh");
        }

        // Get item weight from definition
        var displayName = corpse is not null
            ? $"Corpse of {corpse.DisplayName}"
            : itemDef?.DisplayName ?? Humanize(item.DefId);
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
            storage,
            displayName,
            weight,
            item.CarryMode)
        {
            JobBindings = BuildItemJobBindings(item.Id),
        };
    }

    private ItemJobBindingView[] BuildItemJobBindings(int itemId)
    {
        var jobSystem = _ctx!.TryGet<JobSystem>();
        if (jobSystem is null)
            return Array.Empty<ItemJobBindingView>();

        var registry = _ctx.TryGet<EntityRegistry>();
        var bindings = new List<ItemJobBindingView>();
        foreach (var job in jobSystem.GetOpenJobsReferencingItem(itemId))
        {
            string? assignedDwarfName = null;
            if (job.AssignedDwarfId >= 0 && registry?.TryGetById<Dwarf>(job.AssignedDwarfId, out var dwarf) == true && dwarf is not null)
                assignedDwarfName = dwarf.FirstName;

            bindings.Add(new ItemJobBindingView(
                job.Id,
                job.JobDefId,
                job.Status,
                job.AssignedDwarfId,
                assignedDwarfName));
        }

        bindings.Sort(static (left, right) => left.JobId.CompareTo(right.JobId));
        return bindings.ToArray();
    }

    private StoredItemsView? BuildItemStorageView(Item item, bool supportsStorage)
    {
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        if (itemSystem is null)
            return supportsStorage ? new StoredItemsView(0, Array.Empty<ItemView>()) : null;

        var storedItems = itemSystem.GetItemsInItem(item.Id)
            .OrderBy(storedItem => storedItem.Id)
            .ToArray();

        if (!supportsStorage && storedItems.Length == 0)
            return null;

        var contents = storedItems
            .Select(ToItemView)
            .ToArray();

        return new StoredItemsView(contents.Length, contents);
    }

    private static EventLogEntryView ToEventLogView(EntityEventLogEntry entry)
        => new(entry.Message, entry.Position, FormatTimeLabel(entry), entry.LinkedTarget);

    private static FortressAnnouncementView ToFortressAnnouncementView(FortressAnnouncementEntry entry)
        => new(entry.Sequence, entry.Kind, entry.Message, entry.Position, entry.HasLocation, entry.Severity,
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

    private static string Humanize(string value)
        => string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Replace('_', ' ');

    private DwarfAttributeView[] BuildAttributeViews(DwarfAttributeComponent attributes)
    {
        var data = _ctx!.TryGet<DataManager>();
        if (data is null) return Array.Empty<DwarfAttributeView>();

        var attrDefs = data.Attributes;
        var views = new List<DwarfAttributeView>();

        // Build views for all defined attributes, showing level and label
        foreach (var attrDef in attrDefs.All())
        {
            var level = attributes.GetLevel(attrDef.Id);
            var attr = new DwarfAttribute(attrDef.Id, level, attrDef);
            views.Add(new DwarfAttributeView(
                attrDef.Id,
                attrDef.DisplayName,
                level,
                attr.Label,
                attrDef.Category));
        }

        return views.ToArray();
    }
}

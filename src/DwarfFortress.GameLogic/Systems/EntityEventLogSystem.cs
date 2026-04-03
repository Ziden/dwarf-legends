using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Jobs;

namespace DwarfFortress.GameLogic.Systems;

public record struct EntityActivityEvent(int EntityId, string Description, Vec3i Position);

public sealed record EntityEventLogEntry(string Message, Vec3i Position, int Year, int Month, int Day, int Hour, EventLogLinkTarget? LinkedTarget = null);

public sealed class EntityEventLogSystem : IGameSystem
{
    private const int MaxEntriesPerEntity = 48;

    private readonly Dictionary<int, List<EntityEventLogEntry>> _entries = new();
    private readonly Dictionary<int, TrackedJobInfo> _trackedJobs = new();

    private GameContext? _ctx;

    public string SystemId => SystemIds.EntityEventLogSystem;
    public int UpdateOrder => 96;
    public bool IsEnabled { get; set; } = true;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;

        ctx.EventBus.On<EntityActivityEvent>(OnEntityActivity);
        ctx.EventBus.On<BehaviorFiredEvent>(OnBehaviorFired);
        ctx.EventBus.On<ItemPickedUpEvent>(OnItemPickedUp);
        ctx.EventBus.On<ItemDroppedEvent>(OnItemDropped);
        ctx.EventBus.On<EntityDiedEvent>(OnEntityDied);
        ctx.EventBus.On<MoodChangedEvent>(OnMoodChanged);
        ctx.EventBus.On<ThoughtAddedEvent>(OnThoughtAdded);
        ctx.EventBus.On<SkillLeveledUpEvent>(OnSkillLeveledUp);
        ctx.EventBus.On<CombatHitEvent>(OnCombatHit);
        ctx.EventBus.On<CombatMissEvent>(OnCombatMiss);
        ctx.EventBus.On<DwarfWoundedEvent>(OnDwarfWounded);
        ctx.EventBus.On<IntoxicationChangedEvent>(OnIntoxicationChanged);
        ctx.EventBus.On<SubstanceIngestedEvent>(OnSubstanceIngested);
        ctx.EventBus.On<ReactionFiredEvent>(OnReactionFired);
        ctx.EventBus.On<JobAssignedEvent>(OnJobAssigned);
        ctx.EventBus.On<JobCompletedEvent>(OnJobCompleted);
        ctx.EventBus.On<JobFailedEvent>(OnJobFailed);
        ctx.EventBus.On<JobCancelledEvent>(OnJobCancelled);
    }

    public void Tick(float delta) { }

    public void OnSave(SaveWriter w) { }

    public void OnLoad(SaveReader r)
    {
        _entries.Clear();
        _trackedJobs.Clear();
    }

    public IReadOnlyList<EntityEventLogEntry> GetEntries(int entityId)
        => _entries.TryGetValue(entityId, out var entries)
            ? entries
            : Array.Empty<EntityEventLogEntry>();

    private void OnEntityActivity(EntityActivityEvent e)
        => AddEntry(e.EntityId, e.Description, e.Position);

    private void OnBehaviorFired(BehaviorFiredEvent e)
    {
        var description = e.BehaviorId switch
        {
            BehaviorIds.Grooming => "Groomed itself",
            BehaviorIds.Socialize => "Spent time socializing",
            BehaviorIds.Tantrum => "Lost control in a rage",
            _ => null,
        };

        if (description is null || TryGetEntityPosition(e.EntityId) is not Vec3i position)
            return;

        AddEntry(e.EntityId, description, position);
    }

    private void OnItemPickedUp(ItemPickedUpEvent e)
    {
        var itemLink = ResolveItemLink(e.ItemId, e.ItemDefId);
        AddEntry(e.CarrierEntityId, $"Picked up {itemLink?.DisplayName ?? ResolveItemDisplayName(e.ItemId, e.ItemDefId)}", e.Position, itemLink);
    }

    private void OnItemDropped(ItemDroppedEvent e)
    {
        var verb = e.ContainerBuildingId >= 0 ? "Stored" : "Dropped";
        var itemLink = ResolveItemLink(e.ItemId, e.ItemDefId);
        AddEntry(e.CarrierEntityId, $"{verb} {itemLink?.DisplayName ?? ResolveItemDisplayName(e.ItemId, e.ItemDefId)}", e.Position, itemLink);
    }

    private void OnEntityDied(EntityDiedEvent e)
        => AddEntry(e.EntityId, $"Died from {Humanize(e.Cause)}", e.Position);

    private void OnMoodChanged(MoodChangedEvent e)
    {
        if (TryGetEntityPosition(e.DwarfId) is not Vec3i position)
            return;

        AddEntry(e.DwarfId, $"Mood shifted from {Humanize(e.OldMood.ToString())} to {Humanize(e.NewMood.ToString())}", position);
    }

    private void OnThoughtAdded(ThoughtAddedEvent e)
    {
        if (TryGetEntityPosition(e.DwarfId) is not Vec3i position)
            return;

        var description = ResolveThoughtDescription(e.DwarfId, e.ThoughtId);
        AddEntry(e.DwarfId, description is null ? $"New thought: {Humanize(e.ThoughtId)}" : $"Thought: {description}", position);
    }

    private void OnSkillLeveledUp(SkillLeveledUpEvent e)
    {
        if (TryGetEntityPosition(e.DwarfId) is not Vec3i position)
            return;

        AddEntry(e.DwarfId, $"Improved {Humanize(e.SkillId)} to level {e.NewLevel}", position);
    }

    private void OnCombatHit(CombatHitEvent e)
    {
        var defenderLink = ResolveEntityLink(e.DefenderId);
        var attackerLink = ResolveEntityLink(e.AttackerId);

        if (TryGetEntityPosition(e.AttackerId) is Vec3i attackerPos)
            AddEntry(e.AttackerId,
                $"Hit {defenderLink?.DisplayName ?? ResolveEntityName(e.DefenderId)} in the {Humanize(e.BodyPartId)} for {e.Damage:0.#} damage",
                attackerPos,
                defenderLink);

        if (TryGetEntityPosition(e.DefenderId) is Vec3i defenderPos)
            AddEntry(e.DefenderId,
                $"Was hit by {attackerLink?.DisplayName ?? ResolveEntityName(e.AttackerId)} in the {Humanize(e.BodyPartId)} for {e.Damage:0.#} damage",
                defenderPos,
                attackerLink);
    }

    private void OnCombatMiss(CombatMissEvent e)
    {
        var defenderLink = ResolveEntityLink(e.DefenderId);
        var attackerLink = ResolveEntityLink(e.AttackerId);

        if (TryGetEntityPosition(e.AttackerId) is Vec3i attackerPos)
            AddEntry(e.AttackerId,
                $"Missed an attack on {defenderLink?.DisplayName ?? ResolveEntityName(e.DefenderId)}",
                attackerPos,
                defenderLink);

        if (TryGetEntityPosition(e.DefenderId) is Vec3i defenderPos)
            AddEntry(e.DefenderId,
                $"Dodged an attack from {attackerLink?.DisplayName ?? ResolveEntityName(e.AttackerId)}",
                defenderPos,
                attackerLink);
    }

    private void OnDwarfWounded(DwarfWoundedEvent e)
    {
        if (TryGetEntityPosition(e.DwarfId) is not Vec3i position)
            return;

        AddEntry(e.DwarfId, $"Suffered a {Humanize(e.Severity.ToString())} wound to the {Humanize(e.BodyPartId)}", position);
    }

    private void OnIntoxicationChanged(IntoxicationChangedEvent e)
    {
        if (e.OldState == e.NewState || TryGetEntityPosition(e.EntityId) is not Vec3i position)
            return;

        AddEntry(e.EntityId,
            $"Intoxication changed from {Humanize(e.OldState.ToString())} to {Humanize(e.NewState.ToString())}",
            position);
    }

    private void OnSubstanceIngested(SubstanceIngestedEvent e)
    {
        if (TryGetEntityPosition(e.EntityId) is not Vec3i position)
            return;

        AddEntry(e.EntityId, $"Ingested {Humanize(e.SubstanceId)}", position);
    }

    private void OnReactionFired(ReactionFiredEvent e)
    {
        if (TryGetEntityPosition(e.EntityId) is not Vec3i position)
            return;

        AddEntry(e.EntityId, $"Triggered reaction {Humanize(e.ReactionDefId)}", position);
    }

    private void OnJobAssigned(JobAssignedEvent e)
    {
        var job = _ctx!.TryGet<JobSystem>()?.GetJob(e.JobId);
        if (job is null ||
            string.Equals(job.JobDefId, JobDefIds.Idle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(job.JobDefId, JobDefIds.EngageHostile, StringComparison.OrdinalIgnoreCase))
            return;

        _trackedJobs[e.JobId] = new TrackedJobInfo(e.DwarfId, job.JobDefId, job.TargetPos);
        AddEntry(e.DwarfId, $"Started {Humanize(job.JobDefId)}", job.TargetPos);
    }

    private void OnJobCompleted(JobCompletedEvent e)
    {
        if (string.Equals(e.JobDefId, JobDefIds.Idle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.JobDefId, JobDefIds.EngageHostile, StringComparison.OrdinalIgnoreCase))
            return;

        _trackedJobs.Remove(e.JobId);
        AddEntry(e.DwarfId, $"Finished {Humanize(e.JobDefId)}", e.TargetPos);
    }

    private void OnJobFailed(JobFailedEvent e)
    {
        if (!_trackedJobs.Remove(e.JobId, out var tracked))
            return;

        AddEntry(tracked.DwarfId,
            $"Could not finish {Humanize(tracked.JobDefId)} ({Humanize(e.Reason)})",
            tracked.TargetPos);
    }

    private void OnJobCancelled(JobCancelledEvent e)
    {
        if (!_trackedJobs.Remove(e.JobId, out var tracked))
            return;

        AddEntry(tracked.DwarfId, $"Cancelled {Humanize(tracked.JobDefId)}", tracked.TargetPos);
    }

    private void AddEntry(int entityId, string message, Vec3i position, EventLogLinkTarget? linkedTarget = null)
    {
        if (entityId < 0 || string.IsNullOrWhiteSpace(message))
            return;

        if (!_entries.TryGetValue(entityId, out var entries))
        {
            entries = new List<EntityEventLogEntry>();
            _entries[entityId] = entries;
        }

        var (year, month, day, hour) = SnapshotTime();
    entries.Insert(0, new EntityEventLogEntry(message, position, year, month, day, hour, linkedTarget));

        if (entries.Count > MaxEntriesPerEntity)
            entries.RemoveRange(MaxEntriesPerEntity, entries.Count - MaxEntriesPerEntity);
    }

    private (int Year, int Month, int Day, int Hour) SnapshotTime()
    {
        var time = _ctx?.TryGet<TimeSystem>();
        return time is null
            ? (0, 0, 0, 0)
            : (time.Year, time.Month, time.Day, time.Hour);
    }

    private Vec3i? TryGetEntityPosition(int entityId)
    {
        var entity = _ctx!.Get<EntityRegistry>().TryGetById(entityId);
        if (entity is null || !entity.Components.Has<PositionComponent>())
            return null;

        return entity.Components.Get<PositionComponent>().Position;
    }

    private string? ResolveThoughtDescription(int dwarfId, string thoughtId)
    {
        if (!_ctx!.Get<EntityRegistry>().TryGetById<Dwarf>(dwarfId, out var dwarf) || dwarf is null)
            return null;

        return dwarf.Thoughts.Active.LastOrDefault(thought => thought.Id == thoughtId)?.Description;
    }

    private string ResolveEntityName(int entityId)
        => ResolveEntityLink(entityId)?.DisplayName ?? $"#{entityId}";

    private EventLogLinkTarget? ResolveEntityLink(int entityId)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        var entity = registry.TryGetById(entityId);
        if (entity is null)
            return null;

        if (entity is Dwarf dwarf)
            return new EventLogLinkTarget(dwarf.Id, EventLogLinkType.Entity, dwarf.DefId, dwarf.FirstName);

        if (entity is Creature creature)
        {
            var displayName = _ctx.TryGet<DataManager>()?.Creatures.GetOrNull(creature.DefId)?.DisplayName
                              ?? Humanize(creature.DefId);
            return new EventLogLinkTarget(creature.Id, EventLogLinkType.Entity, creature.DefId, displayName);
        }

        return new EventLogLinkTarget(entity.Id, EventLogLinkType.Entity, entity.DefId, Humanize(entity.DefId));
    }

    private EventLogLinkTarget? ResolveItemLink(int itemId, string fallbackItemDefId)
    {
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        if (itemSystem?.TryGetItem(itemId, out var item) != true || item is null)
            return new EventLogLinkTarget(itemId, EventLogLinkType.Item, fallbackItemDefId, ResolveItemDisplayName(itemId, fallbackItemDefId));

        return new EventLogLinkTarget(item.Id, EventLogLinkType.Item, item.DefId, ResolveItemDisplayName(item), item.MaterialId);
    }

    private string ResolveItemDisplayName(int itemId, string fallbackItemDefId)
    {
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        return itemSystem?.TryGetItem(itemId, out var item) == true && item is not null
            ? ResolveItemDisplayName(item)
            : _ctx!.TryGet<DataManager>()?.Items.GetOrNull(fallbackItemDefId)?.DisplayName
              ?? Humanize(fallbackItemDefId);
    }

    private string ResolveItemDisplayName(Item item)
    {
        var corpse = item.Components.TryGet<CorpseComponent>();
        if (corpse is not null)
            return $"Corpse of {corpse.DisplayName}";

        return _ctx!.TryGet<DataManager>()?.Items.GetOrNull(item.DefId)?.DisplayName
               ?? Humanize(item.DefId);
    }

    private static string Humanize(string value)
        => value.Replace('_', ' ');

    private sealed record TrackedJobInfo(int DwarfId, string JobDefId, Vec3i TargetPos);
}

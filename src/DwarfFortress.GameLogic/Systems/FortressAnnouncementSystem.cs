using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Jobs;

namespace DwarfFortress.GameLogic.Systems;

public enum FortressAnnouncementSeverity
{
    Info = 0,
    Attention = 1,
    Warning = 2,
    Critical = 3,
}

public sealed record FortressAnnouncementEntry(
    int Sequence,
    string Message,
    Vec3i Position,
    bool HasLocation,
    FortressAnnouncementSeverity Severity,
    int RepeatCount,
    int Year,
    int Month,
    int Day,
    int Hour);

public sealed class FortressAnnouncementSystem : IGameSystem
{
    private const int MaxEntries = 160;

    private readonly List<FortressAnnouncementEntry> _entries = new();
    private readonly Dictionary<string, int> _lastThrottleHours = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, TrackedJobInfo> _trackedJobs = new();

    private GameContext? _ctx;
    private int _nextSequence = 1;

    public string SystemId => SystemIds.FortressAnnouncementSystem;
    public int UpdateOrder => 96;
    public bool IsEnabled { get; set; } = true;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;

        ctx.EventBus.On<FortressStartedEvent>(OnFortressStarted);
        ctx.EventBus.On<DiscoveryUnlockedEvent>(OnDiscoveryUnlocked);
        ctx.EventBus.On<SeasonChangedEvent>(OnSeasonChanged);
        ctx.EventBus.On<YearStartedEvent>(OnYearStarted);
        ctx.EventBus.On<WorldEventFiredEvent>(OnWorldEventFired);
        ctx.EventBus.On<BuildingPlacementRejectedEvent>(OnBuildingPlacementRejected);
        ctx.EventBus.On<JobAssignedEvent>(OnJobAssigned);
        ctx.EventBus.On<JobFailedEvent>(OnJobFailed);
        ctx.EventBus.On<JobCompletedEvent>(OnJobCompleted);
        ctx.EventBus.On<JobCancelledEvent>(OnJobCancelled);
        ctx.EventBus.On<MoodChangedEvent>(OnMoodChanged);
        ctx.EventBus.On<NeedCriticalEvent>(OnNeedCritical);
        ctx.EventBus.On<EntityDiedEvent>(OnEntityDied);
        ctx.EventBus.On<BehaviorFiredEvent>(OnBehaviorFired);
        ctx.EventBus.On<CombatHitEvent>(OnCombatHit);
        ctx.EventBus.On<FloodedTileEvent>(OnFloodedTile);
        ctx.EventBus.On<DwarfFledFromAnimalEvent>(OnDwarfFledFromAnimal);
        ctx.EventBus.On<GameSavedEvent>(OnGameSaved);
        ctx.EventBus.On<GameLoadedEvent>(OnGameLoaded);
    }

    public void Tick(float delta) { }
    public void OnSave(SaveWriter w) { }

    public void OnLoad(SaveReader r)
    {
        _entries.Clear();
        _lastThrottleHours.Clear();
        _trackedJobs.Clear();
        _nextSequence = 1;
    }

    public IReadOnlyList<FortressAnnouncementEntry> GetEntries(int maxEntries = MaxEntries)
        => _entries.Take(Math.Max(0, maxEntries)).ToArray();

    private void OnFortressStarted(FortressStartedEvent e)
        => Publish("Fortress founded.", FortressAnnouncementSeverity.Attention);

    private void OnDiscoveryUnlocked(DiscoveryUnlockedEvent e)
    {
        var kind = string.Equals(e.Kind, "recipe", StringComparison.OrdinalIgnoreCase) ? "recipe" : "building";
        Publish($"New {kind} discovered: {e.DisplayName}", FortressAnnouncementSeverity.Attention,
            collapseKey: $"discovery:{kind}:{e.Id}");
    }

    private void OnSeasonChanged(SeasonChangedEvent e)
        => Publish($"{e.Season} has come.", FortressAnnouncementSeverity.Attention,
            collapseKey: $"season:{e.Year}:{e.Season}");

    private void OnYearStarted(YearStartedEvent e)
        => Publish($"The year is now {e.Year}.", FortressAnnouncementSeverity.Attention,
            collapseKey: $"year:{e.Year}");

    private void OnWorldEventFired(WorldEventFiredEvent e)
    {
        switch (e.EventDefId)
        {
            case WorldEventIds.MigrantWave:
                Publish($"{e.DisplayName}: new migrants have arrived.", FortressAnnouncementSeverity.Attention,
                    collapseKey: $"world:{e.EventDefId}", throttleHours: 24);
                break;
            case WorldEventIds.GoblinRaid:
                Publish($"{e.DisplayName}: goblins are attacking!", FortressAnnouncementSeverity.Critical,
                    collapseKey: $"world:{e.EventDefId}", throttleHours: 12);
                break;
            default:
                Publish($"World event: {e.DisplayName}", FortressAnnouncementSeverity.Attention,
                    collapseKey: $"world:{e.EventDefId}", throttleHours: 24);
                break;
        }
    }

    private void OnBuildingPlacementRejected(BuildingPlacementRejectedEvent e)
        => Publish($"Cannot build {Humanize(e.BuildingDefId)}: {e.Reason}", FortressAnnouncementSeverity.Warning,
            e.Origin, collapseKey: $"build-rejected:{e.BuildingDefId}:{e.Origin.X}:{e.Origin.Y}:{e.Origin.Z}:{e.Reason}", throttleHours: 2);

    private void OnJobAssigned(JobAssignedEvent e)
    {
        var job = _ctx!.TryGet<JobSystem>()?.GetJob(e.JobId);
        if (job is null || string.Equals(job.JobDefId, JobDefIds.Idle, StringComparison.OrdinalIgnoreCase))
            return;

        _trackedJobs[e.JobId] = new TrackedJobInfo(job.JobDefId, job.TargetPos);
    }

    private void OnJobFailed(JobFailedEvent e)
    {
        if (!_trackedJobs.Remove(e.JobId, out var tracked) ||
            string.Equals(tracked.JobDefId, JobDefIds.Idle, StringComparison.OrdinalIgnoreCase))
            return;

        Publish($"{Humanize(tracked.JobDefId)} failed: {Humanize(e.Reason)}", FortressAnnouncementSeverity.Warning,
            tracked.TargetPos, collapseKey: $"job-failed:{e.JobId}:{e.Reason}");
    }

    private void OnJobCompleted(JobCompletedEvent e)
    {
        _trackedJobs.Remove(e.JobId);
    }

    private void OnJobCancelled(JobCancelledEvent e)
    {
        _trackedJobs.Remove(e.JobId);
    }

    private void OnMoodChanged(MoodChangedEvent e)
    {
        if (e.NewMood > Mood.Unhappy)
            return;

        var name = ResolveEntityName(e.DwarfId);
        Publish($"{name} is now {Humanize(e.NewMood.ToString())}.", FortressAnnouncementSeverity.Warning,
            TryGetEntityPosition(e.DwarfId), collapseKey: $"mood:{e.DwarfId}:{e.NewMood}", throttleHours: 8);
    }

    private void OnNeedCritical(NeedCriticalEvent e)
    {
        var name = ResolveEntityName(e.EntityId);
        Publish($"{name} needs {Humanize(e.NeedId)}.", FortressAnnouncementSeverity.Warning,
            TryGetEntityPosition(e.EntityId), collapseKey: $"need:{e.EntityId}:{e.NeedId}", throttleHours: 8);
    }

    private void OnEntityDied(EntityDiedEvent e)
    {
        if (!e.IsDwarf)
            return;

        var name = string.IsNullOrWhiteSpace(e.DisplayName) ? ResolveEntityName(e.EntityId) : e.DisplayName;
        Publish($"{name} has died. Cause: {Humanize(e.Cause)}.", FortressAnnouncementSeverity.Critical,
            e.Position, collapseKey: $"death:{e.EntityId}");
    }

    private void OnBehaviorFired(BehaviorFiredEvent e)
    {
        if (!string.Equals(e.BehaviorId, BehaviorIds.Tantrum, StringComparison.OrdinalIgnoreCase))
            return;

        var name = ResolveEntityName(e.EntityId);
        Publish($"{name} is throwing a tantrum!", FortressAnnouncementSeverity.Warning,
            TryGetEntityPosition(e.EntityId), collapseKey: $"tantrum:{e.EntityId}", throttleHours: 4);
    }

    private void OnCombatHit(CombatHitEvent e)
    {
        if (!TryGetEntityPosition(e.DefenderId).HasValue)
            return;

        var registry = _ctx!.Get<EntityRegistry>();
        var defender = registry.TryGetById(e.DefenderId);
        if (defender is not Dwarf)
            return;

        Publish($"{ResolveEntityName(e.DefenderId)} was struck for {e.Damage:0.#} damage.", FortressAnnouncementSeverity.Critical,
            TryGetEntityPosition(e.DefenderId), collapseKey: $"combat-hit:{e.DefenderId}", throttleHours: 1);
    }

    private void OnFloodedTile(FloodedTileEvent e)
        => Publish($"Flooding reported at {FormatPosition(e.Position)}.",
            e.Level >= 4 ? FortressAnnouncementSeverity.Warning : FortressAnnouncementSeverity.Attention,
            e.Position, collapseKey: $"flood:{e.Position.X}:{e.Position.Y}:{e.Position.Z}", throttleHours: 2);

    private void OnDwarfFledFromAnimal(DwarfFledFromAnimalEvent e)
        => Publish($"{ResolveEntityName(e.DwarfId)} fled from an animal.", FortressAnnouncementSeverity.Info,
            e.To, collapseKey: $"flee:{e.DwarfId}", throttleHours: 4);

    private void OnGameSaved(GameSavedEvent e)
        => Publish($"Game saved: {System.IO.Path.GetFileName(e.FilePath)}", FortressAnnouncementSeverity.Info,
            collapseKey: $"save:{e.FilePath}");

    private void OnGameLoaded(GameLoadedEvent e)
        => Publish($"Game loaded: {System.IO.Path.GetFileName(e.FilePath)}", FortressAnnouncementSeverity.Info,
            collapseKey: $"load:{e.FilePath}");

    private void Publish(
        string message,
        FortressAnnouncementSeverity severity,
        Vec3i? position = null,
        string? collapseKey = null,
        int throttleHours = 0)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var snapshot = SnapshotTime();
        var hasLocation = position.HasValue;
        var resolvedPosition = position ?? Vec3i.Zero;

        if (_entries.Count > 0)
        {
            var latest = _entries[0];
            if (latest.Message == message &&
                latest.Position == resolvedPosition &&
                latest.HasLocation == hasLocation &&
                latest.Severity == severity)
            {
                _entries[0] = latest with
                {
                    Sequence = _nextSequence++,
                    RepeatCount = latest.RepeatCount + 1,
                    Year = snapshot.Year,
                    Month = snapshot.Month,
                    Day = snapshot.Day,
                    Hour = snapshot.Hour,
                };
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(collapseKey) && throttleHours > 0)
        {
            var absoluteHour = ToAbsoluteHour(snapshot.Year, snapshot.Month, snapshot.Day, snapshot.Hour);
            if (_lastThrottleHours.TryGetValue(collapseKey, out var lastHour) && absoluteHour - lastHour < throttleHours)
                return;

            _lastThrottleHours[collapseKey] = absoluteHour;
        }

        _entries.Insert(0, new FortressAnnouncementEntry(
            Sequence: _nextSequence++,
            Message: message,
            Position: resolvedPosition,
            HasLocation: hasLocation,
            Severity: severity,
            RepeatCount: 1,
            Year: snapshot.Year,
            Month: snapshot.Month,
            Day: snapshot.Day,
            Hour: snapshot.Hour));

        if (_entries.Count > MaxEntries)
            _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
    }

    private (int Year, int Month, int Day, int Hour) SnapshotTime()
    {
        var time = _ctx?.TryGet<TimeSystem>();
        return time is null
            ? (0, 0, 0, 0)
            : (time.Year, time.Month, time.Day, time.Hour);
    }

    private static int ToAbsoluteHour(int year, int month, int day, int hour)
        => (((year * TimeSystem.MonthsPerYear) + month) * TimeSystem.DaysPerMonth + day) * TimeSystem.HoursPerDay + hour;

    private Vec3i? TryGetEntityPosition(int entityId)
    {
        var entity = _ctx!.Get<EntityRegistry>().TryGetById(entityId);
        if (entity is null || !entity.Components.Has<Entities.Components.PositionComponent>())
            return null;

        return entity.Components.Get<Entities.Components.PositionComponent>().Position;
    }

    private string ResolveEntityName(int entityId)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        var entity = registry.TryGetById(entityId);
        if (entity is null)
            return $"#{entityId}";

        if (entity is Dwarf dwarf)
            return dwarf.FirstName;

        if (entity is Creature creature)
            return _ctx.TryGet<DataManager>()?.Creatures.GetOrNull(creature.DefId)?.DisplayName ?? Humanize(creature.DefId);

        return Humanize(entity.DefId);
    }

    private static string FormatPosition(Vec3i position)
        => $"({position.X}, {position.Y}, z{position.Z})";

    private static string Humanize(string value)
        => value.Replace('_', ' ');

    private sealed record TrackedJobInfo(string JobDefId, Vec3i TargetPos);
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

public enum FortressAnnouncementKind
{
    Status = 0,
    Discovery = 1,
    Calendar = 2,
    Migration = 3,
    WorldEvent = 4,
    Threat = 5,
    Construction = 6,
    Labor = 7,
    Mood = 8,
    Need = 9,
    Death = 10,
    Combat = 11,
    Flood = 12,
    Wildlife = 13,
}

public enum FortressAnnouncementSeverity
{
    Info = 0,
    Attention = 1,
    Warning = 2,
    Critical = 3,
}

public sealed record FortressAnnouncementEntry(
    int Sequence,
    FortressAnnouncementKind Kind,
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
    private const float FloodAnnouncementWarmupSeconds = 12f;
    private const float CombatAnnouncementActivitySeconds = 6f;
    private const int MaxCombatAnnouncementLines = 3;
    private const int MaxTrackedCombatActivities = 12;

    private readonly List<FortressAnnouncementEntry> _entries = new();
    private readonly List<CombatAnnouncementActivity> _combatActivities = new();
    private readonly Dictionary<string, int> _lastThrottleHours = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, TrackedJobInfo> _trackedJobs = new();

    private GameContext? _ctx;
    private int _nextSequence = 1;
    private int? _combatAnnouncementSequence;
    private float _floodAnnouncementWarmupRemaining;

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
        ctx.EventBus.On<EntityDiedEvent>(OnEntityDied);
        ctx.EventBus.On<BehaviorFiredEvent>(OnBehaviorFired);
        ctx.EventBus.On<CombatHitEvent>(OnCombatHit);
        ctx.EventBus.On<CombatMissEvent>(OnCombatMiss);
        ctx.EventBus.On<FloodedTileEvent>(OnFloodedTile);
        ctx.EventBus.On<MiningDesignationSafetyCancelledEvent>(OnMiningDesignationSafetyCancelled);
        ctx.EventBus.On<DwarfFledFromAnimalEvent>(OnDwarfFledFromAnimal);
        ctx.EventBus.On<GameSavedEvent>(OnGameSaved);
        ctx.EventBus.On<GameLoadedEvent>(OnGameLoaded);
    }

    public void Tick(float delta)
    {
        if (_floodAnnouncementWarmupRemaining > 0f)
            _floodAnnouncementWarmupRemaining = Math.Max(0f, _floodAnnouncementWarmupRemaining - delta);

        TickCombatAnnouncements(delta);
    }

    public void OnSave(SaveWriter w) { }

    public void OnLoad(SaveReader r)
    {
        _entries.Clear();
        _combatActivities.Clear();
        _lastThrottleHours.Clear();
        _trackedJobs.Clear();
        _nextSequence = 1;
        _combatAnnouncementSequence = null;
        _floodAnnouncementWarmupRemaining = 0f;
    }

    public IReadOnlyList<FortressAnnouncementEntry> GetEntries(int maxEntries = MaxEntries)
        => _entries.Take(Math.Max(0, maxEntries)).ToArray();

    private void OnFortressStarted(FortressStartedEvent e)
    {
        ClearCombatAnnouncements();
        _floodAnnouncementWarmupRemaining = FloodAnnouncementWarmupSeconds;
        Publish(FortressAnnouncementKind.Status, "Fortress founded.", FortressAnnouncementSeverity.Attention);
    }

    private void OnDiscoveryUnlocked(DiscoveryUnlockedEvent e)
    {
        var kind = string.Equals(e.Kind, "recipe", StringComparison.OrdinalIgnoreCase) ? "recipe" : "building";
        Publish(FortressAnnouncementKind.Discovery, $"New {kind} discovered: {e.DisplayName}", FortressAnnouncementSeverity.Attention,
            collapseKey: $"discovery:{kind}:{e.Id}");
    }

    private void OnSeasonChanged(SeasonChangedEvent e)
        => Publish(FortressAnnouncementKind.Calendar, $"{e.Season} has come.", FortressAnnouncementSeverity.Attention,
            collapseKey: $"season:{e.Year}:{e.Season}");

    private void OnYearStarted(YearStartedEvent e)
        => Publish(FortressAnnouncementKind.Calendar, $"The year is now {e.Year}.", FortressAnnouncementSeverity.Attention,
            collapseKey: $"year:{e.Year}");

    private void OnWorldEventFired(WorldEventFiredEvent e)
    {
        switch (e.EventDefId)
        {
            case WorldEventIds.MigrantWave:
                Publish(FortressAnnouncementKind.Migration, $"{e.DisplayName}: new migrants have arrived.", FortressAnnouncementSeverity.Attention,
                    collapseKey: $"world:{e.EventDefId}", throttleHours: 24);
                break;
            case WorldEventIds.GoblinRaid:
                Publish(FortressAnnouncementKind.Threat, $"{e.DisplayName}: goblins are attacking!", FortressAnnouncementSeverity.Critical,
                    collapseKey: $"world:{e.EventDefId}", throttleHours: 12);
                break;
            default:
                Publish(FortressAnnouncementKind.WorldEvent, $"World event: {e.DisplayName}", FortressAnnouncementSeverity.Attention,
                    collapseKey: $"world:{e.EventDefId}", throttleHours: 24);
                break;
        }
    }

    private void OnBuildingPlacementRejected(BuildingPlacementRejectedEvent e)
        => Publish(FortressAnnouncementKind.Construction, $"Cannot build {Humanize(e.BuildingDefId)}: {e.Reason}", FortressAnnouncementSeverity.Warning,
            e.Origin, collapseKey: $"build-rejected:{e.BuildingDefId}:{e.Origin.X}:{e.Origin.Y}:{e.Origin.Z}:{e.Reason}", throttleHours: 2);

    private void OnJobAssigned(JobAssignedEvent e)
    {
        var job = _ctx!.TryGet<JobSystem>()?.GetJob(e.JobId);
        if (job is null ||
            string.Equals(job.JobDefId, JobDefIds.Idle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(job.JobDefId, JobDefIds.EngageHostile, StringComparison.OrdinalIgnoreCase))
            return;

        _trackedJobs[e.JobId] = new TrackedJobInfo(job.JobDefId, job.TargetPos);
    }

    private void OnJobFailed(JobFailedEvent e)
    {
        if (!_trackedJobs.Remove(e.JobId, out var tracked) ||
            string.Equals(tracked.JobDefId, JobDefIds.Idle, StringComparison.OrdinalIgnoreCase))
            return;

        Publish(FortressAnnouncementKind.Labor, $"{Humanize(tracked.JobDefId)} failed: {Humanize(e.Reason)}", FortressAnnouncementSeverity.Warning,
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

    private void OnEntityDied(EntityDiedEvent e)
    {
        if (!e.IsDwarf)
            return;

        var name = string.IsNullOrWhiteSpace(e.DisplayName) ? ResolveEntityName(e.EntityId) : e.DisplayName;
        Publish(FortressAnnouncementKind.Death, $"{name} has died. Cause: {Humanize(e.Cause)}.", FortressAnnouncementSeverity.Critical,
            e.Position, collapseKey: $"death:{e.EntityId}");
    }

    private void OnBehaviorFired(BehaviorFiredEvent e)
    {
        if (!string.Equals(e.BehaviorId, BehaviorIds.Tantrum, StringComparison.OrdinalIgnoreCase))
            return;

        var name = ResolveEntityName(e.EntityId);
        Publish(FortressAnnouncementKind.Mood, $"{name} is throwing a tantrum!", FortressAnnouncementSeverity.Warning,
            TryGetEntityPosition(e.EntityId), collapseKey: $"tantrum:{e.EntityId}", throttleHours: 4);
    }

    private void OnCombatHit(CombatHitEvent e)
        => RecordCombatActivity(e.AttackerId, e.DefenderId, didHit: true);

    private void OnCombatMiss(CombatMissEvent e)
        => RecordCombatActivity(e.AttackerId, e.DefenderId, didHit: false);

    private void OnFloodedTile(FloodedTileEvent e)
    {
        if (_floodAnnouncementWarmupRemaining > 0f)
            return;
        if (!IsNewFlood(e) || !IsFortressRelevantFloodTile(e.Position))
            return;

        Publish(FortressAnnouncementKind.Flood, $"Flooding reported at {FormatPosition(e.Position)}.",
            e.Level >= 4 ? FortressAnnouncementSeverity.Warning : FortressAnnouncementSeverity.Attention,
            e.Position, collapseKey: $"flood:{e.Position.X}:{e.Position.Y}:{e.Position.Z}", throttleHours: 2);
    }

    private void OnDwarfFledFromAnimal(DwarfFledFromAnimalEvent e)
        => Publish(FortressAnnouncementKind.Wildlife, $"{ResolveEntityName(e.DwarfId)} fled from an animal.", FortressAnnouncementSeverity.Info,
            e.To, collapseKey: $"flee:{e.DwarfId}", throttleHours: 4);

    private void OnMiningDesignationSafetyCancelled(MiningDesignationSafetyCancelledEvent e)
    {
        var hazardLabel = string.Equals(e.HazardKind, "damp", StringComparison.OrdinalIgnoreCase)
            ? "Damp stone"
            : string.Equals(e.HazardKind, "warm", StringComparison.OrdinalIgnoreCase)
                ? "Warm stone"
            : Humanize(e.HazardKind);
        Publish(FortressAnnouncementKind.Construction, $"{hazardLabel} detected. Mining halted at {FormatPosition(e.Position)}.",
            FortressAnnouncementSeverity.Warning, e.Position,
            collapseKey: $"mining-hazard:{e.HazardKind}:{e.Position.X}:{e.Position.Y}:{e.Position.Z}", throttleHours: 6);
    }

    private void OnGameSaved(GameSavedEvent e)
        => Publish(FortressAnnouncementKind.Status, $"Game saved: {System.IO.Path.GetFileName(e.FilePath)}", FortressAnnouncementSeverity.Info,
            collapseKey: $"save:{e.FilePath}");

    private void OnGameLoaded(GameLoadedEvent e)
    {
        _floodAnnouncementWarmupRemaining = FloodAnnouncementWarmupSeconds;
        Publish(FortressAnnouncementKind.Status, $"Game loaded: {System.IO.Path.GetFileName(e.FilePath)}", FortressAnnouncementSeverity.Info,
            collapseKey: $"load:{e.FilePath}");
    }

    private void TickCombatAnnouncements(float delta)
    {
        if (_combatActivities.Count == 0)
            return;

        var changed = false;
        for (var index = _combatActivities.Count - 1; index >= 0; index--)
        {
            var activity = _combatActivities[index];
            activity.TimeRemaining -= delta;
            if (activity.TimeRemaining > 0f)
                continue;

            _combatActivities.RemoveAt(index);
            changed = true;
        }

        if (changed)
            SyncCombatAnnouncement();
    }

    private void RecordCombatActivity(int attackerId, int defenderId, bool didHit)
    {
        var activity = BuildCombatActivity(attackerId, defenderId, didHit);
        if (activity is null)
            return;

        _combatActivities.Insert(0, activity);
        if (_combatActivities.Count > MaxTrackedCombatActivities)
            _combatActivities.RemoveRange(MaxTrackedCombatActivities, _combatActivities.Count - MaxTrackedCombatActivities);

        SyncCombatAnnouncement();
    }

    private CombatAnnouncementActivity? BuildCombatActivity(int attackerId, int defenderId, bool didHit)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        var attacker = registry.TryGetById(attackerId);
        var defender = registry.TryGetById(defenderId);
        var position = TryGetEntityPosition(defenderId) ?? TryGetEntityPosition(attackerId);
        var severity = ResolveCombatSeverity(attacker, defender, didHit);
        var message = didHit
            ? $"{ResolveEntityName(attackerId)} hit {ResolveEntityName(defenderId)}."
            : $"{ResolveEntityName(attackerId)} missed {ResolveEntityName(defenderId)}.";

        return new CombatAnnouncementActivity(message, position, severity, CombatAnnouncementActivitySeconds);
    }

    private static FortressAnnouncementSeverity ResolveCombatSeverity(Entity? attacker, Entity? defender, bool didHit)
    {
        if (didHit && defender is Dwarf)
            return FortressAnnouncementSeverity.Critical;

        if (defender is Dwarf)
            return FortressAnnouncementSeverity.Warning;

        if (attacker is Dwarf || defender is Dwarf)
            return FortressAnnouncementSeverity.Attention;

        return didHit ? FortressAnnouncementSeverity.Attention : FortressAnnouncementSeverity.Info;
    }

    private void SyncCombatAnnouncement()
    {
        RemoveCombatAnnouncementEntry();

        if (_combatActivities.Count == 0)
            return;

        var location = ResolveCombatAnnouncementLocation();
        var snapshot = SnapshotTime();
        var entry = new FortressAnnouncementEntry(
            Sequence: _nextSequence++,
            Kind: FortressAnnouncementKind.Combat,
            Message: BuildCombatAnnouncementMessage(),
            Position: location ?? Vec3i.Zero,
            HasLocation: location.HasValue,
            Severity: ResolveCombatAnnouncementSeverity(),
            RepeatCount: 1,
            Year: snapshot.Year,
            Month: snapshot.Month,
            Day: snapshot.Day,
            Hour: snapshot.Hour);

        _entries.Insert(0, entry);
        _combatAnnouncementSequence = entry.Sequence;

        if (_entries.Count > MaxEntries)
            _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
    }

    private string BuildCombatAnnouncementMessage()
    {
        var summaries = SummarizeCombatActivities();
        var visibleCount = Math.Min(MaxCombatAnnouncementLines, summaries.Count);
        var hiddenCount = summaries.Count - visibleCount;
        var builder = new StringBuilder();
        builder.AppendLine("Fight Happening");
        builder.Append(_combatActivities.Count)
            .Append(" exchange")
            .Append(_combatActivities.Count == 1 ? string.Empty : "s")
            .Append(" active.");

        for (var index = 0; index < visibleCount; index++)
        {
            builder.AppendLine();
            builder.Append(FormatCombatActivitySummary(summaries[index]));
        }

        if (hiddenCount > 0)
        {
            builder.AppendLine();
            builder.Append('+')
                .Append(hiddenCount)
                .Append(" more clash")
                .Append(hiddenCount == 1 ? string.Empty : "es")
                .Append('.');
        }

        return builder.ToString();
    }

    private List<CombatAnnouncementSummary> SummarizeCombatActivities()
    {
        var summaries = new List<CombatAnnouncementSummary>();
        var summaryIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var activity in _combatActivities)
        {
            var key = BuildCombatActivityKey(activity);
            if (summaryIndexes.TryGetValue(key, out var existingIndex))
            {
                summaries[existingIndex].Count++;
                continue;
            }

            summaryIndexes[key] = summaries.Count;
            summaries.Add(new CombatAnnouncementSummary(activity));
        }

        return summaries;
    }

    private static string BuildCombatActivityKey(CombatAnnouncementActivity activity)
    {
        if (!activity.HasLocation)
            return activity.Message;

        return $"{activity.Message}|{activity.Position.X}:{activity.Position.Y}:{activity.Position.Z}";
    }

    private static string FormatCombatActivitySummary(CombatAnnouncementSummary summary)
    {
        var line = summary.Activity.HasLocation
            ? $"{summary.Activity.Message} at {FormatPosition(summary.Activity.Position)}"
            : summary.Activity.Message;

        if (summary.Count > 1)
            line += $" x{summary.Count}";

        return line;
    }

    private FortressAnnouncementSeverity ResolveCombatAnnouncementSeverity()
    {
        var severity = FortressAnnouncementSeverity.Info;
        for (var index = 0; index < _combatActivities.Count; index++)
        {
            if (_combatActivities[index].Severity > severity)
                severity = _combatActivities[index].Severity;
        }

        return severity;
    }

    private Vec3i? ResolveCombatAnnouncementLocation()
    {
        Vec3i? location = null;
        foreach (var activity in _combatActivities)
        {
            if (!activity.HasLocation)
                return null;

            if (!location.HasValue)
            {
                location = activity.Position;
                continue;
            }

            if (location.Value != activity.Position)
                return null;
        }

        return location;
    }

    private void ClearCombatAnnouncements()
    {
        _combatActivities.Clear();
        RemoveCombatAnnouncementEntry();
    }

    private void RemoveCombatAnnouncementEntry()
    {
        if (!_combatAnnouncementSequence.HasValue)
            return;

        var existingIndex = _entries.FindIndex(entry => entry.Sequence == _combatAnnouncementSequence.Value);
        if (existingIndex >= 0)
            _entries.RemoveAt(existingIndex);

        _combatAnnouncementSequence = null;
    }

    private bool IsFortressRelevantFloodTile(Vec3i position)
    {
        var map = _ctx!.TryGet<World.WorldMap>();
        var tile = map?.GetTile(position) ?? default;
        if (tile.IsDesignated || tile.IsUnderConstruction)
            return true;

        var spatial = _ctx.TryGet<SpatialIndexSystem>();
        if (spatial is not null)
        {
            if (spatial.GetDwarvesAt(position).Count > 0)
                return true;
            if (spatial.GetCreaturesAt(position).Count > 0)
                return true;
            if (spatial.GetItemsAt(position).Count > 0)
                return true;
            if (spatial.GetBuildingAt(position).HasValue)
                return true;
        }

        var stockpiles = _ctx.TryGet<StockpileManager>();
        if (stockpiles is not null)
        {
            foreach (var stockpile in stockpiles.GetAll())
            {
                if (position.X < Math.Min(stockpile.From.X, stockpile.To.X) || position.X > Math.Max(stockpile.From.X, stockpile.To.X))
                    continue;
                if (position.Y < Math.Min(stockpile.From.Y, stockpile.To.Y) || position.Y > Math.Max(stockpile.From.Y, stockpile.To.Y))
                    continue;
                if (position.Z < Math.Min(stockpile.From.Z, stockpile.To.Z) || position.Z > Math.Max(stockpile.From.Z, stockpile.To.Z))
                    continue;
                return true;
            }
        }

        return false;
    }

    private static bool IsNewFlood(FloodedTileEvent e)
        => e.PreviousLevel == 0 || e.PreviousFluid == FluidType.None;

    private void Publish(
        FortressAnnouncementKind kind,
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
                latest.Kind == kind &&
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
            Kind: kind,
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

    private sealed class CombatAnnouncementActivity
    {
        public CombatAnnouncementActivity(string message, Vec3i? position, FortressAnnouncementSeverity severity, float timeRemaining)
        {
            Message = message;
            Position = position ?? Vec3i.Zero;
            HasLocation = position.HasValue;
            Severity = severity;
            TimeRemaining = timeRemaining;
        }

        public string Message { get; }
        public Vec3i Position { get; }
        public bool HasLocation { get; }
        public FortressAnnouncementSeverity Severity { get; }
        public float TimeRemaining { get; set; }
    }

    private sealed class CombatAnnouncementSummary
    {
        public CombatAnnouncementSummary(CombatAnnouncementActivity activity)
        {
            Activity = activity;
            Count = 1;
        }

        public CombatAnnouncementActivity Activity { get; }
        public int Count { get; set; }
    }
}
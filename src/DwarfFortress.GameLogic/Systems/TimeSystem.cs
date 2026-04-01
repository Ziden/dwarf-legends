using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct DayStartedEvent    (int Year, int Month, int Day);
public record struct SeasonChangedEvent (int Year, Season Season);
public record struct YearStartedEvent   (int Year);

// ─────────────────────────────────────────────────────────────────────────────

public enum Season { Spring, Summer, Autumn, Winter }

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Tracks in-game time: Year / Month / Day / Hour.
/// Emits DayStartedEvent, SeasonChangedEvent, YearStartedEvent.
/// An in-game hour = 60 real seconds (at 1× speed).
/// </summary>
public sealed class TimeSystem : IGameSystem
{
    public string SystemId    => SystemIds.TimeSystem;
    public int    UpdateOrder => 1;   // Very early — other systems may query current time
    public bool   IsEnabled   { get; set; } = true;

    // Elapsed real seconds accumulator
    private float _elapsed = 0f;

    // Real-seconds per in-game hour (adjustable for game-speed)
    public float SecondsPerHour { get; set; } = 60f;

    public int    Year   { get; private set; } = 1;
    public int    Month  { get; private set; } = 1;   // 1–12
    public int    Day    { get; private set; } = 1;   // 1–28
    public int    Hour   { get; private set; } = 6;   // 0–23
    public Season CurrentSeason => (Season)((Month - 1) / 3);

    public const int HoursPerDay   = 24;
    public const int DaysPerMonth  = 28;
    public const int MonthsPerYear = 12;

    private GameContext? _ctx;

    public void Initialize(GameContext ctx) => _ctx = ctx;

    public void Tick(float delta)
    {
        _elapsed += delta;
        if (_elapsed < SecondsPerHour) return;

        _elapsed -= SecondsPerHour;
        AdvanceHour();
    }

    public void OnSave(SaveWriter w)
    {
        w.Write("year",  Year);
        w.Write("month", Month);
        w.Write("day",   Day);
        w.Write("hour",  Hour);
    }

    public void OnLoad(SaveReader r)
    {
        Year  = r.TryRead<int>("year");
        Month = r.TryRead<int>("month");
        Day   = r.TryRead<int>("day");
        Hour  = r.TryRead<int>("hour");
    }

    // ── Private ────────────────────────────────────────────────────────────

    private void AdvanceHour()
    {
        var prevSeason = CurrentSeason;
        Hour++;

        if (Hour < HoursPerDay) return;
        Hour = 0;
        Day++;
        _ctx!.EventBus.Emit(new DayStartedEvent(Year, Month, Day));

        if (Day <= DaysPerMonth) return;
        Day = 1;
        Month++;

        var newSeason = CurrentSeason;
        if (newSeason != prevSeason)
            _ctx.EventBus.Emit(new SeasonChangedEvent(Year, newSeason));

        if (Month <= MonthsPerYear) return;
        Month = 1;
        Year++;
        _ctx.EventBus.Emit(new YearStartedEvent(Year));
    }
}

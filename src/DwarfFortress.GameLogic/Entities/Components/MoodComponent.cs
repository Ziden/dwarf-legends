namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>A dwarf's current overall emotional state.</summary>
public enum Mood
{
    Ecstatic   = 5,
    Happy      = 4,
    Content    = 3,
    Unhappy    = 2,
    Miserable  = 1,
    /// <summary>Threshold where the dwarf may go berserk or catatonic.</summary>
    Sufferer   = 0,
}

/// <summary>
/// The current mood of a sapient entity, derived from the sum of active thoughts.
/// Updated by MoodSystem each tick.
/// </summary>
public sealed class MoodComponent
{
    public Mood  Current    { get; set; } = Mood.Content;

    /// <summary>Cumulative happiness score (–100 to +100) used to derive Current mood.</summary>
    public float Happiness  { get; set; }

    public bool HasSnapped  { get; set; }   // true = dwarf has gone berserk / catatonic

    public static Mood FromHappiness(float happiness) => happiness switch
    {
        >= 0.3f  => Mood.Ecstatic,
        >= 0.1f  => Mood.Happy,
        >= -0.1f => Mood.Content,
        >= -0.3f => Mood.Unhappy,
        >= -0.5f => Mood.Miserable,
        _        => Mood.Sufferer,
    };
}

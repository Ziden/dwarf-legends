using System;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>A single biological need with a 0–1 level (1 = fully satisfied).</summary>
public sealed class Need
{
    public string Name          { get; }
    public float  Level         { get; private set; }
    public float  DecayPerTick  { get; }              // decrease per simulation second
    public float  CriticalThreshold { get; }          // below this → critical event
    public float  TimeAtZeroSeconds { get; private set; }

    public bool IsCritical => Level < CriticalThreshold;
    public bool IsSatisfied => Level >= 0.75f;

    public Need(string name, float decayPerTick, float criticalThreshold = 0.1f)
    {
        Name              = name;
        Level             = 1.0f;
        DecayPerTick      = decayPerTick;
        CriticalThreshold = criticalThreshold;
    }

    public void Decay(float delta)
    {
        Level = Math.Max(0f, Level - DecayPerTick * delta);
        TimeAtZeroSeconds = Level <= 0f ? TimeAtZeroSeconds + Math.Max(0f, delta) : 0f;
    }

    public void Satisfy(float amount)
    {
        Level = Math.Min(1f, Level + amount);
        if (Level > 0f)
            TimeAtZeroSeconds = 0f;
    }

    public void SetLevel(float value, float timeAtZeroSeconds = 0f)
    {
        Level = Math.Clamp(value, 0f, 1f);
        TimeAtZeroSeconds = Level <= 0f ? Math.Max(0f, timeAtZeroSeconds) : 0f;
    }
}

/// <summary>All needs for a sapient entity (dwarf).</summary>
public sealed class NeedsComponent
{
    public Need Hunger     { get; } = new(NeedIds.Hunger,      decayPerTick: 0.002f);
    public Need Thirst     { get; } = new(NeedIds.Thirst,      decayPerTick: 0.004f);
    public Need Sleep      { get; } = new(NeedIds.Sleep,       decayPerTick: 0.001f);
    public Need Social     { get; } = new(NeedIds.Social,      decayPerTick: 0.0005f);
    public Need Recreation { get; } = new(NeedIds.Recreation,  decayPerTick: 0.0008f);

    public Need Get(string needId) => needId switch
    {
        NeedIds.Hunger     => Hunger,
        NeedIds.Thirst     => Thirst,
        NeedIds.Sleep      => Sleep,
        NeedIds.Social     => Social,
        NeedIds.Recreation => Recreation,
        _ => throw new ArgumentException($"[NeedsComponent] Unknown need: '{needId}'.")
    };

    public Need[] All => new[] { Hunger, Thirst, Sleep, Social, Recreation };
}

/// <summary>String constants for need IDs. No magic strings in simulation code.</summary>
public static class NeedIds
{
    public const string Hunger     = "hunger";
    public const string Thirst     = "thirst";
    public const string Sleep      = "sleep";
    public const string Social     = "social";
    public const string Recreation = "recreation";
}

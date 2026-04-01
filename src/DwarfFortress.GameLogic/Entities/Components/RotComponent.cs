using System;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// Tracks corpse decay over time. Progress is normalized 0..1.
/// </summary>
public sealed class RotComponent
{
    private const float SecondsToFullyRot = 4f * 24f * 60f;

    public float Progress { get; private set; }

    public string Stage => Progress switch
    {
        < 0.20f => "fresh",
        < 0.45f => "stale",
        < 0.75f => "rotting",
        _ => "putrid",
    };

    public void Tick(float delta)
    {
        if (delta <= 0f || Progress >= 1f)
            return;

        Progress = Math.Clamp(Progress + (delta / SecondsToFullyRot), 0f, 1f);
    }

    public void Restore(float progress)
        => Progress = Math.Clamp(progress, 0f, 1f);
}
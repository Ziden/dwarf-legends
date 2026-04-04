using System;
using DwarfFortress.GameLogic.Entities.Components;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public static class EmoteVisuals
{
    public const float FadeOutDuration = 1f;

    public static Color ResolveIconColor(Emote emote)
        => emote.Id switch
        {
            EmoteIds.Sleep => new Color(0.60f, 0.72f, 1.00f, 0.95f),
            EmoteIds.Fear => new Color(1.00f, 0.34f, 0.25f, 0.95f),
            EmoteIds.Hungry => new Color(0.95f, 0.66f, 0.20f, 0.95f),
            EmoteIds.Happy => new Color(0.24f, 0.82f, 0.40f, 0.95f),
            EmoteIds.Angry => new Color(0.93f, 0.25f, 0.20f, 0.95f),
            EmoteIds.Sad => new Color(0.37f, 0.48f, 0.82f, 0.95f),
            EmoteIds.Eat => new Color(0.88f, 0.55f, 0.24f, 0.95f),
            EmoteIds.Drink => new Color(0.25f, 0.58f, 0.98f, 0.95f),
            EmoteIds.NeedFood => new Color(0.73f, 0.42f, 0.11f, 0.98f),
            EmoteIds.NeedWater => new Color(0.14f, 0.47f, 0.83f, 0.98f),
            EmoteIds.MoodUp => ResolveMoodUpColor(emote.Intensity),
            EmoteIds.MoodDown => ResolveMoodDownColor(emote.Intensity),
            _ => new Color(0.82f, 0.82f, 0.82f, 0.95f),
        };

    public static Color ResolveBubbleColor(Emote emote)
        => emote.Id switch
        {
            EmoteIds.NeedFood => new Color(1.00f, 0.94f, 0.78f, 0.96f),
            EmoteIds.NeedWater => new Color(0.84f, 0.94f, 1.00f, 0.96f),
            EmoteIds.MoodUp => new Color(0.88f, 1.00f, 0.90f, 0.96f),
            EmoteIds.MoodDown => new Color(1.00f, 0.89f, 0.84f, 0.96f),
            _ => new Color(0.97f, 0.97f, 0.97f, 0.94f),
        };

    public static float ResolveAlpha(Emote emote)
    {
        if (emote.IsPersistent || float.IsPositiveInfinity(emote.TimeLeft))
            return 1f;

        return emote.TimeLeft < FadeOutDuration
            ? Mathf.Clamp(emote.TimeLeft / FadeOutDuration, 0f, 1f)
            : 1f;
    }

    public static float ResolveScale(Emote emote)
    {
        var intensity = Mathf.Clamp(emote.Intensity, 0f, 1f);
        if (emote.VisualStyle == EmoteVisualStyle.Balloon)
            return 0.82f + (intensity * 0.16f);

        return 0.72f + (intensity * 0.12f);
    }

    public static float ResolveWorldLift(Emote emote, bool isCreature)
    {
        var baseLift = emote.VisualStyle == EmoteVisualStyle.Balloon ? 1.74f : 1.60f;
        return isCreature ? baseLift + 0.12f : baseLift;
    }

    private static Color ResolveMoodUpColor(float intensity)
        => intensity switch
        {
            >= 0.85f => new Color(0.15f, 0.74f, 0.24f, 0.98f),
            >= 0.55f => new Color(0.24f, 0.82f, 0.38f, 0.98f),
            _ => new Color(0.40f, 0.84f, 0.48f, 0.98f),
        };

    private static Color ResolveMoodDownColor(float intensity)
        => intensity switch
        {
            >= 0.85f => new Color(0.86f, 0.16f, 0.10f, 0.98f),
            >= 0.55f => new Color(0.92f, 0.28f, 0.12f, 0.98f),
            _ => new Color(0.96f, 0.48f, 0.20f, 0.98f),
        };
}
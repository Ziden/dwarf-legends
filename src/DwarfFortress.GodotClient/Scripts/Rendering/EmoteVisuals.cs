using System.Collections.Generic;
using DwarfFortress.GameLogic.Entities.Components;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public static class EmoteVisuals
{
    public const float FadeOutDuration = 1f;

    private static readonly Dictionary<string, Color> EmoteColors = new()
    {
        { EmoteIds.Sleep, new Color(0.6f, 0.7f, 1.0f, 0.9f) },
        { EmoteIds.Fear, new Color(1.0f, 0.3f, 0.2f, 0.9f) },
        { EmoteIds.Hungry, new Color(1.0f, 0.7f, 0.2f, 0.9f) },
        { EmoteIds.Happy, new Color(0.3f, 1.0f, 0.3f, 0.9f) },
        { EmoteIds.Angry, new Color(1.0f, 0.2f, 0.2f, 0.9f) },
        { EmoteIds.Sad, new Color(0.4f, 0.5f, 0.8f, 0.9f) },
        { EmoteIds.Eat, new Color(0.9f, 0.6f, 0.3f, 0.9f) },
        { EmoteIds.Drink, new Color(0.3f, 0.6f, 1.0f, 0.9f) },
    };

    private static readonly Dictionary<string, string> EmoteSymbols = new()
    {
        { EmoteIds.Sleep, "Z" },
        { EmoteIds.Fear, "!" },
        { EmoteIds.Hungry, "~" },
        { EmoteIds.Happy, "+" },
        { EmoteIds.Angry, ">" },
        { EmoteIds.Sad, "-" },
        { EmoteIds.Eat, "*" },
        { EmoteIds.Drink, "." },
    };

    public static Color ResolveColor(string emoteId)
        => EmoteColors.TryGetValue(emoteId, out var color)
            ? color
            : new Color(0.8f, 0.8f, 0.8f, 0.9f);

    public static string ResolveSymbol(string emoteId)
        => EmoteSymbols.TryGetValue(emoteId, out var symbol)
            ? symbol
            : "?";

    public static float ResolveAlpha(float timeLeft)
        => timeLeft < FadeOutDuration
            ? Mathf.Clamp(timeLeft / FadeOutDuration, 0f, 1f)
            : 1f;
}
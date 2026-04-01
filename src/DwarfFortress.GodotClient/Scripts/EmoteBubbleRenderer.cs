using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;
using Godot;

/// <summary>
/// Renders emote bubbles above entities in the Godot client.
/// Displays simple icon bubbles for sleep, fear, hunger, etc.
/// </summary>
public partial class EmoteBubbleRenderer : Node2D
{
    // Emote configuration
    private const float BubbleWidth = 20f;
    private const float BubbleHeight = 16f;
    private const float BubbleOffsetY = -35f; // Above entity
    private const float FontSize = 10f;
    private const float FadeOutDuration = 1f;

    // Emote color definitions (matching emotes.json)
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

    private GameSimulation? _simulation;
    private EntityRegistry? _registry;
    private Func<int, Vec3i, float, Vector2>? _resolveSmoothedCenter;

    public void Setup(GameSimulation simulation, Func<int, Vec3i, float, Vector2> resolveSmoothedCenter)
    {
        _simulation = simulation;
        _registry = simulation.Context.Get<EntityRegistry>();
        _resolveSmoothedCenter = resolveSmoothedCenter;
    }

    /// <summary>
    /// Draw all active emotes for entities on the current Z layer.
    /// Call this from GameRoot._Draw() after drawing entities.
    /// </summary>
    public void DrawEmotes(Node2D canvas, int currentZ)
    {
        if (_registry is null) return;

        // Draw dwarf emotes
        foreach (var dwarf in _registry.GetAlive<Dwarf>())
        {
            if (dwarf.Position.Position.Z != currentZ) continue;
            if (!dwarf.Emotes.HasEmote) continue;

            var emote = dwarf.Emotes.CurrentEmote!;
            var screenPos = ResolveEntityCenter(dwarf.Id, dwarf.Position.Position, 8f) + new Vector2(0f, BubbleOffsetY);
            DrawEmoteBubble(canvas, screenPos, emote);
        }

        // Draw creature emotes
        foreach (var creature in _registry.GetAlive<Creature>())
        {
            if (creature.Position.Position.Z != currentZ) continue;
            if (!creature.Emotes.HasEmote) continue;

            var emote = creature.Emotes.CurrentEmote!;
            var screenPos = ResolveEntityCenter(creature.Id, creature.Position.Position, 10f) + new Vector2(0f, BubbleOffsetY);
            DrawEmoteBubble(canvas, screenPos, emote);
        }
    }

    private Vector2 ResolveEntityCenter(int entityId, Vec3i pos, float yOffset)
    {
        if (_resolveSmoothedCenter is not null)
            return _resolveSmoothedCenter(entityId, pos, yOffset);

        return WorldToScreenCenter(pos) + new Vector2(0f, yOffset);
    }

    private static void DrawEmoteBubble(Node2D canvas, Vector2 position, Emote emote)
    {
        var color = GetEmoteColor(emote.Id);
        var symbol = GetEmoteSymbol(emote.Id);

        // Calculate fade based on remaining time
        float alpha = 1f;
        if (emote.TimeLeft < FadeOutDuration)
        {
            alpha = emote.TimeLeft / FadeOutDuration;
        }

        var finalColor = new Color(color.R, color.G, color.B, color.A * alpha);

        // Draw bubble background (rounded rectangle approximation)
        var bubbleRect = new Rect2(
            position - new Vector2(BubbleWidth / 2f, BubbleHeight / 2f),
            new Vector2(BubbleWidth, BubbleHeight));

        // Draw bubble with slight transparency
        canvas.DrawRect(bubbleRect, new Color(0.1f, 0.1f, 0.1f, 0.7f * alpha));
        canvas.DrawRect(bubbleRect, finalColor, false, 1.5f);

        // Draw symbol in center
        var font = ThemeDB.FallbackFont;
        var textSize = font.GetStringSize(symbol, HorizontalAlignment.Left, -1, (int)FontSize);
        var textPos = position - textSize / 2f + new Vector2(0f, FontSize / 2f);
        canvas.DrawString(font, textPos, symbol, fontSize: 10, modulate: finalColor);
    }

    private static Color GetEmoteColor(string emoteId)
    {
        if (EmoteColors.TryGetValue(emoteId, out var color))
            return color;
        return new Color(0.8f, 0.8f, 0.8f, 0.9f); // Default gray
    }

    private static string GetEmoteSymbol(string emoteId)
    {
        if (EmoteSymbols.TryGetValue(emoteId, out var symbol))
            return symbol;
        return "?";
    }

    private static Vector2 WorldToScreenCenter(Vec3i pos)
    {
        const int tileSize = 64;
        return new Vector2(pos.X * tileSize + tileSize / 2f, pos.Y * tileSize + tileSize / 2f);
    }
}
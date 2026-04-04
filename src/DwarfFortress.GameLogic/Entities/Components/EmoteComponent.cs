using System;

namespace DwarfFortress.GameLogic.Entities.Components;

public enum EmoteVisualStyle
{
    Symbol = 0,
    Balloon = 1,
}

public enum EmoteCategory
{
    Generic = 0,
    Need = 1,
    Mood = 2,
}

/// <summary>
/// A visual emote (speech bubble icon) displayed above an entity.
/// Used for quick visual feedback like sleeping (Zzz), fear (!), hunger, etc.
/// </summary>
public sealed class Emote
{
    public string Id { get; }
    public float Duration { get; }
    public float TimeLeft { get; private set; }
    public float Intensity { get; }
    public EmoteVisualStyle VisualStyle { get; }
    public EmoteCategory Category { get; }
    public bool IsPersistent { get; }

    public bool IsExpired => !IsPersistent && TimeLeft <= 0f;

    public Emote(
        string id,
        float duration,
        float intensity = 1f,
        EmoteVisualStyle visualStyle = EmoteVisualStyle.Symbol,
        EmoteCategory category = EmoteCategory.Generic,
        bool isPersistent = false)
    {
        Id = id;
        Duration = isPersistent ? float.PositiveInfinity : Math.Max(0f, duration);
        TimeLeft = isPersistent ? float.PositiveInfinity : Math.Max(0f, duration);
        Intensity = Math.Clamp(intensity, 0f, 1f);
        VisualStyle = visualStyle;
        Category = category;
        IsPersistent = isPersistent;
    }

    public void Tick(float delta)
    {
        if (IsPersistent)
            return;

        TimeLeft = Math.Max(0f, TimeLeft - Math.Max(0f, delta));
    }
}

/// <summary>
/// Manages the current emote displayed above an entity.
/// Only one emote can be active at a time; setting a new one replaces the old.
/// </summary>
public sealed class EmoteComponent
{
    private Emote? _transientEmote;
    private Emote? _persistentEmote;

    public Emote? CurrentEmote => ResolveCurrentEmote();

    public Emote? PersistentEmote => _persistentEmote;

    public bool HasEmote => CurrentEmote is not null;

    public void SetEmote(
        string emoteId,
        float duration,
        float intensity = 1f,
        EmoteVisualStyle visualStyle = EmoteVisualStyle.Symbol,
        EmoteCategory category = EmoteCategory.Generic)
    {
        _transientEmote = new Emote(emoteId, duration, intensity, visualStyle, category);
    }

    public void SetPersistentEmote(
        string emoteId,
        float intensity = 1f,
        EmoteVisualStyle visualStyle = EmoteVisualStyle.Balloon,
        EmoteCategory category = EmoteCategory.Generic)
    {
        _persistentEmote = new Emote(emoteId, duration: 0f, intensity, visualStyle, category, isPersistent: true);
    }

    public void ClearEmote()
    {
        if (_transientEmote is not null)
        {
            _transientEmote = null;
            return;
        }

        _persistentEmote = null;
    }

    public void ClearPersistentEmote()
    {
        _persistentEmote = null;
    }

    public void Tick(float delta)
    {
        if (_transientEmote is null)
            return;

        _transientEmote.Tick(delta);
        if (_transientEmote.IsExpired)
            _transientEmote = null;
    }

    private Emote? ResolveCurrentEmote()
    {
        if (_transientEmote is not null)
        {
            if (_transientEmote.IsExpired)
            {
                _transientEmote = null;
            }
            else
            {
                return _transientEmote;
            }
        }

        return _persistentEmote;
    }
}

/// <summary>
/// String constants for emote IDs. No magic strings in simulation code.
/// </summary>
public static class EmoteIds
{
    public const string Sleep   = "sleep";
    public const string Fear    = "fear";
    public const string Hungry  = "hungry";
    public const string Happy   = "happy";
    public const string Angry   = "angry";
    public const string Sad     = "sad";
    public const string Eat     = "eat";
    public const string Drink   = "drink";
    public const string NeedFood = "need_food";
    public const string NeedWater = "need_water";
    public const string MoodUp   = "mood_up";
    public const string MoodDown = "mood_down";
}
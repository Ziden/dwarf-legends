using System;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// A visual emote (speech bubble icon) displayed above an entity.
/// Used for quick visual feedback like sleeping (Zzz), fear (!), hunger, etc.
/// </summary>
public sealed class Emote
{
    public string Id { get; }
    public float Duration { get; }
    public float TimeLeft { get; private set; }

    public bool IsExpired => TimeLeft <= 0f;

    public Emote(string id, float duration)
    {
        Id = id;
        Duration = duration;
        TimeLeft = duration;
    }

    public void Tick(float delta)
    {
        if (TimeLeft < 0f) return;
        TimeLeft = Math.Max(0f, TimeLeft - delta);
    }
}

/// <summary>
/// Manages the current emote displayed above an entity.
/// Only one emote can be active at a time; setting a new one replaces the old.
/// </summary>
public sealed class EmoteComponent
{
    private Emote? _currentEmote;

    public Emote? CurrentEmote => _currentEmote;

    public bool HasEmote => _currentEmote is not null && !_currentEmote.IsExpired;

    public void SetEmote(string emoteId, float duration)
    {
        _currentEmote = new Emote(emoteId, duration);
    }

    public void ClearEmote()
    {
        _currentEmote = null;
    }

    public void Tick(float delta)
    {
        if (_currentEmote is not null)
        {
            _currentEmote.Tick(delta);
            if (_currentEmote.IsExpired)
                _currentEmote = null;
        }
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
}
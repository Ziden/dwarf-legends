using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// A single timed status effect on an entity.
/// </summary>
public sealed class StatusEffect
{
    public string Id              { get; }
    public float  RemainingSeconds { get; set; }

    public StatusEffect(string id, float durationSeconds)
    {
        Id               = id;
        RemainingSeconds = durationSeconds;
    }

    public bool IsExpired => RemainingSeconds <= 0f;
}

/// <summary>
/// Holds all active status effects for a dwarf (nausea, drunk, etc.).
/// </summary>
public sealed class StatusEffectComponent
{
    private readonly List<StatusEffect> _effects = new();

    public IReadOnlyList<StatusEffect> All => _effects;

    public bool Has(string id)
    {
        foreach (var e in _effects)
            if (e.Id == id) return true;
        return false;
    }

    /// <summary>Adds or refreshes an effect, taking the longer duration.</summary>
    public void Apply(string id, float durationSeconds)
    {
        foreach (var e in _effects)
        {
            if (e.Id == id)
            {
                if (durationSeconds > e.RemainingSeconds)
                    e.RemainingSeconds = durationSeconds;
                return;
            }
        }
        _effects.Add(new StatusEffect(id, durationSeconds));
    }

    /// <summary>Ticks all effects down. Removes expired ones.</summary>
    public void Tick(float delta)
    {
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            _effects[i].RemainingSeconds -= delta;
            if (_effects[i].IsExpired)
                _effects.RemoveAt(i);
        }
    }
}

public static class StatusEffectIds
{
    public const string Nausea = "nausea";
}

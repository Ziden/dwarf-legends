using System;
using System.Collections.Generic;
using System.Linq;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>A single thought that affects happiness. Can be positive or negative.</summary>
public sealed class Thought
{
    public string  Id           { get; }
    public string  Description  { get; }
    public float   HappinessMod { get; }   // positive = good, negative = bad
    public float   TimeLeft     { get; private set; } // seconds; -1 = permanent

    public bool IsExpired => TimeLeft == 0f;

    public Thought(string id, string description, float happinessMod, float duration = -1f)
    {
        Id           = id;
        Description  = description;
        HappinessMod = happinessMod;
        TimeLeft     = duration;
    }

    public void Tick(float delta)
    {
        if (TimeLeft < 0f) return;
        TimeLeft = Math.Max(0f, TimeLeft - delta);
    }
}

/// <summary>Manages active thoughts for a sapient entity.</summary>
public sealed class ThoughtComponent
{
    private readonly List<Thought> _thoughts = new();

    public void AddThought(Thought thought)
    {
        // Replace existing thought of the same ID to avoid stacking
        _thoughts.RemoveAll(t => t.Id == thought.Id);
        _thoughts.Add(thought);
    }

    public void RemoveThought(string thoughtId) =>
        _thoughts.RemoveAll(t => t.Id == thoughtId);

    public bool HasThought(string thoughtId) =>
        _thoughts.Any(t => t.Id == thoughtId);

    public void Tick(float delta)
    {
        foreach (var t in _thoughts) t.Tick(delta);
        _thoughts.RemoveAll(t => t.IsExpired);
    }

    /// <summary>Sum of all active thought happiness modifiers.</summary>
    public float TotalHappiness => _thoughts.Sum(t => t.HappinessMod);

    public IReadOnlyList<Thought> Active => _thoughts;
    public int Count => _thoughts.Count;
}

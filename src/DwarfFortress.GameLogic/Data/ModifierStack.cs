using System.Collections.Generic;
using System.Linq;

namespace DwarfFortress.GameLogic.Data;

/// <summary>
/// Resolves a final stat value from a set of layered modifiers.
/// Resolution order: (base + flat) * (1 + sumPercentAdd) * product(1 + PercentMult)
/// </summary>
public sealed class ModifierStack
{
    private readonly List<Modifier> _modifiers = new();

    /// <summary>Add a modifier to the stack.</summary>
    public void Add(Modifier modifier) => _modifiers.Add(modifier);

    /// <summary>Remove all modifiers with the given source ID.</summary>
    public void Remove(string sourceId) =>
        _modifiers.RemoveAll(m => m.SourceId == sourceId);

    /// <summary>Returns true if any modifier with the given source ID is active.</summary>
    public bool Has(string sourceId) =>
        _modifiers.Any(m => m.SourceId == sourceId);

    /// <summary>Advance all timed modifiers; remove expired ones.</summary>
    public void Tick(float delta)
    {
        for (int i = _modifiers.Count - 1; i >= 0; i--)
        {
            var mod = _modifiers[i];
            if (mod.Duration < 0f) continue; // permanent

            var remaining = mod.Duration - delta;
            if (remaining <= 0f)
                _modifiers.RemoveAt(i);
            else
                _modifiers[i] = mod with { Duration = remaining };
        }
    }

    /// <summary>
    /// Resolve the final value from the base stat.
    /// Formula: (base + flatSum) * (1 + percentAddSum) * pctMultProduct
    /// </summary>
    public float Resolve(float baseValue)
    {
        float flatSum      = _modifiers.Where(m => m.Type == ModType.Flat)
                                       .Sum(m => m.Value);
        float percentAdd   = _modifiers.Where(m => m.Type == ModType.PercentAdd)
                                       .Sum(m => m.Value);
        float percentMult  = _modifiers.Where(m => m.Type == ModType.PercentMult)
                                       .Aggregate(1f, (acc, m) => acc * (1f + m.Value));

        return (baseValue + flatSum) * (1f + percentAdd) * percentMult;
    }

    public int Count => _modifiers.Count;

    public IReadOnlyList<Modifier> All => _modifiers;
}

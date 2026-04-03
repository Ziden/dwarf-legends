using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// Stores dwarf attributes (1-5 scale) at runtime.
/// Each attribute is resolved against its definition for effects.
/// </summary>
public sealed class DwarfAttributeComponent
{
    private readonly Dictionary<string, int> _levels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DwarfAttribute> _resolved = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, int> AllLevels => _levels;

    /// <summary>Get the level (1-5) for an attribute. Returns 3 (default) if not set.</summary>
    public int GetLevel(string attributeId)
        => _levels.TryGetValue(attributeId, out var level) ? level : 3;

    /// <summary>Set an attribute level (clamped to 1-5).</summary>
    public void SetLevel(string attributeId, int level)
    {
        _levels[attributeId] = Math.Clamp(level, 1, 5);
        _resolved.Remove(attributeId); // invalidate cached resolved
    }

    public void SetLevels(IReadOnlyDictionary<string, int>? levels)
    {
        Clear();
        if (levels is null)
            return;

        foreach (var (attributeId, level) in levels)
        {
            if (string.IsNullOrWhiteSpace(attributeId))
                continue;

            SetLevel(attributeId, level);
        }
    }

    public void Randomize(IEnumerable<string> attributeIds, Random rng)
    {
        ArgumentNullException.ThrowIfNull(attributeIds);
        ArgumentNullException.ThrowIfNull(rng);

        Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attributeId in attributeIds)
        {
            if (string.IsNullOrWhiteSpace(attributeId) || !seen.Add(attributeId))
                continue;

            SetLevel(attributeId, RollRandom(rng));
        }
    }

    /// <summary>Get or create a resolved DwarfAttribute (1-5 with def lookup).</summary>
    public DwarfAttribute GetResolved(string attributeId, Registry<DwarfAttributeDef> defs)
    {
        if (!_resolved.TryGetValue(attributeId, out var resolved))
        {
            var level = GetLevel(attributeId);
            var def = defs.GetOrNull(attributeId);
            resolved = def is not null
                ? new DwarfAttribute(attributeId, level, def)
                : new DwarfAttribute(attributeId, level, new DwarfAttributeDef(attributeId, attributeId, "", "", TagSet.Empty, new Dictionary<string, AttributeEffectCurve>()));
            _resolved[attributeId] = resolved;
        }
        return resolved;
    }

    /// <summary>Get all attribute levels that are not default (level != 3).</summary>
    public IEnumerable<DwarfAttribute> GetVisible(Registry<DwarfAttributeDef> defs)
    {
        foreach (var kv in _levels)
        {
            if (kv.Value == 3) continue;
            var def = defs.GetOrNull(kv.Key);
            if (def is not null)
                yield return new DwarfAttribute(kv.Key, kv.Value, def);
        }
    }

    /// <summary>Roll a random attribute value biased toward the mean (2-4 range).</summary>
    /// Uses a triangular distribution: 3 is most common, 2 and 4 are common, 1 and 5 are rare.
    public static int RollRandom(Random rng)
    {
        // Triangular distribution: roll 2d3 and map 2-6 onto 1-5.
        // This keeps 3 as the most common result while still allowing both extremes.
        var roll = rng.Next(1, 4) + rng.Next(1, 4); // 2-6
        return roll switch
        {
            2 => 1,
            3 => 2,
            4 => 3,
            5 => 4,
            6 => 5,
            _ => 3,
        };
    }

    /// <summary>Roll a more extreme value (biased toward 2 or 4).</summary>
    public static int RollBiased(Random rng, int biasLevel)
    {
        // Bias: higher biasLevel → more likely to roll higher
        if (biasLevel >= 4)
        {
            // 60% chance of 4-5, 40% chance of 2-3
            return rng.NextDouble() < 0.6
                ? rng.Next(4, 6)
                : rng.Next(2, 4);
        }
        if (biasLevel <= 2)
        {
            // 60% chance of 1-2, 40% chance of 3-4
            return rng.NextDouble() < 0.6
                ? rng.Next(1, 3)
                : rng.Next(3, 5);
        }
        return RollRandom(rng);
    }

    public void Clear()
    {
        _levels.Clear();
        _resolved.Clear();
    }

    public int Count => _levels.Count;
}
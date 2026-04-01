using System;
using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// Tracks the concentration of named substances in an entity's bloodstream.
/// Concentrations accumulate from ingestion/contact and decay via metabolism over time.
///
/// A concentration of 1.0 represents a "fully saturated" threshold;
/// values above 1.0 are legal (extreme doses).
/// </summary>
public sealed class BodyChemistryComponent
{
    // Default metabolic decay rates per substance (concentration units / simulated second).
    // Any substance not listed here decays at DefaultDecayRate.
    private static readonly Dictionary<string, float> DecayRates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alcohol"]    = 0.01f,   // ~100 sim-seconds to fully metabolize 1 unit
        ["caffeine"]   = 0.02f,
        ["poison"]     = 0.002f,  // slow to clear
        ["magma_heat"] = 0.05f,
        ["mud"]        = 0.1f,
    };

    private const float DefaultDecayRate = 0.005f;

    private readonly Dictionary<string, float> _substances = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Add (or accumulate) a substance concentration.</summary>
    public void AddSubstance(string substanceId, float amount)
    {
        _substances.TryGetValue(substanceId, out var current);
        _substances[substanceId] = current + amount;
    }

    /// <summary>Get current concentration; returns 0 if absent.</summary>
    public float Get(string substanceId)
        => _substances.TryGetValue(substanceId, out var v) ? v : 0f;

    /// <summary>True if concentration is above threshold.</summary>
    public bool Exceeds(string substanceId, float threshold)
        => Get(substanceId) > threshold;

    /// <summary>Decay all substances by metabolic rates. Call once per tick.</summary>
    public void DecayAll(float delta)
    {
        // Snapshot keys to avoid mutation during iteration
        var keys = new List<string>(_substances.Keys);
        foreach (var key in keys)
        {
            var rate = DecayRates.TryGetValue(key, out var r) ? r : DefaultDecayRate;
            var newVal = Math.Max(0f, _substances[key] - rate * delta);
            if (newVal <= 0f)
                _substances.Remove(key);
            else
                _substances[key] = newVal;
        }
    }

    public IEnumerable<KeyValuePair<string, float>> All => _substances;
}

/// <summary>String constants for known substance IDs.</summary>
public static class SubstanceIds
{
    public const string Alcohol   = "alcohol";
    public const string Caffeine  = "caffeine";
    public const string Poison    = "poison";
    public const string MagmaHeat = "magma_heat";
    public const string Mud       = "mud";
    public const string Blood     = "blood";
}

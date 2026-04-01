using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Data;

/// <summary>
/// A single effect block from a JSON definition.
/// Used in ReactionDef consequences, item use effects, food/drink effects, etc.
/// </summary>
public sealed record EffectBlock(
    string                      Op,
    IReadOnlyDictionary<string, string> Params);

/// <summary>Helpers for reading typed values from EffectBlock params.</summary>
public static class EffectBlockExtensions
{
    public static string GetString(this EffectBlock block, string key)
    {
        if (block.Params.TryGetValue(key, out var val))
            return val;

        throw new KeyNotFoundException(
            $"[EffectBlock op='{block.Op}'] Missing required parameter '{key}'.");
    }

    public static float GetFloat(this EffectBlock block, string key, float defaultValue = 0f)
        => block.Params.TryGetValue(key, out var val) && float.TryParse(val, out var f)
            ? f
            : defaultValue;

    public static int GetInt(this EffectBlock block, string key, int defaultValue = 0)
        => block.Params.TryGetValue(key, out var val) && int.TryParse(val, out var i)
            ? i
            : defaultValue;

    public static bool GetBool(this EffectBlock block, string key, bool defaultValue = false)
        => block.Params.TryGetValue(key, out var val) && bool.TryParse(val, out var b)
            ? b
            : defaultValue;
}

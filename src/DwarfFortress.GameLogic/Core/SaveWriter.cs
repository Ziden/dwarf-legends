using System.Collections.Generic;
using System.Text.Json;

namespace DwarfFortress.GameLogic.Core;

/// <summary>
/// Thin wrapper for writing system state to a save slot.
/// Uses System.Text.Json under the hood — no Godot dependency.
/// </summary>
public sealed class SaveWriter
{
    private readonly Dictionary<string, JsonElement> _data = new();
    private readonly JsonSerializerOptions _options = new() { WriteIndented = false };

    /// <summary>Persist a serializable value under the given key.</summary>
    public void Write<T>(string key, T value)
        => _data[key] = JsonSerializer.SerializeToElement(value, _options);

    /// <summary>Returns the full save data as a JSON string.</summary>
    public string Serialize()
        => JsonSerializer.Serialize(_data, _options);
}

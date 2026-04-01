using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DwarfFortress.GameLogic.Core;

/// <summary>
/// Thin wrapper for restoring system state from a save slot.
/// Uses System.Text.Json under the hood — no Godot dependency.
/// </summary>
public sealed class SaveReader
{
    private readonly Dictionary<string, JsonElement> _data;

    public SaveReader(string json)
    {
        _data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                ?? throw new ArgumentException("Invalid save JSON.", nameof(json));
    }

    /// <summary>Read a value by key. Throws if the key is absent.</summary>
    public T Read<T>(string key)
    {
        if (!_data.TryGetValue(key, out var element))
            throw new KeyNotFoundException($"[SaveReader] Key '{key}' not found in save data.");

        return element.Deserialize<T>()
               ?? throw new InvalidOperationException($"[SaveReader] Key '{key}' deserialized to null.");
    }

    /// <summary>Try to read a value by key; returns default if absent.</summary>
    public T? TryRead<T>(string key)
        => _data.TryGetValue(key, out var element) ? element.Deserialize<T>() : default;
}

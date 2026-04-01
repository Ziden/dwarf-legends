using System;
using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Data;

/// <summary>
/// Typed, read-only registry for content definitions loaded from JSON.
/// All mutations happen during DataManager initialization only.
/// After Initialize() completes, the registry is effectively sealed.
/// </summary>
public sealed class Registry<T> where T : class
{
    private readonly Dictionary<string, T> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _sealed;

    /// <summary>Register a definition by its string ID. Throws on duplicate.</summary>
    public void Add(string id, T definition)
    {
        if (_sealed)
            throw new InvalidOperationException(
                $"[Registry<{typeof(T).Name}>] Cannot add '{id}' — registry is sealed after initialization.");

        if (_entries.ContainsKey(id))
            throw new InvalidOperationException(
                $"[Registry<{typeof(T).Name}>] Duplicate definition ID: '{id}'.");

        _entries[id] = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    /// <summary>
    /// Get a definition by ID. Throws if not found, providing a clear error message.
    /// Prefer this over GetOrNull in simulation code — missing defs are config errors.
    /// </summary>
    public T Get(string id)
    {
        if (_entries.TryGetValue(id, out var def))
            return def;

        throw new KeyNotFoundException(
            $"[Registry<{typeof(T).Name}>] Definition '{id}' not found. " +
            $"Check your JSON data files.");
    }

    /// <summary>Returns null if the ID is not registered.</summary>
    public T? GetOrNull(string id)
        => _entries.TryGetValue(id, out var def) ? def : null;

    /// <summary>Returns true if the given ID is registered.</summary>
    public bool Contains(string id) => _entries.ContainsKey(id);

    /// <summary>All registered definitions.</summary>
    public IReadOnlyCollection<T> All() => _entries.Values;

    /// <summary>All registered IDs.</summary>
    public IReadOnlyCollection<string> AllIds() => _entries.Keys;

    /// <summary>Number of registered definitions.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Seals the registry after initialization.
    /// Called by DataManager after all JSON files are loaded.
    /// </summary>
    internal void Seal() => _sealed = true;
}

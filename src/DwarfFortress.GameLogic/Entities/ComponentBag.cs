using System;
using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Entities;

/// <summary>
/// Holds typed components for an entity.
/// Components are plain C# objects — no Godot Node inheritance.
/// </summary>
public sealed class ComponentBag
{
    private readonly Dictionary<Type, object> _components = new();

    /// <summary>Add a component. Throws if this type is already present.</summary>
    public void Add<T>(T component) where T : class
    {
        var key = typeof(T);
        if (_components.ContainsKey(key))
            throw new InvalidOperationException(
                $"[ComponentBag] Component '{key.Name}' already added. Use Get<T>() to access it.");

        _components[key] = component ?? throw new ArgumentNullException(nameof(component));
    }

    /// <summary>Get a component. Throws if not present.</summary>
    public T Get<T>() where T : class
    {
        if (_components.TryGetValue(typeof(T), out var c))
            return (T)c;

        throw new InvalidOperationException(
            $"[ComponentBag] Component '{typeof(T).Name}' not found. " +
            $"Ensure it was added during entity construction.");
    }

    /// <summary>Try to get a component; returns null if not present.</summary>
    public T? TryGet<T>() where T : class
        => _components.TryGetValue(typeof(T), out var c) ? (T)c : null;

    /// <summary>Returns true if this bag contains a component of type T.</summary>
    public bool Has<T>() where T : class => _components.ContainsKey(typeof(T));

    /// <summary>Remove a component by type. Returns true if it was present.</summary>
    public bool Remove<T>() where T : class => _components.Remove(typeof(T));
}

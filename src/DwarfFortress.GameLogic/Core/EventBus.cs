using System;
using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Core;

/// <summary>
/// Typed, pure-C# event dispatcher.
/// Systems subscribe during Initialize(); events are emitted synchronously on the simulation thread.
/// The GodotEventBridge (Godot project) forwards selected events to Godot signals.
/// </summary>
public sealed class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _subscriptions = new();

    /// <summary>Subscribe to all events of type <typeparamref name="T"/>.</summary>
    public void On<T>(Action<T> handler)
    {
        var key = typeof(T);
        if (!_subscriptions.TryGetValue(key, out var list))
        {
            list = new List<Delegate>();
            _subscriptions[key] = list;
        }
        list.Add(handler);
    }

    /// <summary>Remove a previously registered handler for <typeparamref name="T"/>.</summary>
    public void Off<T>(Action<T> handler)
    {
        if (_subscriptions.TryGetValue(typeof(T), out var list))
            list.Remove(handler);
    }

    /// <summary>
    /// Emit an event to all subscribers of type <typeparamref name="T"/>.
    /// Subscribers are called in registration order on the calling thread.
    /// </summary>
    public void Emit<T>(T ev)
    {
        if (!_subscriptions.TryGetValue(typeof(T), out var list))
            return;

        // Snapshot to avoid mutation during iteration
        var snapshot = list.ToArray();
        foreach (var d in snapshot)
            ((Action<T>)d)(ev);
    }

    /// <summary>Returns the number of registered handlers for <typeparamref name="T"/>.</summary>
    public int SubscriberCount<T>()
        => _subscriptions.TryGetValue(typeof(T), out var list) ? list.Count : 0;
}

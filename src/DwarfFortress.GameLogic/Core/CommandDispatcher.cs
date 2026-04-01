using System;
using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Core;

/// <summary>
/// Routes typed ICommand objects to registered handlers.
/// Systems register their handlers during Initialize().
/// Godot InputHandler calls Dispatch() — no simulation internals exposed to the UI.
/// </summary>
public sealed class CommandDispatcher
{
    private readonly Dictionary<Type, Action<ICommand>> _handlers = new();
    private readonly ILogger _logger;

    public CommandDispatcher(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Register a strongly-typed handler for command type <typeparamref name="T"/>.
    /// Each type may have at most one handler; the last registration wins.
    /// </summary>
    public void Register<T>(Action<T> handler) where T : ICommand
        => _handlers[typeof(T)] = cmd => handler((T)cmd);

    /// <summary>
    /// Dispatch a command to its registered handler.
    /// If no handler is registered, logs a warning and does nothing.
    /// </summary>
    public void Dispatch(ICommand command)
    {
        if (_handlers.TryGetValue(command.GetType(), out var handler))
            handler(command);
        else
            _logger.Warn($"[CommandDispatcher] No handler registered for {command.GetType().Name}");
    }

    /// <summary>Returns true if a handler is registered for <typeparamref name="T"/>.</summary>
    public bool HasHandler<T>() where T : ICommand
        => _handlers.ContainsKey(typeof(T));
}

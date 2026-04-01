using System;
using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Core;

/// <summary>
/// Passed to every IGameSystem.Initialize().
/// Provides typed handles to cross-cutting services without creating circular dependencies.
/// Systems communicate via EventBus after init — never hold direct IGameSystem references.
/// </summary>
public sealed class GameContext
{
    public EventBus          EventBus          { get; }
    public CommandDispatcher Commands          { get; }
    public ILogger           Logger            { get; }
    public IDataSource       DataSource        { get; }

    private readonly Dictionary<Type, IGameSystem> _systemMap = new();

    public GameContext(
        EventBus          eventBus,
        CommandDispatcher commands,
        ILogger           logger,
        IDataSource       dataSource)
    {
        EventBus   = eventBus   ?? throw new ArgumentNullException(nameof(eventBus));
        Commands   = commands   ?? throw new ArgumentNullException(nameof(commands));
        Logger     = logger     ?? throw new ArgumentNullException(nameof(logger));
        DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    /// <summary>
    /// Register a system so it can be resolved by type.
    /// Called by GameSimulation before Initialize() is invoked.
    /// </summary>
    internal void RegisterSystem(IGameSystem system)
        => _systemMap[system.GetType()] = system;

    /// <summary>
    /// Resolve a system by concrete type.
    /// Only call this during Initialize(), never during Tick() — use EventBus instead.
    /// </summary>
    public T Get<T>() where T : class, IGameSystem
    {
        if (_systemMap.TryGetValue(typeof(T), out var sys))
            return (T)sys;

        throw new InvalidOperationException(
            $"[GameContext] System '{typeof(T).Name}' not registered. " +
            $"Ensure it is added to GameSimulation before Initialize() is called.");
    }

    /// <summary>Returns null if the system is not registered.</summary>
    public T? TryGet<T>() where T : class, IGameSystem
        => _systemMap.TryGetValue(typeof(T), out var sys) ? (T)sys : null;
}

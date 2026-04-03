using System;
using System.Collections.Generic;
using System.Linq;

namespace DwarfFortress.GameLogic.Core;

/// <summary>
/// Top-level entry point for the pure-C# simulation.
/// Owns the EventBus, CommandDispatcher, and all IGameSystem instances.
/// Constructed by GameBootstrapper (Godot) or ConsoleRunner (tests/profiling).
/// </summary>
public sealed class GameSimulation
{
    public EventBus          EventBus          { get; }
    public CommandDispatcher CommandDispatcher { get; }
    public GameContext       Context           { get; }
    public SimulationProfiler Profiler         { get; }

    private readonly List<IGameSystem>   _systems = new();
    private          IGameSystem[]       _orderedSystems = Array.Empty<IGameSystem>();
    private readonly ILogger             _logger;
    private          bool                _initialized;

    public GameSimulation(ILogger logger, IDataSource dataSource)
    {
        _logger           = logger     ?? throw new ArgumentNullException(nameof(logger));
        EventBus          = new EventBus();
        CommandDispatcher = new CommandDispatcher(logger);
        Profiler          = new SimulationProfiler();
        Context           = new GameContext(EventBus, CommandDispatcher, logger, dataSource, Profiler);
    }

    // ── System Registration ────────────────────────────────────────────────

    /// <summary>
    /// Register a system. Must be called before Initialize().
    /// Systems are stored in the order registered but initialized/ticked by UpdateOrder.
    /// </summary>
    public void RegisterSystem(IGameSystem system)
    {
        if (_initialized)
            throw new InvalidOperationException(
                "[GameSimulation] Cannot register systems after Initialize() has been called.");

        _systems.Add(system);
        Context.RegisterSystem(system);
        _logger.Debug($"[GameSimulation] Registered system: {system.SystemId} (order={system.UpdateOrder})");
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    /// <summary>
    /// Initialize all registered systems in UpdateOrder.
    /// Must be called exactly once, after all RegisterSystem() calls.
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
            throw new InvalidOperationException("[GameSimulation] Already initialized.");

        _orderedSystems = _systems.OrderBy(s => s.UpdateOrder).ToArray();
        foreach (var system in _orderedSystems)
        {
            _logger.Debug($"[GameSimulation] Initializing {system.SystemId}...");
            system.Initialize(Context);
        }

        _initialized = true;
        _logger.Info($"[GameSimulation] Initialized {_systems.Count} systems.");
    }

    /// <summary>
    /// Advance the simulation by one tick.
    /// Only enabled systems are ticked, in UpdateOrder.
    /// </summary>
    public void Tick(float delta)
    {
        if (!_initialized)
            throw new InvalidOperationException("[GameSimulation] Call Initialize() before Tick().");

        Profiler.BeginFrame(delta);
        try
        {
            foreach (var system in _orderedSystems)
            {
                if (!system.IsEnabled)
                    continue;

                Profiler.BeginSystem(system.SystemId, system.UpdateOrder);
                try
                {
                    system.Tick(delta);
                }
                finally
                {
                    Profiler.EndSystem();
                }
            }
        }
        finally
        {
            Profiler.EndFrame();
        }
    }

    // ── Save / Load ────────────────────────────────────────────────────────

    /// <summary>Collect save data from every system and return the serialized JSON.</summary>
    public string Save()
    {
        var writer = new SaveWriter();
        foreach (var system in _systems)
            system.OnSave(writer);
        return writer.Serialize();
    }

    /// <summary>Distribute saved JSON to every system for restoration.</summary>
    public void Load(string saveJson)
    {
        var reader = new SaveReader(saveJson);
        foreach (var system in _systems)
            system.OnLoad(reader);
    }

    // ── Diagnostics ────────────────────────────────────────────────────────

    /// <summary>Returns a snapshot of registered system IDs in UpdateOrder for diagnostics.</summary>
    public IReadOnlyList<string> GetSystemIds()
        => _orderedSystems.Length > 0
            ? _orderedSystems.Select(s => s.SystemId).ToList()
            : _systems.OrderBy(s => s.UpdateOrder).Select(s => s.SystemId).ToList();
}

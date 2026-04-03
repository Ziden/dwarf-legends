using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GodotClient.Presentation;

/// <summary>
/// Owns fixed-step simulation ticking for the Godot client.
/// Keeps pause state, speed multiplier usage, and accumulator bookkeeping out
/// of GameRoot while leaving frame orchestration in the composition root.
/// </summary>
public sealed class SimulationLoopController
{
    private const float SimulationStep = 0.1f;

    private GameSimulation? _simulation;
    private ActionBar? _actionBar;
    private double _accumulator;
    private double _simulatedSeconds;

    public double PresentationTimeSeconds => _simulatedSeconds + _accumulator;

    public void Bind(GameSimulation simulation, ActionBar? actionBar)
    {
        _simulation = simulation;
        _actionBar = actionBar;
        _accumulator = 0d;
        _simulatedSeconds = 0d;
    }

    public void Advance(double deltaSeconds)
    {
        if (_simulation is null)
            return;

        if (_actionBar?.IsPaused ?? false)
            return;

        _accumulator += deltaSeconds * (_actionBar?.SpeedMultiplier ?? 1f);
        while (_accumulator >= SimulationStep)
        {
            _simulation.Tick(SimulationStep);
            _accumulator -= SimulationStep;
            _simulatedSeconds += SimulationStep;
        }
    }
}

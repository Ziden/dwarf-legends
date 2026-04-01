using System.Text.Json;
using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.Systems;

/// <summary>
/// Wraps full-simulation JSON save/load operations.
/// Delegates per-system state via IGameSystem.OnSave/OnLoad.
/// Order 98.
/// </summary>
public sealed class SaveGameSystem : IGameSystem
{
    public string SystemId    => SystemIds.SaveGameSystem;
    public int    UpdateOrder => 98;
    public bool   IsEnabled   { get; set; } = true;

    private GameContext? _ctx;

    public void Initialize(GameContext ctx) => _ctx = ctx;
    public void Tick(float delta) { }
    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    /// <summary>
    /// Captures a full game-state JSON payload using GameSimulation.Save().
    /// </summary>
    public string CaptureToJson(GameSimulation sim) => sim.Save();

    /// <summary>
    /// Restores full game-state JSON using GameSimulation.Load().
    /// </summary>
    public void RestoreFromJson(GameSimulation sim, string json) => sim.Load(json);
}
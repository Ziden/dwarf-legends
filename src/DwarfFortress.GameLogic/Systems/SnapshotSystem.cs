using System.Text.Json;
using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.Systems;

/// <summary>
/// Serialises a full game state snapshot to JSON and restores from one.
/// Delegates per-system state via IGameSystem.OnSave/OnLoad.
/// Order 98.
/// </summary>
public sealed class SnapshotSystem : IGameSystem
{
    public string SystemId    => SystemIds.SnapshotSystem;
    public int    UpdateOrder => 98;
    public bool   IsEnabled   { get; set; } = true;

    private GameContext? _ctx;

    public void Initialize(GameContext ctx) => _ctx = ctx;
    public void Tick(float delta) { }
    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    /// <summary>
    /// Captures a world snapshot from all registered systems and returns JSON.
    /// Uses GameSimulation's built-in Save() which calls OnSave on all systems.
    /// </summary>
    public string CaptureToJson(GameSimulation sim) => sim.Save();

    /// <summary>
    /// Restores world state from a previously captured JSON snapshot.
    /// </summary>
    public void RestoreFromJson(GameSimulation sim, string json) => sim.Load(json);
}

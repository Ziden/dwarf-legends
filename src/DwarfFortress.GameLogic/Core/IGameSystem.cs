namespace DwarfFortress.GameLogic.Core;

/// <summary>
/// Contract for every simulation system.
/// Systems are registered with GameSimulation, initialized in UpdateOrder,
/// and ticked each simulation step.
/// </summary>
public interface IGameSystem
{
    /// <summary>Stable unique identifier used for diagnostics and save/load.</summary>
    string SystemId { get; }

    /// <summary>
    /// Lower values initialize and tick first.
    /// Systems should only depend on systems with lower UpdateOrder.
    /// </summary>
    int UpdateOrder { get; }

    /// <summary>When false the system's Tick() is skipped.</summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Called once after all systems are registered, in UpdateOrder.
    /// Subscribe to EventBus and register CommandDispatcher handlers here.
    /// </summary>
    void Initialize(GameContext ctx);

    /// <summary>Called each simulation tick with elapsed seconds.</summary>
    void Tick(float delta);

    /// <summary>Serialize this system's runtime state to a save slot.</summary>
    void OnSave(SaveWriter writer);

    /// <summary>Restore this system's runtime state from a save slot.</summary>
    void OnLoad(SaveReader reader);
}

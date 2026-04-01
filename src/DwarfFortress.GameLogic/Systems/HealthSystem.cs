using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct DwarfDiedEvent    (int DwarfId, string Cause);
public record struct DwarfWoundedEvent (int DwarfId, string BodyPartId, WoundSeverity Severity);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Ticks HealthComponents; kills dwarves/creatures that reach zero health.
/// Order 9.
/// </summary>
public sealed class HealthSystem : IGameSystem
{
    public string SystemId    => SystemIds.HealthSystem;
    public int    UpdateOrder => 9;
    public bool   IsEnabled   { get; set; } = true;

    private GameContext? _ctx;

    public void Initialize(GameContext ctx) => _ctx = ctx;

    public void Tick(float delta)
    {
        var registry = _ctx!.Get<EntityRegistry>();

        foreach (var dwarf in registry.GetAlive<Dwarf>().ToArray())
            TickHealth(dwarf, delta, registry);

        foreach (var creature in registry.GetAlive<Creature>().ToArray())
            TickHealth(creature, delta, registry);
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    // ── Private ────────────────────────────────────────────────────────────

    private void TickHealth(Entity entity, float delta, EntityRegistry registry)
    {
        entity.Components.Get<StatComponent>().Tick(delta);

        var health = entity.Components.Get<HealthComponent>();
        health.Tick(delta);

        if (!health.IsDead) return;

        var cause = health.CurrentHealth <= 0 ? "blood_loss" : "wounds";
        registry.Kill(entity.Id, cause);
    }
}

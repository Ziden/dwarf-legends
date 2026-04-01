using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Systems;

public record struct NauseaAppliedEvent(int DwarfId, float Duration);
public record struct NauseaClearedEvent(int DwarfId);

/// <summary>
/// Ticks status effects on all dwarves.
/// Emits <see cref="NauseaClearedEvent"/> when nausea wears off.
/// Order 5 — runs between NeedsSystem (4) and NutritionSystem (6).
/// </summary>
public sealed class NauseaSystem : IGameSystem
{
    public string SystemId    => SystemIds.NauseaSystem;
    public int    UpdateOrder => 5;
    public bool   IsEnabled   { get; set; } = true;

    private GameContext? _ctx;

    public void Initialize(GameContext ctx) => _ctx = ctx;

    public void Tick(float delta)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            var fx = dwarf.Components.TryGet<StatusEffectComponent>();
            if (fx is null) continue;

            bool hadNausea = fx.Has(StatusEffectIds.Nausea);
            fx.Tick(delta);
            bool hasNausea = fx.Has(StatusEffectIds.Nausea);

            if (hadNausea && !hasNausea)
                _ctx.EventBus.Emit(new NauseaClearedEvent(dwarf.Id));
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }
}

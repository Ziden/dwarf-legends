using System;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Systems;

/// <summary>
/// Applies EffectBlocks from reactions, world events, items, etc.
/// Works as a pure utility service — no Tick logic, just ApplyEffect().
/// Order 13.
/// </summary>
public sealed class EffectApplicator : IGameSystem
{
    public string SystemId    => SystemIds.EffectApplicator;
    public int    UpdateOrder => 13;
    public bool   IsEnabled   { get; set; } = true;

    private GameContext? _ctx;

    public void Initialize(GameContext ctx) => _ctx = ctx;
    public void Tick(float delta) { }
    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    /// <summary>Applies a single EffectBlock to a target entity.</summary>
    public void Apply(Data.EffectBlock effect, int targetEntityId)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        var entity   = registry.TryGetById(targetEntityId);
        if (entity is null) return;

        switch (effect.Op)
        {
            case "damage":
                ApplyDamage(effect, entity);
                break;
            case "heal":
                ApplyHeal(effect, entity);
                break;
            case "add_modifier":
                ApplyModifier(effect, entity);
                break;
            case "add_thought":
                ApplyThought(effect, entity);
                break;
            case "satisfy_need":
                ApplySatisfyNeed(effect, entity);
                break;
            case "add_substance":
                ApplyAddSubstance(effect, entity);
                break;
            default:
                _ctx.Logger?.Warn($"EffectApplicator: unknown op '{effect.Op}'");
                break;
        }
    }

    // ── Op handlers ────────────────────────────────────────────────────────

    private static void ApplyDamage(Data.EffectBlock effect, Entity entity)
    {
        if (!entity.Components.Has<HealthComponent>()) return;
        var amount = effect.GetFloat("amount", 10f);
        entity.Components.Get<HealthComponent>().TakeDamage(amount);
    }

    private static void ApplyHeal(Data.EffectBlock effect, Entity entity)
    {
        if (!entity.Components.Has<HealthComponent>()) return;
        var amount = effect.GetFloat("amount", 10f);
        entity.Components.Get<HealthComponent>().Heal(amount);
    }

    private static void ApplyModifier(Data.EffectBlock effect, Entity entity)
    {
        if (!entity.Components.Has<StatComponent>()) return;
        var stat     = effect.GetString("stat");
        var modType  = Enum.Parse<Data.ModType>(effect.GetString("mod_type"), ignoreCase: true);
        var value    = effect.GetFloat("value", 0f);
        var duration = effect.GetFloat("duration", -1f);
        var sourceId = effect.GetString("source_id");

        var statComp = entity.Components.Get<StatComponent>();
        if (statComp.TryGet(stat, out var statObj))
            statObj.Modifiers.Add(new Data.Modifier(sourceId, modType, value, duration));
    }

    private void ApplyThought(Data.EffectBlock effect, Entity entity)
    {
        if (!entity.Components.Has<ThoughtComponent>()) return;
        var id        = effect.GetString("thought_id");
        var desc      = effect.GetString("description");
        var happiness = effect.GetFloat("happiness_mod", 0f);
        var duration  = effect.GetFloat("duration", 3600f);

        entity.Components.Get<ThoughtComponent>()
              .AddThought(new Thought(id, desc, happiness, duration));
    }

    private static void ApplySatisfyNeed(Data.EffectBlock effect, Entity entity)
    {
        if (!entity.Components.Has<NeedsComponent>()) return;
        var needId = effect.GetString("need_id");
        var amount = effect.GetFloat("amount", 0.5f);
        entity.Components.Get<NeedsComponent>().Get(needId).Satisfy(amount);
    }

    private static void ApplyAddSubstance(Data.EffectBlock effect, Entity entity)
    {
        if (!entity.Components.Has<BodyChemistryComponent>()) return;
        var substanceId = effect.GetString("substance_id");
        var amount      = effect.GetFloat("amount", 0.1f);
        entity.Components.Get<BodyChemistryComponent>().AddSubstance(substanceId, amount);
    }
}

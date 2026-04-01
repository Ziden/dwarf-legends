using System;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct ReactionFiredEvent(string ReactionDefId, int EntityId);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Evaluates all ReactionDefs each tick. When a trigger condition is met,
/// fires the reaction's EffectBlocks via EffectApplicator.
/// Order 14.
/// </summary>
public sealed class ReactionPipeline : IGameSystem
{
    public string SystemId    => SystemIds.ReactionPipeline;
    public int    UpdateOrder => 14;
    public bool   IsEnabled   { get; set; } = true;

    private GameContext? _ctx;

    public void Initialize(GameContext ctx) => _ctx = ctx;

    public void Tick(float delta)
    {
        var dm        = _ctx!.TryGet<DataManager>();
        var applicator = _ctx!.TryGet<EffectApplicator>();
        var registry  = _ctx!.Get<EntityRegistry>();

        if (dm is null || applicator is null) return;

        foreach (var reactionDef in dm.Reactions.All())
        {
            foreach (var entity in registry.GetAll<Entity>())
            {
                bool triggered = reactionDef.Triggers.Any(t => EvaluateTrigger(t, entity));
                if (!triggered) continue;

                if (reactionDef.Probability < 1f &&
                    Random.Shared.NextSingle() > reactionDef.Probability) continue;

                foreach (var effect in reactionDef.Effects)
                    applicator.Apply(effect, entity.Id);

                _ctx.EventBus.Emit(new ReactionFiredEvent(reactionDef.Id, entity.Id));
            }
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    // ── Private ────────────────────────────────────────────────────────────

    private bool EvaluateTrigger(Data.Defs.ReactionTrigger trigger, Entity entity)
    {
        return trigger.Type switch
        {
            "entity_has_tag"        => EntityHasTag(trigger, entity),
            "need_critical"         => NeedIsCritical(trigger, entity),
            "entity_has_substance"  => EntityHasSubstance(trigger, entity),
            "body_part_has_coating" => BodyPartHasCoating(trigger, entity),
            _                       => false,
        };
    }

    private static bool EntityHasTag(Data.Defs.ReactionTrigger trigger, Entity entity)
    {
        if (!trigger.Params.TryGetValue("tag", out var tag)) return false;
        return entity.Components.Has<Entities.Components.StatComponent>(); // stub
    }

    private static bool NeedIsCritical(Data.Defs.ReactionTrigger trigger, Entity entity)
    {
        if (!entity.Components.Has<Entities.Components.NeedsComponent>()) return false;
        if (!trigger.Params.TryGetValue("need_id", out var needId)) return false;
        return entity.Components.Get<Entities.Components.NeedsComponent>().Get(needId).IsCritical;
    }

    /// <summary>
    /// Trigger: entity_has_substance
    /// Params: substance_id, op (gt/lt/gte/lte/eq), threshold
    /// Example: { "type": "entity_has_substance", "params": { "substance_id": "alcohol", "op": "gt", "threshold": "0.2" } }
    /// </summary>
    private static bool EntityHasSubstance(Data.Defs.ReactionTrigger trigger, Entity entity)
    {
        if (!entity.Components.Has<Entities.Components.BodyChemistryComponent>()) return false;
        if (!trigger.Params.TryGetValue("substance_id", out var substanceId)) return false;

        var chemistry   = entity.Components.Get<Entities.Components.BodyChemistryComponent>();
        float conc      = chemistry.Get(substanceId);

        trigger.Params.TryGetValue("threshold", out var threshStr);
        float threshold = float.TryParse(threshStr, out var t) ? t : 0f;
        trigger.Params.TryGetValue("op", out var op);

        return (op ?? "gt") switch
        {
            "gt"  or ">"  => conc >  threshold,
            "gte" or ">=" => conc >= threshold,
            "lt"  or "<"  => conc <  threshold,
            "lte" or "<=" => conc <= threshold,
            "eq"  or "="  => Math.Abs(conc - threshold) < 0.001f,
            _             => conc >  threshold,
        };
    }

    /// <summary>
    /// Trigger: body_part_has_coating
    /// Params: part_id, material_id (optional — if omitted any coating matches)
    /// Example: { "type": "body_part_has_coating", "params": { "part_id": "paws", "material_id": "beer" } }
    /// </summary>
    private static bool BodyPartHasCoating(Data.Defs.ReactionTrigger trigger, Entity entity)
    {
        if (!entity.Components.Has<Entities.Components.BodyPartComponent>()) return false;
        if (!trigger.Params.TryGetValue("part_id", out var partId)) return false;

        var parts = entity.Components.Get<Entities.Components.BodyPartComponent>();
        if (!parts.TryGet(partId, out var part) || part is null) return false;
        if (part.CoatingMaterialId is null) return false;

        if (trigger.Params.TryGetValue("material_id", out var materialId))
            return string.Equals(part.CoatingMaterialId, materialId, StringComparison.OrdinalIgnoreCase);

        return true; // any coating
    }
}

using System;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;

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
        var dm         = _ctx!.TryGet<DataManager>();
        var applicator = _ctx!.TryGet<EffectApplicator>();
        var registry   = _ctx!.Get<EntityRegistry>();

        if (dm is null || applicator is null) return;

        foreach (var reactionDef in dm.Reactions.All())
        {
            foreach (var entity in registry.GetAll<Entity>())
            {
                if (!entity.IsAlive)
                    continue;

                bool triggered = reactionDef.Triggers.Any(t => EvaluateTrigger(t, entity, dm));
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

    private static bool EvaluateTrigger(Data.Defs.ReactionTrigger trigger, Entity entity, DataManager dm)
    {
        return trigger.Type switch
        {
            ReactionTriggerTypes.EntityHasTag => EntityHasTag(trigger, entity, dm),
            ReactionTriggerTypes.EntityDefIs => EntityDefIs(trigger, entity),
            ReactionTriggerTypes.EntityHasLabor => EntityHasLabor(trigger, entity),
            ReactionTriggerTypes.EntityProfessionIs => EntityProfessionIs(trigger, entity),
            ReactionTriggerTypes.EntityHasFactionRole => EntityHasFactionRole(trigger, entity, dm),
            ReactionTriggerTypes.EntityAttributeAtLeast => EntityAttributeAtLeast(trigger, entity),
            ReactionTriggerTypes.EntityAttributeAtMost => EntityAttributeAtMost(trigger, entity),
            ReactionTriggerTypes.NeedCritical => NeedIsCritical(trigger, entity),
            ReactionTriggerTypes.EntityHasSubstance => EntityHasSubstance(trigger, entity),
            ReactionTriggerTypes.BodyPartHasCoating => BodyPartHasCoating(trigger, entity),
            _ => false,
        };
    }

    private static bool EntityHasTag(Data.Defs.ReactionTrigger trigger, Entity entity, DataManager dm)
    {
        if (!trigger.Params.TryGetValue("tag", out var tag)) return false;
        return ResolveEntityTags(entity, dm).Contains(tag);
    }

    private static bool EntityDefIs(Data.Defs.ReactionTrigger trigger, Entity entity)
    {
        if (!trigger.Params.TryGetValue("def_id", out var defId) || string.IsNullOrWhiteSpace(defId))
            return false;

        return string.Equals(entity.DefId, defId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EntityHasLabor(Data.Defs.ReactionTrigger trigger, Entity entity)
    {
        if (entity is not Dwarf dwarf)
            return false;
        if (!trigger.Params.TryGetValue("labor_id", out var laborId) || string.IsNullOrWhiteSpace(laborId))
            return false;

        return dwarf.Labors.IsEnabled(laborId);
    }

    private static bool EntityProfessionIs(Data.Defs.ReactionTrigger trigger, Entity entity)
    {
        if (entity is not Dwarf dwarf)
            return false;
        if (!trigger.Params.TryGetValue("profession_id", out var professionId) || string.IsNullOrWhiteSpace(professionId))
            return false;

        return string.Equals(dwarf.ProfessionId, professionId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EntityHasFactionRole(Data.Defs.ReactionTrigger trigger, Entity entity, DataManager dm)
    {
        if (!trigger.Params.TryGetValue("role_id", out var roleId) || string.IsNullOrWhiteSpace(roleId))
            return false;

        var def = ResolveEntityCreatureDef(entity, dm);
        return def?.HasFactionRole(roleId) == true;
    }

    private static bool EntityAttributeAtLeast(Data.Defs.ReactionTrigger trigger, Entity entity)
        => EntityAttributeMatches(trigger, entity, (current, threshold) => current >= threshold);

    private static bool EntityAttributeAtMost(Data.Defs.ReactionTrigger trigger, Entity entity)
        => EntityAttributeMatches(trigger, entity, (current, threshold) => current <= threshold);

    private static bool EntityAttributeMatches(Data.Defs.ReactionTrigger trigger, Entity entity, Func<int, int, bool> comparator)
    {
        if (entity is not Dwarf dwarf)
            return false;
        if (!trigger.Params.TryGetValue("attribute_id", out var attributeId) || string.IsNullOrWhiteSpace(attributeId))
            return false;
        if (!trigger.Params.TryGetValue("level", out var levelValue) || !int.TryParse(levelValue, out var level))
            return false;

        return comparator(dwarf.Attributes.GetLevel(attributeId), level);
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

    private static TagSet ResolveEntityTags(Entity entity, DataManager dm)
    {
        var tags = TagSet.Empty;

        switch (entity)
        {
            case Dwarf:
            {
                var dwarfDef = dm.Creatures.GetOrNull(DefIds.Dwarf);
                if (dwarfDef is not null)
                    tags = tags.Union(dwarfDef.Tags);
                break;
            }
            case Creature creature:
            {
                var creatureDef = dm.Creatures.GetOrNull(creature.DefId);
                if (creatureDef is not null)
                    tags = tags.Union(creatureDef.Tags);
                if (creature.IsHostile && !tags.Contains(TagIds.Hostile))
                    tags = tags.With(TagIds.Hostile);
                break;
            }
            case Item item:
            {
                var itemDef = dm.Items.GetOrNull(item.DefId);
                if (itemDef is not null)
                    tags = tags.Union(itemDef.Tags);
                if (!string.IsNullOrWhiteSpace(item.MaterialId))
                {
                    var materialDef = dm.Materials.GetOrNull(item.MaterialId);
                    if (materialDef is not null)
                        tags = tags.Union(materialDef.Tags);
                }
                break;
            }
            case Box:
                tags = tags.With(TagIds.Container);
                break;
        }

        return tags;
    }

    private static CreatureDef? ResolveEntityCreatureDef(Entity entity, DataManager dm)
    {
        return entity switch
        {
            Dwarf => dm.Creatures.GetOrNull(DefIds.Dwarf),
            Creature creature => dm.Creatures.GetOrNull(creature.DefId),
            _ => null,
        };
    }
}

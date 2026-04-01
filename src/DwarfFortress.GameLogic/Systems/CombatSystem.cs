using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct CombatHitEvent  (int AttackerId, int DefenderId, float Damage, string BodyPartId);
public record struct CombatMissEvent (int AttackerId, int DefenderId);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Handles melee and ranged combat resolution.
/// Uses SpatialIndexSystem for O(1) neighbor queries instead of O(n²) brute force.
/// Order 15.
/// </summary>
public sealed class CombatSystem : IGameSystem
{
    public string SystemId    => SystemIds.CombatSystem;
    public int    UpdateOrder => 15;
    public bool   IsEnabled   { get; set; } = true;

    private GameContext? _ctx;
    private SpatialIndexSystem? _spatial;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        _spatial = ctx.TryGet<SpatialIndexSystem>();
    }

    public void Tick(float delta)
    {
        var registry = _ctx!.Get<EntityRegistry>();

        // Use spatial index for O(1) neighbor lookups instead of O(n²) brute force
        if (_spatial is not null)
        {
            foreach (var creature in registry.GetAlive<Creature>())
            {
                if (!creature.IsHostile) continue;

                var cPos = creature.Components.Get<PositionComponent>().Position;
                var dwarvesAtPos = _spatial.GetDwarvesAt(cPos);
                if (dwarvesAtPos.Count == 0)
                {
                    // Check adjacent tiles for dwarves
                    bool attacked = false;
                    foreach (var neighbor in cPos.Neighbours6())
                    {
                        var adjacentDwarves = _spatial.GetDwarvesAt(neighbor);
                        foreach (var dwarfId in adjacentDwarves)
                        {
                            AttackEntity(creature.Id, dwarfId);
                            attacked = true;
                            break;
                        }
                        if (attacked) break;
                    }
                }
                else
                {
                    // Dwarf on same tile — attack first one
                    AttackEntity(creature.Id, dwarvesAtPos[0]);
                }
            }
        }
        else
        {
            // Fallback: brute force if spatial index unavailable
            foreach (var creature in registry.GetAlive<Creature>())
            {
                if (!creature.IsHostile) continue;

                var cPos = creature.Components.Get<PositionComponent>().Position;
                foreach (var dwarf in registry.GetAlive<Dwarf>())
                {
                    var dPos = dwarf.Components.Get<PositionComponent>().Position;
                    if (cPos.ManhattanDistanceTo(dPos) > 1) continue;
                    AttackEntity(creature.Id, dwarf.Id);
                    break; // one attack per creature per tick
                }
            }
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Resolves one melee attack from attacker → defender.</summary>
    public void AttackEntity(int attackerId, int defenderId)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        if (!registry.TryGetById<Entity>(attackerId, out var attacker) || attacker is null) return;
        if (!registry.TryGetById<Entity>(defenderId, out var defender) || defender is null) return;
        if (!defender.Components.Has<HealthComponent>()) return;

        var attackerStats = attacker.Components.Has<StatComponent>()
            ? attacker.Components.Get<StatComponent>()
            : null;

        float strength  = attackerStats?.Get(StatNames.Strength).Value  ?? 10f;
        float toughness = defender.Components.Has<StatComponent>()
            ? defender.Components.Get<StatComponent>().Get(StatNames.Toughness).Value
            : 10f;

        float agility   = attackerStats?.Get(StatNames.Agility).Value ?? 10f;
        float hitChance = Math.Clamp(0.5f + (agility - 10f) / 40f, 0.1f, 0.95f);
        if (Random.Shared.NextSingle() > hitChance)
        {
            _ctx!.EventBus.Emit(new CombatMissEvent(attackerId, defenderId));
            return;
        }

        float damage  = Math.Max(1f, strength - toughness * 0.5f);
        var   partId  = BodyPartIds.CombatTargets[Random.Shared.Next(BodyPartIds.CombatTargets.Count)];

        var health = defender.Components.Get<HealthComponent>();
        health.TakeDamage(damage);
        health.AddWound(new Wound(partId, damage > 20 ? WoundSeverity.Critical : damage > 10 ? WoundSeverity.Serious : WoundSeverity.Minor, isBleeding: damage > 10));
        _ctx!.EventBus.Emit(new CombatHitEvent(attackerId, defenderId, damage, partId));
    }
}

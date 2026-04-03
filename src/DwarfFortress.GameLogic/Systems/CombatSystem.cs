using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
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
    private const float BaselineHitChance = 0.75f;
    private const float HitChancePerAgilityDelta = 1f / 50f;
    private const float MinHitChance = 0.2f;
    private const float MaxHitChance = 0.97f;

    public string SystemId    => SystemIds.CombatSystem;
    public int    UpdateOrder => 15;
    public bool   IsEnabled   { get; set; } = true;

    private GameContext? _ctx;
    private SpatialIndexSystem? _spatial;
    private readonly Dictionary<int, float> _attackCooldowns = new();
    private readonly List<int> _expiredCooldowns = new();
    private readonly List<int> _cooldownEntityIds = new();

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        _spatial = ctx.TryGet<SpatialIndexSystem>();
    }

    public void Tick(float delta)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        TickAttackCooldowns(delta);

        ResolveHostileAttacks(registry);
        ResolveDwarfCounterattacks(registry);
    }

    public void OnSave(SaveWriter w) { }

    public void OnLoad(SaveReader r)
    {
        _attackCooldowns.Clear();
        _expiredCooldowns.Clear();
        _cooldownEntityIds.Clear();
    }

    private void TickAttackCooldowns(float delta)
    {
        if (_attackCooldowns.Count == 0)
            return;

        _expiredCooldowns.Clear();
        _cooldownEntityIds.Clear();
        foreach (var entityId in _attackCooldowns.Keys)
            _cooldownEntityIds.Add(entityId);

        for (var i = 0; i < _cooldownEntityIds.Count; i++)
        {
            var entityId = _cooldownEntityIds[i];
            var remaining = _attackCooldowns[entityId];
            var updated = remaining - delta;
            if (updated <= 0f)
            {
                _expiredCooldowns.Add(entityId);
                continue;
            }

            _attackCooldowns[entityId] = updated;
        }

        for (var i = 0; i < _expiredCooldowns.Count; i++)
            _attackCooldowns.Remove(_expiredCooldowns[i]);
    }

    private void ResolveHostileAttacks(EntityRegistry registry)
    {
        // Use spatial index for O(1) neighbor lookups instead of O(n²) brute force.
        foreach (var creature in registry.GetAlive<Creature>())
        {
            if (!creature.IsHostile || !CanInitiateAttack(creature))
                continue;

            if (!TryFindAdjacentDwarfId(creature.Position.Position, registry, out var dwarfId))
                continue;

            TryAttackAdjacentEntity(creature.Id, dwarfId);
        }
    }

    private void ResolveDwarfCounterattacks(EntityRegistry registry)
    {
        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            if (!CanInitiateAttack(dwarf))
                continue;

            if (!TryFindAdjacentHostileCreatureId(dwarf.Position.Position, registry, out var creatureId))
                continue;

            TryAttackAdjacentEntity(dwarf.Id, creatureId);
        }
    }

    public bool TryAttackAdjacentEntity(int attackerId, int defenderId)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        if (!registry.TryGetById<Entity>(attackerId, out var attacker) || attacker is null)
            return false;
        if (!registry.TryGetById<Entity>(defenderId, out var defender) || defender is null)
            return false;
        if (!CanInitiateAttack(attacker) || !IsValidCombatTarget(defender))
            return false;
        if (!IsInMeleeRange(attacker, defender))
            return false;

        return TryAttackWithCooldown(attackerId, defenderId, attacker);
    }

    private bool TryAttackWithCooldown(int attackerId, int defenderId, Entity attacker)
    {
        if (_attackCooldowns.ContainsKey(attackerId))
            return false;

        AttackEntity(attackerId, defenderId);
        _attackCooldowns[attackerId] = GetAttackCooldownSeconds(attacker);
        return true;
    }

    public static float CalculateAttackCooldownSeconds(Entity attacker)
    {
        var speed = attacker.Components.Has<StatComponent>()
            ? attacker.Components.Get<StatComponent>().Speed.Value
            : 1f;

        return Math.Clamp(1.2f / Math.Max(0.25f, speed), 0.35f, 2.0f);
    }

    private static float GetAttackCooldownSeconds(Entity attacker)
        => CalculateAttackCooldownSeconds(attacker);

    private bool TryFindAdjacentDwarfId(Vec3i origin, EntityRegistry registry, out int dwarfId)
    {
        if (_spatial is not null)
        {
            if (TrySelectDwarf(_spatial.GetDwarvesAt(origin), registry, out dwarfId))
                return true;

            foreach (var neighbor in origin.Neighbours6())
                if (TrySelectDwarf(_spatial.GetDwarvesAt(neighbor), registry, out dwarfId))
                    return true;

            dwarfId = -1;
            return false;
        }

        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            if (!IsValidCombatTarget(dwarf) || origin.ManhattanDistanceTo(dwarf.Position.Position) > 1)
                continue;

            dwarfId = dwarf.Id;
            return true;
        }

        dwarfId = -1;
        return false;
    }

    private bool TryFindAdjacentHostileCreatureId(Vec3i origin, EntityRegistry registry, out int creatureId)
    {
        if (_spatial is not null)
        {
            if (TrySelectHostileCreature(_spatial.GetCreaturesAt(origin), registry, out creatureId))
                return true;

            foreach (var neighbor in origin.Neighbours6())
                if (TrySelectHostileCreature(_spatial.GetCreaturesAt(neighbor), registry, out creatureId))
                    return true;

            creatureId = -1;
            return false;
        }

        foreach (var creature in registry.GetAlive<Creature>())
        {
            if (!creature.IsHostile || !IsValidCombatTarget(creature) || origin.ManhattanDistanceTo(creature.Position.Position) > 1)
                continue;

            creatureId = creature.Id;
            return true;
        }

        creatureId = -1;
        return false;
    }

    private static bool TrySelectDwarf(IReadOnlyCollection<int> dwarfIds, EntityRegistry registry, out int dwarfId)
    {
        foreach (var candidateId in dwarfIds)
        {
            if (!registry.TryGetById<Dwarf>(candidateId, out var dwarf) || dwarf is null || !IsValidCombatTarget(dwarf))
                continue;

            dwarfId = dwarf.Id;
            return true;
        }

        dwarfId = -1;
        return false;
    }

    private static bool TrySelectHostileCreature(IReadOnlyCollection<int> creatureIds, EntityRegistry registry, out int creatureId)
    {
        foreach (var candidateId in creatureIds)
        {
            if (!registry.TryGetById<Creature>(candidateId, out var creature) || creature is null ||
                !creature.IsHostile || !IsValidCombatTarget(creature))
                continue;

            creatureId = creature.Id;
            return true;
        }

        creatureId = -1;
        return false;
    }

    private static bool CanInitiateAttack(Entity entity)
    {
        if (!entity.Components.Has<HealthComponent>())
            return true;

        var health = entity.Components.Get<HealthComponent>();
        return !health.IsDead && health.IsConscious;
    }

    private static bool IsValidCombatTarget(Entity entity)
        => entity.Components.Has<HealthComponent>() && !entity.Components.Get<HealthComponent>().IsDead;

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Resolves one melee attack from attacker → defender.</summary>
    public void AttackEntity(int attackerId, int defenderId)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        if (!registry.TryGetById<Entity>(attackerId, out var attacker) || attacker is null) return;
        if (!registry.TryGetById<Entity>(defenderId, out var defender) || defender is null) return;
        if (!CanInitiateAttack(attacker)) return;
        if (!defender.Components.Has<HealthComponent>()) return;
        if (!IsInMeleeRange(attacker, defender)) return;

        var health = defender.Components.Get<HealthComponent>();
        if (health.IsDead) return;

        var attackerStats = attacker.Components.Has<StatComponent>()
            ? attacker.Components.Get<StatComponent>()
            : null;
        var defenderStats = defender.Components.Has<StatComponent>()
            ? defender.Components.Get<StatComponent>()
            : null;

        float strength = attackerStats?.Get(StatNames.Strength).Value ?? StatComponent.DefaultPrimaryBaseValue;
        float toughness = defenderStats?.Get(StatNames.Toughness).Value ?? StatComponent.DefaultPrimaryBaseValue;
        float attackerAgility = attackerStats?.Get(StatNames.Agility).Value ?? StatComponent.DefaultPrimaryBaseValue;
        float defenderAgility = defenderStats?.Get(StatNames.Agility).Value ?? StatComponent.DefaultPrimaryBaseValue;

        float hitChance = CalculateHitChance(attackerAgility, defenderAgility);
        if (Random.Shared.NextSingle() > hitChance)
        {
            _ctx!.EventBus.Emit(new CombatMissEvent(attackerId, defenderId));
            return;
        }

        var baseDamage = CalculateBaseMeleeDamage(strength, toughness);
        float damage  = Math.Max(0.5f, baseDamage * ResolveMeleeDamageMultiplier(attacker, _ctx?.TryGet<DataManager>()));
        var   partId  = BodyPartIds.CombatTargets[Random.Shared.Next(BodyPartIds.CombatTargets.Count)];
        var severity = damage > 20f ? WoundSeverity.Critical : damage > 10f ? WoundSeverity.Serious : WoundSeverity.Minor;
        health.TakeDamage(damage);
        health.AddWound(new Wound(partId, severity, isBleeding: damage > 10f));
        if (defender is Dwarf)
            _ctx!.EventBus.Emit(new DwarfWoundedEvent(defenderId, partId, severity));

        _ctx!.EventBus.Emit(new CombatHitEvent(attackerId, defenderId, damage, partId));
    }

    public static float CalculateHitChance(float attackerAgility, float defenderAgility)
        => Math.Clamp(
            BaselineHitChance + (attackerAgility - defenderAgility) * HitChancePerAgilityDelta,
            MinHitChance,
            MaxHitChance);

    public static float CalculateBaseMeleeDamage(float attackerStrength, float defenderToughness)
        => Math.Max(1f, attackerStrength - defenderToughness * 0.5f);

    private static bool IsInMeleeRange(Entity attacker, Entity defender)
    {
        if (!attacker.Components.Has<PositionComponent>() || !defender.Components.Has<PositionComponent>())
            return false;

        var attackerPos = attacker.Components.Get<PositionComponent>().Position;
        var defenderPos = defender.Components.Get<PositionComponent>().Position;
        return attackerPos.ManhattanDistanceTo(defenderPos) <= 1;
    }

    private static float ResolveMeleeDamageMultiplier(Entity attacker, DataManager? dataManager)
    {
        if (attacker is not Dwarf dwarf)
            return 1f;

        return AttributeEffectSystem.GetConfiguredMultiplier(
            dwarf,
            dataManager,
            AttributeIds.Strength,
            "melee_damage_multiplier",
            1.0f);
    }
}

using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.Tests.Fakes;
using DwarfFortress.GameLogic.World;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

/// <summary>
/// Tests for SkillSystem XP/levelling and HealthSystem/CombatSystem wound mechanics.
/// </summary>
public sealed class SkillCombatTests
{
    // ── SkillSystem ────────────────────────────────────────────────────────

    [Fact]
    public void SkillSystem_Awards_XP_When_Mining_Job_Completes()
    {
        var (sim, er, js, _) = Build();

        var dwarf = new Dwarf(er.NextId(), "Miner", new Vec3i(1, 0, 0));
        er.Register(dwarf);

        float xpBefore = dwarf.Skills.GetOrCreate(SkillIds.Mining).Xp;

        // Complete a mining job by firing the completion event directly
        sim.Context.EventBus.Emit(new JobCompletedEvent(
            JobId:   0,
            DwarfId: dwarf.Id,
            JobDefId: "mine_tile",
            EntityId: -1));

        float xpAfter = dwarf.Skills.GetOrCreate(SkillIds.Mining).Xp;
        Assert.True(xpAfter > xpBefore, "Mining XP should increase after a mine_tile job completes.");
    }

    [Fact]
    public void SkillSystem_Levels_Up_After_Enough_XP()
    {
        var (sim, er, _, _) = Build();

        var dwarf = new Dwarf(er.NextId(), "Expert", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        int levelBefore = dwarf.Skills.GetLevel(SkillIds.Mining);
        dwarf.Skills.AddXp(SkillIds.Mining, 99999f);
        int levelAfter = dwarf.Skills.GetLevel(SkillIds.Mining);

        Assert.True(levelAfter > levelBefore,
            $"Level should increase after large XP gain; was {levelBefore}, still {levelAfter}.");
    }

    // ── HealthSystem ───────────────────────────────────────────────────────

    [Fact]
    public void HealthSystem_Marks_Dwarf_Unconscious_When_Health_Zero()
    {
        var (sim, er, _, _) = Build();

        var dwarf = new Dwarf(er.NextId(), "Wounded", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        // Apply lethal damage directly on the component
        dwarf.Health.TakeDamage(9999f);

        for (int i = 0; i < 5; i++) sim.Tick(0.1f);

        Assert.False(dwarf.Health.IsConscious,
            "Dwarf should be unconscious / dead after lethal damage.");
    }

    [Fact]
    public void HealthSystem_Fires_DwarfDied_Event_On_Lethal_Damage()
    {
        var (sim, er, _, _) = Build();

        var dwarf = new Dwarf(er.NextId(), "Dying", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        DwarfDiedEvent? diedEv = null;
        sim.Context.EventBus.On<DwarfDiedEvent>(ev => diedEv = ev);

        // Apply lethal damage directly; HealthSystem.Tick() emits DwarfDiedEvent
        dwarf.Health.TakeDamage(9999f);

        for (int i = 0; i < 10; i++) sim.Tick(0.1f);

        Assert.NotNull(diedEv);
        Assert.Equal(dwarf.Id, diedEv!.Value.DwarfId);
    }

    [Fact]
    public void HealthSystem_Bleeds_Health_Down_Over_Ticks()
    {
        var (sim, er, _, _) = Build();

        var dwarf = new Dwarf(er.NextId(), "Bleeding", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        float healthBefore = dwarf.Health.CurrentHealth;

        // Add a bleeding wound directly; HealthSystem.Tick() will drain health
        dwarf.Health.AddWound(new Wound("arm", WoundSeverity.Minor, isBleeding: true));

        for (int i = 0; i < 60; i++) sim.Tick(0.1f);

        Assert.True(dwarf.Health.CurrentHealth < healthBefore,
            "Bleeding wound should drain health over time.");
    }

    // ── CombatSystem ──────────────────────────────────────────────────────

    [Fact]
    public void CombatSystem_Reduces_Defender_Health_After_Attack_Command()
    {
        var (sim, er, _, _) = Build();

        var attacker = new Dwarf(er.NextId(), "Attacker", new Vec3i(0, 0, 0));
        var defender = new Dwarf(er.NextId(), "Defender", new Vec3i(1, 0, 0));
        er.Register(attacker);
        er.Register(defender);

        float healthBefore = defender.Health.CurrentHealth;

        // Attack can miss, so give the combat system several attempts.
        for (int i = 0; i < 20 && defender.Health.CurrentHealth >= healthBefore; i++)
            sim.Context.Get<CombatSystem>().AttackEntity(attacker.Id, defender.Id);

        for (int i = 0; i < 5; i++) sim.Tick(0.1f);

        Assert.True(defender.Health.CurrentHealth < healthBefore,
            "Defender health should decrease after an attack.");
    }

    [Fact]
    public void CombatSystem_AutomaticCombat_Uses_Attack_Cooldowns()
    {
        var (sim, er, _, _) = Build();

        var dwarf = new Dwarf(er.NextId(), "Defender", new Vec3i(0, 0, 0));
        var goblin = new Creature(er.NextId(), DefIds.Goblin, new Vec3i(1, 0, 0), maxHealth: 60f, isHostile: true);
        er.Register(dwarf);
        er.Register(goblin);

        var hostileAttacks = 0;
        sim.Context.EventBus.On<CombatHitEvent>(ev =>
        {
            if (ev.AttackerId == goblin.Id)
                hostileAttacks++;
        });
        sim.Context.EventBus.On<CombatMissEvent>(ev =>
        {
            if (ev.AttackerId == goblin.Id)
                hostileAttacks++;
        });

        for (int i = 0; i < 10; i++)
            sim.Tick(0.1f);

        Assert.True(hostileAttacks <= 1,
            $"Expected automatic combat to respect attack cooldowns. HostileAttacks={hostileAttacks}.");
    }

    [Fact]
    public void CombatSystem_CalculateHitChance_Uses_Both_Combatants_Agility()
    {
        var evenMatch = CombatSystem.CalculateHitChance(10f, 10f);
        var attackerAdvantage = CombatSystem.CalculateHitChance(20f, 10f);
        var defenderAdvantage = CombatSystem.CalculateHitChance(10f, 20f);

        Assert.Equal(0.75f, evenMatch, 3);
        Assert.True(attackerAdvantage > evenMatch,
            $"Expected higher attacker agility to improve hit chance. Even={evenMatch:0.###}, Advantage={attackerAdvantage:0.###}.");
        Assert.True(defenderAdvantage < evenMatch,
            $"Expected higher defender agility to reduce hit chance. Even={evenMatch:0.###}, DefenderAdvantage={defenderAdvantage:0.###}.");
    }

    [Fact]
    public void Dwarf_ApplyBaseStats_Uses_CreatureDef_Combat_Profile()
    {
        var dwarf = new Dwarf(1, "Urist", Vec3i.Zero);
        var def = new CreatureDef(
            Id: DefIds.Dwarf,
            DisplayName: "Dwarf",
            Tags: TagSet.Empty,
            BaseSpeed: 1.25f,
            BaseStrength: 14f,
            BaseToughness: 12f);

        dwarf.ApplyBaseStats(def);

        Assert.Equal(1.25f, dwarf.Stats.Speed.BaseValue);
        Assert.Equal(14f, dwarf.Stats.Strength.BaseValue);
        Assert.Equal(12f, dwarf.Stats.Toughness.BaseValue);
    }

    [Fact]
    public void CombatSystem_Dwarves_Retaliate_Against_Adjacent_Hostile_Creatures()
    {
        var (sim, er, _, _) = Build();

        var dwarf = new Dwarf(er.NextId(), "Defender", new Vec3i(0, 0, 0));
        dwarf.Stats.Strength.BaseValue = 40f;
        dwarf.Stats.Agility.BaseValue = 40f;
        dwarf.Stats.Speed.BaseValue = 3f;

        var goblin = new Creature(er.NextId(), DefIds.Goblin, new Vec3i(1, 0, 0), maxHealth: 20f, isHostile: true);
        goblin.Stats.Toughness.BaseValue = 1f;

        er.Register(dwarf);
        er.Register(goblin);

        CombatHitEvent? dwarfHit = null;
        sim.Context.EventBus.On<CombatHitEvent>(ev =>
        {
            if (ev.AttackerId == dwarf.Id && ev.DefenderId == goblin.Id)
                dwarfHit = ev;
        });

        for (int i = 0; i < 100 && dwarfHit is null; i++)
            sim.Tick(0.1f);

        Assert.NotNull(dwarfHit);
        Assert.True(goblin.Health.CurrentHealth < goblin.Health.MaxHealth,
            "Expected the dwarf to damage the adjacent hostile creature.");
    }

    [Fact]
    public void CombatResponseSystem_Dwarves_Engage_Nearby_Hostiles_Before_They_Are_Adjacent()
    {
        var (sim, er, js, _) = Build();

        var dwarf = new Dwarf(er.NextId(), "Guard", new Vec3i(4, 4, 0));
        dwarf.Stats.Strength.BaseValue = 40f;
        dwarf.Stats.Agility.BaseValue = 40f;
        dwarf.Stats.Speed.BaseValue = 2f;

        var goblin = new Creature(er.NextId(), DefIds.Goblin, new Vec3i(10, 4, 0), maxHealth: 20f, isHostile: true);
        goblin.Stats.Toughness.BaseValue = 1f;

        er.Register(dwarf);
        er.Register(goblin);

        sim.Tick(0.1f);

        var assignedJob = js.GetAssignedJob(dwarf.Id);
        Assert.NotNull(assignedJob);
        Assert.Equal(JobDefIds.EngageHostile, assignedJob!.JobDefId);
        Assert.Equal(goblin.Id, assignedJob.EntityId);

        CombatHitEvent? dwarfHit = null;
        sim.Context.EventBus.On<CombatHitEvent>(ev =>
        {
            if (ev.AttackerId == dwarf.Id && ev.DefenderId == goblin.Id)
                dwarfHit = ev;
        });

        for (int i = 0; i < 200 && dwarfHit is null; i++)
            sim.Tick(0.1f);

        Assert.NotNull(dwarfHit);
        Assert.True(goblin.Health.CurrentHealth < goblin.Health.MaxHealth,
            "Expected the dwarf to close into melee and damage the nearby hostile creature.");
    }

    [Fact]
    public void CombatSystem_Uses_Existing_BodyPart_Ids_For_Wounds()
    {
        var (sim, er, _, _) = Build();

        var attacker = new Dwarf(er.NextId(), "Attacker", new Vec3i(0, 0, 0));
        var defender = new Dwarf(er.NextId(), "Defender", new Vec3i(1, 0, 0));
        er.Register(attacker);
        er.Register(defender);

        int woundsBefore = defender.Health.Wounds.Count;

        for (int i = 0; i < 20 && defender.Health.Wounds.Count == woundsBefore; i++)
            sim.Context.Get<CombatSystem>().AttackEntity(attacker.Id, defender.Id);

        Assert.True(defender.Health.Wounds.Count > woundsBefore,
            "Defender should eventually receive a wound after repeated attack attempts.");

        var lastWound = defender.Health.Wounds.Last();
        Assert.Contains(lastWound.BodyPartId, defender.BodyParts.All.Select(part => part.PartId));
    }

    [Fact]
    public void CombatSystem_DwarfStrengthAttribute_Scales_Melee_Damage()
    {
        var (sim, er, _, _) = Build();
        var combat = sim.Context.Get<CombatSystem>();

        var weakAttacker = new Dwarf(er.NextId(), "Datan", new Vec3i(0, 0, 0));
        weakAttacker.Attributes.SetLevel(AttributeIds.Strength, 1);
        weakAttacker.Stats.Strength.BaseValue = 10f;
        weakAttacker.Stats.Agility.BaseValue = 100f;

        var weakTarget = new Creature(er.NextId(), DefIds.Goblin, new Vec3i(1, 0, 0), maxHealth: 60f, isHostile: true);
        weakTarget.Stats.Toughness.BaseValue = 0f;

        var strongAttacker = new Dwarf(er.NextId(), "Urist", new Vec3i(0, 2, 0));
        strongAttacker.Attributes.SetLevel(AttributeIds.Strength, 5);
        strongAttacker.Stats.Strength.BaseValue = 10f;
        strongAttacker.Stats.Agility.BaseValue = 100f;

        var strongTarget = new Creature(er.NextId(), DefIds.Goblin, new Vec3i(1, 2, 0), maxHealth: 60f, isHostile: true);
        strongTarget.Stats.Toughness.BaseValue = 0f;

        er.Register(weakAttacker);
        er.Register(weakTarget);
        er.Register(strongAttacker);
        er.Register(strongTarget);

        CombatHitEvent? weakHit = null;
        CombatHitEvent? strongHit = null;
        sim.Context.EventBus.On<CombatHitEvent>(ev =>
        {
            if (ev.AttackerId == weakAttacker.Id && ev.DefenderId == weakTarget.Id)
                weakHit = ev;
            else if (ev.AttackerId == strongAttacker.Id && ev.DefenderId == strongTarget.Id)
                strongHit = ev;
        });

        for (var i = 0; i < 20 && weakHit is null; i++)
            combat.AttackEntity(weakAttacker.Id, weakTarget.Id);

        for (var i = 0; i < 20 && strongHit is null; i++)
            combat.AttackEntity(strongAttacker.Id, strongTarget.Id);

        Assert.NotNull(weakHit);
        Assert.NotNull(strongHit);
        Assert.True(strongHit!.Value.Damage > weakHit!.Value.Damage,
            $"Expected higher Strength attribute to increase melee damage. Weak={weakHit.Value.Damage:0.##}, Strong={strongHit.Value.Damage:0.##}.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static (GameSimulation sim, EntityRegistry er, JobSystem js, WorldMap map) Build()
    {
        var (sim, map, er, js, _) = TestFixtures.BuildFullSim();
        return (sim, er, js, map);
    }
}

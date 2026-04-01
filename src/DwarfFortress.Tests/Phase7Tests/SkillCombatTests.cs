using System.Linq;
using DwarfFortress.GameLogic.Core;
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

    // ── Helpers ────────────────────────────────────────────────────────────

    private static (GameSimulation sim, EntityRegistry er, JobSystem js, WorldMap map) Build()
    {
        var (sim, map, er, js, _) = TestFixtures.BuildFullSim();
        return (sim, er, js, map);
    }
}

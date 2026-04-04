using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Jobs.Strategies;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic;

/// <summary>
/// Assembles a fully wired GameSimulation with all production systems and strategies.
/// Used by smoke tests and (eventually) the Godot bootstrap layer.
/// </summary>
public static class GameBootstrapper
{
    /// <summary>
    /// Create and initialize a complete simulation, ready to Tick().
    /// All systems are registered in dependency order.
    /// Job strategies are registered with the JobSystem automatically.
    /// </summary>
    public static GameSimulation Build(ILogger logger, IDataSource dataSource)
    {
        var sim = new GameSimulation(logger, dataSource);

        // ── Core data & time ───────────────────────────────────────────────
        sim.RegisterSystem(new DataManager());
        sim.RegisterSystem(new DiscoverySystem());
        sim.RegisterSystem(new TimeSystem());

        // ── World ─────────────────────────────────────────────────────────
        sim.RegisterSystem(new WorldMap());
        sim.RegisterSystem(new MapGenerationService());
        sim.RegisterSystem(new WorldHistoryRuntimeService());
        sim.RegisterSystem(new WorldMacroStateService());
        sim.RegisterSystem(new WorldLoreSystem());
        sim.RegisterSystem(new BuildingSystem());
        sim.RegisterSystem(new FortressLocationSystem());

        // ── Entities ──────────────────────────────────────────────────────
        sim.RegisterSystem(new EntityRegistry());

        // ── Items & stockpiles ────────────────────────────────────────────
        sim.RegisterSystem(new ItemSystem());
        sim.RegisterSystem(new SpatialIndexSystem());
        sim.RegisterSystem(new StockpileManager());
        sim.RegisterSystem(new FortressBootstrapSystem());

        // ── Dwarf simulation systems ──────────────────────────────────────
        sim.RegisterSystem(new NeedsSystem());
        sim.RegisterSystem(new SleepSystem());
        sim.RegisterSystem(new NauseaSystem());
        sim.RegisterSystem(new NutritionSystem());
        sim.RegisterSystem(new AttributeEffectSystem());
        sim.RegisterSystem(new WeightSystem());
        sim.RegisterSystem(new VegetationSystem());
        sim.RegisterSystem(new ThoughtSystem());
        sim.RegisterSystem(new MoodSystem());
        sim.RegisterSystem(new SkillSystem());
        sim.RegisterSystem(new HealthSystem());
        sim.RegisterSystem(new DeathSystem());
        sim.RegisterSystem(new EntityEventLogSystem());
        sim.RegisterSystem(new FortressAnnouncementSystem());
        sim.RegisterSystem(new CombatResponseSystem());

        // ── Jobs ──────────────────────────────────────────────────────────
        sim.RegisterSystem(new MovementPresentationSystem());
        var jobSystem = new JobSystem();
        sim.RegisterSystem(jobSystem);

        // ── Production ────────────────────────────────────────────────────
        sim.RegisterSystem(new RecipeSystem());
        sim.RegisterSystem(new EffectApplicator());
        sim.RegisterSystem(new ReactionPipeline());

        // ── Emergent interaction systems ──────────────────────────────────
        sim.RegisterSystem(new ContaminationSystem());
        sim.RegisterSystem(new BehaviorSystem());
        sim.RegisterSystem(new EmoteFeedbackSystem());
        sim.RegisterSystem(new AlcoholEffectSystem());

        // ── World dynamics ────────────────────────────────────────────────
        sim.RegisterSystem(new FluidSimulator());
        sim.RegisterSystem(new CombatSystem());
        sim.RegisterSystem(new MilitaryManager());
        sim.RegisterSystem(new WorldEventManager());

        // ── Persistence ───────────────────────────────────────────────────
        sim.RegisterSystem(new WorldQuerySystem());
        sim.RegisterSystem(new SaveGameSystem());
        sim.RegisterSystem(new SaveSystem());

        // ── Initialize everything ─────────────────────────────────────────
        sim.Initialize();

        // ── Wire SaveSystem back-reference ────────────────────────────────
        sim.Context.Get<SaveSystem>().SetSimulation(sim);

        // ── Register job strategies post-initialize (they need ctx.Get<>) ─
        jobSystem.RegisterStrategy(new MineTileStrategy());
        jobSystem.RegisterStrategy(new CutTreeStrategy());
        jobSystem.RegisterStrategy(new HarvestPlantStrategy());
        jobSystem.RegisterStrategy(new HaulItemStrategy());
        jobSystem.RegisterStrategy(new PlaceBoxStrategy());
        jobSystem.RegisterStrategy(new ConstructBuildingStrategy());
        jobSystem.RegisterStrategy(new CraftItemStrategy());
        jobSystem.RegisterStrategy(new EatStrategy());
        jobSystem.RegisterStrategy(new DrinkStrategy());
        jobSystem.RegisterStrategy(new SleepStrategy());
        jobSystem.RegisterStrategy(new EngageHostileStrategy());
        jobSystem.RegisterStrategy(new IdleStrategy());

        return sim;
    }
}

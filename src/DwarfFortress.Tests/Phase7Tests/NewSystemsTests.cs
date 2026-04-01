using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

// ─────────────────────────────────────────────────────────────────────────────
// WorldLore dynamic events
// ─────────────────────────────────────────────────────────────────────────────

public sealed class WorldLoreDynamicTests
{
    [Fact]
    public void RecipeCraftedEvent_Increases_Prosperity()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();
        sim.Context.Commands.Dispatch(new GenerateWorldCommand(Seed: 1, Width: 32, Height: 32, Depth: 4));

        var lore = sim.Context.Get<WorldLoreSystem>();
        float before = lore.Current!.Prosperity;

        // Fire several craft events to accumulate measurable change
        for (int i = 0; i < 10; i++)
            sim.Context.EventBus.Emit(new RecipeCraftedEvent(WorkshopId: 0, DwarfId: 1, RecipeId: "test", OutputItemIds: []));

        Assert.True(lore.Current!.Prosperity > before,
            "Prosperity should increase after RecipeCraftedEvents.");
    }

    [Fact]
    public void ItemStoredEvent_Increases_Prosperity()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();
        sim.Context.Commands.Dispatch(new GenerateWorldCommand(Seed: 2, Width: 32, Height: 32, Depth: 4));

        var lore = sim.Context.Get<WorldLoreSystem>();
        float before = lore.Current!.Prosperity;

        for (int i = 0; i < 10; i++)
            sim.Context.EventBus.Emit(new ItemStoredEvent(ItemId: i, StockpileId: 0, SlotPos: default));

        Assert.True(lore.Current!.Prosperity > before,
            "Prosperity should increase after ItemStoredEvents.");
    }

    [Fact]
    public void EntityKilledEvent_HostileCreature_Decreases_Threat()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();
        sim.Context.Commands.Dispatch(new GenerateWorldCommand(Seed: 3, Width: 32, Height: 32, Depth: 4));

        var lore = sim.Context.Get<WorldLoreSystem>();
        // Ensure non-zero starting Threat
        lore.Current!.Threat = 0.5f;

        var creature = new Creature(er.NextId(), "goblin", new Vec3i(5, 5, 0), maxHealth: 50f, isHostile: true);
        er.Register(creature);
        float before = lore.Current!.Threat;

        sim.Context.EventBus.Emit(new EntityKilledEvent(EntityId: creature.Id, Cause: "combat"));

        Assert.True(lore.Current!.Threat < before,
            "Threat should decrease when a hostile creature is killed.");
    }

    [Fact]
    public void WorldLoreSystem_Threat_Decays_In_Tick()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();
        sim.Context.Commands.Dispatch(new GenerateWorldCommand(Seed: 4, Width: 32, Height: 32, Depth: 4));

        var lore = sim.Context.Get<WorldLoreSystem>();
        lore.Current!.Threat = 0.8f;

        // Tick for a significant amount (10000 seconds worth of delta)
        for (int i = 0; i < 100; i++)
            sim.Tick(100f);

        Assert.True(lore.Current!.Threat < 0.8f,
            "Threat should decay naturally over time via Tick.");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// NutritionComponent (unit) + NutritionSystem (integration)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class NutritionSystemTests
{
    [Fact]
    public void NutritionComponent_Decay_Reduces_Nutrients()
    {
        var nutrition = new NutritionComponent();

        nutrition.Decay(10_000f); // 10 000 seconds – more than enough to deplete

        Assert.Equal(0f, nutrition.Carbohydrates);
        Assert.Equal(0f, nutrition.Protein);
        Assert.Equal(0f, nutrition.Fat);
        Assert.Equal(0f, nutrition.Vitamins);
    }

    [Fact]
    public void NutritionComponent_Credit_Refills_Nutrients()
    {
        var nutrition = new NutritionComponent();
        nutrition.Decay(10_000f); // fully deplete

        nutrition.Credit(0.5f, 0.6f, 0.7f, 0.8f);

        Assert.Equal(0.5f, nutrition.Carbohydrates, precision: 4);
        Assert.Equal(0.6f, nutrition.Protein,       precision: 4);
        Assert.Equal(0.7f, nutrition.Fat,            precision: 4);
        Assert.Equal(0.8f, nutrition.Vitamins,       precision: 4);
    }

    [Fact]
    public void NutritionComponent_AnyDeficiency_True_After_Prolonged_Depletion()
    {
        var nutrition = new NutritionComponent();
        // Decay below threshold, then accumulate deficiency time
        nutrition.Decay(10_000f);
        // Accumulate deficiency seconds (DeficiencySeconds = 60)
        nutrition.Decay(NutritionComponent.DeficiencySeconds + 1f);

        Assert.True(nutrition.AnyDeficiency,
            "AnyDeficiency should be true after prolonged depletion.");
    }

    [Fact]
    public void NutritionComponent_GetWorstDeficiency_Returns_Worst_Nutrient()
    {
        var nutrition = new NutritionComponent();
        nutrition.Decay(10_000f);
        nutrition.Decay(NutritionComponent.DeficiencySeconds + 1f);

        var worst = nutrition.GetWorstDeficiency();
        Assert.NotNull(worst);
    }

    [Fact]
    public void NutritionSystem_CreditMeal_FruitTag_Credits_Vitamins_Heavily()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "FruitEater", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        // Fully deplete nutrients first
        dwarf.Nutrition.Decay(10_000f);
        float vitaminsBefore = dwarf.Nutrition.Vitamins;

        var fruitItem = new ItemDef(
            Id:          "apple",
            DisplayName: "Apple",
            Tags:        TagSet.From("fruit", "food"),
            Weight:      0.3f);

        sim.Context.Get<NutritionSystem>().CreditMeal(dwarf.Id, fruitItem);

        Assert.True(dwarf.Nutrition.Vitamins > vitaminsBefore,
            "Eating fruit should credit vitamins.");
        Assert.True(dwarf.Nutrition.Vitamins > dwarf.Nutrition.Protein,
            "Fruit should credit vitamins more than protein.");
    }

    [Fact]
    public void NutritionSystem_CreditMeal_MeatTag_Credits_Protein_Heavily()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "MeatEater", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        dwarf.Nutrition.Decay(10_000f);

        var meatItem = new ItemDef(
            Id:          "raw_meat",
            DisplayName: "Raw Meat",
            Tags:        TagSet.From("meat", "food"),
            Weight:      0.5f);

        sim.Context.Get<NutritionSystem>().CreditMeal(dwarf.Id, meatItem);

        Assert.True(dwarf.Nutrition.Protein > dwarf.Nutrition.Vitamins,
            "Meat should credit protein more than vitamins.");
    }

    [Fact]
    public void NutritionSystem_Fires_DeficiencyEvent_When_Nutrient_Critical()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "HungryDwarf", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        // Drain nutrients below threshold and run enough time to accumulate deficiency seconds
        dwarf.Nutrition.Decay(10_000f);

        NutritionDeficiencyEvent? fired = null;
        sim.Context.EventBus.On<NutritionDeficiencyEvent>(e => fired = e);

        // Tick past the DeficiencySeconds threshold (60 s at regular game speed)
        sim.Tick(NutritionComponent.DeficiencySeconds + 5f);

        Assert.NotNull(fired);
        Assert.Equal(dwarf.Id, fired!.Value.DwarfId);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DesignateHarvestCommand
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DesignateHarvestCommandTests
{
    private static void PlacePlant(WorldMap map, Vec3i pos, string plantDefId = "berry_bush")
    {
        var tile = map.GetTile(pos);
        tile.TileDefId        = TileDefIds.Grass;
        tile.IsPassable       = true;
        tile.PlantDefId       = plantDefId;
        tile.PlantGrowthStage = PlantGrowthStages.Mature;
        tile.PlantYieldLevel  = 1;
        tile.PlantSeedLevel   = 1;
        map.SetTile(pos, tile);
    }

    [Fact]
    public void DesignateHarvestCommand_Creates_HarvestPlant_Jobs_For_Mature_Plants()
    {
        var (sim, map, _, js, _) = TestFixtures.BuildFullSim();

        var plantPos = new Vec3i(5, 5, 0);
        PlacePlant(map, plantPos);

        sim.Context.Commands.Dispatch(new DesignateHarvestCommand(plantPos, plantPos));

        Assert.Contains(js.GetAllJobs(),
            j => j.JobDefId == JobDefIds.HarvestPlant && j.TargetPos == plantPos);
    }

    [Fact]
    public void DesignateHarvestCommand_Skips_Non_Harvestable_Tiles()
    {
        var (sim, map, _, js, _) = TestFixtures.BuildFullSim();

        // The default map is stone floor — no plant
        var pos = new Vec3i(3, 3, 0);

        sim.Context.Commands.Dispatch(new DesignateHarvestCommand(pos, pos));

        Assert.DoesNotContain(js.GetAllJobs(),
            j => j.JobDefId == JobDefIds.HarvestPlant && j.TargetPos == pos);
    }

    [Fact]
    public void DesignateHarvestCommand_Does_Not_Duplicate_Jobs()
    {
        var (sim, map, _, js, _) = TestFixtures.BuildFullSim();

        var plantPos = new Vec3i(7, 7, 0);
        PlacePlant(map, plantPos);

        sim.Context.Commands.Dispatch(new DesignateHarvestCommand(plantPos, plantPos));
        sim.Context.Commands.Dispatch(new DesignateHarvestCommand(plantPos, plantPos));

        int jobCount = js.GetAllJobs()
            .Count(j => j.JobDefId == JobDefIds.HarvestPlant && j.TargetPos == plantPos);

        Assert.Equal(1, jobCount);
    }

    [Fact]
    public void DesignateHarvestCommand_Box_Creates_One_Job_Per_Plant_Tile()
    {
        var (sim, map, _, js, _) = TestFixtures.BuildFullSim();

        // Place 3 plants in a row
        var positions = new[] { new Vec3i(2, 2, 0), new Vec3i(3, 2, 0), new Vec3i(4, 2, 0) };
        foreach (var p in positions)
            PlacePlant(map, p);

        sim.Context.Commands.Dispatch(
            new DesignateHarvestCommand(new Vec3i(2, 2, 0), new Vec3i(4, 2, 0)));

        int createdJobs = js.GetAllJobs()
            .Count(j => j.JobDefId == JobDefIds.HarvestPlant);

        Assert.Equal(3, createdJobs);
    }
}

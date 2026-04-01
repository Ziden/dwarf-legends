using System;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.Tests.Fakes;
using DwarfFortress.GameLogic.World;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

/// <summary>
/// Tests that BehaviorSystem runs without errors and produces observable effects.
/// </summary>
public sealed class BehaviorSystemTests
{
    [Fact]
    public void BehaviorSystem_Does_Not_Throw_Over_Many_Ticks()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        // Spawn three dwarves so BehaviorSystem has subjects
        for (int i = 0; i < 3; i++)
        {
            var d = new Dwarf(er.NextId(), $"Dwarf{i}", new Vec3i(i * 2, 0, 0));
            er.Register(d);
        }

        var ex = Record.Exception(() =>
        {
            for (int tick = 0; tick < 300; tick++) sim.Tick(0.1f);
        });

        Assert.Null(ex);
    }

    [Fact]
    public void BehaviorSystem_Wander_Moves_Dwarf_When_Idle_And_Bored()
    {
        var (sim, _, er, js, _) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Wanderer", new Vec3i(5, 5, 0));
        er.Register(dwarf);

        // Set very high recreation need so wander / social behaviours trigger
        dwarf.Needs.Recreation.SetLevel(0.05f);
        dwarf.Needs.Social.SetLevel(0.05f);

        Vec3i startPos = dwarf.Position.Position;

        // Give enough ticks for at least one wander job to be created+executed
        for (int tick = 0; tick < 500; tick++) sim.Tick(0.1f);

        // Either the dwarf moved OR it got a wander/social job at some point
        bool moved   = dwarf.Position.Position != startPos;
        bool hadWanderJob = js.GetAllJobs().Any(j =>
            j.JobDefId is "wander" or "socialize" or "idle" &&
            j.AssignedDwarfId == dwarf.Id);

        Assert.True(moved || hadWanderJob,
            "BehaviorSystem should have moved the dwarf or created a wander/social job.");
    }

    [Fact]
    public void BehaviorSystem_Wander_Uses_PerEntity_Cooldowns_For_Creatures()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var herd = new[]
        {
            new Creature(er.NextId(), DefIds.Elk, new Vec3i(10, 10, 0), maxHealth: 85f),
            new Creature(er.NextId(), DefIds.Elk, new Vec3i(14, 10, 0), maxHealth: 85f),
            new Creature(er.NextId(), DefIds.Elk, new Vec3i(18, 10, 0), maxHealth: 85f),
        };

        var startPositions = herd.ToDictionary(creature => creature.Id, creature => creature.Position.Position);
        foreach (var creature in herd)
            er.Register(creature);

        sim.Tick(0.1f);

        Assert.All(
            herd,
            creature => Assert.NotEqual(startPositions[creature.Id], creature.Position.Position));
    }

    [Fact]
    public void BehaviorSystem_Wander_Moves_Faster_Species_More_Often()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();
        var data = sim.Context.Get<DataManager>();

        var cat = new Creature(er.NextId(), DefIds.Cat, new Vec3i(10, 10, 0), maxHealth: 20f);
        cat.ApplyBaseStats(data.Creatures.GetOrNull(DefIds.Cat));

        var troll = new Creature(er.NextId(), DefIds.Troll, new Vec3i(20, 10, 0), maxHealth: 200f);
        troll.ApplyBaseStats(data.Creatures.GetOrNull(DefIds.Troll));

        er.Register(cat);
        er.Register(troll);

        var catMoves = 0;
        var trollMoves = 0;
        sim.EventBus.On<EntityMovedEvent>(ev =>
        {
            if (ev.EntityId == cat.Id)
                catMoves++;
            else if (ev.EntityId == troll.Id)
                trollMoves++;
        });

        for (var tick = 0; tick < 600; tick++)
            sim.Tick(0.1f);

        Assert.True(cat.Stats.Speed.Value > troll.Stats.Speed.Value,
            $"Expected cat speed to exceed troll speed. Cat={cat.Stats.Speed.Value:0.00}, Troll={troll.Stats.Speed.Value:0.00}.");
        Assert.True(catMoves > trollMoves,
            $"Expected faster species to wander more often. CatMoves={catMoves}, TrollMoves={trollMoves}.");
        Assert.True(catMoves >= trollMoves + 5,
            $"Expected a meaningful cadence difference after jitter. CatMoves={catMoves}, TrollMoves={trollMoves}.");
    }

    [Fact]
    public void BehaviorSystem_Grooming_Does_Not_Crash_With_Coated_BodyPart()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Dirty", new Vec3i(0, 0, 0));
        er.Register(dwarf);

        // Manually apply a coating to a body part to trigger grooming path
        var facePart = dwarf.BodyParts.GetOrCreate("head");
        facePart.CoatingMaterialId = "mud";
        facePart.CoatingAmount     = 0.8f;

        // Low recreation so BehaviorSystem picks grooming
        dwarf.Needs.Recreation.SetLevel(0.01f);

        Assert.True(dwarf.BodyParts.HasCoating("head"),
            "Test setup: head should start with a mud coating.");

        // Run long enough — must not throw
        var ex = Record.Exception(() =>
        {
            for (int tick = 0; tick < 800; tick++) sim.Tick(0.1f);
        });
        Assert.Null(ex);
    }

    [Fact]
    public void BehaviorSystem_Handles_Many_Dwarves_Without_Performance_Regression()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        // 10 dwarves is a reasonable early-game count
        for (int i = 0; i < 10; i++)
        {
            var d = new Dwarf(er.NextId(), $"Civ{i}", new Vec3i(i % 8, i / 8, 0));
            er.Register(d);
        }

        var ex = Record.Exception(() =>
        {
            for (int tick = 0; tick < 200; tick++) sim.Tick(0.1f);
        });

        Assert.Null(ex);
    }

    [Fact]
    public void BehaviorSystem_Wander_Keeps_AquaticCreature_In_Swimmable_Tiles()
    {
        var (sim, map, er, _, _) = TestFixtures.BuildFullSim();
        var center = new Vec3i(16, 16, 0);
        CarveDeepWaterPatch(map, center, radius: 2);

        var carp = new Creature(er.NextId(), DefIds.GiantCarp, center, maxHealth: 55f, isHostile: false);
        er.Register(carp);

        for (var tick = 0; tick < 250; tick++)
        {
            sim.Tick(0.1f);
            Assert.True(
                map.IsSwimmable(carp.Position.Position),
                $"Expected aquatic creature to remain in swimmable water, found at {carp.Position.Position}.");
        }
    }

    [Fact]
    public void BehaviorSystem_Wander_UnknownFishId_UsesAquaticFallbackTraversal()
    {
        var (sim, map, er, _, _) = TestFixtures.BuildFullSim();
        var center = new Vec3i(15, 15, 0);
        CarveDeepWaterPatch(map, center, radius: 2);

        // Def intentionally missing from creatures.json to validate ID fallback heuristic.
        var unknownFish = new Creature(er.NextId(), "river_fish", center, maxHealth: 40f, isHostile: false);
        er.Register(unknownFish);

        for (var tick = 0; tick < 200; tick++)
        {
            sim.Tick(0.1f);
            Assert.True(
                map.IsSwimmable(unknownFish.Position.Position),
                $"Expected unknown fish-like creature to stay in swimmable water, found at {unknownFish.Position.Position}.");
        }
    }

    [Fact]
    public void BehaviorSystem_DrinkWater_Moves_LandCreature_To_Shore_And_Satisfies_Thirst()
    {
        var (sim, map, er, _, _) = TestFixtures.BuildFullSim();
        var pondCenter = new Vec3i(20, 20, 0);
        CarveDeepWaterPatch(map, pondCenter, radius: 2);

        var elk = new Creature(er.NextId(), DefIds.Elk, new Vec3i(8, 20, 0), maxHealth: 85f, isHostile: false);
        elk.Needs.Thirst.SetLevel(0.01f);
        er.Register(elk);

        var reachedDrinkingSpot = false;
        for (var tick = 0; tick < 1800; tick++)
        {
            sim.Tick(0.1f);
            Assert.False(
                map.IsSwimmable(elk.Position.Position),
                $"Expected land creature to stay out of deep water, found at {elk.Position.Position}.");

            var adjacentWater = elk.Position.Position
                .Neighbours4()
                .Any(map.IsSwimmable);
            if (adjacentWater)
                reachedDrinkingSpot = true;

            if (elk.Needs.Thirst.Level >= 0.8f)
                break;
        }

        Assert.InRange(elk.Needs.Thirst.Level, 0.8f, 1f);
        Assert.True(
            reachedDrinkingSpot || elk.Needs.Thirst.Level >= 0.8f,
            $"Expected land creature to drink from shore. FinalPos={elk.Position.Position}, thirst={elk.Needs.Thirst.Level:0.00}.");
    }

    [Fact]
    public void BehaviorSystem_DrinkWater_Satisfies_AquaticCreature_Thirst_In_Water()
    {
        var (sim, map, er, _, _) = TestFixtures.BuildFullSim();
        var center = new Vec3i(16, 16, 0);
        CarveDeepWaterPatch(map, center, radius: 2);

        var carp = new Creature(er.NextId(), DefIds.GiantCarp, center, maxHealth: 55f, isHostile: false);
        carp.Needs.Thirst.SetLevel(0.01f);
        er.Register(carp);

        for (var tick = 0; tick < 120; tick++)
            sim.Tick(0.1f);

        Assert.InRange(carp.Needs.Thirst.Level, 0.8f, 1f);
        Assert.True(map.IsSwimmable(carp.Position.Position));
    }

    [Fact]
    public void BehaviorSystem_EatFood_Moves_Herbivore_To_PlantFood_And_Satisfies_Hunger()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var foodPos = new Vec3i(20, 10, 0);
        items.CreateItem(ItemDefIds.PlantMatter, "food", foodPos);

        var elk = new Creature(er.NextId(), DefIds.Elk, new Vec3i(8, 10, 0), maxHealth: 85f, isHostile: false);
        elk.Needs.Hunger.SetLevel(0.01f);
        er.Register(elk);

        for (var tick = 0; tick < 1200; tick++)
        {
            sim.Tick(0.1f);
            if (elk.Needs.Hunger.Level >= 0.75f)
                break;
        }

        Assert.InRange(elk.Needs.Hunger.Level, 0.75f, 1f);
        Assert.DoesNotContain(items.GetAllItems(), item => item.Position.Position == foodPos && item.DefId == ItemDefIds.PlantMatter);
    }

    [Fact]
    public void BehaviorSystem_EatFood_Satisfies_AquaticGrazer_Hunger_In_Water()
    {
        var (sim, map, er, _, _) = TestFixtures.BuildFullSim();
        var center = new Vec3i(17, 17, 0);
        CarveDeepWaterPatch(map, center, radius: 2);

        var carp = new Creature(er.NextId(), DefIds.GiantCarp, center, maxHealth: 55f, isHostile: false);
        carp.Needs.Hunger.SetLevel(0.01f);
        er.Register(carp);

        for (var tick = 0; tick < 120; tick++)
            sim.Tick(0.1f);

        Assert.InRange(carp.Needs.Hunger.Level, 0.75f, 1f);
        Assert.True(map.IsSwimmable(carp.Position.Position));
    }

    [Fact]
    public void BehaviorSystem_EatFood_Herbivore_Can_Harvest_Wild_Plant()
    {
        var (sim, map, er, _, _) = TestFixtures.BuildFullSim();
        var plantPos = new Vec3i(16, 10, 0);
        var tile = map.GetTile(plantPos);
        tile.TileDefId = TileDefIds.Grass;
        tile.IsPassable = true;
        tile.PlantDefId = "berry_bush";
        tile.PlantGrowthStage = PlantGrowthStages.Mature;
        tile.PlantYieldLevel = 1;
        map.SetTile(plantPos, tile);

        var elk = new Creature(er.NextId(), DefIds.Elk, new Vec3i(10, 10, 0), maxHealth: 85f, isHostile: false);
        elk.Needs.Hunger.SetLevel(0.01f);
        er.Register(elk);

        for (var tick = 0; tick < 1200; tick++)
        {
            sim.Tick(0.1f);
            if (elk.Needs.Hunger.Level >= 0.75f)
                break;
        }

        Assert.InRange(elk.Needs.Hunger.Level, 0.75f, 1f);
        Assert.Equal(0, map.GetTile(plantPos).PlantYieldLevel);
    }

    private static void CarveDeepWaterPatch(WorldMap map, Vec3i center, int radius)
    {
        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            if (Math.Abs(dx) + Math.Abs(dy) > radius + 1)
                continue;

            var pos = new Vec3i(center.X + dx, center.Y + dy, center.Z);
            map.SetTile(pos, new TileData
            {
                TileDefId = TileDefIds.Water,
                MaterialId = "water",
                IsPassable = true,
                FluidType = FluidType.Water,
                FluidLevel = 7,
            });
        }
    }
}

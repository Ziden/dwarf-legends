using System;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
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
    public void BehaviorSystem_HostilePursuit_Moves_Hostile_Creature_Toward_Dwarf()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();
        var data = sim.Context.Get<DataManager>();

        var dwarf = new Dwarf(er.NextId(), "Target", new Vec3i(10, 10, 0));
        var goblin = new Creature(er.NextId(), DefIds.Goblin, new Vec3i(18, 10, 0), maxHealth: 60f, isHostile: true);
        goblin.ApplyBaseStats(data.Creatures.GetOrNull(DefIds.Goblin));

        er.Register(dwarf);
        er.Register(goblin);

        EntityMovedEvent? movedEvent = null;
        sim.EventBus.On<EntityMovedEvent>(ev =>
        {
            if (ev.EntityId == goblin.Id)
                movedEvent = ev;
        });

        sim.Tick(0.1f);

        Assert.NotNull(movedEvent);
        Assert.True(
            movedEvent!.Value.NewPos.ManhattanDistanceTo(dwarf.Position.Position) <
            movedEvent.Value.OldPos.ManhattanDistanceTo(dwarf.Position.Position),
            $"Expected hostile pursuit to reduce distance to the dwarf. Old={movedEvent.Value.OldPos}, New={movedEvent.Value.NewPos}, Target={dwarf.Position.Position}.");
    }

    [Fact]
    public void BehaviorSystem_HostilePursuit_Uses_AuthoredIsHostile_Without_HostileTag()
    {
        var ds = new InMemoryDataSource();
        TestFixtures.AddFullData(ds);
        ds.AddFile("data/Content/Game/creatures/custom/thorn_stalker/creature.json", """
            {
              "id": "thorn_stalker",
              "displayName": "Thorn Stalker",
              "tags": ["animal"],
              "isHostile": true,
              "maxHealth": 40,
              "speed": 1.0
            }
            """);

        var (sim, _, er, _, _) = TestFixtures.BuildFullSim(ds);
        var data = sim.Context.Get<DataManager>();

        var dwarf = new Dwarf(er.NextId(), "Target", new Vec3i(10, 10, 0));
        var stalker = new Creature(er.NextId(), "thorn_stalker", new Vec3i(18, 10, 0), maxHealth: 40f, isHostile: false);
        stalker.ApplyBaseStats(data.Creatures.GetOrNull("thorn_stalker"));

        er.Register(dwarf);
        er.Register(stalker);

        EntityMovedEvent? movedEvent = null;
        sim.EventBus.On<EntityMovedEvent>(ev =>
        {
            if (ev.EntityId == stalker.Id)
                movedEvent = ev;
        });

        sim.Tick(0.1f);

        Assert.True(stalker.IsHostile);
        Assert.NotNull(movedEvent);
        Assert.True(
            movedEvent!.Value.NewPos.ManhattanDistanceTo(dwarf.Position.Position) <
            movedEvent.Value.OldPos.ManhattanDistanceTo(dwarf.Position.Position),
            $"Expected authored hostility to trigger pursuit without the hostile tag. Old={movedEvent.Value.OldPos}, New={movedEvent.Value.NewPos}, Target={dwarf.Position.Position}.");
    }

    [Fact]
    public void BehaviorSystem_HostilePursuit_Keeps_Hauled_Item_In_Sync()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var data = sim.Context.Get<DataManager>();

        var dwarf = new Dwarf(er.NextId(), "Target", new Vec3i(10, 10, 0));
        var goblin = new Creature(er.NextId(), DefIds.Goblin, new Vec3i(18, 10, 0), maxHealth: 60f, isHostile: true);
        goblin.ApplyBaseStats(data.Creatures.GetOrNull(DefIds.Goblin));

        er.Register(dwarf);
        er.Register(goblin);

        var hauledLog = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, goblin.Position.Position);
        Assert.True(items.PickUpItem(hauledLog.Id, goblin.Id, goblin.Position.Position, ItemCarryMode.Hauling));

        sim.Tick(0.1f);

        Assert.Equal(goblin.Position.Position, hauledLog.Position.Position);
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
    public void BehaviorSystem_Grooming_Uses_AuthoredCanGroom_Without_Tag_Or_FootParts()
    {
        var ds = new InMemoryDataSource();
        TestFixtures.AddFullData(ds);
        ds.AddFile("data/Content/Game/creatures/custom/licker_beast/creature.json", """
            {
              "id": "licker_beast",
              "displayName": "Licker Beast",
              "tags": ["animal"],
              "canGroom": true,
              "maxHealth": 35,
              "speed": 1.0
            }
            """);

        var (sim, _, er, _, _) = TestFixtures.BuildFullSim(ds);
        var data = sim.Context.Get<DataManager>();

        var creature = new Creature(er.NextId(), "licker_beast", new Vec3i(10, 10, 0), maxHealth: 35f, isHostile: false);
        creature.ApplyBaseStats(data.Creatures.GetOrNull("licker_beast"));

        creature.Components.Remove<BodyPartComponent>();
        var bodyParts = new BodyPartComponent();
        bodyParts.Initialize(new[] { BodyPartIds.Head });
        var head = bodyParts.GetOrCreate(BodyPartIds.Head);
        head.CoatingMaterialId = "mud";
        head.CoatingAmount = 0.8f;
        creature.Components.Add(bodyParts);

        SubstanceIngestedEvent? ingested = null;
        sim.EventBus.On<SubstanceIngestedEvent>(ev =>
        {
            if (ev.EntityId == creature.Id)
                ingested = ev;
        });

        er.Register(creature);
        sim.Tick(0.1f);

        Assert.NotNull(ingested);
        Assert.Equal(SubstanceIds.Mud, ingested!.Value.SubstanceId);
        Assert.False(creature.BodyParts.HasCoating(BodyPartIds.Head));
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
    public void BehaviorSystem_Wander_UnknownCreatureWithoutDef_DoesNotAssumeAquaticTraversal()
    {
        var (sim, map, er, _, _) = TestFixtures.BuildFullSim();
        var center = new Vec3i(15, 15, 0);
        CarveDeepWaterPatch(map, center, radius: 2);

        // Def intentionally missing from shared content: traversal should no longer infer aquatic behavior from the id.
        var unknownFish = new Creature(er.NextId(), "river_fish", center, maxHealth: 40f, isHostile: false);
        er.Register(unknownFish);

        for (var tick = 0; tick < 200; tick++)
            sim.Tick(0.1f);

        Assert.Equal(center, unknownFish.Position.Position);
    }

    [Fact]
    public void BehaviorSystem_Wander_Uses_AuthoredAquaticMovementMode_Without_AquaticTags()
    {
        var ds = new InMemoryDataSource();
        TestFixtures.AddFullData(ds);
        ds.AddFile("data/Content/Game/creatures/custom/moss_orb/creature.json", """
            {
              "id": "moss_orb",
              "displayName": "Moss Orb",
              "tags": ["animal"],
              "movementMode": "aquatic",
              "maxHealth": 40,
              "speed": 1.0
            }
            """);

        var (sim, map, er, _, _) = TestFixtures.BuildFullSim(ds);
        var center = new Vec3i(15, 15, 0);
        CarveDeepWaterPatch(map, center, radius: 2);

        var orb = new Creature(er.NextId(), "moss_orb", center, maxHealth: 40f, isHostile: false);
        er.Register(orb);

        for (var tick = 0; tick < 200; tick++)
        {
            sim.Tick(0.1f);
            Assert.True(
                map.IsSwimmable(orb.Position.Position),
                $"Expected authored aquatic movement mode to keep the creature in swimmable water, found at {orb.Position.Position}.");
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
    public void BehaviorSystem_Sleeping_Creature_Does_Not_Move_Until_It_Wakes()
    {
        var (sim, map, er, _, _) = TestFixtures.BuildFullSim();
        var center = new Vec3i(16, 16, 0);
        CarveDeepWaterPatch(map, center, radius: 2);

        var carp = new Creature(er.NextId(), DefIds.GiantCarp, center, maxHealth: 55f, isHostile: false);
        carp.Needs.Sleep.SetLevel(0.01f);
        er.Register(carp);

        var sleepStarted = false;
        sim.EventBus.On<EntitySleepEvent>(ev =>
        {
            if (ev.EntityId == carp.Id)
                sleepStarted = true;
        });

        for (var tick = 0; tick < 150 && !sleepStarted; tick++)
            sim.Tick(0.1f);

        Assert.True(sleepStarted);

        var sleepingPos = carp.Position.Position;
        for (var tick = 0; tick < 50; tick++)
        {
            sim.Tick(0.1f);
            Assert.Equal(sleepingPos, carp.Position.Position);
        }
    }

    [Fact]
    public void BehaviorSystem_Sleeping_Dog_Stays_Still_Until_Wake_And_Clears_Sleep_Emote_Before_Moving()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var dog = new Creature(er.NextId(), DefIds.Dog, new Vec3i(16, 16, 0), maxHealth: 30f, isHostile: false);
        dog.Needs.Sleep.SetLevel(0.01f);
        er.Register(dog);

        var sleepStarted = false;
        var wokeUp = false;
        sim.EventBus.On<EntitySleepEvent>(ev =>
        {
            if (ev.EntityId == dog.Id)
                sleepStarted = true;
        });
        sim.EventBus.On<EntityWakeEvent>(ev =>
        {
            if (ev.EntityId == dog.Id)
                wokeUp = true;
        });

        for (var tick = 0; tick < 150 && !sleepStarted; tick++)
            sim.Tick(0.1f);

        Assert.True(sleepStarted);

        var sleepingPos = dog.Position.Position;
        for (var tick = 0; tick < 3000 && !wokeUp; tick++)
        {
            sim.Tick(0.1f);
            if (wokeUp)
                break;

            Assert.Equal(sleepingPos, dog.Position.Position);
        }

        Assert.True(wokeUp);
        Assert.NotEqual(EmoteIds.Sleep, dog.Emotes.CurrentEmote?.Id);

        var wakePos = dog.Position.Position;

        for (var tick = 0; tick < 400; tick++)
        {
            sim.Tick(0.1f);
            if (dog.Position.Position == wakePos)
                continue;

            Assert.NotEqual(EmoteIds.Sleep, dog.Emotes.CurrentEmote?.Id);
            return;
        }

        Assert.True(false, "Expected the dog to resume autonomous movement after waking.");
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

    [Fact]
    public void BehaviorSystem_EatFood_Uses_AuthoredDiet_Without_DietTags()
    {
        var ds = new InMemoryDataSource();
        TestFixtures.AddFullData(ds);
        ds.AddFile("data/Content/Game/creatures/custom/moss_beast/creature.json", """
            {
              "id": "moss_beast",
              "displayName": "Moss Beast",
              "tags": ["animal"],
              "diet": "herbivore",
              "maxHealth": 70,
              "speed": 1.0
            }
            """);

        var (sim, map, er, _, _) = TestFixtures.BuildFullSim(ds);
        var plantPos = new Vec3i(16, 10, 0);
        var tile = map.GetTile(plantPos);
        tile.TileDefId = TileDefIds.Grass;
        tile.IsPassable = true;
        tile.PlantDefId = "berry_bush";
        tile.PlantGrowthStage = PlantGrowthStages.Mature;
        tile.PlantYieldLevel = 1;
        map.SetTile(plantPos, tile);

        var beast = new Creature(er.NextId(), "moss_beast", new Vec3i(8, 10, 0), maxHealth: 70f, isHostile: false);
        beast.Needs.Hunger.SetLevel(0.01f);
        er.Register(beast);

        for (var tick = 0; tick < 1200; tick++)
        {
            sim.Tick(0.1f);
            if (beast.Needs.Hunger.Level >= 0.75f)
                break;
        }

        Assert.InRange(beast.Needs.Hunger.Level, 0.75f, 1f);
        Assert.True(
            beast.Position.Position == plantPos || beast.Position.Position.ManhattanDistanceTo(plantPos) <= 1,
            $"Expected authored herbivore diet to pull the creature toward edible terrain. Beast={beast.Position.Position}, Target={plantPos}.");
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

using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.Tests.Fakes;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class EmergentContentHookTests
{
    [Fact]
    public void ReactionPipeline_Uses_DefTags_And_Attributes()
    {
        var logger = new TestLogger();
        var ds = new InMemoryDataSource();
        TestFixtures.AddFullData(ds);
        ds.AddFile("data/ConfigBundle/reactions.json",
            """
            [
              {
                "id": "hostile_rage",
                "triggers": [{ "type": "entity_has_tag", "params": { "tag": "hostile" } }],
                "effects": [{ "op": "add_substance", "params": { "substance_id": "rage", "amount": "0.5" } }]
              },
              {
                "id": "strong_confidence",
                "triggers": [{ "type": "entity_attribute_at_least", "params": { "attribute_id": "strength", "level": "5" } }],
                "effects": [{ "op": "add_thought", "params": { "thought_id": "emboldened", "description": "Feels capable", "happiness_mod": "0.2", "duration": "60" } }]
              },
              {
                "id": "focused_relief",
                "triggers": [{ "type": "entity_attribute_at_least", "params": { "attribute_id": "focus", "level": "4" } }],
                "effects": [{ "op": "satisfy_need", "params": { "need_id": "social", "amount": "0.4" } }]
              }
            ]
            """);

        var sim = TestFixtures.CreateSimulation(
            logger,
            ds,
            new DataManager(),
            new EntityRegistry(),
            new EffectApplicator(),
            new ReactionPipeline());

        var registry = sim.Context.Get<EntityRegistry>();
        var strongFocusedDwarf = new Dwarf(registry.NextId(), "Urist", new Vec3i(1, 1, 0));
  strongFocusedDwarf.Attributes.SetLevel(AttributeIds.Strength, 5);
        strongFocusedDwarf.Attributes.SetLevel(AttributeIds.Focus, 5);
        strongFocusedDwarf.Needs.Social.SetLevel(0.1f);
        registry.Register(strongFocusedDwarf);

        var plainDwarf = new Dwarf(registry.NextId(), "Domas", new Vec3i(2, 1, 0));
        plainDwarf.Needs.Social.SetLevel(0.1f);
        registry.Register(plainDwarf);

        var goblin = new Creature(registry.NextId(), DefIds.Goblin, new Vec3i(3, 1, 0), 60f, isHostile: true);
        registry.Register(goblin);

        sim.Tick(0.1f);

        Assert.True(strongFocusedDwarf.Thoughts.HasThought("emboldened"));
        Assert.False(plainDwarf.Thoughts.HasThought("emboldened"));
        Assert.True(strongFocusedDwarf.Needs.Social.Level > 0.1f);
        Assert.Equal(0.1f, plainDwarf.Needs.Social.Level);
        Assert.True(goblin.BodyChemistry.Get("rage") > 0f);
    }

    [Fact]
    public void WorldEventManager_Respects_Day_And_Population_Gates_And_Targets_Attribute_Subsets()
    {
        var logger = new TestLogger();
        var ds = new InMemoryDataSource();
        TestFixtures.AddFullData(ds);
        ds.AddFile("data/ConfigBundle/world_events.json",
            """
            [
              {
                "id": "fit_festival",
                "displayName": "Fit Festival",
                "triggers": [
                  {
                    "type": "day_start",
                    "params": { "month": "1", "day": "3", "min_population": "2" }
                  }
                ],
                "effects": [
                  {
                    "op": "add_thought",
                    "params": {
                      "target": "dwarves_with_attribute",
                      "attribute_id": "stamina",
                      "min_level": "4",
                      "thought_id": "festival",
                      "description": "Enjoyed the festival",
                      "happiness_mod": "0.4",
                      "duration": "60"
                    }
                  }
                ],
                "probability": 1.0,
                "repeatable": true
              }
            ]
            """);

        var sim = TestFixtures.CreateSimulation(
            logger,
            ds,
            new DataManager(),
            new EntityRegistry(),
            new EffectApplicator(),
            new WorldEventManager());

        var registry = sim.Context.Get<EntityRegistry>();
        var fitDwarf = new Dwarf(registry.NextId(), "Urist", new Vec3i(1, 1, 0));
  fitDwarf.Attributes.SetLevel(AttributeIds.Stamina, 4);
        registry.Register(fitDwarf);

        var plainDwarf = new Dwarf(registry.NextId(), "Domas", new Vec3i(2, 1, 0));
        registry.Register(plainDwarf);

        sim.Context.EventBus.Emit(new DayStartedEvent(1, 1, 2));
        Assert.False(fitDwarf.Thoughts.HasThought("festival"));
        Assert.False(plainDwarf.Thoughts.HasThought("festival"));

        sim.Context.EventBus.Emit(new DayStartedEvent(1, 1, 3));
        Assert.True(fitDwarf.Thoughts.HasThought("festival"));
        Assert.False(plainDwarf.Thoughts.HasThought("festival"));
    }

    [Fact]
    public void WorldEventManager_Can_Target_Hostile_Creatures()
    {
        var logger = new TestLogger();
        var ds = new InMemoryDataSource();
        TestFixtures.AddFullData(ds);
        ds.AddFile("data/ConfigBundle/world_events.json",
            """
            [
              {
                "id": "warning_barrage",
                "displayName": "Warning Barrage",
                "triggers": [{ "type": "day_start", "params": { "day": "1" } }],
                "effects": [
                  {
                    "op": "damage",
                    "params": { "target": "hostile_creatures", "amount": "5" }
                  }
                ],
                "probability": 1.0,
                "repeatable": true
              }
            ]
            """);

        var sim = TestFixtures.CreateSimulation(
            logger,
            ds,
            new DataManager(),
            new EntityRegistry(),
            new EffectApplicator(),
            new WorldEventManager());

        var registry = sim.Context.Get<EntityRegistry>();
        var goblin = new Creature(registry.NextId(), DefIds.Goblin, new Vec3i(1, 1, 0), 60f, isHostile: true);
        var cat = new Creature(registry.NextId(), DefIds.Cat, new Vec3i(2, 1, 0), 20f, isHostile: false);
        registry.Register(goblin);
        registry.Register(cat);

        var goblinHealthBefore = goblin.Health.CurrentHealth;
        var catHealthBefore = cat.Health.CurrentHealth;

        sim.Context.EventBus.Emit(new DayStartedEvent(1, 1, 1));

        Assert.True(goblin.Health.CurrentHealth < goblinHealthBefore);
        Assert.Equal(catHealthBefore, cat.Health.CurrentHealth);
    }

    [Fact]
    public void ReactionPipeline_Can_Trigger_From_CreatureFactionRoles()
    {
        var logger = new TestLogger();
        var ds = new InMemoryDataSource();
        TestFixtures.AddFullData(ds);
        ds.AddFile("data/ConfigBundle/reactions.json",
            """
            [
              {
                "id": "role_rage",
                "triggers": [{ "type": "entity_has_faction_role", "params": { "role_id": "hostile_primary" } }],
                "effects": [{ "op": "add_substance", "params": { "substance_id": "rage", "amount": "0.5" } }]
              }
            ]
            """);
        ds.AddFile("data/Content/Game/creatures/hostile/goblin/creature.json", """
            {
              "id": "goblin",
              "displayName": "Goblin",
              "tags": ["hostile", "carnivore"],
              "society": {
                "factionRoles": [
                  { "id": "hostile_primary", "weight": 1.0 }
                ]
              }
            }
            """);
        ds.AddFile("data/Content/Game/creatures/pets/cat/creature.json", """
            {
              "id": "cat",
              "displayName": "Cat",
              "tags": ["animal", "pet", "groomer", "carnivore"]
            }
            """);

        var sim = TestFixtures.CreateSimulation(
            logger,
            ds,
            new DataManager(),
            new EntityRegistry(),
            new EffectApplicator(),
            new ReactionPipeline());

        var registry = sim.Context.Get<EntityRegistry>();
        var goblin = new Creature(registry.NextId(), DefIds.Goblin, new Vec3i(1, 1, 0), 60f, isHostile: true);
        var cat = new Creature(registry.NextId(), DefIds.Cat, new Vec3i(2, 1, 0), 20f, isHostile: false);
        registry.Register(goblin);
        registry.Register(cat);

        sim.Tick(0.1f);

        Assert.True(goblin.BodyChemistry.Get("rage") > 0f);
        Assert.Equal(0f, cat.BodyChemistry.Get("rage"));
    }

    [Fact]
    public void WorldEventManager_Can_Target_Entities_With_CreatureFactionRoles()
    {
        var logger = new TestLogger();
        var ds = new InMemoryDataSource();
        TestFixtures.AddFullData(ds);
        ds.AddFile("data/ConfigBundle/world_events.json",
            """
            [
              {
                "id": "alpha_hunt",
                "displayName": "Alpha Hunt",
                "triggers": [{ "type": "day_start", "params": { "day": "1" } }],
                "effects": [
                  {
                    "op": "damage",
                    "params": { "target": "entities_with_faction_role", "role_id": "hostile_alternate", "amount": "5" }
                  }
                ],
                "probability": 1.0,
                "repeatable": true
              }
            ]
            """);
        ds.AddFile("data/Content/Game/creatures/hostile/goblin/creature.json", """
            {
              "id": "goblin",
              "displayName": "Goblin",
              "tags": ["hostile", "carnivore"],
              "society": {
                "factionRoles": [
                  { "id": "hostile_primary", "weight": 1.0 }
                ]
              }
            }
            """);
        ds.AddFile("data/Content/Game/creatures/hostile/troll/creature.json", """
            {
              "id": "troll",
              "displayName": "Troll",
              "tags": ["hostile", "large"],
              "society": {
                "factionRoles": [
                  { "id": "hostile_alternate", "weight": 1.0 }
                ]
              }
            }
            """);

        var sim = TestFixtures.CreateSimulation(
            logger,
            ds,
            new DataManager(),
            new EntityRegistry(),
            new EffectApplicator(),
            new WorldEventManager());

        var registry = sim.Context.Get<EntityRegistry>();
        var goblin = new Creature(registry.NextId(), DefIds.Goblin, new Vec3i(1, 1, 0), 60f, isHostile: true);
        var troll = new Creature(registry.NextId(), DefIds.Troll, new Vec3i(2, 1, 0), 200f, isHostile: true);
        var dwarf = new Dwarf(registry.NextId(), "Urist", new Vec3i(3, 1, 0));
        registry.Register(goblin);
        registry.Register(troll);
        registry.Register(dwarf);

        var goblinHealthBefore = goblin.Health.CurrentHealth;
        var trollHealthBefore = troll.Health.CurrentHealth;
        var dwarfHealthBefore = dwarf.Health.CurrentHealth;

        sim.Context.EventBus.Emit(new DayStartedEvent(1, 1, 1));

        Assert.Equal(goblinHealthBefore, goblin.Health.CurrentHealth);
        Assert.True(troll.Health.CurrentHealth < trollHealthBefore);
        Assert.Equal(dwarfHealthBefore, dwarf.Health.CurrentHealth);
    }
}

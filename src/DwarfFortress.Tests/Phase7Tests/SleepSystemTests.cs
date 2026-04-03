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

public sealed class SleepSystemTests
{
    [Fact]
    public void SleepSystem_AquaticCreature_Stays_In_Water_When_Sleep_Goes_Critical()
    {
        var (sim, map, er, _, _) = TestFixtures.BuildFullSim();
        var center = new Vec3i(16, 16, 0);
        CarveDeepWaterPatch(map, center, radius: 2);

        var carp = new Creature(er.NextId(), DefIds.GiantCarp, center, maxHealth: 55f, isHostile: false);
        carp.Needs.Sleep.SetLevel(0.01f);
        er.Register(carp);

        for (var tick = 0; tick < 110; tick++)
            sim.Tick(0.1f);

        Assert.True(
            map.IsSwimmable(carp.Position.Position),
            $"Expected sleeping aquatic creature to remain in swimmable water, found at {carp.Position.Position}.");
    }

    [Fact]
    public void SleepSystem_AquaticCreature_Move_Updates_WorldQuery_And_SpatialIndex()
    {
        var (sim, map, er, _, _) = TestFixtures.BuildFullSim();
        var spatial = sim.Context.Get<SpatialIndexSystem>();
        var query = sim.Context.Get<WorldQuerySystem>();

        var waterCenter = new Vec3i(16, 16, 0);
        CarveDeepWaterPatch(map, waterCenter, radius: 2);

        var strandedPos = new Vec3i(12, 16, 0);
        var carp = new Creature(er.NextId(), DefIds.GiantCarp, strandedPos, maxHealth: 55f, isHostile: false);
        carp.Needs.Sleep.SetLevel(0.01f);
        er.Register(carp);

        for (var tick = 0; tick < 110; tick++)
            sim.Tick(0.1f);

        var resolvedPos = carp.Position.Position;
        Assert.NotEqual(strandedPos, resolvedPos);
        Assert.DoesNotContain(carp.Id, spatial.GetCreaturesAt(strandedPos));
        Assert.Contains(carp.Id, spatial.GetCreaturesAt(resolvedPos));
        Assert.DoesNotContain(query.QueryTile(strandedPos).Creatures, creature => creature.Id == carp.Id);
        Assert.Contains(query.QueryTile(resolvedPos).Creatures, creature => creature.Id == carp.Id);
    }

    [Fact]
    public void SleepSystem_LandCreature_SeekingSleep_Does_Not_Teleport()
    {
        var (sim, map, er, _, _) = TestFixtures.BuildFullSim();
        sim.Context.Get<BehaviorSystem>().IsEnabled = false;

        var poolCenter = new Vec3i(16, 16, 0);
        CarveDeepWaterPatch(map, poolCenter, radius: 2);

        var elk = new Creature(er.NextId(), DefIds.Elk, poolCenter, maxHealth: 85f, isHostile: false);
        elk.Needs.Sleep.SetLevel(0.01f);
        er.Register(elk);

        for (var tick = 0; tick < 110; tick++)
            sim.Tick(0.1f);

        Assert.InRange(elk.Position.Position.ManhattanDistanceTo(poolCenter), 0, 1);
    }

    [Fact]
    public void SleepSystem_Dwarf_Does_Not_Show_Sleep_Emote_While_Walking_To_Sleep_Spot()
    {
        var (sim, map, er, js, _) = TestFixtures.BuildFullSim();
        var sleepSystem = sim.Context.Get<SleepSystem>();

        map.SetTile(new Vec3i(14, 15, 0), new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = MaterialIds.Granite,
            IsPassable = false,
        });
        map.SetTile(new Vec3i(15, 14, 0), new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = MaterialIds.Granite,
            IsPassable = false,
        });

        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(4, 4, 0));
        dwarf.Needs.Sleep.SetLevel(0.01f);
        dwarf.Needs.Hunger.SetLevel(1f);
        dwarf.Needs.Thirst.SetLevel(1f);
        er.Register(dwarf);

        var sawSleepMoveStep = false;
        for (var tick = 0; tick < 300; tick++)
        {
            sim.Tick(0.1f);

            var job = js.GetAssignedJob(dwarf.Id);
            if (job?.JobDefId != JobDefIds.Sleep)
                continue;

            if (js.GetCurrentStep(job.Id) is not MoveToStep)
                continue;

            if (!sawSleepMoveStep)
            {
                sawSleepMoveStep = true;
                continue;
            }

            Assert.False(sleepSystem.IsSleeping(dwarf.Id));
            Assert.NotEqual(EmoteIds.Sleep, dwarf.Emotes.CurrentEmote?.Id);
            return;
        }

        Assert.True(false, "Expected the dwarf to spend at least one full tick walking toward a sleep target.");
    }

        [Fact]
        public void SleepSystem_Uses_Configured_Stamina_Effects()
        {
                var dwarf = new Dwarf(1, "Rigoth", new Vec3i(0, 0, 0));
                dwarf.Attributes.SetLevel(AttributeIds.Stamina, 4);

                var dataManager = CreateAttributeDataManager(
                        """
                        {
                            "stamina": {
                                "id": "stamina",
                                "displayName": "Stamina",
                                "description": "Test stamina config.",
                                "category": "physiological",
                                "tags": ["sleep"],
                                "effectCurves": {
                                    "3": { "effects": {} },
                                    "4": { "effects": { "sleep_need_decay_multiplier": 0.33, "sleep_recovery_multiplier": 1.77 } }
                                }
                            }
                        }
                        """);

                Assert.Equal(0.33f, SleepSystem.GetSleepDecayMultiplier(dwarf, dataManager), precision: 4);
                Assert.Equal(1.77f, SleepSystem.GetSleepRecoveryMultiplier(dwarf, dataManager), precision: 4);
        }

        private static DataManager CreateAttributeDataManager(string dwarfAttributesJson)
        {
                var (ctx, _, ds) = TestFixtures.CreateContext();
                TestFixtures.AddFullData(ds);
                ds.AddFile("data/ConfigBundle/dwarf_attributes.json", dwarfAttributesJson);

                var dataManager = new DataManager();
                dataManager.Initialize(ctx);
                return dataManager;
        }

    private static void CarveDeepWaterPatch(WorldMap map, Vec3i center, int radius)
    {
        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            if (System.Math.Abs(dx) + System.Math.Abs(dy) > radius + 1)
                continue;

            var pos = new Vec3i(center.X + dx, center.Y + dy, center.Z);
            if (!map.IsInBounds(pos))
                continue;

            var tile = map.GetTile(pos);
            tile.TileDefId = TileDefIds.Water;
            tile.IsPassable = true;
            tile.FluidType = FluidType.Water;
            tile.FluidMaterialId = "water";
            tile.FluidLevel = WorldMap.MinSwimmableWaterLevel;
            map.SetTile(pos, tile);
        }
    }
}

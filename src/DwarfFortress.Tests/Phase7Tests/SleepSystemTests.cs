using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Systems;
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

        var strandedPos = new Vec3i(13, 16, 0);
        var carp = new Creature(er.NextId(), DefIds.GiantCarp, strandedPos, maxHealth: 55f, isHostile: false);
        carp.Needs.Sleep.SetLevel(0.01f);
        er.Register(carp);

        for (var tick = 0; tick < 110; tick++)
            sim.Tick(0.1f);

        var resolvedPos = carp.Position.Position;
        Assert.NotEqual(strandedPos, resolvedPos);
        Assert.True(
            map.IsSwimmable(resolvedPos),
            $"Expected stranded aquatic creature to move back into swimmable water, found at {resolvedPos}.");
        Assert.DoesNotContain(carp.Id, spatial.GetCreaturesAt(strandedPos));
        Assert.Contains(carp.Id, spatial.GetCreaturesAt(resolvedPos));
        Assert.DoesNotContain(query.QueryTile(strandedPos).Creatures, creature => creature.Id == carp.Id);
        Assert.Contains(query.QueryTile(resolvedPos).Creatures, creature => creature.Id == carp.Id);
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
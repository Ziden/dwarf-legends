using System;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.Tests.Fakes;
using DwarfFortress.GameLogic.World;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class FluidSimulatorTests
{
    [Fact]
    public void FluidSimulator_ReducesLargeHorizontalWaterLevelGap()
    {
        var (_, logger, ds) = TestFixtures.CreateContext();
        var map = new WorldMap();
        var fluid = new FluidSimulator();
        var sim = TestFixtures.CreateSimulation(logger, ds, map, fluid);

        map.SetDimensions(5, 5, 1);

        // Isolate two neighboring passable cells so horizontal balancing is deterministic.
        for (var x = 0; x < 5; x++)
        for (var y = 0; y < 5; y++)
        {
            map.SetTile(new Vec3i(x, y, 0), new TileData
            {
                TileDefId = TileDefIds.GraniteWall,
                MaterialId = "granite",
                IsPassable = false,
            });
        }

        var high = new Vec3i(2, 2, 0);
        var low = new Vec3i(3, 2, 0);
        map.SetTile(high, new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            MaterialId = "granite",
            IsPassable = true,
            FluidType = FluidType.Water,
            FluidLevel = 7,
        });
        map.SetTile(low, new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            MaterialId = "granite",
            IsPassable = true,
            FluidType = FluidType.Water,
            FluidLevel = 1,
        });

        for (var i = 0; i < 20; i++)
            sim.Tick(0.1f);

        var highTile = map.GetTile(high);
        var lowTile = map.GetTile(low);
        var delta = Math.Abs(highTile.FluidLevel - lowTile.FluidLevel);

        Assert.True(delta <= 1,
            $"Expected isolated water levels to nearly equalize; got {highTile.FluidLevel} and {lowTile.FluidLevel}.");
    }
}

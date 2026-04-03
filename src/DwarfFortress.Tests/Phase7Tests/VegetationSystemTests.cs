using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class VegetationSystemTests
{
    [Fact]
    public void VegetationSystem_Tracks_Plants_Placed_After_Initialize()
    {
        var (sim, map, _, _, _) = TestFixtures.BuildFullSim();
        var plantPos = new Vec3i(12, 12, 0);
        var tile = map.GetTile(plantPos);
        tile.TileDefId = TileDefIds.Grass;
        tile.IsPassable = true;
        tile.PlantDefId = "berry_bush";
        tile.PlantGrowthStage = PlantGrowthStages.Seed;
        tile.PlantGrowthProgressSeconds = 0f;
        tile.PlantYieldLevel = 0;
        tile.PlantSeedLevel = 1;
        map.SetTile(plantPos, tile);

        sim.Tick(1f);

        var updatedTile = map.GetTile(plantPos);
        Assert.True(updatedTile.PlantGrowthProgressSeconds > 0f);
    }

    [Fact]
    public void VegetationSystem_Invalidates_Water_Cache_When_Nearby_Water_Changes()
    {
        var (sim, map, _, _, _) = TestFixtures.BuildFullSim();
        var plantPos = new Vec3i(14, 14, 0);
        var waterPos = new Vec3i(15, 14, 0);

        var plantTile = map.GetTile(plantPos);
        plantTile.TileDefId = TileDefIds.Grass;
        plantTile.IsPassable = true;
        plantTile.PlantDefId = "berry_bush";
        plantTile.PlantGrowthStage = PlantGrowthStages.Seed;
        plantTile.PlantGrowthProgressSeconds = 0f;
        plantTile.PlantYieldLevel = 0;
        plantTile.PlantSeedLevel = 1;
        map.SetTile(plantPos, plantTile);

        sim.Tick(1f);
        var afterDryTick = map.GetTile(plantPos);

        var waterTile = map.GetTile(waterPos);
        waterTile.TileDefId = TileDefIds.Water;
        waterTile.IsPassable = true;
        waterTile.FluidType = FluidType.Water;
        waterTile.FluidLevel = 1;
        map.SetTile(waterPos, waterTile);

        sim.Tick(1f);
        var afterWetTick = map.GetTile(plantPos);

        var wetGrowthDelta = afterWetTick.PlantGrowthProgressSeconds - afterDryTick.PlantGrowthProgressSeconds;
        Assert.True(wetGrowthDelta > 1f);
    }
}
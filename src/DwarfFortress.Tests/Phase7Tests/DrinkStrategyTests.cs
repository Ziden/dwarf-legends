using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.World;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class DrinkStrategyTests
{
    [Fact]
    public void Dwarf_Drinks_From_Nearby_Water_When_No_Drink_Items_Exist()
    {
        var (sim, map, er, _, items) = TestFixtures.BuildFullSim();

        foreach (var drink in items.GetAllItems().Where(item => item.DefId == ItemDefIds.Drink).ToList())
            items.DestroyItem(drink.Id);

        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(8, 8, 0));
        er.Register(dwarf);
        dwarf.Needs.Thirst.SetLevel(0.01f);

        var waterPos = new Vec3i(14, 8, 0);
        var waterTile = map.GetTile(waterPos);
        waterTile.TileDefId = TileDefIds.Water;
        waterTile.IsPassable = true;
        waterTile.FluidType = FluidType.Water;
        waterTile.FluidLevel = 7;
        map.SetTile(waterPos, waterTile);

        for (var tick = 0; tick < 1800; tick++)
        {
            sim.Tick(0.1f);
            if (dwarf.Needs.Thirst.Level >= 0.8f)
                break;
        }

        Assert.InRange(dwarf.Needs.Thirst.Level, 0.8f, 1f);
        Assert.True(dwarf.IsAlive);
        Assert.False(map.IsSwimmable(dwarf.Position.Position));
    }
}

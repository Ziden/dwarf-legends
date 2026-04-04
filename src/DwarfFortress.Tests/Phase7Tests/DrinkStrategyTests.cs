using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Systems;
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

    [Fact]
    public void Dwarf_Drinks_From_Fortress_Location_When_No_Local_Water_Exists()
    {
        var (sim, map, er, _, items) = TestFixtures.BuildFullSim();

        foreach (var drink in items.GetAllItems().Where(item => item.DefId == ItemDefIds.Drink).ToList())
            items.DestroyItem(drink.Id);

        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(4, 4, 0));
        er.Register(dwarf);
        dwarf.Needs.Thirst.SetLevel(0.01f);

        var waterPos = new Vec3i(24, 4, 0);
        var waterTile = map.GetTile(waterPos);
        waterTile.TileDefId = TileDefIds.Water;
        waterTile.IsPassable = true;
        waterTile.FluidType = FluidType.Water;
        waterTile.FluidLevel = 7;
        map.SetTile(waterPos, waterTile);

        var fortressLocations = sim.Context.Get<FortressLocationSystem>();
        fortressLocations.SetLocation(FortressLocationIds.EmbarkCenter, dwarf.Position.Position);
        fortressLocations.SetLocation(FortressLocationIds.ClosestDrink, waterPos);

        for (var tick = 0; tick < 2500; tick++)
        {
            sim.Tick(0.1f);
            if (dwarf.Needs.Thirst.Level >= 0.8f)
                break;
        }

        Assert.InRange(dwarf.Needs.Thirst.Level, 0.8f, 1f);
        Assert.True(dwarf.IsAlive);
        Assert.InRange(dwarf.Position.Position.ManhattanDistanceTo(waterPos), 0, 1);
        Assert.False(map.IsSwimmable(dwarf.Position.Position));
    }

    [Fact]
    public void Multiple_Dwarves_Can_Drink_From_Boxed_Stockpile_Items_On_One_Tile()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var boxPos = new Vec3i(10, 10, 0);
        var box = new Box(er.NextId(), boxPos);
        er.Register(box);

        for (var i = 0; i < 3; i++)
        {
            var drink = items.CreateItem(ItemDefIds.Drink, MaterialIds.Drink, boxPos);
            items.StoreItemInBox(drink.Id, box);
        }

        var dwarves = new[]
        {
            new Dwarf(er.NextId(), "Urist", new Vec3i(4, 10, 0)),
            new Dwarf(er.NextId(), "Rigoth", new Vec3i(4, 9, 0)),
            new Dwarf(er.NextId(), "Domas", new Vec3i(4, 11, 0)),
        };

        foreach (var dwarf in dwarves)
        {
            dwarf.Needs.Thirst.SetLevel(0.01f);
            er.Register(dwarf);
        }

        for (var tick = 0; tick < 3600; tick++)
        {
            sim.Tick(0.1f);
            if (dwarves.All(dwarf => dwarf.Needs.Thirst.Level >= 0.8f))
                break;
        }

        Assert.All(dwarves, dwarf => Assert.InRange(dwarf.Needs.Thirst.Level, 0.8f, 1f));
    }

    [Fact]
    public void Multiple_Dwarves_Can_Drink_From_One_Natural_Water_Tile()
    {
        var (sim, map, er, _, items) = TestFixtures.BuildFullSim();

        foreach (var drink in items.GetAllItems().Where(item => item.DefId == ItemDefIds.Drink).ToList())
            items.DestroyItem(drink.Id);

        var waterPos = new Vec3i(10, 10, 0);
        var waterTile = map.GetTile(waterPos);
        waterTile.TileDefId = TileDefIds.Water;
        waterTile.IsPassable = true;
        waterTile.FluidType = FluidType.Water;
        waterTile.FluidLevel = 7;
        map.SetTile(waterPos, waterTile);

        var dwarves = new[]
        {
            new Dwarf(er.NextId(), "Urist", new Vec3i(4, 10, 0)),
            new Dwarf(er.NextId(), "Rigoth", new Vec3i(4, 9, 0)),
            new Dwarf(er.NextId(), "Domas", new Vec3i(4, 11, 0)),
        };

        foreach (var dwarf in dwarves)
        {
            dwarf.Needs.Thirst.SetLevel(0.01f);
            er.Register(dwarf);
        }

        for (var tick = 0; tick < 3600; tick++)
        {
            sim.Tick(0.1f);
            if (dwarves.All(dwarf => dwarf.Needs.Thirst.Level >= 0.8f))
                break;
        }

        Assert.All(dwarves, dwarf => Assert.InRange(dwarf.Needs.Thirst.Level, 0.8f, 1f));
        Assert.All(dwarves, dwarf => Assert.True(dwarf.IsAlive));
    }
}

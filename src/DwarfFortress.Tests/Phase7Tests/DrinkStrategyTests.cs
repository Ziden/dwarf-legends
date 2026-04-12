using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Jobs;
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
    public void Dwarf_Can_Find_Surface_Water_From_Underground_Through_Stairs()
    {
        var (sim, map, er, _, items) = TestFixtures.BuildFullSim();

        foreach (var drink in items.GetAllItems().Where(item => item.DefId == ItemDefIds.Drink).ToList())
            items.DestroyItem(drink.Id);

        var stairSurface = new Vec3i(10, 10, 0);
        var stairDepth = new Vec3i(10, 10, 1);
        var undergroundStand = new Vec3i(10, 11, 1);
        var waterPos = new Vec3i(10, 9, 0);

        map.SetTile(stairSurface, new TileData
        {
            TileDefId = TileDefIds.Staircase,
            MaterialId = MaterialIds.Granite,
            IsPassable = true,
        });
        map.SetTile(stairDepth, new TileData
        {
            TileDefId = TileDefIds.Staircase,
            MaterialId = MaterialIds.Granite,
            IsPassable = true,
        });
        map.SetTile(undergroundStand, new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            MaterialId = MaterialIds.Granite,
            IsPassable = true,
        });

        var waterTile = map.GetTile(waterPos);
        waterTile.TileDefId = TileDefIds.Water;
        waterTile.IsPassable = true;
        waterTile.FluidType = FluidType.Water;
        waterTile.FluidLevel = 7;
        map.SetTile(waterPos, waterTile);

        var dwarf = new Dwarf(er.NextId(), "Urist", undergroundStand);
        dwarf.Needs.Thirst.SetLevel(0.01f);
        er.Register(dwarf);

        for (var tick = 0; tick < 2500; tick++)
        {
            sim.Tick(0.1f);
            if (dwarf.Needs.Thirst.Level >= 0.8f)
                break;
        }

        Assert.InRange(dwarf.Needs.Thirst.Level, 0.8f, 1f);
        Assert.True(dwarf.IsAlive);
        Assert.True(dwarf.Position.Position.Z is 0 or 1);
    }

    [Fact]
    public void Dwarf_Falls_Back_To_Water_When_Only_Drink_Item_Is_Unreachable()
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

        var sealedDrinkPos = new Vec3i(20, 20, 0);
        var unreachableDrink = items.CreateItem(ItemDefIds.Drink, MaterialIds.Drink, sealedDrinkPos);
        foreach (var wallPos in new[]
                 {
                     sealedDrinkPos + Vec3i.North,
                     sealedDrinkPos + Vec3i.South,
                     sealedDrinkPos + Vec3i.East,
                     sealedDrinkPos + Vec3i.West,
                 })
        {
            map.SetTile(wallPos, new TileData
            {
                TileDefId = TileDefIds.GraniteWall,
                MaterialId = MaterialIds.Granite,
                IsPassable = false,
            });
        }

        for (var tick = 0; tick < 2500; tick++)
        {
            sim.Tick(0.1f);
            if (dwarf.Needs.Thirst.Level >= 0.8f)
                break;
        }

        Assert.InRange(dwarf.Needs.Thirst.Level, 0.8f, 1f);
        Assert.True(dwarf.IsAlive);
        Assert.True(items.TryGetItem(unreachableDrink.Id, out var preservedDrink));
        Assert.False(preservedDrink!.IsClaimed);
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
    public void NeedsSystem_Reserves_Distinct_Boxed_Drinks_When_Creating_Thirst_Jobs()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var needs = sim.Context.Get<NeedsSystem>();
        var jobs = sim.Context.Get<JobSystem>();

        var boxPos = new Vec3i(10, 10, 0);
        var box = new Box(er.NextId(), boxPos);
        er.Register(box);

        var firstDrink = items.CreateItem(ItemDefIds.Drink, MaterialIds.Drink, boxPos);
        var secondDrink = items.CreateItem(ItemDefIds.Drink, MaterialIds.Drink, boxPos);
        items.StoreItemInBox(firstDrink.Id, box);
        items.StoreItemInBox(secondDrink.Id, box);

        var dwarves = new[]
        {
            new Dwarf(er.NextId(), "Urist", new Vec3i(4, 10, 0)),
            new Dwarf(er.NextId(), "Rigoth", new Vec3i(4, 11, 0)),
        };

        foreach (var dwarf in dwarves)
        {
            dwarf.Needs.Thirst.SetLevel(0.01f);
            er.Register(dwarf);
        }

        needs.Tick(0.1f);

        var drinkJobs = jobs.GetPendingJobs()
            .Where(job => job.JobDefId == JobDefIds.Drink)
            .OrderBy(job => job.Id)
            .ToList();

        Assert.Equal(2, drinkJobs.Count);
        Assert.All(drinkJobs, job => Assert.Single(job.ReservedItemIds));
        Assert.Equal(2, drinkJobs.Select(job => job.ReservedItemIds[0]).Distinct().Count());
        Assert.All(drinkJobs.Select(job => job.ReservedItemIds[0]), itemId =>
        {
            Assert.True(items.TryGetItem(itemId, out var reservedDrink));
            Assert.True(reservedDrink!.IsClaimed);
        });
    }

    [Fact]
    public void NeedsSystem_Does_Not_Create_More_Drink_Jobs_Than_Available_Boxed_Drinks()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var needs = sim.Context.Get<NeedsSystem>();
        var jobs = sim.Context.Get<JobSystem>();

        var boxPos = new Vec3i(11, 10, 0);
        var box = new Box(er.NextId(), boxPos);
        er.Register(box);

        var drink = items.CreateItem(ItemDefIds.Drink, MaterialIds.Drink, boxPos);
        items.StoreItemInBox(drink.Id, box);

        var firstDwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(4, 10, 0));
        var secondDwarf = new Dwarf(er.NextId(), "Rigoth", new Vec3i(4, 11, 0));
        firstDwarf.Needs.Thirst.SetLevel(0.01f);
        secondDwarf.Needs.Thirst.SetLevel(0.01f);
        er.Register(firstDwarf);
        er.Register(secondDwarf);

        needs.Tick(0.1f);

        var drinkJobs = jobs.GetPendingJobs().Where(job => job.JobDefId == JobDefIds.Drink).ToList();
        var reservedJob = Assert.Single(drinkJobs);
        Assert.Single(reservedJob.ReservedItemIds);
        Assert.Equal(drink.Id, reservedJob.ReservedItemIds[0]);
        Assert.True(drink.IsClaimed);
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

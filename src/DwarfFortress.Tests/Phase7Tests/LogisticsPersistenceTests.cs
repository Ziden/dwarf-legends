using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

/// <summary>
/// Verifies hauling/storage correctness and job persistence across save/load.
/// </summary>
public sealed class LogisticsPersistenceTests
{
    [Fact]
    public void HaulJob_Stores_Item_In_Stockpile_And_Emits_ItemStoredEvent()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var hauler = new Dwarf(er.NextId(), "Hauler", new Vec3i(5, 5, 0));
        hauler.Labors.DisableAll();
        hauler.Labors.Enable(LaborIds.Hauling);
        er.Register(hauler);

        sim.Context.Commands.Dispatch(new CreateStockpileCommand(
            From: new Vec3i(10, 10, 0),
            To: new Vec3i(10, 10, 0),
            AcceptedTags: ["stone"]));

        var boulder = items.CreateItem(ItemDefIds.GraniteBoulder, "granite", new Vec3i(5, 5, 0));

        ItemStoredEvent? stored = null;
        sim.Context.EventBus.On<ItemStoredEvent>(ev => stored = ev);

        for (int i = 0; i < 300; i++) sim.Tick(0.1f);

        Assert.NotNull(stored);
        Assert.Equal(boulder.Id, stored!.Value.ItemId);
        Assert.Equal(new Vec3i(10, 10, 0), boulder.Position.Position);
        Assert.True(boulder.StockpileId > 0);

        var stockpile = sim.Context.Get<StockpileManager>().GetById(boulder.StockpileId);
        Assert.NotNull(stockpile);
        Assert.Contains(new Vec3i(10, 10, 0), stockpile!.OccupiedSlots);
    }

    [Fact]
    public void Pending_Jobs_Survive_Save_And_Load()
    {
        var (sim, map, _, js, _) = TestFixtures.BuildFullSim();

        var target = new Vec3i(8, 8, 0);
        map.SetTile(target, new TileData
        {
            TileDefId = TileDefIds.GraniteWall,
            MaterialId = "granite",
            IsPassable = false,
            IsDesignated = true,
        });
        sim.Context.Commands.Dispatch(new DesignateMineCommand(target, target));

        Assert.Contains(js.GetAllJobs(), j => j.JobDefId == JobDefIds.MineTile && j.TargetPos == target);

        var json = sim.Save();

        var (sim2, _, _, js2, _) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        Assert.Contains(js2.GetAllJobs(), j => j.JobDefId == JobDefIds.MineTile && j.TargetPos == target);
    }

    [Fact]
    public void Full_Stockpile_Does_Not_Return_Default_TopLeft_Slot()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new CreateStockpileCommand(
            From: new Vec3i(10, 10, 0),
            To: new Vec3i(10, 10, 0),
            AcceptedTags: ["food"]));

        var stockpiles = sim.Context.Get<StockpileManager>();
        var firstMeal = items.CreateItem(ItemDefIds.Meal, "food", new Vec3i(5, 5, 0));
        var secondMeal = items.CreateItem(ItemDefIds.Meal, "food", new Vec3i(6, 5, 0));

        Assert.True(stockpiles.TryReserveSlot(firstMeal, out var stockpileId, out var slot));
        Assert.Equal(new Vec3i(10, 10, 0), slot);

        var openSlot = stockpiles.FindOpenSlot(secondMeal);

        Assert.False(openSlot.HasValue);
        Assert.False(stockpiles.TryReserveSlot(secondMeal, out stockpileId, out slot));
        Assert.Equal(default, slot);
        Assert.Equal(-1, stockpileId);
    }

    [Fact]
    public void HaulJobs_Can_Move_Multiple_Items_From_The_Same_Tile()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var haulerA = new Dwarf(er.NextId(), "Hauler A", new Vec3i(5, 5, 0));
        haulerA.Labors.DisableAll();
        haulerA.Labors.Enable(LaborIds.Hauling);
        er.Register(haulerA);

        var haulerB = new Dwarf(er.NextId(), "Hauler B", new Vec3i(6, 5, 0));
        haulerB.Labors.DisableAll();
        haulerB.Labors.Enable(LaborIds.Hauling);
        er.Register(haulerB);

        sim.Context.Commands.Dispatch(new CreateStockpileCommand(
            From: new Vec3i(10, 10, 0),
            To: new Vec3i(11, 10, 0),
            AcceptedTags: ["wood"]));

        var dropTile = new Vec3i(5, 6, 0);
        var firstLog = items.CreateItem(ItemDefIds.Log, "wood", dropTile);
        var secondLog = items.CreateItem(ItemDefIds.Log, "wood", dropTile);

        for (int i = 0; i < 400; i++) sim.Tick(0.1f);

        Assert.True(firstLog.StockpileId > 0);
        Assert.True(secondLog.StockpileId > 0);
        Assert.NotEqual(dropTile, firstLog.Position.Position);
        Assert.NotEqual(dropTile, secondLog.Position.Position);
    }
}

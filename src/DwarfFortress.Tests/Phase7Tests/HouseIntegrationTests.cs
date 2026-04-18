using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Jobs.Strategies;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class HouseIntegrationTests
{
    [Fact]
    public void House_Definition_Is_A_2x2_Wooden_Hut_With_Two_Log_Cost()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();
        var data = sim.Context.Get<DataManager>();

        var houseDef = data.Buildings.Get(BuildingDefIds.House);
        var input = Assert.Single(houseDef.ConstructionInputs);

        Assert.Equal("Hut", houseDef.DisplayName);
        Assert.Equal(2, input.Quantity);
        Assert.True(input.RequiredTags.Contains(TagIds.Log));
        Assert.Equal(
            new[] { new Vec2i(0, 0), new Vec2i(1, 0), new Vec2i(0, 1), new Vec2i(1, 1) },
            houseDef.Footprint.Select(tile => tile.Offset).ToArray());
        Assert.All(houseDef.Footprint, tile => Assert.Equal(TileDefIds.WoodFloor, tile.TileDefId));
        var entry = Assert.Single(houseDef.Entries);
        Assert.Equal(new Vec2i(1, 1), entry.Offset);
        Assert.Equal(Vec2i.South, entry.OutwardDirection);
        Assert.NotNull(houseDef.VisualProfile);
        Assert.Equal(BuildingVisualArchetypes.Hut, houseDef.VisualProfile!.Archetype);
        Assert.True(houseDef.VisualProfile.HideRoofOnHover);
    }

    [Fact]
    public void SpatialIndexSystem_Indexes_All_House_Footprint_Tiles()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();
        var spatial = sim.Context.Get<SpatialIndexSystem>();
        var data = sim.Context.Get<DataManager>();

        var house = PlaceHouse(sim, items, new Vec3i(1, 1, 0), new Vec3i(10, 10, 0), BuildingRotation.Clockwise270);
        var houseDef = data.Buildings.Get(BuildingDefIds.House);

        foreach (var position in BuildingPlacementGeometry.EnumerateWorldFootprint(houseDef, house.Origin, house.Rotation))
            Assert.Equal(house.Id, spatial.GetBuildingAt(position));
    }

    [Fact]
    public void BuildingSystem_Requires_Two_Logs_To_Place_Hut()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();
        var rejections = new List<BuildingPlacementRejectedEvent>();
        sim.Context.EventBus.On<BuildingPlacementRejectedEvent>(rejection => rejections.Add(rejection));

        items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, new Vec3i(1, 1, 0));
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(BuildingDefIds.House, new Vec3i(10, 10, 0)));

        Assert.Null(sim.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(10, 10, 0)));
        Assert.Contains(rejections, rejection => rejection.BuildingDefId == BuildingDefIds.House && rejection.Reason == "Missing construction materials.");

        items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, new Vec3i(2, 1, 0));
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(BuildingDefIds.House, new Vec3i(10, 10, 0)));

        var building = sim.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(10, 10, 0));
        Assert.NotNull(building);
        Assert.False(building!.IsComplete);
        Assert.True(building.ConstructionJobId > 0);
    }

    [Fact]
    public void BuildingSystem_Can_Place_Hut_Using_Boxed_Logs()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var boxPos = new Vec3i(4, 4, 0);
        var box = new Box(er.NextId(), boxPos);
        er.Register(box);

        var firstLog = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, boxPos);
        var secondLog = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, boxPos);
        items.StoreItemInBox(firstLog.Id, box);
        items.StoreItemInBox(secondLog.Id, box);

        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(BuildingDefIds.House, new Vec3i(10, 10, 0)));

        var building = sim.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(10, 10, 0));
        Assert.NotNull(building);
        Assert.False(items.TryGetItem(firstLog.Id, out _));
        Assert.False(items.TryGetItem(secondLog.Id, out _));
        Assert.Empty(box.Container.StoredItemIds);
    }

    [Fact]
    public void ConstructionJob_Completes_Hut_And_Creates_Owned_Stockpile()
    {
        var (sim, map, er, js, _) = TestFixtures.BuildFullSim();
        var stockpiles = sim.Context.Get<StockpileManager>();
        var builder = new Dwarf(er.NextId(), "Builder", new Vec3i(10, 9, 0));
        er.Register(builder);

        var hut = TestFixtures.PlaceBuildingWithMaterials(
            sim,
            BuildingDefIds.House,
            new Vec3i(10, 10, 0),
            materialStart: new Vec3i(1, 1, 0),
            complete: false);

        Assert.False(hut.IsComplete);
        Assert.True(map.GetTile(hut.Origin).IsUnderConstruction);
        Assert.Null(stockpiles.GetByOwnerBuilding(hut.Id));
        var constructionJobId = hut.ConstructionJobId;
        Assert.Contains(js.GetAllJobs(), job => job.Id == constructionJobId && job.JobDefId == JobDefIds.Construct);

        for (var i = 0; i < 300 && !hut.IsComplete; i++)
            sim.Tick(0.1f);

        Assert.True(hut.IsComplete);
        Assert.False(map.GetTile(hut.Origin).IsUnderConstruction);
        Assert.NotNull(stockpiles.GetByOwnerBuilding(hut.Id));
        Assert.DoesNotContain(js.GetAllJobs(), job => job.Id == constructionJobId);
    }

    [Fact]
    public void SaveLoad_Restores_House_Rotation_Residents_Stockpile_And_UnderlyingTiles()
    {
        var (sim, map, er, _, items) = TestFixtures.BuildFullSim();
        var houseOrigin = new Vec3i(12, 9, 0);

        map.SetTile(houseOrigin, new TileData
        {
            TileDefId = TileDefIds.Grass,
            MaterialId = "soil",
            PlantDefId = "berry_bush",
            PlantGrowthStage = PlantGrowthStages.Young,
            PlantYieldLevel = 1,
            PlantSeedLevel = 0,
            IsPassable = true,
        });
        map.SetTile(houseOrigin + new Vec3i(1, 0, 0), new TileData
        {
            TileDefId = TileDefIds.Soil,
            MaterialId = "limestone",
            CoatingMaterialId = "beer",
            CoatingAmount = 0.35f,
            IsDesignated = true,
            IsPassable = true,
        });

        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(14, 15, 0));
        er.Register(dwarf);

        var house = PlaceHouse(sim, items, new Vec3i(1, 1, 0), houseOrigin, BuildingRotation.Clockwise90);
        sim.Tick(0.1f);

        Assert.Equal(house.Id, dwarf.Residence.HomeBuildingId);
        Assert.True(house.LinkedStockpileId > 0);

        var json = sim.Save();

        var (sim2, _, er2, _, _) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        var loadedBuildings = sim2.Context.Get<BuildingSystem>();
        var loadedStockpiles = sim2.Context.Get<StockpileManager>();
        var loadedHouse = loadedBuildings.GetByOrigin(houseOrigin);
        Assert.NotNull(loadedHouse);
        var restoredHouse = loadedHouse!;
        var loadedResident = Assert.Single(er2.GetAlive<Dwarf>());
        var loadedStockpile = loadedStockpiles.GetByOwnerBuilding(loadedHouse.Id);
        Assert.NotNull(loadedStockpile);
        var restoredStockpile = loadedStockpile!;

        Assert.Equal(BuildingRotation.Clockwise90, restoredHouse.Rotation);
        Assert.Equal(house.Id, restoredHouse.Id);
        Assert.Equal(restoredHouse.Id, loadedResident.Residence.HomeBuildingId);
        Assert.Equal(restoredHouse.LinkedStockpileId, restoredStockpile.Id);
        Assert.Contains(TagIds.Food, restoredStockpile.AcceptedTags);
        Assert.Contains(TagIds.Drink, restoredStockpile.AcceptedTags);

        var savedGrassTile = Assert.Single(restoredHouse.UnderlyingTiles, tile => tile.Position == houseOrigin);
        var savedCoatedTile = Assert.Single(restoredHouse.UnderlyingTiles, tile => tile.Position == houseOrigin + new Vec3i(1, 0, 0));
        Assert.Equal(TileDefIds.Grass, savedGrassTile.Tile.TileDefId);
        Assert.Equal("berry_bush", savedGrassTile.Tile.PlantDefId);
        Assert.Equal(TileDefIds.Soil, savedCoatedTile.Tile.TileDefId);
        Assert.Equal("limestone", savedCoatedTile.Tile.MaterialId);
        Assert.Equal("beer", savedCoatedTile.Tile.CoatingMaterialId);
    }

    [Fact]
    public void Deconstructing_House_Restores_Original_Footprint_Tiles()
    {
        var (sim, map, _, _, items) = TestFixtures.BuildFullSim();
        var houseOrigin = new Vec3i(8, 8, 0);
        var expectedTiles = new Dictionary<Vec3i, TileData>
        {
            [houseOrigin + new Vec3i(0, 0, 0)] = new TileData
            {
                TileDefId = TileDefIds.StoneFloor,
                MaterialId = "granite",
                IsPassable = true,
            },
            [houseOrigin + new Vec3i(1, 0, 0)] = new TileData
            {
                TileDefId = TileDefIds.Soil,
                MaterialId = "soil",
                IsPassable = true,
                IsDesignated = true,
            },
            [houseOrigin + new Vec3i(0, 1, 0)] = new TileData
            {
                TileDefId = TileDefIds.WoodFloor,
                MaterialId = "wood",
                IsPassable = true,
                CoatingMaterialId = "mud",
                CoatingAmount = 0.2f,
            },
            [houseOrigin + new Vec3i(1, 1, 0)] = new TileData
            {
                TileDefId = TileDefIds.StoneBrick,
                MaterialId = "limestone",
                IsPassable = true,
            },
        };

        foreach (var pair in expectedTiles)
            map.SetTile(pair.Key, pair.Value);

        var house = PlaceHouse(sim, items, new Vec3i(1, 1, 0), houseOrigin, BuildingRotation.Clockwise180);

        sim.Context.Commands.Dispatch(new DeconstructBuildingCommand(house.Origin + new Vec3i(1, 1, 0)));

        foreach (var pair in expectedTiles)
            AssertTileDataEqual(pair.Value, map.GetTile(pair.Key));
    }

    [Fact]
    public void House_Boundary_Traversal_Only_Allows_Door_Crossing()
    {
        var (sim, map, _, _, items) = TestFixtures.BuildFullSim();
        var house = PlaceHouse(sim, items, new Vec3i(1, 1, 0), new Vec3i(10, 10, 0), BuildingRotation.Clockwise90);

        var doorwayOutside = new Vec3i(9, 11, 0);
        var doorwayInside = new Vec3i(10, 11, 0);
        var blockedOutside = new Vec3i(12, 11, 0);
        var blockedInside = new Vec3i(11, 11, 0);
        var pathToInterior = Pathfinder.FindPath(map, blockedOutside, new Vec3i(11, 10, 0));

        Assert.True(map.CanTraverse(doorwayOutside, doorwayInside));
        Assert.False(map.CanTraverse(blockedOutside, blockedInside));
        Assert.NotEmpty(pathToInterior);
        Assert.Contains(doorwayInside, pathToInterior);
        Assert.Equal(house.Id, sim.Context.Get<BuildingSystem>().GetByFootprintTile(doorwayInside)?.Id);
    }

    [Fact]
    public void HousingSystem_Assigns_Dwarves_Deterministically_And_Respects_Capacity()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var housing = sim.Context.Get<HousingSystem>();

        var firstHouse = PlaceHouse(sim, items, new Vec3i(1, 1, 0), new Vec3i(4, 4, 0));
        var secondHouse = PlaceHouse(sim, items, new Vec3i(1, 2, 0), new Vec3i(10, 4, 0));
        var dwarves = Enumerable.Range(0, 7)
            .Select(index =>
            {
                var dwarf = new Dwarf(er.NextId(), $"Dwarf {index + 1}", new Vec3i(8, 10, 0));
                er.Register(dwarf);
                return dwarf;
            })
            .ToArray();

        sim.Tick(0.1f);

        Assert.Equal(new[] { dwarves[0].Id, dwarves[2].Id, dwarves[4].Id }, housing.GetResidents(firstHouse.Id).Select(d => d.Id).ToArray());
        Assert.Equal(new[] { dwarves[1].Id, dwarves[3].Id, dwarves[5].Id }, housing.GetResidents(secondHouse.Id).Select(d => d.Id).ToArray());
        Assert.Equal(-1, dwarves[6].Residence.HomeBuildingId);
    }

    [Fact]
    public void HousingSystem_Reassigns_Residents_When_A_House_Is_Removed()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var housing = sim.Context.Get<HousingSystem>();

        var firstHouse = PlaceHouse(sim, items, new Vec3i(1, 1, 0), new Vec3i(4, 4, 0));
        var secondHouse = PlaceHouse(sim, items, new Vec3i(1, 2, 0), new Vec3i(10, 4, 0));
        var dwarves = Enumerable.Range(0, 4)
            .Select(index =>
            {
                var dwarf = new Dwarf(er.NextId(), $"Dwarf {index + 1}", new Vec3i(8, 10, 0));
                er.Register(dwarf);
                return dwarf;
            })
            .ToArray();

        sim.Tick(0.1f);
        sim.Context.Commands.Dispatch(new DeconstructBuildingCommand(firstHouse.Origin + new Vec3i(1, 1, 0)));
        sim.Tick(0.1f);

        Assert.Equal(new[] { dwarves[0].Id, dwarves[1].Id, dwarves[3].Id }, housing.GetResidents(secondHouse.Id).Select(d => d.Id).ToArray());
        Assert.Equal(-1, dwarves[2].Residence.HomeBuildingId);
    }

    [Fact]
    public void SleepStrategy_Prefers_Assigned_House_But_Still_Prefers_Beds()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var dwarf = new Dwarf(er.NextId(), "Sleeper", new Vec3i(8, 14, 0));
        dwarf.Needs.Sleep.SetLevel(0.05f);
        er.Register(dwarf);

        var house = PlaceHouse(sim, items, new Vec3i(1, 1, 0), new Vec3i(10, 10, 0));
        sim.Tick(0.1f);

        var job = new Job(1, JobDefIds.Sleep, dwarf.Position.Position, priority: 100);
        var withoutBed = new SleepStrategy().GetSteps(job, dwarf.Id, sim.Context);
        var houseMove = Assert.IsType<MoveToStep>(withoutBed[0]);

        Assert.Equal(house.Id, dwarf.Residence.HomeBuildingId);
        Assert.Equal(new Vec3i(10, 10, 0), houseMove.Target);

        TestFixtures.PlaceBuildingWithMaterials(sim, BuildingDefIds.Bed, new Vec3i(7, 13, 0), materialStart: new Vec3i(6, 13, 0));

        var withBed = new SleepStrategy().GetSteps(job, dwarf.Id, sim.Context);
        var bedMove = Assert.IsType<MoveToStep>(withBed[0]);

        Assert.Equal(new Vec3i(7, 13, 0), bedMove.Target);
    }

    [Fact]
    public void House_Creates_And_Removes_Owned_Food_Drink_Stockpile()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();
        var stockpiles = sim.Context.Get<StockpileManager>();
        var data = sim.Context.Get<DataManager>();

        var house = PlaceHouse(sim, items, new Vec3i(1, 1, 0), new Vec3i(10, 10, 0), BuildingRotation.Clockwise90);
        var houseDef = data.Buildings.Get(BuildingDefIds.House);
        var pantryCells = BuildingPlacementGeometry.GetAutoStockpileCells(houseDef, house.Origin, house.Rotation);
        var stockpile = stockpiles.GetByOwnerBuilding(house.Id);
        Assert.NotNull(stockpile);
        var ownedStockpile = stockpile!;

        Assert.Equal(house.LinkedStockpileId, ownedStockpile.Id);
        Assert.Equal(pantryCells.Min(cell => cell.X), ownedStockpile.From.X);
        Assert.Equal(pantryCells.Max(cell => cell.X), ownedStockpile.To.X);
        Assert.Equal(pantryCells.Min(cell => cell.Y), ownedStockpile.From.Y);
        Assert.Equal(pantryCells.Max(cell => cell.Y), ownedStockpile.To.Y);
        Assert.Contains(TagIds.Food, ownedStockpile.AcceptedTags);
        Assert.Contains(TagIds.Drink, ownedStockpile.AcceptedTags);

        var meal = items.CreateItem(ItemDefIds.Meal, MaterialIds.Food, new Vec3i(3, 3, 0));
        var drink = items.CreateItem(ItemDefIds.Drink, MaterialIds.Drink, new Vec3i(4, 3, 0));
        var log = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, new Vec3i(5, 3, 0));

        Assert.True(StoreItemInStockpile(items, stockpiles, meal, out var mealSlot));
        Assert.True(StoreItemInStockpile(items, stockpiles, drink, out var drinkSlot));
        Assert.False(stockpiles.FindOpenSlot(log).HasValue);
        Assert.Contains(mealSlot, pantryCells);
        Assert.Contains(drinkSlot, pantryCells);

        sim.Context.Commands.Dispatch(new DeconstructBuildingCommand(house.Origin + new Vec3i(1, 1, 0)));

        Assert.Null(stockpiles.GetByOwnerBuilding(house.Id));
        Assert.Equal(-1, meal.StockpileId);
        Assert.Equal(-1, drink.StockpileId);
    }

    [Fact]
    public void WorldQuerySystem_Exposes_House_Capacity_Residents_Rotation_And_Storage()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var stockpiles = sim.Context.Get<StockpileManager>();
        var data = sim.Context.Get<DataManager>();

        var firstDwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(15, 15, 0));
        var secondDwarf = new Dwarf(er.NextId(), "Domas", new Vec3i(15, 15, 0));
        er.Register(firstDwarf);
        er.Register(secondDwarf);

        var house = PlaceHouse(sim, items, new Vec3i(1, 1, 0), new Vec3i(12, 10, 0), BuildingRotation.Clockwise270);
        sim.Tick(0.1f);

        var meal = items.CreateItem(ItemDefIds.Meal, MaterialIds.Food, new Vec3i(3, 3, 0));
        var drink = items.CreateItem(ItemDefIds.Drink, MaterialIds.Drink, new Vec3i(4, 3, 0));
        Assert.True(StoreItemInStockpile(items, stockpiles, meal, out var pantrySlot));
        Assert.True(StoreItemInStockpile(items, stockpiles, drink, out _));

        var houseDef = data.Buildings.Get(BuildingDefIds.House);
        var buildingView = queries.GetBuildingView(house.Id);
        Assert.NotNull(buildingView);
        var houseView = buildingView!;
        var tileQuery = queries.QueryTile(BuildingPlacementGeometry.GetPreferredSleepCells(houseDef, house.Origin, house.Rotation).First());
        var pantryQuery = queries.QueryTile(pantrySlot);

        Assert.Equal(BuildingRotation.Clockwise270, houseView.Rotation);
        Assert.Equal(3, houseView.HousingCapacity);
        Assert.Equal(new[] { firstDwarf.Id, secondDwarf.Id }, houseView.ResidentDwarfIds);
        Assert.Equal(new[] { "Urist", "Domas" }, houseView.ResidentNames);
        Assert.Equal(house.LinkedStockpileId, houseView.LinkedStockpileId);
        Assert.Equal(0, houseView.StoredItemCount);
        Assert.Equal(2, houseView.StorageItemCount);
        Assert.NotNull(tileQuery.Building);
        Assert.Equal(house.Id, tileQuery.Building!.Id);
        Assert.NotNull(pantryQuery.Stockpile);
        Assert.Equal(house.LinkedStockpileId, pantryQuery.Stockpile!.Id);
    }

    private static PlacedBuildingData PlaceHouse(
        GameSimulation sim,
        ItemSystem items,
        Vec3i logStart,
        Vec3i origin,
        BuildingRotation rotation = BuildingRotation.None,
        string materialId = MaterialIds.Wood)
    {
        return TestFixtures.PlaceBuildingWithMaterials(
            sim,
            BuildingDefIds.House,
            origin,
            rotation,
            logStart,
            materialId);
    }

    private static bool StoreItemInStockpile(ItemSystem items, StockpileManager stockpiles, Item item, out Vec3i slot)
    {
        if (!stockpiles.TryReserveSlot(item, out var stockpileId, out slot))
            return false;

        items.MoveItem(item.Id, slot);
        item.StockpileId = stockpileId;
        stockpiles.ConfirmStoredItem(item.Id, stockpileId, slot);
        return true;
    }

    private static void AssertTileDataEqual(TileData expected, TileData actual)
    {
        Assert.Equal(expected.TileDefId, actual.TileDefId);
        Assert.Equal(expected.MaterialId, actual.MaterialId);
        Assert.Equal(expected.TreeSpeciesId, actual.TreeSpeciesId);
        Assert.Equal(expected.PlantDefId, actual.PlantDefId);
        Assert.Equal(expected.PlantGrowthStage, actual.PlantGrowthStage);
        Assert.Equal(expected.PlantGrowthProgressSeconds, actual.PlantGrowthProgressSeconds);
        Assert.Equal(expected.PlantYieldLevel, actual.PlantYieldLevel);
        Assert.Equal(expected.PlantSeedLevel, actual.PlantSeedLevel);
        Assert.Equal(expected.OreItemDefId, actual.OreItemDefId);
        Assert.Equal(expected.IsAquifer, actual.IsAquifer);
        Assert.Equal(expected.FluidType, actual.FluidType);
        Assert.Equal(expected.FluidLevel, actual.FluidLevel);
        Assert.Equal(expected.FluidMaterialId, actual.FluidMaterialId);
        Assert.Equal(expected.CoatingMaterialId, actual.CoatingMaterialId);
        Assert.Equal(expected.CoatingAmount, actual.CoatingAmount);
        Assert.Equal(expected.IsDesignated, actual.IsDesignated);
        Assert.Equal(expected.IsUnderConstruction, actual.IsUnderConstruction);
        Assert.Equal(expected.IsPassable, actual.IsPassable);
    }
}

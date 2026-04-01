using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class WorldQuerySystemTests
{
    [Fact]
    public void WorldQuerySystem_Can_Query_Dwarf_By_Id()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();

        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(5, 5, 0));
        er.Register(dwarf);

        var view = queries.GetDwarfView(dwarf.Id);

        Assert.NotNull(view);
        Assert.Equal(dwarf.Id, view!.Id);
        Assert.Equal("Urist", view.Name);
        Assert.Equal(5, view.Position.X);
    }

    [Fact]
    public void WorldQuerySystem_Exposes_Fortress_Announcements()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();

        sim.Context.EventBus.Emit(new BuildingPlacementRejectedEvent(
            BuildingDefId: BuildingDefIds.CarpenterWorkshop,
            Origin: new Vec3i(8, 9, 0),
            Reason: "Footprint is blocked."));

        var announcements = queries.GetFortressAnnouncements();

        Assert.Contains(announcements, announcement =>
            announcement.HasLocation &&
            announcement.Position == new Vec3i(8, 9, 0) &&
            announcement.Severity == FortressAnnouncementSeverity.Warning &&
            announcement.Message.Contains("Cannot build", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldQuerySystem_Collapses_Repeated_Fortress_Announcements()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();

        var ev = new BuildingPlacementRejectedEvent(
            BuildingDefId: BuildingDefIds.CarpenterWorkshop,
            Origin: new Vec3i(5, 5, 0),
            Reason: "Footprint is blocked.");

        sim.Context.EventBus.Emit(ev);
        sim.Context.EventBus.Emit(ev);

        var top = queries.GetFortressAnnouncements().First();

        Assert.Equal(2, top.RepeatCount);
        Assert.Equal(FortressAnnouncementSeverity.Warning, top.Severity);
    }

    [Fact]
    public void WorldQuerySystem_Can_Query_Tile_Contents()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();

        var pos = new Vec3i(7, 7, 0);
        var dwarf = new Dwarf(er.NextId(), "Domas", pos);
        er.Register(dwarf);
        var item = items.CreateItem("log", "wood", pos);

        var tile = queries.QueryTile(pos);

        Assert.NotNull(tile.Tile);
        Assert.Contains(tile.Dwarves, d => d.Id == dwarf.Id);
        Assert.Contains(tile.Items, i => i.Id == item.Id);
    }

    [Fact]
    public void WorldQuerySystem_TileView_Exposes_Plant_State()
    {
        var (sim, map, _, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var pos = new Vec3i(5, 6, 0);
        map.SetTile(pos, new TileData
        {
            TileDefId = TileDefIds.Grass,
            MaterialId = "soil",
            PlantDefId = "berry_bush",
            PlantGrowthStage = PlantGrowthStages.Young,
            PlantYieldLevel = 1,
            PlantSeedLevel = 0,
            IsPassable = true,
        });

        var view = queries.GetTileView(pos);

        Assert.NotNull(view);
        Assert.Equal("berry_bush", view!.PlantDefId);
        Assert.Equal(PlantGrowthStages.Young, view.PlantGrowthStage);
        Assert.Equal(1, view.PlantYieldLevel);
    }

    [Fact]
    public void WorldQuerySystem_Hides_Item_From_Source_Tile_Once_Picked_Up()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();

        var dwarf = new Dwarf(er.NextId(), "Hauler", new Vec3i(4, 5, 0));
        er.Register(dwarf);

        var source = new Vec3i(5, 5, 0);
        items.CreateItem("log", "wood", new Vec3i(7, 4, 0));
        items.CreateItem("log", "wood", new Vec3i(7, 5, 0));
        items.CreateItem("log", "wood", new Vec3i(7, 6, 0));
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: "carpenter_workshop",
            Origin: new Vec3i(8, 5, 0)));

        var workshop = sim.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(8, 5, 0));
        Assert.NotNull(workshop);

        var item = items.CreateItem("log", "wood", source);

        sim.Context.Commands.Dispatch(new SetProductionOrderCommand(
            WorkshopEntityId: workshop!.Id,
            RecipeDefId: "make_plank",
            Quantity: 1));

        for (int i = 0; i < 30 && items.GetItemsAt(source).Any(it => it.Id == item.Id); i++)
            sim.Tick(0.5f);

        var sourceTile = queries.QueryTile(source);
        var dwarfView = queries.GetDwarfView(dwarf.Id);

        Assert.DoesNotContain(sourceTile.Items, view => view.Id == item.Id);
        Assert.Contains(item.Id, dwarf.Inventory.CarriedItemIds);
        Assert.NotNull(dwarfView);
        Assert.Contains(dwarfView!.CarriedItems, view => view.Id == item.Id && view.CarriedByEntityId == dwarf.Id);
        Assert.Equal(dwarf.Position.Position, dwarfView.CarriedItems.Single(view => view.Id == item.Id).Position);

        for (int i = 0; i < 40 && items.GetItemsInBuilding(workshop.Id).All(it => it.Id != item.Id); i++)
            sim.Tick(0.5f);

        var destinationTile = queries.QueryTile(workshop.Origin);
        Assert.Contains(destinationTile.Items, view => view.Id == item.Id && view.ContainerBuildingId == workshop.Id);
        Assert.Contains(items.GetItemsInBuilding(workshop.Id), carriedItem => carriedItem.Id == item.Id);
        Assert.DoesNotContain(item.Id, dwarf.Inventory.CarriedItemIds);
    }

    [Fact]
    public void WorldQuerySystem_Resolves_Building_By_Id_And_Tile()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 1, Width: 48, Height: 48, Depth: 8));

        var building = sim.Context.Get<BuildingSystem>().GetAll()
            .First(b => b.BuildingDefId == BuildingDefIds.CarpenterWorkshop);

        var byId = queries.GetBuildingView(building.Id);
        var byTile = queries.QueryTile(building.Origin + new Vec3i(1, 1, 0));

        Assert.NotNull(byId);
        Assert.Equal(building.Id, byId!.Id);
        Assert.NotNull(byTile.Building);
        Assert.Equal(building.Id, byTile.Building!.Id);
    }

    [Fact]
    public void WorldQuerySystem_TileView_Exposes_TreeSpecies()
    {
        var (sim, map, _, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var pos = new Vec3i(4, 6, 0);
        map.SetTile(pos, new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = "wood",
            TreeSpeciesId = "oak",
            IsPassable = false,
        });

        var view = queries.GetTileView(pos);

        Assert.NotNull(view);
        Assert.Equal("oak", view!.TreeSpeciesId);
    }

    [Fact]
    public void WorldQuerySystem_TileView_Exposes_OreItemDefId()
    {
        var (sim, map, _, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var pos = new Vec3i(5, 7, 0);
        map.SetTile(pos, new TileData
        {
            TileDefId = TileDefIds.StoneWall,
            MaterialId = MaterialIds.Granite,
            OreItemDefId = ItemDefIds.IronOre,
            IsPassable = false,
        });

        var view = queries.GetTileView(pos);

        Assert.NotNull(view);
        Assert.Equal(ItemDefIds.IronOre, view!.OreItemDefId);
        Assert.True(view.IsVisible);
    }

    [Fact]
    public void WorldQuerySystem_TileView_Hides_Ore_When_Tile_Is_Not_Exposed()
    {
        var (sim, map, _, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var pos = new Vec3i(6, 6, 1);

        map.SetTile(pos, new TileData
        {
            TileDefId = TileDefIds.StoneWall,
            MaterialId = MaterialIds.Granite,
            OreItemDefId = ItemDefIds.GoldOre,
            IsPassable = false,
        });

        foreach (var offset in new[] { Vec3i.North, Vec3i.South, Vec3i.East, Vec3i.West })
        {
            map.SetTile(pos + offset, new TileData
            {
                TileDefId = TileDefIds.StoneWall,
                MaterialId = MaterialIds.Granite,
                IsPassable = false,
            });
        }

        var view = queries.GetTileView(pos);

        Assert.NotNull(view);
        Assert.Null(view!.OreItemDefId);
        Assert.False(view.IsVisible);
    }

    [Fact]
    public void WorldQuerySystem_TileView_Does_Not_Reveal_From_Designation_Only()
    {
        var (sim, map, _, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var pos = new Vec3i(7, 7, 1);

        map.SetTile(pos, new TileData
        {
            TileDefId = TileDefIds.StoneWall,
            MaterialId = MaterialIds.Granite,
            OreItemDefId = ItemDefIds.SilverOre,
            IsPassable = false,
        });

        map.SetTile(pos + Vec3i.North, new TileData
        {
            TileDefId = TileDefIds.StoneWall,
            MaterialId = MaterialIds.Granite,
            IsPassable = false,
            IsDesignated = true,
        });

        map.SetTile(pos + Vec3i.South, new TileData
        {
            TileDefId = TileDefIds.StoneWall,
            MaterialId = MaterialIds.Granite,
            IsPassable = false,
        });
        map.SetTile(pos + Vec3i.East, new TileData
        {
            TileDefId = TileDefIds.StoneWall,
            MaterialId = MaterialIds.Granite,
            IsPassable = false,
        });
        map.SetTile(pos + Vec3i.West, new TileData
        {
            TileDefId = TileDefIds.StoneWall,
            MaterialId = MaterialIds.Granite,
            IsPassable = false,
        });

        var view = queries.GetTileView(pos);

        Assert.NotNull(view);
        Assert.False(view!.IsVisible);
        Assert.Null(view.OreItemDefId);
    }

    [Fact]
    public void WorldQuerySystem_CreatureView_Exposes_Recent_Eat_And_Drink_Events()
    {
        var (sim, map, er, _, items) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var origin = new Vec3i(8, 8, 0);

        map.SetTile(origin + Vec3i.East, new TileData
        {
            TileDefId = TileDefIds.Water,
            MaterialId = "water",
            IsPassable = true,
            FluidType = FluidType.Water,
            FluidLevel = 7,
        });

        items.CreateItem(ItemDefIds.Meal, "food", origin);

        var dog = new Creature(er.NextId(), DefIds.Dog, origin, maxHealth: 30f);
        dog.Needs.Hunger.SetLevel(0f);
        dog.Needs.Thirst.SetLevel(0f);
        er.Register(dog);

        for (int i = 0; i < 6; i++)
            sim.Tick(0.5f);

        var view = queries.GetCreatureView(dog.Id);

        Assert.NotNull(view);
        Assert.Contains(view!.EventLog, entry => entry.Message.Contains("Drank water", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(view.EventLog, entry => entry.Message.Contains("Ate", System.StringComparison.OrdinalIgnoreCase));
        Assert.All(view.EventLog, entry => Assert.False(string.IsNullOrWhiteSpace(entry.TimeLabel)));
    }
}

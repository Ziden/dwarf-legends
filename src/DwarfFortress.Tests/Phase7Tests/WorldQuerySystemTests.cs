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

public sealed class WorldQuerySystemTests
{
    [Fact]
    public void WorldQuerySystem_Can_Query_Dwarf_By_Id()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();

        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(5, 5, 0));
        dwarf.Provenance.WorldSeed = 77;
        dwarf.Provenance.FigureId = "figure_test";
        dwarf.Provenance.HouseholdId = "household_test";
        dwarf.Provenance.CivilizationId = "civ_test";
        dwarf.Provenance.OriginSiteId = "site_test";
        dwarf.Provenance.BirthSiteId = "site_test";
        dwarf.Provenance.MigrationWaveId = "wave_test";
        er.Register(dwarf);

        var view = queries.GetDwarfView(dwarf.Id);

        Assert.NotNull(view);
        Assert.Equal(dwarf.Id, view!.Id);
        Assert.Equal("Urist", view.Name);
        Assert.Equal(5, view.Position.X);
        Assert.NotNull(view.Provenance);
        Assert.Equal("figure_test", view.Provenance!.FigureId);
        Assert.Equal("household_test", view.Provenance.HouseholdId);
        Assert.Equal("civ_test", view.Provenance!.CivilizationId);
        Assert.Equal("site_test", view.Provenance.OriginSiteId);
    }

    [Fact]
    public void WorldQuerySystem_LoreSummary_Prefers_Canonical_History_Runtime_View()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 7, Width: 48, Height: 48, Depth: 8));

        var lore = sim.Context.Get<WorldQuerySystem>().GetLoreSummary();
        var macro = sim.Context.Get<WorldMacroStateService>().Current;

        Assert.NotNull(lore);
        Assert.NotNull(macro);
        Assert.True(lore!.UsesCanonicalHistory);
        Assert.False(string.IsNullOrWhiteSpace(lore.OwnerCivilizationId));
        Assert.False(string.IsNullOrWhiteSpace(lore.PrimarySiteId));
        Assert.True(lore.PrimarySitePopulation is > 0);
        Assert.True(lore.PrimarySiteHouseholdCount is > 0);
        Assert.True(lore.PrimarySiteMilitaryCount is >= 0);
        Assert.NotEmpty(lore.RecentEvents);
        Assert.Equal(macro!.Threat, lore.Threat);
        Assert.Equal(macro.Prosperity, lore.Prosperity);
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
            announcement.Kind == FortressAnnouncementKind.Construction &&
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
    public void WorldQuerySystem_Aggregates_Active_Combat_Announcements()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(15, 15, 0));
        var goblin = new Creature(er.NextId(), DefIds.Goblin, new Vec3i(16, 15, 0), maxHealth: 60f, isHostile: true);

        er.Register(dwarf);
        er.Register(goblin);

        sim.Context.EventBus.Emit(new CombatHitEvent(dwarf.Id, goblin.Id, 7f, BodyPartIds.Head));
        sim.Context.EventBus.Emit(new CombatMissEvent(goblin.Id, dwarf.Id));

        var combatAnnouncements = queries.GetFortressAnnouncements()
            .Where(announcement => announcement.Kind == FortressAnnouncementKind.Combat)
            .ToArray();

        var combat = Assert.Single(combatAnnouncements);
        Assert.False(combat.HasLocation);
        Assert.Contains("Fight Happening", combat.Message, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Urist hit Goblin", combat.Message, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Goblin missed Urist", combat.Message, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("damage", combat.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorldQuerySystem_Removes_Combat_Announcement_When_Fight_Goes_Quiet()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(15, 15, 0));
        var goblin = new Creature(er.NextId(), DefIds.Goblin, new Vec3i(16, 15, 0), maxHealth: 60f, isHostile: true);

        er.Register(dwarf);
        er.Register(goblin);

        sim.Context.EventBus.Emit(new CombatHitEvent(goblin.Id, dwarf.Id, 6f, BodyPartIds.Head));

        Assert.Contains(queries.GetFortressAnnouncements(), announcement => announcement.Kind == FortressAnnouncementKind.Combat);

        for (var tick = 0; tick < 70; tick++)
            sim.Tick(0.1f);

        Assert.DoesNotContain(queries.GetFortressAnnouncements(), announcement => announcement.Kind == FortressAnnouncementKind.Combat);
    }

    [Fact]
    public void WorldQuerySystem_Emits_Dwarf_Need_Announcements_But_Ignores_Creatures()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();

        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(6, 6, 0));
        er.Register(dwarf);

        var elk = new Creature(er.NextId(), DefIds.Elk, new Vec3i(7, 6, 0), maxHealth: 85f);
        er.Register(elk);

        sim.Context.EventBus.Emit(new NeedCriticalEvent(dwarf.Id, "thirst"));
        sim.Context.EventBus.Emit(new NeedCriticalEvent(elk.Id, "hunger"));

        var announcements = queries.GetFortressAnnouncements();

        Assert.Contains(announcements, announcement =>
            announcement.Kind == FortressAnnouncementKind.Need &&
            announcement.Message.Contains("Urist needs thirst", System.StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(announcements, announcement =>
            announcement.Message.Contains("elk needs hunger", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldQuerySystem_Suppresses_Startup_Flood_Announcements_During_Stabilization()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var floodPos = new Vec3i(12, 12, 0);
        er.Register(new Dwarf(er.NextId(), "Urist", floodPos));

        sim.Context.EventBus.Emit(new FortressStartedEvent(Seed: 1, Width: 48, Height: 48, Depth: 8, StartingDwarves: 3, WorkshopBuildingId: -1));
        sim.Context.EventBus.Emit(new FloodedTileEvent(floodPos, FluidType.Water, Level: 3, PreviousFluid: FluidType.None, PreviousLevel: 0));

        Assert.DoesNotContain(queries.GetFortressAnnouncements(), announcement =>
            announcement.Kind == FortressAnnouncementKind.Flood &&
            announcement.Position == floodPos);

        for (var tick = 0; tick < 130; tick++)
            sim.Tick(0.1f);

        sim.Context.EventBus.Emit(new FloodedTileEvent(floodPos, FluidType.Water, Level: 3, PreviousFluid: FluidType.None, PreviousLevel: 0));

        Assert.Contains(queries.GetFortressAnnouncements(), announcement =>
            announcement.Kind == FortressAnnouncementKind.Flood &&
            announcement.Position == floodPos &&
            announcement.Message.Contains("Flooding reported", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldQuerySystem_Ignores_Flood_Announcements_For_Irrelevant_Tiles()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var floodPos = new Vec3i(20, 20, 0);

        sim.Context.EventBus.Emit(new FortressStartedEvent(Seed: 1, Width: 48, Height: 48, Depth: 8, StartingDwarves: 3, WorkshopBuildingId: -1));
        for (var tick = 0; tick < 130; tick++)
            sim.Tick(0.1f);

        sim.Context.EventBus.Emit(new FloodedTileEvent(floodPos, FluidType.Water, Level: 3, PreviousFluid: FluidType.None, PreviousLevel: 0));

        Assert.DoesNotContain(queries.GetFortressAnnouncements(), announcement =>
            announcement.Kind == FortressAnnouncementKind.Flood &&
            announcement.Position == floodPos);
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
    public void WorldQuerySystem_Exposes_Contained_Items_For_Container_Items()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();

        var pos = new Vec3i(9, 9, 0);
        var barrel = items.CreateItem(ItemDefIds.Barrel, MaterialIds.Wood, pos);
        var meal = items.CreateItem(ItemDefIds.Meal, MaterialIds.Food, pos);
        items.StoreItemInItem(meal.Id, barrel.Id, pos);

        var tile = queries.QueryTile(pos);
        var containedItems = queries.GetContainedItemViews(barrel.Id);
        var barrelView = Assert.Single(tile.Items, item => item.Id == barrel.Id);

        Assert.DoesNotContain(tile.Items, item => item.Id == meal.Id);
        Assert.NotNull(barrelView.Storage);
        Assert.Equal(1, barrelView.Storage!.StoredItemCount);
        Assert.Contains(barrelView.Storage.Contents, item => item.Id == meal.Id);

        var storedMeal = Assert.Single(containedItems);
        Assert.Equal(meal.Id, storedMeal.Id);
        Assert.Equal(barrel.Id, storedMeal.ContainerItemId);
        Assert.Equal(pos, storedMeal.Position);
    }

    [Fact]
    public void WorldQuerySystem_Exposes_Empty_Storage_For_Container_Items()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();

        var pos = new Vec3i(9, 10, 0);
        var barrel = items.CreateItem(ItemDefIds.Barrel, MaterialIds.Wood, pos);

        var barrelView = queries.GetItemView(barrel.Id);

        Assert.NotNull(barrelView);
        Assert.NotNull(barrelView!.Storage);
        Assert.Equal(0, barrelView.Storage!.StoredItemCount);
        Assert.Empty(barrelView.Storage.Contents);
    }

    [Fact]
    public void WorldQuerySystem_QueryTile_Exposes_Stockpile_Box_Entities_And_Their_Contents()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();

        var pos = new Vec3i(10, 10, 0);
        var box = new Box(er.NextId(), pos);
        er.Register(box);

        var meal = items.CreateItem(ItemDefIds.Meal, MaterialIds.Food, pos);
        items.StoreItemInBox(meal.Id, box);

        var tile = queries.QueryTile(pos);
        var containedItems = queries.GetContainedItemViews(box.Id);

        var containerView = Assert.Single(tile.Containers);
        Assert.Equal(box.Id, containerView.Id);
        Assert.Equal(Box.DefId, containerView.DefId);
        Assert.Equal(pos, containerView.Position);
        Assert.Equal(1, containerView.Storage.StoredItemCount);
        Assert.Equal(Box.DefaultCapacity, containerView.Storage.Capacity);
        Assert.Contains(containerView.Storage.Contents, item => item.Id == meal.Id);
        Assert.DoesNotContain(tile.Items, item => item.Id == meal.Id);

        var storedMeal = Assert.Single(containedItems);
        Assert.Equal(meal.Id, storedMeal.Id);
        Assert.Equal(box.Id, storedMeal.ContainerItemId);
    }

    [Fact]
    public void WorldQuerySystem_QueryTile_Exposes_Generic_Container_Entities_With_Stored_Items()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();

        var pos = new Vec3i(11, 10, 0);
        var container = new TestContainerEntity(er.NextId(), "test_cache", pos, capacity: 4);
        er.Register(container);

        var meal = items.CreateItem(ItemDefIds.Meal, MaterialIds.Food, pos);
        meal.ContainerItemId = container.Id;
        container.Components.Get<ContainerComponent>().TryAdd(meal.Id);

        var tile = queries.QueryTile(pos);
        var containedItems = queries.GetContainedItemViews(container.Id);

        var containerView = Assert.Single(tile.Containers);
        Assert.Equal(container.Id, containerView.Id);
        Assert.Equal("test_cache", containerView.DefId);
        Assert.Equal(pos, containerView.Position);
        Assert.Equal(1, containerView.Storage.StoredItemCount);
        Assert.Equal(4, containerView.Storage.Capacity);
        Assert.Contains(containerView.Storage.Contents, item => item.Id == meal.Id);
        Assert.DoesNotContain(tile.Items, item => item.Id == meal.Id);

        var storedMeal = Assert.Single(containedItems);
        Assert.Equal(meal.Id, storedMeal.Id);
        Assert.Equal(container.Id, storedMeal.ContainerItemId);
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
        items.CreateItem("log", "wood", new Vec3i(7, 5, 0));
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: "carpenter_workshop",
            Origin: new Vec3i(8, 5, 0)));

        var workshop = sim.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(8, 5, 0));
        Assert.NotNull(workshop);

        var item = items.CreateItem("log", "wood", source);
        items.PickUpItem(item.Id, dwarf.Id, dwarf.Position.Position);

        var sourceTile = queries.QueryTile(source);
        var dwarfView = queries.GetDwarfView(dwarf.Id);

        Assert.DoesNotContain(sourceTile.Items, view => view.Id == item.Id);
        Assert.Contains(item.Id, dwarf.Inventory.CarriedItemIds);
        Assert.NotNull(dwarfView);
        Assert.Contains(dwarfView!.CarriedItems, view => view.Id == item.Id && view.CarriedByEntityId == dwarf.Id);
        Assert.Equal(dwarf.Position.Position, dwarfView.CarriedItems.Single(view => view.Id == item.Id).Position);

        items.StoreItemInBuilding(item.Id, workshop.Id, workshop.Origin);

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
    public void WorldQuerySystem_Exposes_Building_Material()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();

        items.CreateItem(ItemDefIds.Log, "oak_wood", new Vec3i(2, 2, 0));
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: BuildingDefIds.CarpenterWorkshop,
            Origin: new Vec3i(8, 5, 0)));

        var building = sim.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(8, 5, 0));
        Assert.NotNull(building);

        var view = queries.GetBuildingView(building!.Id);

        Assert.NotNull(view);
        Assert.Equal("oak_wood", view!.MaterialId);
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
    public void WorldQuerySystem_TileView_Exposes_Damp_Walls_When_Aquifer_Is_Visible()
    {
        var (sim, map, _, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var wallPos = new Vec3i(10, 10, 1);

        map.SetTile(new Vec3i(9, 10, 1), new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            MaterialId = "granite",
            IsPassable = true,
        });
        map.SetTile(wallPos, new TileData
        {
            TileDefId = TileDefIds.GraniteWall,
            MaterialId = "limestone",
            IsPassable = false,
            IsAquifer = true,
        });

        var view = queries.GetTileView(wallPos);

        Assert.NotNull(view);
        Assert.True(view!.IsAquifer);
        Assert.True(view.IsDamp);
        Assert.True(view.IsVisible);
    }

    [Fact]
    public void WorldQuerySystem_TileView_Exposes_Warm_Walls_When_Magma_Is_Adjacent()
    {
        var (sim, map, _, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var wallPos = new Vec3i(10, 11, 1);

        map.SetTile(new Vec3i(9, 11, 1), new TileData
        {
            TileDefId = TileDefIds.StoneFloor,
            MaterialId = "granite",
            IsPassable = true,
        });
        map.SetTile(wallPos, new TileData
        {
            TileDefId = TileDefIds.GraniteWall,
            MaterialId = "granite",
            IsPassable = false,
        });
        map.SetTile(wallPos + Vec3i.Up, new TileData
        {
            TileDefId = TileDefIds.Magma,
            MaterialId = "magma",
            IsPassable = true,
            FluidType = FluidType.Magma,
            FluidLevel = 7,
        });

        var view = queries.GetTileView(wallPos);

        Assert.NotNull(view);
        Assert.True(view!.IsWarm);
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

    [Fact]
    public void WorldQuerySystem_DwarfEventLog_Exposes_Linked_Items_With_Corpse_Names()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var pos = new Vec3i(14, 14, 0);

        var dwarf = new Dwarf(er.NextId(), "Urist", pos);
        er.Register(dwarf);

        var corpse = items.CreateItem(ItemDefIds.Corpse, string.Empty, pos);
        corpse.Components.Add(new CorpseComponent(999, DefIds.Elk, "Elk", "dehydration"));

        items.PickUpItem(corpse.Id, dwarf.Id, pos);

        var entry = Assert.Single(queries.GetDwarfView(dwarf.Id)!.EventLog.Where(logEntry =>
            logEntry.Message.Contains("Picked up", System.StringComparison.OrdinalIgnoreCase)));
        Assert.NotNull(entry.LinkedTarget);
        var linkedTarget = entry.LinkedTarget!;

        Assert.Equal(EventLogLinkType.Item, linkedTarget.Type);
        Assert.Equal(corpse.Id, linkedTarget.Id);
        Assert.Equal(ItemDefIds.Corpse, linkedTarget.DefId);
        Assert.Equal("Corpse of Elk", linkedTarget.DisplayName);
        Assert.Contains("Corpse of Elk", entry.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorldQuerySystem_CombatEventLog_Exposes_Linked_Opponents()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();
        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(15, 15, 0));
        var goblin = new Creature(er.NextId(), DefIds.Goblin, new Vec3i(16, 15, 0), maxHealth: 60f);

        er.Register(dwarf);
        er.Register(goblin);

        sim.Context.EventBus.Emit(new CombatMissEvent(dwarf.Id, goblin.Id));

        var attackerEntry = Assert.Single(queries.GetDwarfView(dwarf.Id)!.EventLog);
        Assert.NotNull(attackerEntry.LinkedTarget);
        var linkedTarget = attackerEntry.LinkedTarget!;

        Assert.Equal(EventLogLinkType.Entity, linkedTarget.Type);
        Assert.Equal(goblin.Id, linkedTarget.Id);
        Assert.Equal(DefIds.Goblin, linkedTarget.DefId);
        Assert.Equal("Goblin", linkedTarget.DisplayName);
        Assert.Contains("Goblin", attackerEntry.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestContainerEntity : Entity
    {
        public TestContainerEntity(int id, string defId, Vec3i position, int capacity)
            : base(id, defId)
        {
            Components.Add(new PositionComponent(position));
            Components.Add(new ContainerComponent(capacity));
        }
    }
}

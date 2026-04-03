using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.Tests.Fakes;
using DwarfFortress.GameLogic.World;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

/// <summary>
/// Verifies that save → load roundtrips correctly preserve all persistent state.
/// </summary>
public sealed class SaveLoadTests
{
    // ── WorldMap ───────────────────────────────────────────────────────────

    [Fact]
    public void WorldMap_Save_Restores_Non_Empty_Tiles()
    {
        var (sim, map, _, _, _) = TestFixtures.BuildFullSim();

        // Mark one specific tile with a unique material so we can identify it
        map.SetTile(new Vec3i(7, 8, 0), new TileData
        {
            TileDefId  = TileDefIds.GraniteWall,
            MaterialId = "limestone",
            IsPassable = false,
        });

        var json = sim.Save();

        // Clear the map to all empty (no dimensions)
        var (sim2, map2, _, _, _) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        var restored = map2.GetTile(new Vec3i(7, 8, 0));
        Assert.Equal(TileDefIds.GraniteWall, restored.TileDefId);
        Assert.Equal("limestone", restored.MaterialId);
    }

    [Fact]
    public void WorldMap_Save_Restores_Dimensions()
    {
        var (sim, map, _, _, _) = TestFixtures.BuildFullSim();

        var json = sim.Save();

        var (sim2, map2, _, _, _) = TestFixtures.BuildFullSim();
        // Override dimensions before load to verify they get replaced
        map2.SetDimensions(8, 8, 2);
        sim2.Load(json);

        Assert.Equal(32, map2.Width);
        Assert.Equal(32, map2.Height);
        Assert.Equal(8,  map2.Depth);
    }

    [Fact]
    public void WorldMap_Save_Restores_Plant_Growth_Metadata()
    {
        var (sim, map, _, _, _) = TestFixtures.BuildFullSim();
        var pos = new Vec3i(10, 9, 0);
        map.SetTile(pos, new TileData
        {
            TileDefId = TileDefIds.Grass,
            MaterialId = "soil",
            PlantDefId = "sunroot",
            PlantGrowthStage = PlantGrowthStages.Mature,
            PlantGrowthProgressSeconds = 77f,
            PlantYieldLevel = 1,
            PlantSeedLevel = 1,
            IsPassable = true,
        });

        var json = sim.Save();

        var (sim2, map2, _, _, _) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        var restored = map2.GetTile(pos);
        Assert.Equal("sunroot", restored.PlantDefId);
        Assert.Equal(PlantGrowthStages.Mature, restored.PlantGrowthStage);
        Assert.Equal(77f, restored.PlantGrowthProgressSeconds);
        Assert.Equal(1, restored.PlantYieldLevel);
        Assert.Equal(1, restored.PlantSeedLevel);
    }

    // ── EntityRegistry / Dwarves ────────────────────────────────────────────

    [Fact]
    public void WorldMap_Save_Restores_TreeSpecies_Metadata()
    {
        var (sim, map, _, _, _) = TestFixtures.BuildFullSim();
        var pos = new Vec3i(9, 9, 0);
        map.SetTile(pos, new TileData
        {
            TileDefId = TileDefIds.Tree,
            MaterialId = "wood",
            TreeSpeciesId = "pine",
            IsPassable = false,
        });

        var json = sim.Save();

        var (sim2, map2, _, _, _) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        var restored = map2.GetTile(pos);
        Assert.Equal(TileDefIds.Tree, restored.TileDefId);
        Assert.Equal("pine", restored.TreeSpeciesId);
    }

    [Fact]
    public void SaveLoad_Restores_Placed_Building_Material_And_Footprint()
    {
        var (sim, map, _, _, items) = TestFixtures.BuildFullSim();

        items.CreateItem(ItemDefIds.Log, "oak_wood", new Vec3i(2, 2, 0));
        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(
            BuildingDefId: BuildingDefIds.CarpenterWorkshop,
            Origin: new Vec3i(5, 5, 0)));

        Assert.Equal("oak_wood", map.GetTile(new Vec3i(5, 5, 0)).MaterialId);

        var json = sim.Save();

        var (sim2, map2, _, _, _) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        var restoredBuilding = sim2.Context.Get<BuildingSystem>().GetByOrigin(new Vec3i(5, 5, 0));
        Assert.NotNull(restoredBuilding);
        Assert.Equal("oak_wood", restoredBuilding!.MaterialId);
        Assert.Equal("oak_wood", map2.GetTile(new Vec3i(5, 5, 0)).MaterialId);
        Assert.Equal("oak_wood", map2.GetTile(new Vec3i(6, 6, 0)).MaterialId);
    }

    [Fact]
    public void EntityRegistry_Save_Restores_Dwarf_Name_And_Position()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Urist McTest", new Vec3i(3, 4, 0));
        er.Register(dwarf);
        int savedId = dwarf.Id;

        var json = sim.Save();

        var (sim2, _, er2, _, _) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        var restored = er2.GetAlive<Dwarf>().FirstOrDefault(d => d.FirstName == "Urist McTest");
        Assert.NotNull(restored);
        Assert.Equal(savedId, restored!.Id);
        Assert.Equal(new Vec3i(3, 4, 0), restored.Position.Position);
    }

    [Fact]
    public void EntityRegistry_Save_Restores_Dwarf_Needs()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Hungry", new Vec3i(0, 0, 0));
        er.Register(dwarf);
        dwarf.Needs.Hunger.SetLevel(0.3f);
        dwarf.Needs.Thirst.SetLevel(0.2f);

        var json = sim.Save();

        var (sim2, _, er2, _, _) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        var restored = er2.GetAlive<Dwarf>().First(d => d.FirstName == "Hungry");
        Assert.InRange(restored.Needs.Hunger.Level, 0.29f, 0.31f);
        Assert.InRange(restored.Needs.Thirst.Level, 0.19f, 0.21f);
    }

    [Fact]
    public void EntityRegistry_Save_Restores_Creature_Hunger_And_Thirst()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var creature = new Creature(er.NextId(), DefIds.Elk, new Vec3i(6, 6, 0), maxHealth: 85f);
        er.Register(creature);
        creature.Needs.Hunger.SetLevel(0.33f);
        creature.Needs.Thirst.SetLevel(0.22f);

        var json = sim.Save();

        var (sim2, _, er2, _, _) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        var restored = er2.GetAlive<Creature>().First(c => c.DefId == DefIds.Elk);
        Assert.InRange(restored.Needs.Hunger.Level, 0.32f, 0.34f);
        Assert.InRange(restored.Needs.Thirst.Level, 0.21f, 0.23f);
    }

    [Fact]
    public void EntityRegistry_Save_Restores_Dwarf_Skills()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Skilled", new Vec3i(0, 0, 0));
        er.Register(dwarf);
        dwarf.Skills.AddXp(SkillIds.Mining, 9999f); // Level up many times

        int expectedLevel = dwarf.Skills.GetLevel(SkillIds.Mining);
        Assert.True(expectedLevel > 0);

        var json = sim.Save();

        var (sim2, _, er2, _, _) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        var restored = er2.GetAlive<Dwarf>().First(d => d.FirstName == "Skilled");
        Assert.Equal(expectedLevel, restored.Skills.GetLevel(SkillIds.Mining));
    }

    [Fact]
    public void EntityRegistry_Save_Restores_Dwarf_EnabledLabors()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Laborer", new Vec3i(0, 0, 0));
        er.Register(dwarf);
        dwarf.Labors.DisableAll();
        dwarf.Labors.Enable(LaborIds.Mining);
        dwarf.Labors.Enable(LaborIds.Hauling);

        var json = sim.Save();

        var (sim2, _, er2, _, _) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        var restored = er2.GetAlive<Dwarf>().First(d => d.FirstName == "Laborer");
        Assert.True(restored.Labors.IsEnabled(LaborIds.Mining));
        Assert.True(restored.Labors.IsEnabled(LaborIds.Hauling));
        Assert.False(restored.Labors.IsEnabled(LaborIds.Crafting));
    }

    // ── ItemSystem ─────────────────────────────────────────────────────────

    [Fact]
    public void ItemSystem_Save_Restores_Item_Position_And_DefId()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();

        items.CreateItem(ItemDefIds.GraniteBoulder, "granite", new Vec3i(10, 10, 0));

        var json = sim.Save();

        var (sim2, _, _, _, items2) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        var restored = items2.GetAllItems().FirstOrDefault(i => i.DefId == ItemDefIds.GraniteBoulder);
        Assert.NotNull(restored);
        Assert.Equal(new Vec3i(10, 10, 0), restored!.Position.Position);
    }

    [Fact]
    public void ItemSystem_Save_Restores_Multiple_Items()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();

        items.CreateItem(ItemDefIds.GraniteBoulder, "granite", new Vec3i(1, 1, 0));
        items.CreateItem(ItemDefIds.GraniteBoulder, "granite", new Vec3i(2, 2, 0));
        items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(3, 3, 0));

        var json = sim.Save();

        var (sim2, _, _, _, items2) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        Assert.Equal(3, items2.GetAllItems().Count());
    }

    // ── StockpileManager ───────────────────────────────────────────────────

    [Fact]
    public void StockpileManager_Save_Restores_Zone_Bounds_And_Tags()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new CreateStockpileCommand(
            From: new Vec3i(5, 5, 0),
            To:   new Vec3i(8, 5, 0),
            AcceptedTags: new[] { "stone", "ore" }));

        var json = sim.Save();

        var (sim2, _, _, _, _) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        var sm = sim2.Context.Get<StockpileManager>();
        var restored = sm.GetAll().FirstOrDefault();
        Assert.NotNull(restored);
        Assert.Equal(new Vec3i(5, 5, 0), restored!.From);
        Assert.Equal(new Vec3i(8, 5, 0), restored.To);
        Assert.Contains("stone", restored.AcceptedTags);
    }

    // ── Full roundtrip ─────────────────────────────────────────────────────

    [Fact]
    public void Full_Roundtrip_Sim_Ticks_Without_Error_After_Load()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Roundtrip", new Vec3i(5, 5, 0));
        er.Register(dwarf);

        // Run a few ticks, save
        for (int i = 0; i < 10; i++) sim.Tick(0.1f);
        var json = sim.Save();

        // Build fresh sim, load, tick again — should not throw
        var (sim2, _, er2, _, _) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < 20; i++) sim2.Tick(0.1f);
        });
        Assert.Null(ex);

        // Confirm dwarf is still alive
        Assert.Equal(1, er2.CountAlive<Dwarf>());
    }
}

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

/// <summary>
/// Verifies the backend can start a fortress and expose a stable client-facing state.
/// </summary>
public sealed class FortressBootstrapTests
{
    [Fact]
    public void StartFortressCommand_Creates_Playable_Starter_State()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 42, Width: 48, Height: 48, Depth: 8));

        Assert.Equal(3, er.CountAlive<Dwarf>());
        Assert.True(er.CountAlive<Creature>() >= 2);
        Assert.Contains(er.GetAlive<Creature>(), c => c.DefId == DefIds.Cat);
        Assert.Contains(er.GetAlive<Creature>(), c => c.DefId == DefIds.Dog);
        Assert.True(items.GetAllItems().Count() >= 20);
        Assert.NotEmpty(sim.Context.Get<StockpileManager>().GetAll());
        Assert.Contains(sim.Context.Get<BuildingSystem>().GetAll(), b => b.BuildingDefId == BuildingDefIds.CarpenterWorkshop);
        Assert.DoesNotContain(sim.Context.Get<BuildingSystem>().GetAll(), b => b.BuildingDefId == BuildingDefIds.Kitchen);
        Assert.DoesNotContain(sim.Context.Get<BuildingSystem>().GetAll(), b => b.BuildingDefId == BuildingDefIds.Still);
    }

    [Fact]
    public void StartFortressCommand_Seeds_Enough_Stockpile_Capacity_For_Starter_Items()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 42, Width: 48, Height: 48, Depth: 8));

        var allStockpiles = sim.Context.Get<StockpileManager>().GetAll().ToList();
        Assert.NotEmpty(allStockpiles);
        var totalCapacity = allStockpiles.Sum(sp => sp.AllSlots().Count());
        var totalItems = items.GetAllItems().Count();

        Assert.True(totalCapacity >= 1,
            $"Expected at least 1 stockpile slot, got {totalCapacity}.");
        _ = totalItems; // referenced to avoid warning
    }

    [Fact]
    public void WorldQuerySystem_Exposes_Client_Read_Model()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 1, Width: 48, Height: 48, Depth: 8));
        var queries = sim.Context.Get<WorldQuerySystem>();
        var map = sim.Context.Get<WorldMap>();
        var registry = sim.Context.Get<EntityRegistry>();
        var items = sim.Context.Get<ItemSystem>();
        var buildings = sim.Context.Get<BuildingSystem>();
        var stockpiles = sim.Context.Get<StockpileManager>();
        var firstDwarf = registry.GetAlive<Dwarf>().First();
        var firstCreature = registry.GetAlive<Creature>().First();
        var dwarfView = queries.GetDwarfView(firstDwarf.Id);
        var creatureView = queries.GetCreatureView(firstCreature.Id);

        Assert.Equal(3, registry.CountAlive<Dwarf>());
        Assert.True(registry.CountAlive<Creature>() >= 2);
        Assert.True(items.GetAllItems().Count() >= 20);
        Assert.NotEmpty(buildings.GetAll());
        Assert.NotEmpty(stockpiles.GetAll());
        Assert.True(CountNonEmptyTiles(map) > 0);

        Assert.NotNull(dwarfView);
        Assert.True(dwarfView!.MaxHealth > 0f);
        Assert.True(dwarfView.CurrentHealth <= dwarfView.MaxHealth);
        Assert.False(string.IsNullOrWhiteSpace(dwarfView.Appearance.HairType));
        Assert.NotNull(dwarfView.Wounds);
        Assert.NotNull(dwarfView.Substances);
        Assert.NotNull(dwarfView.EventLog);

        Assert.NotNull(creatureView);
        Assert.True(creatureView!.MaxHealth > 0f);
        Assert.True(creatureView.CurrentHealth <= creatureView.MaxHealth);
        Assert.NotEmpty(creatureView.Needs);
        Assert.Contains(creatureView.Needs, need => need.Id == NeedIds.Hunger);
        Assert.Contains(creatureView.Needs, need => need.Id == NeedIds.Thirst);
        Assert.NotEmpty(creatureView.Stats);
        Assert.NotNull(creatureView.Wounds);
        Assert.NotNull(creatureView.Substances);
        Assert.NotNull(creatureView.EventLog);
    }

    [Fact]
    public void StartFortress_State_Survives_Save_And_Load()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();
        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 7, Width: 48, Height: 48, Depth: 8));
        var originalAppearanceSignatures = sim.Context.Get<EntityRegistry>().GetAlive<Dwarf>()
            .ToDictionary(dwarf => dwarf.Id, dwarf => dwarf.Appearance.Signature);

        var json = sim.Save();

        var (sim2, _, er2, _, _) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        Assert.Equal(3, er2.CountAlive<Dwarf>());
        Assert.True(er2.CountAlive<Creature>() >= 2);
        Assert.Contains(er2.GetAlive<Creature>(), c => c.DefId == DefIds.Cat);
        Assert.Contains(er2.GetAlive<Creature>(), c => c.DefId == DefIds.Dog);
        Assert.Contains(sim2.Context.Get<BuildingSystem>().GetAll(), b => b.BuildingDefId == BuildingDefIds.CarpenterWorkshop);
        Assert.DoesNotContain(sim2.Context.Get<BuildingSystem>().GetAll(), b => b.BuildingDefId == BuildingDefIds.Kitchen);
        Assert.DoesNotContain(sim2.Context.Get<BuildingSystem>().GetAll(), b => b.BuildingDefId == BuildingDefIds.Still);
        Assert.NotEmpty(sim2.Context.Get<StockpileManager>().GetAll());
        Assert.All(er2.GetAlive<Dwarf>(), dwarf => Assert.Equal(originalAppearanceSignatures[dwarf.Id], dwarf.Appearance.Signature));
    }

    [Fact]
    public void StartFortressCommand_Assigns_Distinct_Dwarf_Appearances()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 42, Width: 48, Height: 48, Depth: 8));

        var signatures = er.GetAlive<Dwarf>().Select(dwarf => dwarf.Appearance.Signature).ToList();
        Assert.Equal(signatures.Count, signatures.Distinct().Count());
    }

    [Fact]
    public void StartFortress_SpawnsGeneratedWildlifeFromEmbarkMap()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 42, Width: 48, Height: 48, Depth: 8));

        var mapGen = sim.Context.Get<MapGenerationService>();
        var generatedWildlife = mapGen.LastGeneratedLocalMap?.CreatureSpawns.Count ?? 0;
        var totalCreatures = er.CountAlive<Creature>();

        Assert.True(generatedWildlife > 0, "Expected generated embark map to contain wildlife spawns.");
        Assert.True(totalCreatures >= 2 + generatedWildlife,
            $"Expected at least starter pets + generated wildlife ({2 + generatedWildlife}), got {totalCreatures}.");
    }

    private static int CountNonEmptyTiles(WorldMap map)
    {
        int count = 0;
        for (int x = 0; x < map.Width; x++)
        for (int y = 0; y < map.Height; y++)
        for (int z = 0; z < map.Depth; z++)
            if (map.GetTile(new Vec3i(x, y, z)).TileDefId != TileDefIds.Empty)
                count++;

        return count;
    }
}

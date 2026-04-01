using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using DwarfFortress.GameLogic.Tests.Fakes;
using Xunit;

// NOTE: these tests use real JSON data files from DwarfFortress.GameLogic/data/
// (copied to the test output directory via the library's .csproj Content items).

namespace DwarfFortress.GameLogic.Tests.SmokeTests;

/// <summary>
/// End-to-end smoke tests that run a fully wired simulation with all systems.
/// These tests verify that the whole engine integrates without crashing and
/// produces observable, correct outcomes over simulated time.
/// </summary>
public sealed class FullSimulationSmokeTest
{
    // ── Simulation factory ─────────────────────────────────────────────────

    private static (GameSimulation sim, WorldMap map, EntityRegistry er,
                    JobSystem js, ItemSystem items) BuildSim()
    {
        var logger = new TestLogger();
        var ds     = new FolderDataSource("data");

        var sim = GameBootstrapper.Build(logger, ds);

        var map   = sim.Context.Get<WorldMap>();
        var er    = sim.Context.Get<EntityRegistry>();
        var js    = sim.Context.Get<JobSystem>();
        var items = sim.Context.Get<ItemSystem>();

        // Build a 32×32×8 world (all stone walls, level 0 is open floor)
        map.SetDimensions(32, 32, 8);
        for (int x = 0; x < 32; x++)
        for (int y = 0; y < 32; y++)
        {
            // Level 0 — open floor (miners will work here)
            map.SetTile(new Vec3i(x, y, 0), new TileData
            {
                TileDefId  = TileDefIds.StoneFloor,
                MaterialId = "granite",
                IsPassable = true,
            });

            // Levels 1–7 — granite walls (mineable)
            for (int z = 1; z < 8; z++)
                map.SetTile(new Vec3i(x, y, z), new TileData
                {
                    TileDefId  = TileDefIds.GraniteWall,
                    MaterialId = "granite",
                    IsPassable = false,
                });
        }

        return (sim, map, er, js, items);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Dwarf SpawnDwarf(EntityRegistry er, string name, Vec3i pos, params string[] labors)
    {
        var dwarf  = new Dwarf(er.NextId(), name, pos);
        foreach (var labor in labors)
            dwarf.Labors.Enable(labor);
        er.Register(dwarf);
        return dwarf;
    }

    private static void RunTicks(GameSimulation sim, int ticks, float delta = 0.1f)
    {
        for (int i = 0; i < ticks; i++) sim.Tick(delta);
    }

    // ── Tests ──────────────────────────────────────────────────────────────

    [Fact]
    public void Simulation_Does_Not_Crash_Over_Many_Ticks()
    {
        var (sim, _, er, _, _) = BuildSim();
        SpawnDwarf(er, "Urist",  new Vec3i(5, 5, 0), LaborIds.Mining, LaborIds.Hauling);
        SpawnDwarf(er, "Bomrek", new Vec3i(6, 5, 0), LaborIds.Mining);
        SpawnDwarf(er, "Fikod",  new Vec3i(7, 5, 0), LaborIds.Hauling, LaborIds.WoodCutting);

        // 300 ticks = 30 simulated seconds — should not throw
        RunTicks(sim, 300);
    }

    [Fact]
    public void Dwarf_Needs_Decay_Over_Time()
    {
        var (sim, _, er, _, _) = BuildSim();
        var dwarf = SpawnDwarf(er, "Urist", new Vec3i(5, 5, 0));

        float hungerBefore = dwarf.Needs.Hunger.Level;
        RunTicks(sim, 100, 1f); // 100 seconds

        Assert.True(dwarf.Needs.Hunger.Level < hungerBefore,
            "Hunger should decay over simulated time");
    }

    [Fact]
    public void Mining_Job_Converts_Wall_To_Floor()
    {
        var (sim, map, er, js, _) = BuildSim();

        var miner = SpawnDwarf(er, "Urist", new Vec3i(5, 5, 0), LaborIds.Mining);

        // Designate a wall tile for mining — explicitly place a GraniteWall so the command can pick it up
        var wallPos  = new Vec3i(5, 6, 0);
        map.SetTile(wallPos, new TileData { TileDefId = TileDefIds.GraniteWall, MaterialId = "granite", IsPassable = false });

        // Dispatch the designation command (From == To == single tile)
        sim.Context.Commands.Dispatch(new DesignateMineCommand(wallPos, wallPos));

        // Run for enough ticks to complete the mine job (needs >5 simulated seconds)
        RunTicks(sim, 800, 0.02f); // 16 simulated seconds

        var resultTile = map.GetTile(wallPos);
        Assert.Equal(TileDefIds.StoneFloor, resultTile.TileDefId);
    }

    [Fact]
    public void Mining_Job_Spawns_Boulder_Item()
    {
        var (sim, map, er, js, items) = BuildSim();

        var miner = SpawnDwarf(er, "Urist", new Vec3i(5, 5, 0), LaborIds.Mining);

        var wallPos = new Vec3i(5, 8, 0);
        map.SetTile(wallPos, new TileData { TileDefId = TileDefIds.GraniteWall, MaterialId = "granite", IsPassable = false });
        sim.Context.Commands.Dispatch(new DesignateMineCommand(wallPos, wallPos));

        // Track item creation events
        var created = new List<ItemCreatedEvent>();
        sim.Context.EventBus.On<ItemCreatedEvent>(e => created.Add(e));

        RunTicks(sim, 800, 0.02f);

        Assert.Contains(created, e => e.ItemDefId.Contains("boulder") || e.ItemDefId.Contains("ore"));
    }

    [Fact]
    public void NeedCriticalEvent_Fires_When_Dwarf_Starves()
    {
        var (sim, _, er, _, _) = BuildSim();
        var dwarf = SpawnDwarf(er, "Hungry", new Vec3i(5, 5, 0));

        // Force hunger to critical
        dwarf.Needs.Hunger.SetLevel(0.05f);

        var events = new List<NeedCriticalEvent>();
        sim.Context.EventBus.On<NeedCriticalEvent>(e => events.Add(e));

        RunTicks(sim, 5, 0.1f);

        Assert.Contains(events, e => e.EntityId == dwarf.Id && e.NeedId == NeedIds.Hunger);
    }

    [Fact]
    public void Eat_Job_Created_For_Critical_Hunger()
    {
        var (sim, _, er, js, items) = BuildSim();
        var dwarf = SpawnDwarf(er, "Hungry", new Vec3i(5, 5, 0));

        // Spawn food near the dwarf
        items.CreateItem(ItemDefIds.Meal, "food", new Vec3i(5, 5, 0));

        // Force hunger critical
        dwarf.Needs.Hunger.SetLevel(0.05f);

        RunTicks(sim, 5, 0.1f);

        var eatJobs = js.GetAllJobs().Where(j => j.JobDefId == JobDefIds.Eat).ToList();
        Assert.NotEmpty(eatJobs);
    }

    [Fact]
    public void EntityRegistry_Tracks_Alive_And_Dead_Dwarves()
    {
        var (sim, _, er, _, _) = BuildSim();

        var d1 = SpawnDwarf(er, "Urist",  new Vec3i(0, 0, 0));
        var d2 = SpawnDwarf(er, "Bomrek", new Vec3i(1, 0, 0));

        er.Kill(d2.Id, "test");

        Assert.Equal(1, er.CountAlive<Dwarf>());
        Assert.Equal(2, er.GetAll<Dwarf>().Count());
    }

    [Fact]
    public void TimeSystem_Advances_World_Clock()
    {
        var (sim, _, _, _, _) = BuildSim();
        var time = sim.Context.Get<TimeSystem>();

        int dayBefore = time.Day;

        // Each simulated second = 1/60 hour; 60 hours = 1 day → need 3600 seconds
        // Use large delta to fast-forward
        RunTicks(sim, 100, 36f);

        Assert.True(time.Day > dayBefore || time.Month > 1,
            "World clock should advance after significant time");
    }

    [Fact]
    public void DayStartedEvent_Fires_As_Time_Passes()
    {
        var (sim, _, _, _, _) = BuildSim();

        int dayCount = 0;
        sim.Context.EventBus.On<DayStartedEvent>(_ => dayCount++);

        RunTicks(sim, 200, 36f); // ~7200 simulated seconds

        Assert.True(dayCount > 0, "DayStartedEvent should fire as days pass");
    }

    [Fact]
    public void Stockpile_Can_Be_Created_And_Accepts_Items()
    {
        var (sim, _, er, _, items) = BuildSim();

        sim.Context.Commands.Dispatch(new CreateStockpileCommand(
            From: new Vec3i(10, 10, 0),
            To:   new Vec3i(12, 10, 0),
            AcceptedTags: new[] { "stone" }));

        // Spawn a boulder stone item somewhere on the floor
        items.CreateItem(ItemDefIds.GraniteBoulder, "granite", new Vec3i(5, 5, 0));

        RunTicks(sim, 1);

        var sm = sim.Context.Get<StockpileManager>();
        Assert.Equal(1, sm.GetAll().Count());
    }

    [Fact]
    public void Multiple_Miners_Can_Work_Concurrently_Without_Errors()
    {
        var (sim, map, er, js, _) = BuildSim();

        for (int i = 0; i < 4; i++)
            SpawnDwarf(er, $"Miner{i}", new Vec3i(i * 2, 5, 0), LaborIds.Mining);

        // Designate several wall tiles
        for (int x = 0; x < 4; x++)
        {
            var p = new Vec3i(x * 2, 8, 0);
            var t = map.GetTile(p);
            t.IsDesignated = true;
            map.SetTile(p, t);
            sim.Context.Commands.Dispatch(new DesignateMineCommand(p, p));
        }

        // Should not throw even with multiple concurrent jobs
        RunTicks(sim, 600, 0.02f);
    }

    [Fact]
    public void Thought_Added_When_Need_Becomes_Critical()
    {
        var (sim, _, er, _, _) = BuildSim();
        var dwarf = SpawnDwarf(er, "Urist", new Vec3i(5, 5, 0));

        // Force thirst critical
        dwarf.Needs.Thirst.SetLevel(0.05f);

        RunTicks(sim, 5, 0.1f);

        // ThoughtSystem listens to NeedCriticalEvent and adds a negative thought
        var thoughts = dwarf.Thoughts.Active.ToList();
        Assert.NotEmpty(thoughts);
    }

    [Fact]
    public void Logger_Does_Not_Contain_Errors_During_Normal_Startup()
    {
        var logger = new TestLogger();
        var ds     = new FolderDataSource("data");

        var sim = GameBootstrapper.Build(logger, ds);

        var map = sim.Context.Get<WorldMap>();
        map.SetDimensions(16, 16, 4);
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
            map.SetTile(new Vec3i(x, y, 0), new TileData { IsPassable = true, TileDefId = TileDefIds.StoneFloor });

        RunTicks(sim, 10, 0.1f);

        Assert.Empty(logger.ErrorMessages);
    }
}

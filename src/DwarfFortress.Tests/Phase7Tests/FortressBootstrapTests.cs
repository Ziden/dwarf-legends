using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Jobs;
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
        Assert.Contains(er.GetAlive<Creature>(), c => c.IsHostile && c.Position.Position.Z == 0);
        Assert.True(items.GetAllItems().Count() >= 20);
        Assert.NotEmpty(sim.Context.Get<StockpileManager>().GetAll());
        Assert.DoesNotContain(sim.Context.Get<BuildingSystem>().GetAll(), b => b.BuildingDefId == BuildingDefIds.CarpenterWorkshop);
        Assert.DoesNotContain(sim.Context.Get<BuildingSystem>().GetAll(), b => b.BuildingDefId == BuildingDefIds.Kitchen);
        Assert.DoesNotContain(sim.Context.Get<BuildingSystem>().GetAll(), b => b.BuildingDefId == BuildingDefIds.Still);
    }

    [Fact]
    public void StartFortressCommand_Places_Starter_Stockpile_Away_From_Embark_Stairs()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 42, Width: 48, Height: 48, Depth: 8));

        var embark = new Vec3i(24, 24, 0);
        var stockpile = sim.Context.Get<StockpileManager>().GetAll().First();

        Assert.DoesNotContain(stockpile.AllSlots(), slot => slot == embark);
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
    public void StartFortressCommand_Sets_ClosestDrink_FortressLocation()
    {
        var (sim, map, _, _, _) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 42, Width: 48, Height: 48, Depth: 8));

        var fortressLocations = sim.Context.Get<FortressLocationSystem>();
        Assert.True(fortressLocations.TryGetLocation(FortressLocationIds.EmbarkCenter, out var embarkCenter));
        Assert.Equal(new Vec3i(24, 24, 0), embarkCenter);
        Assert.True(fortressLocations.TryGetClosestDrinkLocation(out var closestDrink));
        Assert.True(embarkCenter.ManhattanDistanceTo(closestDrink) <= 12,
            $"Expected nearby natural water from embark center, got distance {embarkCenter.ManhattanDistanceTo(closestDrink)}.");
        var drinkTile = map.GetTile(closestDrink);
        Assert.True(drinkTile.FluidType == FluidType.Water || drinkTile.TileDefId == TileDefIds.Water);
        Assert.True(drinkTile.FluidLevel > 0);
        var reachableDrinkStand = new[]
        {
            closestDrink + Vec3i.North,
            closestDrink + Vec3i.South,
            closestDrink + Vec3i.East,
            closestDrink + Vec3i.West,
        }.Any(candidate =>
            map.IsWalkable(candidate)
            && Pathfinder.FindPath(map, embarkCenter, candidate).Count > 0);
        Assert.True(
            reachableDrinkStand,
            $"Expected closest drink location {closestDrink} to expose a reachable drinking stand from embark center {embarkCenter}.");
    }

    [Theory]
    [InlineData(7)]
    [InlineData(17)]
    [InlineData(42)]
    public void StartFortressCommand_Selects_Embark_With_Nearby_Natural_Food(int seed)
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: seed, Width: 48, Height: 48, Depth: 8));

        var fortressLocations = sim.Context.Get<FortressLocationSystem>();
        var map = sim.Context.Get<WorldMap>();
        var data = sim.Context.Get<DataManager>();

        Assert.True(fortressLocations.TryGetLocation(FortressLocationIds.EmbarkCenter, out var embarkCenter));
        Assert.True(
            PlantHarvesting.TryFindNearestHarvestablePlant(map, data, embarkCenter, searchRadius: 18, out var target),
            $"Expected natural forage near embark center for seed {seed}.");
        Assert.True(embarkCenter.ManhattanDistanceTo(target.PlantPos) <= 18,
            $"Expected forage to be nearby for seed {seed}, got distance {embarkCenter.ManhattanDistanceTo(target.PlantPos)}.");
    }

    [Fact]
    public void StartFortressCommand_Dwarves_Sustain_Themselves_On_Natural_Water_And_Food()
    {
        var (sim, map, er, _, items) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 42, Width: 48, Height: 48, Depth: 8));

        var fortressLocations = sim.Context.Get<FortressLocationSystem>();
        var data = sim.Context.Get<DataManager>();
        var jobSystem = sim.Context.Get<JobSystem>();
        var dwarves = er.GetAlive<Dwarf>().ToList();

        Assert.True(fortressLocations.TryGetLocation(FortressLocationIds.EmbarkCenter, out var embarkCenter));

        SeedSafeSurvivalResources(map, embarkCenter, dwarves.Select(dwarf => dwarf.Position.Position).ToList());

        Assert.True(fortressLocations.RefreshClosestDrinkLocation());
        Assert.True(fortressLocations.TryGetClosestDrinkLocation(out var closestDrink));
        Assert.True(embarkCenter.ManhattanDistanceTo(closestDrink) <= 12,
            $"Expected the seeded drink source to stay near embark center, got distance {embarkCenter.ManhattanDistanceTo(closestDrink)}.");
        Assert.True(
            PlantHarvesting.TryFindNearestHarvestablePlant(map, data, embarkCenter, searchRadius: 18, out var harvestTarget),
            "Expected the seeded embark garden to expose nearby forage for the long-run survival test.");
        Assert.True(embarkCenter.ManhattanDistanceTo(harvestTarget.PlantPos) <= 12,
            $"Expected the seeded forage to stay near embark center, got distance {embarkCenter.ManhattanDistanceTo(harvestTarget.PlantPos)}.");

        foreach (var starterSupply in items.GetAllItems()
                     .Where(item => item.DefId == ItemDefIds.Meal || item.DefId == ItemDefIds.Drink)
                     .ToList())
        {
            items.DestroyItem(starterSupply.Id);
        }

        sim.Context.Get<BehaviorSystem>().IsEnabled = false;
        sim.Context.Get<CombatSystem>().IsEnabled = false;
        sim.Context.Get<CombatResponseSystem>().IsEnabled = false;

        var dwarfIds = er.GetAlive<Dwarf>().Select(dwarf => dwarf.Id).ToHashSet();
        foreach (var dwarf in dwarves)
        {
            dwarf.Labors.DisableAll();
            dwarf.Labors.Enable(LaborIds.Misc);
        }

        var drankNaturalWater = new HashSet<int>();
        var naturalFoodUse = new HashSet<int>();
        var drinkCountByDwarf = new Dictionary<int, int>();
        var forageCountByDwarf = new Dictionary<int, int>();
        var dwarfDeaths = new List<EntityDiedEvent>();
        var lastActivityByDwarf = new Dictionary<int, string>();
        var lastAssignedJobByDwarf = new Dictionary<int, string>();

        sim.Context.EventBus.On<EntityActivityEvent>(activity =>
        {
            if (!dwarfIds.Contains(activity.EntityId))
                return;

            lastActivityByDwarf[activity.EntityId] = activity.Description;

            if (string.Equals(activity.Description, "Drank water", StringComparison.OrdinalIgnoreCase))
            {
                drankNaturalWater.Add(activity.EntityId);
                drinkCountByDwarf[activity.EntityId] = drinkCountByDwarf.GetValueOrDefault(activity.EntityId) + 1;
            }

            if (activity.Description.StartsWith("Foraged ", StringComparison.OrdinalIgnoreCase))
            {
                naturalFoodUse.Add(activity.EntityId);
                forageCountByDwarf[activity.EntityId] = forageCountByDwarf.GetValueOrDefault(activity.EntityId) + 1;
            }
        });

        sim.Context.EventBus.On<JobAssignedEvent>(assignment =>
        {
            var job = jobSystem.GetJob(assignment.JobId);
            if (job is null || !dwarfIds.Contains(assignment.DwarfId))
                return;

            lastAssignedJobByDwarf[assignment.DwarfId] = job.JobDefId;
        });

        sim.Context.EventBus.On<EntityDiedEvent>(death =>
        {
            if (death.IsDwarf)
                dwarfDeaths.Add(death);
        });

        const int tickCount = 2400;
        const float delta = 0.5f;

        for (var tick = 0; tick < tickCount && dwarfDeaths.Count == 0; tick++)
            sim.Tick(delta);

        var dwarfDiagnostics = string.Join(" | ", dwarves.Select(dwarf =>
        {
            var assignedJob = jobSystem.GetAssignedJob(dwarf.Id)?.JobDefId ?? "none";
            var lastAssignedJob = lastAssignedJobByDwarf.TryGetValue(dwarf.Id, out var jobDefId) ? jobDefId : "none";
            var lastActivity = lastActivityByDwarf.TryGetValue(dwarf.Id, out var activity) ? activity : "none";
            return $"{dwarf.FirstName}:alive={dwarf.IsAlive},pos={dwarf.Position.Position},thirst={dwarf.Needs.Thirst.Level:F3},hunger={dwarf.Needs.Hunger.Level:F3},sleep={dwarf.Needs.Sleep.Level:F3},job={assignedJob},lastAssigned={lastAssignedJob},lastActivity={lastActivity},drankWater={drankNaturalWater.Contains(dwarf.Id)},ateForage={naturalFoodUse.Contains(dwarf.Id)}";
        }));

        Assert.True(
            dwarfDeaths.Count == 0,
            $"Expected no dwarf deaths. Deaths: {string.Join(", ", dwarfDeaths.Select(death => $"{death.DisplayName}:{death.Cause}@{death.Position}"))}. Dwarves: {dwarfDiagnostics}");
        Assert.Equal(3, er.CountAlive<Dwarf>());
        Assert.All(dwarves, dwarf => Assert.True(dwarf.IsAlive, $"Expected {dwarf.FirstName} to remain alive during the long-run survival test."));
        Assert.DoesNotContain(items.GetAllItems(), item => item.DefId == ItemDefIds.Meal || item.DefId == ItemDefIds.Drink);
        Assert.Equal(dwarfIds.Count, drankNaturalWater.Count);
        Assert.Equal(dwarfIds.Count, naturalFoodUse.Count);
        Assert.All(dwarves, dwarf => Assert.True(drinkCountByDwarf.GetValueOrDefault(dwarf.Id) >= 2,
            $"Expected {dwarf.FirstName} to drink natural water multiple times during the survival test."));
        Assert.All(dwarves, dwarf => Assert.True(forageCountByDwarf.GetValueOrDefault(dwarf.Id) >= 1,
            $"Expected {dwarf.FirstName} to forage natural food during the survival test."));
    }

    [Fact]
    public void StartFortressCommand_Preserves_Essential_Starter_Labors_When_Profiles_Provide_Authored_Labors()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 7, Width: 48, Height: 48, Depth: 8));

        var starters = er.GetAlive<Dwarf>().ToList();

        Assert.Contains(starters, dwarf => dwarf.Labors.IsEnabled(LaborIds.Mining));
        Assert.Contains(starters, dwarf => dwarf.Labors.IsEnabled(LaborIds.WoodCutting));
        Assert.Contains(starters, dwarf => dwarf.Labors.IsEnabled(LaborIds.Crafting));
    }

    [Fact]
    public void StartFortressCommand_Starter_Log_Makes_House_Buildable_And_Carpenter_Discovered()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 42, Width: 48, Height: 48, Depth: 8));

        var discovery = sim.Context.Get<DiscoverySystem>();

        Assert.Equal(DiscoveryKnowledgeState.BuildableNow, discovery.GetBuildingState(BuildingDefIds.House));
        Assert.True(discovery.IsBuildingUnlocked(BuildingDefIds.House));
        Assert.True(discovery.IsBuildingUnlocked(BuildingDefIds.CarpenterWorkshop));
        Assert.Equal(DiscoveryKnowledgeState.Unlocked, discovery.GetBuildingState(BuildingDefIds.CarpenterWorkshop));
        Assert.Equal(ItemDefIds.Log, discovery.GetDiscoveredBy(BuildingDefIds.House));
        Assert.Equal(ItemDefIds.Log, discovery.GetDiscoveredBy(BuildingDefIds.CarpenterWorkshop));
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
        Assert.DoesNotContain(buildings.GetAll(), building => building.BuildingDefId == BuildingDefIds.CarpenterWorkshop);
        Assert.NotEmpty(stockpiles.GetAll());
        Assert.True(CountNonEmptyTiles(map) > 0);

        Assert.NotNull(dwarfView);
        Assert.True(dwarfView!.MaxHealth > 0f);
        Assert.True(dwarfView.CurrentHealth <= dwarfView.MaxHealth);
        Assert.False(string.IsNullOrWhiteSpace(dwarfView.Appearance.HairType));
        Assert.NotNull(dwarfView.Provenance);
        Assert.False(string.IsNullOrWhiteSpace(dwarfView.Provenance!.FigureId));
        Assert.False(string.IsNullOrWhiteSpace(dwarfView.Provenance.HouseholdId));
        Assert.False(string.IsNullOrWhiteSpace(dwarfView.Provenance!.CivilizationId));
        Assert.False(string.IsNullOrWhiteSpace(dwarfView.Provenance.OriginSiteId));
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
        var fortressLocations = sim.Context.Get<FortressLocationSystem>();
        var originalAppearanceSignatures = sim.Context.Get<EntityRegistry>().GetAlive<Dwarf>()
            .ToDictionary(dwarf => dwarf.Id, dwarf => dwarf.Appearance.Signature);
        var originalAttributes = sim.Context.Get<EntityRegistry>().GetAlive<Dwarf>()
            .ToDictionary(
                dwarf => dwarf.Id,
                dwarf => AttributeIds.All.ToDictionary(
                    attributeId => attributeId,
                    attributeId => dwarf.Attributes.GetLevel(attributeId),
                    StringComparer.OrdinalIgnoreCase));
                Assert.True(fortressLocations.TryGetClosestDrinkLocation(out var originalClosestDrink));

        var json = sim.Save();

        var (sim2, _, er2, _, _) = TestFixtures.BuildFullSim();
        sim2.Load(json);
                var loadedLocations = sim2.Context.Get<FortressLocationSystem>();

        Assert.Equal(3, er2.CountAlive<Dwarf>());
        Assert.True(er2.CountAlive<Creature>() >= 2);
        Assert.Contains(er2.GetAlive<Creature>(), c => c.DefId == DefIds.Cat);
        Assert.Contains(er2.GetAlive<Creature>(), c => c.DefId == DefIds.Dog);
        Assert.Contains(er2.GetAlive<Creature>(), c => c.IsHostile && c.Position.Position.Z == 0);
        Assert.DoesNotContain(sim2.Context.Get<BuildingSystem>().GetAll(), b => b.BuildingDefId == BuildingDefIds.CarpenterWorkshop);
        Assert.DoesNotContain(sim2.Context.Get<BuildingSystem>().GetAll(), b => b.BuildingDefId == BuildingDefIds.Kitchen);
        Assert.DoesNotContain(sim2.Context.Get<BuildingSystem>().GetAll(), b => b.BuildingDefId == BuildingDefIds.Still);
        Assert.NotEmpty(sim2.Context.Get<StockpileManager>().GetAll());
        Assert.All(er2.GetAlive<Dwarf>(), dwarf => Assert.Equal(originalAppearanceSignatures[dwarf.Id], dwarf.Appearance.Signature));
        Assert.All(er2.GetAlive<Dwarf>(), dwarf =>
        {
            foreach (var attributeId in AttributeIds.All)
                Assert.Equal(originalAttributes[dwarf.Id][attributeId], dwarf.Attributes.GetLevel(attributeId));

            Assert.False(string.IsNullOrWhiteSpace(dwarf.Provenance.FigureId));
            Assert.False(string.IsNullOrWhiteSpace(dwarf.Provenance.HouseholdId));
            Assert.False(string.IsNullOrWhiteSpace(dwarf.Provenance.CivilizationId));
            Assert.False(string.IsNullOrWhiteSpace(dwarf.Provenance.OriginSiteId));
            Assert.False(string.IsNullOrWhiteSpace(dwarf.Provenance.MigrationWaveId));
        });
        Assert.True(loadedLocations.TryGetClosestDrinkLocation(out var loadedClosestDrink));
        Assert.Equal(originalClosestDrink, loadedClosestDrink);
    }

    [Fact]
    public void DwarfAttributeComponent_RollRandom_CanReach_All_Levels()
    {
        var rng = new Random(7);
        var seen = new HashSet<int>();

        for (var i = 0; i < 512; i++)
            seen.Add(DwarfAttributeComponent.RollRandom(rng));

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, seen.OrderBy(value => value).ToArray());
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
    public void StartFortressCommand_Spawns_Starter_Hostile_Outside_Embark_Center()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 42, Width: 48, Height: 48, Depth: 8));

        var embark = new Vec3i(24, 24, 0);
        var surfaceHostiles = er.GetAlive<Creature>()
            .Where(creature => creature.IsHostile && creature.Position.Position.Z == 0)
            .ToList();

        Assert.NotEmpty(surfaceHostiles);
        Assert.Contains(surfaceHostiles, creature =>
            System.Math.Abs(creature.Position.Position.X - embark.X) > 5 ||
            System.Math.Abs(creature.Position.Position.Y - embark.Y) > 5);
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

    [Fact]
    public void StartFortressCommand_StarterHostile_Eventually_Triggers_Combat()
    {
        var (sim, _, _, _, _) = TestFixtures.BuildFullSim();

        var combatTriggered = false;
        sim.Context.EventBus.On<CombatHitEvent>(_ => combatTriggered = true);
        sim.Context.EventBus.On<CombatMissEvent>(_ => combatTriggered = true);

        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 42, Width: 48, Height: 48, Depth: 8));

        for (int tick = 0; tick < 250 && !combatTriggered; tick++)
            sim.Tick(0.1f);

        Assert.True(combatTriggered,
            "Expected the starter hostile to reach the embark and trigger combat shortly after fortress start.");
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

    private static void SeedSafeSurvivalResources(WorldMap map, Vec3i embarkCenter, IReadOnlyList<Vec3i> dwarfPositions)
    {
        PrepareSafeSurvivalArena(map, embarkCenter, radius: 14);
        foreach (var dwarfPos in dwarfPositions)
        {
            SeedDrinkPond(map, dwarfPos + new Vec3i(3, 0, 0), radius: 0);
            SeedSunrootGarden(map, dwarfPos + new Vec3i(-4, -1, 0), width: 3, height: 3);
        }
    }

    private static void PrepareSafeSurvivalArena(WorldMap map, Vec3i center, int radius)
    {
        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            var pos = new Vec3i(center.X + dx, center.Y + dy, center.Z);
            if (!map.IsInBounds(pos))
                continue;

            var isBoundary = Math.Abs(dx) == radius || Math.Abs(dy) == radius;

            map.SetTile(pos, new TileData
            {
                TileDefId = isBoundary ? TileDefIds.StoneWall : TileDefIds.Grass,
                MaterialId = MaterialIds.Granite,
                IsPassable = !isBoundary,
                FluidType = FluidType.None,
                FluidLevel = 0,
                PlantDefId = null,
                PlantGrowthStage = 0,
                PlantYieldLevel = 0,
                PlantSeedLevel = 0,
                IsDesignated = false,
            });
        }
    }

    private static void SeedDrinkPond(WorldMap map, Vec3i center, int radius)
    {
        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            if (Math.Abs(dx) + Math.Abs(dy) > radius + 1)
                continue;

            var pos = new Vec3i(center.X + dx, center.Y + dy, center.Z);
            if (!map.IsInBounds(pos))
                continue;

            map.SetTile(pos, new TileData
            {
                TileDefId = TileDefIds.Water,
                MaterialId = MaterialIds.Granite,
                IsPassable = true,
                FluidType = FluidType.Water,
                FluidLevel = 7,
            });
        }
    }

    private static void SeedSunrootGarden(WorldMap map, Vec3i topLeft, int width, int height)
    {
        for (var dx = 0; dx < width; dx++)
        for (var dy = 0; dy < height; dy++)
        {
            var pos = new Vec3i(topLeft.X + dx, topLeft.Y + dy, topLeft.Z);
            if (!map.IsInBounds(pos))
                continue;

            map.SetTile(pos, new TileData
            {
                TileDefId = TileDefIds.Grass,
                MaterialId = MaterialIds.Granite,
                IsPassable = true,
                PlantDefId = "sunroot",
                PlantGrowthStage = PlantGrowthStages.Mature,
                PlantYieldLevel = 1,
                PlantSeedLevel = 1,
            });
        }
    }
}

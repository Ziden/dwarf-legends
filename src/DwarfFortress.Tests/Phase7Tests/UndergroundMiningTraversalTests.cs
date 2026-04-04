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

public sealed class UndergroundMiningTraversalTests
{
    [Fact]
    public void Dwarf_Can_Mine_Underground_And_Return_Up_Stairs_To_Drink()
    {
        var (sim, map, er, _, items) = TestFixtures.BuildFullSim();

        var stairSurface = new Vec3i(10, 10, 0);
        var stairDepth = new Vec3i(10, 10, 1);
        var undergroundStand = new Vec3i(10, 11, 1);
        var undergroundWall = new Vec3i(10, 12, 1);
        var surfaceDrinkPos = new Vec3i(10, 9, 0);

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
        map.SetTile(undergroundWall, new TileData
        {
            TileDefId = TileDefIds.GraniteWall,
            MaterialId = MaterialIds.Granite,
            IsPassable = false,
        });

        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(9, 10, 0));
        dwarf.Labors.Enable(LaborIds.Mining);
        dwarf.Needs.Hunger.SetLevel(1f);
        dwarf.Needs.Thirst.SetLevel(1f);
        er.Register(dwarf);

        items.CreateItem(ItemDefIds.Drink, MaterialIds.Drink, surfaceDrinkPos);

        sim.Context.Commands.Dispatch(new DesignateMineCommand(undergroundWall, undergroundWall));

        for (var tick = 0; tick < 300; tick++)
        {
            sim.Tick(0.1f);
            if (map.GetTile(undergroundWall).IsPassable)
                break;
        }

        Assert.True(map.GetTile(undergroundWall).IsPassable);

        dwarf.Needs.Thirst.SetLevel(0.01f);

        for (var tick = 0; tick < 400; tick++)
        {
            sim.Tick(0.1f);
            if (dwarf.Needs.Thirst.Level >= 0.8f)
                break;
        }

        Assert.InRange(dwarf.Needs.Thirst.Level, 0.8f, 1f);
        Assert.True(dwarf.Position.Position.Z is 0 or 1);
        Assert.True(Pathfinder.FindPath(map, dwarf.Position.Position, stairSurface).Count > 0 || dwarf.Position.Position == stairSurface);
    }

    [Fact]
    public void Dwarf_Can_Finish_A_MultiTile_Underground_Mining_Designation()
    {
        var (sim, map, er, js, _) = TestFixtures.BuildFullSim();

        var stairSurface = new Vec3i(10, 10, 0);
        var stairDepth = new Vec3i(10, 10, 1);
        var corridor = new[]
        {
            new Vec3i(10, 11, 1),
            new Vec3i(10, 12, 1),
        };

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

        foreach (var floor in corridor)
        {
            map.SetTile(floor, new TileData
            {
                TileDefId = TileDefIds.StoneFloor,
                MaterialId = MaterialIds.Granite,
                IsPassable = true,
            });
        }

        var designatedWalls = new[]
        {
            new Vec3i(9, 13, 1), new Vec3i(10, 13, 1), new Vec3i(11, 13, 1),
            new Vec3i(9, 14, 1), new Vec3i(10, 14, 1), new Vec3i(11, 14, 1),
            new Vec3i(9, 15, 1), new Vec3i(10, 15, 1), new Vec3i(11, 15, 1),
        };

        foreach (var wall in designatedWalls)
        {
            map.SetTile(wall, new TileData
            {
                TileDefId = TileDefIds.GraniteWall,
                MaterialId = MaterialIds.Granite,
                IsPassable = false,
            });
        }

        var dwarf = new Dwarf(er.NextId(), "Miner", new Vec3i(9, 10, 0));
        dwarf.Labors.Enable(LaborIds.Mining);
        dwarf.Needs.Hunger.SetLevel(1f);
        dwarf.Needs.Thirst.SetLevel(1f);
        dwarf.Needs.Sleep.SetLevel(1f);
        er.Register(dwarf);

        sim.Context.Commands.Dispatch(new DesignateMineCommand(designatedWalls[0], designatedWalls[^1]));

        for (var tick = 0; tick < 1600; tick++)
            sim.Tick(0.1f);

        Assert.All(designatedWalls, wall => Assert.True(map.GetTile(wall).IsPassable, $"Expected {wall} to be mined."));
        Assert.DoesNotContain(js.GetAllJobs(), job => job.JobDefId == JobDefIds.MineTile && job.Status is JobStatus.Pending or JobStatus.InProgress);
    }

    [Fact]
    public void FortressStart_Dwarf_Can_Work_Underground_And_Still_Return_For_Drink()
    {
        var (sim, map, er, js, _) = TestFixtures.BuildFullSim();
        sim.Context.Commands.Dispatch(new StartFortressCommand(Seed: 7, Width: 48, Height: 48, Depth: 8));

        var embarkCenter = new Vec3i(24, 24, 0);
        var undergroundTarget = FindReachableMineableTile(map, embarkCenter, new Vec3i(24, 24, 1), searchRadius: 6);
        Assert.NotNull(undergroundTarget);

        sim.Context.Commands.Dispatch(new DesignateMineCommand(undergroundTarget!.Value, undergroundTarget.Value));

        var mineFailures = new List<JobFailedEvent>();
        sim.Context.EventBus.On<JobFailedEvent>(failure =>
        {
            var failedJob = js.GetJob(failure.JobId);
            if (failedJob?.JobDefId == JobDefIds.MineTile)
                mineFailures.Add(failure);
        });

        var dwarf = er.GetAlive<Dwarf>()
            .OrderBy(candidate => candidate.Position.Position.ManhattanDistanceTo(embarkCenter))
            .First();

        for (var tick = 0; tick < 600; tick++)
        {
            sim.Tick(0.1f);
            if (map.GetTile(undergroundTarget.Value).IsPassable)
                break;
        }

        var mineJobStates = string.Join(", ",
            js.GetAllJobs()
                .Where(job => job.JobDefId == JobDefIds.MineTile)
                .Select(job => $"{job.TargetPos}:{job.Status}/assigned={job.AssignedDwarfId}"));
        var mineFailureReasons = string.Join(", ", mineFailures.Select(failure => failure.Reason));
        var dwarfJobs = string.Join(", ",
            er.GetAlive<Dwarf>()
                .Select(candidate =>
                {
                    var assignedJob = js.GetAssignedJob(candidate.Id);
                    return $"{candidate.FirstName}@{candidate.Position.Position}:job={(assignedJob?.JobDefId ?? "none")}/mining={candidate.Labors.IsEnabled(LaborIds.Mining)}";
                }));

        Assert.True(
            map.GetTile(undergroundTarget.Value).IsPassable,
            $"Expected {undergroundTarget.Value} to be mined. Dwarf at {dwarf.Position.Position}. Mine jobs: [{mineJobStates}]. Failures: [{mineFailureReasons}]. Dwarves: [{dwarfJobs}].");

        dwarf.Needs.Thirst.SetLevel(0.01f);

        for (var tick = 0; tick < 600; tick++)
        {
            sim.Tick(0.1f);
            if (dwarf.Needs.Thirst.Level >= 0.8f)
                break;
        }

        Assert.InRange(dwarf.Needs.Thirst.Level, 0.8f, 1f);
        Assert.NotEmpty(Pathfinder.FindPath(map, dwarf.Position.Position, embarkCenter));
    }

    private static Vec3i? FindReachableMineableTile(WorldMap map, Vec3i embarkCenter, Vec3i center, int searchRadius)
    {
        for (var radius = 0; radius <= searchRadius; radius++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            for (var dy = -radius; dy <= radius; dy++)
            {
                var pos = new Vec3i(center.X + dx, center.Y + dy, center.Z);
                if (!map.IsInBounds(pos))
                    continue;

                var tile = map.GetTile(pos);
                if (tile.IsPassable || tile.TileDefId == TileDefIds.Empty || tile.TileDefId == TileDefIds.Staircase)
                    continue;

                if (HasReachableAdjacentStandTile(map, embarkCenter, pos))
                    return pos;
            }
        }

        return null;
    }

    private static bool HasReachableAdjacentStandTile(WorldMap map, Vec3i embarkCenter, Vec3i wallPos)
    {
        var candidates = new[]
        {
            wallPos + Vec3i.North,
            wallPos + Vec3i.South,
            wallPos + Vec3i.East,
            wallPos + Vec3i.West,
        };

        foreach (var candidate in candidates)
        {
            if (!map.IsWalkable(candidate))
                continue;

            if (Pathfinder.FindPath(map, embarkCenter, candidate).Count > 0)
                return true;
        }

        return false;
    }
}
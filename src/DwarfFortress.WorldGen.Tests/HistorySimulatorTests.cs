using System.Text;
using DwarfFortress.WorldGen.History;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.World;

namespace DwarfFortress.WorldGen.Tests;

public sealed class HistorySimulatorTests
{
    [Fact]
    public void Simulate_SameSeedAndWorld_ProducesDeterministicHistory()
    {
        var world = new WorldLayerGenerator().Generate(seed: 812, width: 64, height: 64);
        var simulator = new HistorySimulator();

        var a = simulator.Simulate(world, seed: 4501);
        var b = simulator.Simulate(world, seed: 4501);

        Assert.Equal(Fingerprint(a), Fingerprint(b));
    }

    [Fact]
    public void Simulate_TerritoryAssignments_AreBoundedAndOwnedByKnownCivilizations()
    {
        var world = new WorldLayerGenerator().Generate(seed: 933, width: 48, height: 48);
        var history = new HistorySimulator().Simulate(world, seed: 1203);

        Assert.True(history.Civilizations.Count >= 2, "Expected at least two civilizations.");

        var civilizationIds = history.Civilizations
            .Select(civ => civ.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in history.TerritoryByTile)
        {
            var coord = entry.Key;
            var ownerId = entry.Value;
            Assert.InRange(coord.X, 0, world.Width - 1);
            Assert.InRange(coord.Y, 0, world.Height - 1);
            Assert.Contains(ownerId, civilizationIds);
            Assert.False(MacroBiomeIds.IsOcean(world.GetTile(coord.X, coord.Y).MacroBiomeId));
        }

        Assert.All(history.Civilizations, civ =>
        {
            Assert.NotEmpty(civ.Territory);
            Assert.Contains(civ.Capital, civ.Territory);
        });
    }

    [Fact]
    public void Simulate_Roads_UseCardinalLandPathsBetweenReferencedSites()
    {
        var simulator = new HistorySimulator();
        GeneratedWorldMap? world = null;
        GeneratedWorldHistory? history = null;

        // Find a seed that emits at least one road to validate pathing contracts.
        for (var seed = 200; seed < 260; seed++)
        {
            var candidateWorld = new WorldLayerGenerator().Generate(seed: seed, width: 64, height: 64);
            var candidateHistory = simulator.Simulate(candidateWorld, seed: seed * 17);
            if (candidateHistory.Roads.Count == 0)
                continue;

            world = candidateWorld;
            history = candidateHistory;
            break;
        }

        Assert.NotNull(world);
        Assert.NotNull(history);

        var sitesById = history!.Sites.ToDictionary(site => site.Id, site => site, StringComparer.OrdinalIgnoreCase);
        foreach (var road in history.Roads)
        {
            Assert.True(road.Path.Count >= 2, $"Road {road.Id} should have at least two path nodes.");
            Assert.True(sitesById.ContainsKey(road.FromSiteId), $"Road {road.Id} references unknown source site.");
            Assert.True(sitesById.ContainsKey(road.ToSiteId), $"Road {road.Id} references unknown target site.");

            var source = sitesById[road.FromSiteId];
            var target = sitesById[road.ToSiteId];
            Assert.Equal(source.Location, road.Path[0]);
            Assert.Equal(target.Location, road.Path[^1]);

            for (var i = 1; i < road.Path.Count; i++)
            {
                var prev = road.Path[i - 1];
                var next = road.Path[i];
                var step = Math.Abs(prev.X - next.X) + Math.Abs(prev.Y - next.Y);
                Assert.Equal(1, step);
                Assert.False(MacroBiomeIds.IsOcean(world!.GetTile(next.X, next.Y).MacroBiomeId));
            }
        }
    }

    [Fact]
    public void SimulateTimeline_EmitsYearlySnapshotsWithConsistentFinalHistory()
    {
        var world = new WorldLayerGenerator().Generate(seed: 1441, width: 48, height: 48);
        var simulator = new HistorySimulator();

        var timeline = simulator.SimulateTimeline(world, seed: 4101, simulatedYearsOverride: 32);

        Assert.Equal(32, timeline.Years.Count);
        Assert.Equal(32, timeline.FinalHistory.SimulatedYears);
        Assert.NotEmpty(timeline.FinalHistory.Civilizations);

        for (var i = 0; i < timeline.Years.Count; i++)
            Assert.Equal(i + 1, timeline.Years[i].Year);

        var finalSnapshot = timeline.Years[^1];
        var finalCivs = timeline.FinalHistory.Civilizations.ToDictionary(civ => civ.Id, civ => civ.Territory.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var civ in finalSnapshot.Civilizations)
        {
            if (!finalCivs.TryGetValue(civ.CivilizationId, out var finalTerritoryCount))
                continue;

            Assert.Equal(finalTerritoryCount, civ.TerritoryTiles);
        }

        var totalYearlyEvents = timeline.Years.Sum(year => year.Events.Count);
        Assert.Equal(totalYearlyEvents, timeline.FinalHistory.Events.Count);

        var finalRoadIds = timeline.FinalHistory.Roads
            .Select(road => road.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var finalSnapshotRoadIds = finalSnapshot.Roads
            .Select(road => road.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(finalRoadIds, finalSnapshotRoadIds);

        foreach (var year in timeline.Years)
        {
            var siteIds = year.Sites.Select(site => site.SiteId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var road in year.Roads)
            {
                Assert.True(siteIds.Contains(road.FromSiteId), $"Road {road.Id} references missing source site in year {year.Year}.");
                Assert.True(siteIds.Contains(road.ToSiteId), $"Road {road.Id} references missing target site in year {year.Year}.");
                Assert.True(road.Path.Count >= 2, $"Road {road.Id} should have at least two nodes in year {year.Year}.");

                for (var i = 1; i < road.Path.Count; i++)
                {
                    var prev = road.Path[i - 1];
                    var next = road.Path[i];
                    var step = Math.Abs(prev.X - next.X) + Math.Abs(prev.Y - next.Y);
                    Assert.Equal(1, step);
                }
            }
        }
    }

    [Fact]
    public void CreateSession_AdvancesYearByYearAndCompletesDeterministically()
    {
        var simulator = new HistorySimulator();
        GeneratedWorldMap? world = null;
        HistorySimulator.HistorySimulationSession? session = null;

        for (var seed = 1771; seed < 1810; seed++)
        {
            var candidateWorld = new WorldLayerGenerator().Generate(seed: seed, width: 48, height: 48);
            var candidateSession = simulator.CreateSession(candidateWorld, seed: 4301, simulatedYearsOverride: 20);
            if (candidateSession.TargetYears <= 0)
                continue;

            world = candidateWorld;
            session = candidateSession;
            break;
        }

        Assert.NotNull(world);
        Assert.NotNull(session);

        var baseline = simulator.SimulateTimeline(world!, seed: 4301, simulatedYearsOverride: 20);

        var advancedYears = 0;
        while (session!.TryAdvance(out var snapshot))
        {
            advancedYears++;
            Assert.NotNull(snapshot);
            Assert.Equal(advancedYears, snapshot!.Year);
        }

        var completed = session.Complete();
        Assert.Equal(20, advancedYears);
        Assert.True(session.IsCompleted);
        Assert.Equal(20, session.CurrentYear);
        Assert.Equal(20, session.TargetYears);
        Assert.Equal(20, session.Years.Count);
        Assert.Equal(Fingerprint(baseline.FinalHistory), Fingerprint(completed.FinalHistory));
    }

    [Fact]
    public void CreateSession_ExposesPartialProgressBeforeCompletion()
    {
        var simulator = new HistorySimulator();
        HistorySimulator.HistorySimulationSession? session = null;

        for (var seed = 1889; seed < 1930; seed++)
        {
            var candidateWorld = new WorldLayerGenerator().Generate(seed: seed, width: 40, height: 40);
            var candidateSession = simulator.CreateSession(candidateWorld, seed: 5123, simulatedYearsOverride: 5);
            if (candidateSession.TargetYears <= 0)
                continue;

            session = candidateSession;
            break;
        }

        Assert.NotNull(session);

        Assert.False(session!.IsCompleted);
        Assert.Equal(0, session.CurrentYear);
        Assert.Equal(5, session.TargetYears);
        Assert.Empty(session.Years);

        Assert.True(session.TryAdvance(out var yearOne));
        Assert.NotNull(yearOne);
        Assert.Equal(1, yearOne!.Year);
        Assert.Equal(1, session.CurrentYear);
        Assert.Single(session.Years);
        Assert.False(session.IsCompleted);
        Assert.Null(session.FinalHistory);
    }

    private static string Fingerprint(GeneratedWorldHistory history)
    {
        var sb = new StringBuilder();
        sb.Append(history.SimulatedYears).Append('|');

        foreach (var civ in history.Civilizations.OrderBy(civ => civ.Id, StringComparer.Ordinal))
        {
            sb.Append(civ.Id).Append(':')
              .Append(civ.Name).Append(':')
              .Append(civ.Capital.X).Append(',').Append(civ.Capital.Y).Append(':')
              .Append(civ.Territory.Count).Append(';');
        }

        foreach (var site in history.Sites.OrderBy(site => site.Id, StringComparer.Ordinal))
        {
            sb.Append(site.Id).Append(':')
              .Append(site.OwnerCivilizationId).Append(':')
              .Append(site.Kind).Append(':')
              .Append(site.Location.X).Append(',').Append(site.Location.Y).Append(';');
        }

        foreach (var road in history.Roads.OrderBy(road => road.Id, StringComparer.Ordinal))
        {
            sb.Append(road.Id).Append(':')
              .Append(road.FromSiteId).Append("->").Append(road.ToSiteId).Append(':')
              .Append(road.Path.Count).Append(';');
        }

        foreach (var evt in history.Events.Take(50))
        {
            sb.Append(evt.Year).Append(':')
              .Append(evt.Type).Append(':')
              .Append(evt.PrimaryCivilizationId).Append(':')
              .Append(evt.SecondaryCivilizationId).Append(';');
        }

        return sb.ToString();
    }
}

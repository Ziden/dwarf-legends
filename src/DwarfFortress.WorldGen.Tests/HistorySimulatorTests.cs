using System.Text;
using DwarfFortress.WorldGen.Config;
using DwarfFortress.WorldGen.Content;
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
    public void Simulate_Produces_Figures_And_Households_With_Valid_References()
    {
        var world = new WorldLayerGenerator().Generate(seed: 1207, width: 64, height: 64);
        var history = new HistorySimulator().Simulate(world, seed: 8911, simulatedYearsOverride: 24);

        Assert.NotEmpty(history.Households);
        Assert.NotEmpty(history.Figures);

        var sitesById = history.Sites.ToDictionary(site => site.Id, StringComparer.OrdinalIgnoreCase);
        var householdsById = history.Households.ToDictionary(household => household.Id, StringComparer.OrdinalIgnoreCase);
        var figuresById = history.Figures.ToDictionary(figure => figure.Id, StringComparer.OrdinalIgnoreCase);

        Assert.All(history.Households, household =>
        {
            Assert.True(sitesById.ContainsKey(household.HomeSiteId), $"Household {household.Id} references unknown site {household.HomeSiteId}.");
            Assert.NotEmpty(household.MemberFigureIds);
            Assert.All(household.MemberFigureIds, memberId => Assert.True(figuresById.ContainsKey(memberId), $"Household {household.Id} references unknown figure {memberId}."));
        });

        Assert.All(history.Figures, figure =>
        {
            Assert.True(householdsById.ContainsKey(figure.HouseholdId), $"Figure {figure.Id} references unknown household {figure.HouseholdId}.");
            Assert.True(sitesById.ContainsKey(figure.BirthSiteId), $"Figure {figure.Id} references unknown birth site {figure.BirthSiteId}.");
            Assert.True(sitesById.ContainsKey(figure.CurrentSiteId), $"Figure {figure.Id} references unknown current site {figure.CurrentSiteId}.");
            Assert.Contains(figure.Id, householdsById[figure.HouseholdId].MemberFigureIds);
            Assert.True(figure.IsAlive);
            Assert.False(string.IsNullOrWhiteSpace(figure.Name));
        });

        Assert.Contains(history.Figures, figure =>
            figure.IsFounder &&
            string.Equals(figure.SpeciesDefId, "dwarf", StringComparison.OrdinalIgnoreCase));
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
            var householdIds = year.Households.Select(household => household.HouseholdId).ToHashSet(StringComparer.OrdinalIgnoreCase);
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

            Assert.All(year.Households, household => Assert.Contains(household.HomeSiteId, siteIds));
            Assert.All(year.Figures, figure =>
            {
                Assert.Contains(figure.CurrentSiteId, siteIds);
                Assert.Contains(figure.HouseholdId, householdIds);
            });
        }
    }

    [Fact]
    public void SimulateTimeline_Tracks_Site_Population_History()
    {
        var world = new WorldLayerGenerator().Generate(seed: 1467, width: 48, height: 48);
        var timeline = new HistorySimulator().SimulateTimeline(world, seed: 5017, simulatedYearsOverride: 18);

        Assert.NotEmpty(timeline.FinalHistory.SitePopulations);

        var siteIds = timeline.FinalHistory.Sites
            .Select(site => site.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(timeline.FinalHistory.SitePopulations, record =>
        {
            Assert.Contains(record.SiteId, siteIds);
            Assert.InRange(record.Year, 0, timeline.FinalHistory.SimulatedYears);
            Assert.True(record.Population >= record.HouseholdCount);
            Assert.True(record.MilitaryCount + record.CraftCount + record.AgrarianCount + record.MiningCount <= record.Population);
            Assert.InRange(record.Prosperity, 0f, 1f);
            Assert.InRange(record.Security, 0f, 1f);
        });

        foreach (var year in timeline.Years)
        {
            Assert.Equal(year.Sites.Count, year.SitePopulations.Count);
            Assert.All(year.SitePopulations, record =>
            {
                Assert.Equal(year.Year, record.Year);
                Assert.Contains(record.SiteId, siteIds);
            });
        }

        var finalSnapshotPopulations = timeline.Years[^1].SitePopulations
            .OrderBy(record => record.SiteId, StringComparer.Ordinal)
            .ToArray();
        var finalHistoryPopulations = timeline.FinalHistory.SitePopulations
            .Where(record => record.Year == timeline.FinalHistory.SimulatedYears)
            .OrderBy(record => record.SiteId, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(finalSnapshotPopulations.Select(record => record.SiteId), finalHistoryPopulations.Select(record => record.SiteId));
        Assert.Equal(finalSnapshotPopulations.Select(record => record.Population), finalHistoryPopulations.Select(record => record.Population));
        Assert.Equal(finalSnapshotPopulations.Select(record => record.HouseholdCount), finalHistoryPopulations.Select(record => record.HouseholdCount));
    }

    [Fact]
    public void GeneratedWorldHistoryProjector_FromSnapshot_Preserves_Characters_And_Uses_Baseline_Metadata()
    {
        var world = new WorldLayerGenerator().Generate(seed: 1493, width: 48, height: 48);
        var timeline = new HistorySimulator().SimulateTimeline(world, seed: 6127, simulatedYearsOverride: 18);

        var midSnapshot = timeline.Years[8];
        var projectedMidHistory = GeneratedWorldHistoryProjector.FromSnapshot(midSnapshot, seed: 6127);

        Assert.NotEmpty(projectedMidHistory.Households);
        Assert.NotEmpty(projectedMidHistory.Figures);
        Assert.Equal(midSnapshot.Figures.Count, projectedMidHistory.Figures.Count);
        Assert.Equal(midSnapshot.Households.Count, projectedMidHistory.Households.Count);
        Assert.Equal(midSnapshot.SitePopulations.Count, projectedMidHistory.SitePopulations.Count);
        Assert.Equal(midSnapshot.Events.Count, projectedMidHistory.Events.Count);

        var projectedFigureIds = projectedMidHistory.Figures
            .Select(figure => figure.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(projectedMidHistory.Households, household =>
            Assert.All(household.MemberFigureIds, memberId => Assert.Contains(memberId, projectedFigureIds)));

        Assert.Contains(timeline.FinalHistory.Figures, figure => figure.LaborIds.Count > 0);
        var baselineFigure = timeline.FinalHistory.Figures.First(figure => figure.LaborIds.Count > 0);
        var projectedFinalHistory = GeneratedWorldHistoryProjector.FromSnapshot(timeline.Years[^1], seed: 6127, timeline.FinalHistory);
        var projectedFigure = Assert.Single(projectedFinalHistory.Figures.Where(figure => string.Equals(figure.Id, baselineFigure.Id, StringComparison.OrdinalIgnoreCase)));

        Assert.Equal(baselineFigure.LaborIds, projectedFigure.LaborIds);
        Assert.Equal(
            baselineFigure.SkillLevels.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => $"{entry.Key}:{entry.Value}"),
            projectedFigure.SkillLevels.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => $"{entry.Key}:{entry.Value}"));
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

    [Fact]
    public void Simulate_Uses_Configured_HistoryFigureContent()
    {
        const string json =
            """
            {
              "geologyProfiles": [
                {
                  "id": "custom_history_geology",
                  "seedSalt": 808,
                  "aquiferDepthFraction": 0.12,
                  "layers": [
                    { "rockTypeId": "granite", "thicknessMin": 2, "thicknessMax": 4 }
                  ]
                }
              ],
              "historyFigures": {
                "professionProfiles": [
                  {
                    "id": "scribe",
                    "laborIds": ["hauling"],
                    "skillLevels": { "writing": 4 }
                  }
                ],
                "professionSelectionRules": [
                  {
                    "speciesDefId": "dwarf",
                    "memberIndex": 0,
                    "founderBias": true,
                    "professionIds": ["scribe"]
                  }
                ],
                "defaultProfessionIds": ["scribe"],
                "defaultNonDwarfProfessionIds": ["scribe"],
                "defaultNamePool": ["Archivist"],
                "speciesNamePools": [
                  { "speciesDefId": "dwarf", "names": ["Led", "Deler"] }
                ]
              }
            }
            """;

        var catalog = WorldGenContentCatalog.FromConfig(WorldGenContentConfigLoader.LoadFromJson(json));
        var world = new WorldLayerGenerator().Generate(seed: 2141, width: 48, height: 48);
        var history = new HistorySimulator(catalog).Simulate(world, seed: 9403, simulatedYearsOverride: 8);

        Assert.NotEmpty(history.Figures);
        Assert.All(history.Figures, figure => Assert.Equal("scribe", figure.ProfessionId));

        var dwarfNames = history.Figures
            .Where(figure => string.Equals(figure.SpeciesDefId, "dwarf", StringComparison.OrdinalIgnoreCase))
            .Select(figure => figure.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(dwarfNames);
        Assert.All(dwarfNames, name => Assert.Contains(name, new[] { "Led", "Deler" }));
    }

    [Fact]
    public void Simulate_Uses_SharedCreatureHistoryContent()
    {
        const string json =
            """
            {
              "geologyProfiles": [
                {
                  "id": "custom_history_geology",
                  "seedSalt": 808,
                  "aquiferDepthFraction": 0.12,
                  "layers": [
                    { "rockTypeId": "granite", "thicknessMin": 2, "thicknessMax": 4 }
                  ]
                }
              ]
            }
            """;

        var content = new MemoryContentFileSource();
        content.AddFile("data/Content/Core/materials/granite.json", """
            { "id": "granite", "displayName": "Granite", "tags": ["stone"] }
            """);
        content.AddFile("data/Content/Game/creatures/sapients/dwarf/creature.json", """
            {
              "id": "dwarf",
              "displayName": "Dwarf",
              "tags": ["sapient", "playable"],
              "history": {
                "figureNamePool": ["Led", "Deler"],
                "defaultProfessionIds": ["hauler"],
                "professionRules": [
                  { "memberIndex": 0, "founderBias": true, "professionIds": ["crafter"] }
                ]
              }
            }
            """);
        content.AddFile("data/Content/Game/creatures/hostile/goblin/creature.json", """
            {
              "id": "goblin",
              "displayName": "Goblin",
              "tags": ["hostile"],
              "history": {
                "figureNamePool": ["Snaga", "Ghash"],
                "defaultProfessionIds": ["militia"]
              }
            }
            """);

        var shared = SharedContentCatalogLoader.Load(content);
        var catalog = WorldGenContentCatalog.FromConfig(WorldGenContentConfigLoader.LoadFromJson(json), shared);
        var world = new WorldLayerGenerator().Generate(seed: 2141, width: 48, height: 48);
        var history = new HistorySimulator(catalog).Simulate(world, seed: 9403, simulatedYearsOverride: 8);

        var dwarfFigures = history.Figures
            .Where(figure => string.Equals(figure.SpeciesDefId, "dwarf", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(dwarfFigures);
        Assert.All(dwarfFigures, figure => Assert.Contains(figure.Name, new[] { "Led", "Deler" }));
        Assert.Contains(dwarfFigures, figure => figure.ProfessionId == "crafter");
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

                foreach (var population in history.SitePopulations
                                         .OrderBy(record => record.Year)
                                         .ThenBy(record => record.SiteId, StringComparer.Ordinal))
                {
                        sb.Append(population.SiteId).Append('@').Append(population.Year).Append(':')
                            .Append(population.Population).Append(':')
                            .Append(population.HouseholdCount).Append(':')
                            .Append(population.MilitaryCount).Append(';');
                }

        foreach (var road in history.Roads.OrderBy(road => road.Id, StringComparer.Ordinal))
        {
            sb.Append(road.Id).Append(':')
              .Append(road.FromSiteId).Append("->").Append(road.ToSiteId).Append(':')
              .Append(road.Path.Count).Append(';');
        }

                foreach (var household in history.Households.OrderBy(household => household.Id, StringComparer.Ordinal))
                {
                        sb.Append(household.Id).Append(':')
                            .Append(household.HomeSiteId).Append(':')
                            .Append(household.MemberFigureIds.Count).Append(';');
                }

                foreach (var figure in history.Figures.OrderBy(figure => figure.Id, StringComparer.Ordinal).Take(50))
                {
                        sb.Append(figure.Id).Append(':')
                            .Append(figure.CivilizationId).Append(':')
                            .Append(figure.CurrentSiteId).Append(':')
                            .Append(figure.ProfessionId).Append(';');
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

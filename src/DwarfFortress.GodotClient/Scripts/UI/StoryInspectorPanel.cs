using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;
using DwarfFortress.WorldGen.History;
using Godot;

namespace DwarfFortress.GodotClient.UI;


public partial class StoryInspectorPanel : PanelContainer
{
    private sealed record StoryInspectorView(
        string Title,
        string Subtitle,
        string Source,
        string Overview,
        string Civilizations,
        string Sites,
        string Characters,
        string Events);

    private Label? _titleLabel;
    private Label? _subtitleLabel;
    private Label? _sourceLabel;
    private RichTextLabel? _overviewText;
    private RichTextLabel? _civilizationsText;
    private RichTextLabel? _sitesText;
    private RichTextLabel? _charactersText;
    private RichTextLabel? _eventsText;

    public string DebugTitleText => _titleLabel?.Text ?? string.Empty;
    public string DebugSourceText => _sourceLabel?.Text ?? string.Empty;
    public string DebugOverviewText => _overviewText?.Text ?? string.Empty;
    public string DebugEventsText => _eventsText?.Text ?? string.Empty;

    public override void _Ready()
    {
        BuildUi();
        Hide();
    }

    public void ShowGameplayStory(GameSimulation? simulation)
    {
        var runtimeHistory = simulation?.Context.TryGet<WorldHistoryRuntimeService>();
        var snapshot = runtimeHistory?.Snapshot;
        if (snapshot is null)
        {
            ApplyView(new StoryInspectorView(
                Title: "Story Inspector",
                Subtitle: "Gameplay story unavailable",
                Source: "Canonical runtime history",
                Overview: "Story data has not been generated for this fortress yet.",
                Civilizations: "No civilizations recorded.",
                Sites: "No sites recorded.",
                Characters: "No characters recorded.",
                Events: "No events recorded."));
            Show();
            MoveToFront();
            return;
        }

        ApplyView(BuildGameplayView(snapshot));
        Show();
        MoveToFront();
    }

    public void ShowWorldgenStory(
        GeneratedWorldHistory? history,
        HistoryYearSnapshot? snapshot,
        int generatedYears,
        int targetYears)
    {
        if (history is null)
        {
            ApplyView(new StoryInspectorView(
                Title: "Story Inspector",
                Subtitle: "Worldgen story unavailable",
                Source: "Worldgen snapshot projection",
                Overview: "Generate or advance history to inspect the world story.",
                Civilizations: "No civilizations recorded.",
                Sites: "No sites recorded.",
                Characters: "No characters recorded.",
                Events: "No events recorded."));
            Show();
            MoveToFront();
            return;
        }

        ApplyView(BuildWorldgenView(history, snapshot, generatedYears, targetYears));
        Show();
        MoveToFront();
    }

    private void BuildUi()
    {
        var margin = new MarginContainer
        {
            Name = "Margin",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var layout = new VBoxContainer
        {
            Name = "Layout",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        layout.AddThemeConstantOverride("separation", 8);
        margin.AddChild(layout);

        var header = new HBoxContainer { Name = "Header" };
        header.AddThemeConstantOverride("separation", 8);
        layout.AddChild(header);

        var headerCopy = new VBoxContainer
        {
            Name = "HeaderCopy",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        headerCopy.AddThemeConstantOverride("separation", 2);
        header.AddChild(headerCopy);

        _titleLabel = new Label { Text = "Story Inspector" };
        _titleLabel.AddThemeFontSizeOverride("font_size", 18);
        headerCopy.AddChild(_titleLabel);

        _subtitleLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.78f, 0.78f, 0.78f),
        };
        headerCopy.AddChild(_subtitleLabel);

        _sourceLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.60f, 0.78f, 0.90f),
        };
        headerCopy.AddChild(_sourceLabel);

        var closeButton = new Button
        {
            Text = "Close",
            TooltipText = "Hide the story inspector.",
        };
        closeButton.Pressed += Hide;
        header.AddChild(closeButton);

        layout.AddChild(new HSeparator());

        var tabs = new TabContainer
        {
            Name = "Tabs",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        layout.AddChild(tabs);

        _overviewText = CreateTextTab(tabs, "Overview");
        _civilizationsText = CreateTextTab(tabs, "Civilizations");
        _sitesText = CreateTextTab(tabs, "Sites");
        _charactersText = CreateTextTab(tabs, "Characters");
        _eventsText = CreateTextTab(tabs, "Events");
    }

    private void ApplyView(StoryInspectorView view)
    {
        _titleLabel!.Text = view.Title;
        _subtitleLabel!.Text = view.Subtitle;
        _sourceLabel!.Text = view.Source;
        _overviewText!.Text = view.Overview;
        _civilizationsText!.Text = view.Civilizations;
        _sitesText!.Text = view.Sites;
        _charactersText!.Text = view.Characters;
        _eventsText!.Text = view.Events;
    }

    private static RichTextLabel CreateTextTab(TabContainer tabs, string name)
    {
        var text = new RichTextLabel
        {
            Name = name,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            ScrollActive = true,
            SelectionEnabled = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            BbcodeEnabled = false,
        };
        tabs.AddChild(text);
        return text;
    }

    private static StoryInspectorView BuildGameplayView(WorldHistoryRuntimeSnapshot snapshot)
    {
        var summary = snapshot.EmbarkSummary;
        var civilizationsById = snapshot.Civilizations.ToDictionary(civ => civ.Id, StringComparer.OrdinalIgnoreCase);
        var sitesById = snapshot.Sites.ToDictionary(site => site.Id, StringComparer.OrdinalIgnoreCase);
        var householdsById = snapshot.Households.ToDictionary(household => household.Id, StringComparer.OrdinalIgnoreCase);

        civilizationsById.TryGetValue(summary.OwnerCivilizationId ?? string.Empty, out var ownerCivilization);
        sitesById.TryGetValue(summary.PrimarySiteId ?? string.Empty, out var primarySite);

        var overview = new StringBuilder(1024);
        overview.AppendLine($"Biome: {FormatToken(summary.BiomeId)}");
        overview.AppendLine($"Simulated years: {summary.SimulatedYears}");
        overview.AppendLine($"Owner civilization: {summary.OwnerCivilizationName ?? ownerCivilization?.Name ?? "-"}");
        if (!string.IsNullOrWhiteSpace(summary.OwnerCivilizationId))
            overview.AppendLine($"Owner id: {summary.OwnerCivilizationId}");
        overview.AppendLine($"Primary site: {summary.PrimarySiteName ?? primarySite?.Name ?? "-"}");
        overview.AppendLine($"Primary site kind: {FormatToken(summary.PrimarySiteKind)}");
        overview.AppendLine($"Population: {summary.PrimarySitePopulation}");
        overview.AppendLine($"Households: {summary.PrimarySiteHouseholdCount}");
        overview.AppendLine($"Militia: {summary.PrimarySiteMilitaryCount}");
        overview.AppendLine();
        overview.AppendLine("Story scope");
        overview.AppendLine($"- Civilizations: {snapshot.Civilizations.Count}");
        overview.AppendLine($"- Sites: {snapshot.Sites.Count}");
        overview.AppendLine($"- Households: {snapshot.Households.Count}");
        overview.AppendLine($"- Figures: {snapshot.Figures.Count}");

        var civilizations = new StringBuilder(2048);
        if (snapshot.Civilizations.Count == 0)
        {
            civilizations.Append("No civilizations recorded.");
        }
        else
        {
            foreach (var civ in snapshot.Civilizations
                         .OrderByDescending(civ => civ.Influence)
                         .ThenByDescending(civ => civ.Militarism)
                         .ThenBy(civ => civ.Name, StringComparer.OrdinalIgnoreCase)
                         .Take(18))
            {
                civilizations.AppendLine($"{civ.Name} ({civ.Id})");
                civilizations.AppendLine(
                    $"  Unit {FormatToken(civ.PrimaryUnitDefId)} | Influence {civ.Influence:F2} | Militarism {civ.Militarism:F2} | Trade {civ.TradeFocus:F2} | {(civ.IsHostile ? "hostile" : "non-hostile")}");
                civilizations.AppendLine();
            }
        }

        var sites = new StringBuilder(3072);
        if (snapshot.Sites.Count == 0)
        {
            sites.Append("No sites recorded.");
        }
        else
        {
            foreach (var site in snapshot.Sites
                         .OrderByDescending(site => site.Population)
                         .ThenByDescending(site => site.Development)
                         .ThenBy(site => site.Name, StringComparer.OrdinalIgnoreCase)
                         .Take(20))
            {
                var ownerName = civilizationsById.TryGetValue(site.OwnerCivilizationId, out var owner)
                    ? owner.Name
                    : site.OwnerCivilizationId;
                sites.AppendLine($"{site.Name} ({FormatToken(site.Kind)})");
                sites.AppendLine($"  Owner {ownerName} | Location ({site.WorldX},{site.WorldY})");
                sites.AppendLine(
                    $"  Population {site.Population} | Households {site.HouseholdCount} | Militia {site.MilitaryCount}");
                sites.AppendLine(
                    $"  Development {site.Development:F2} | Security {site.Security:F2} | Craft {site.CraftCount} | Agrarian {site.AgrarianCount} | Mining {site.MiningCount}");
                sites.AppendLine();
            }
        }

        var characters = new StringBuilder(4096);
        if (snapshot.Figures.Count == 0)
        {
            characters.Append("No characters recorded.");
        }
        else
        {
            var aliveCount = snapshot.Figures.Count(figure => figure.IsAlive);
            var founderCount = snapshot.Figures.Count(figure => figure.IsFounder);
            characters.AppendLine($"Figures: {snapshot.Figures.Count} total | {aliveCount} alive | {founderCount} founders");
            characters.AppendLine($"Households: {snapshot.Households.Count}");
            characters.AppendLine();
            characters.AppendLine("Figures");

            foreach (var figure in snapshot.Figures
                         .OrderByDescending(figure => figure.IsFounder)
                         .ThenByDescending(figure => figure.IsAlive)
                         .ThenByDescending(figure => figure.SkillLevels.Values.DefaultIfEmpty(0).Max())
                         .ThenBy(figure => figure.Name, StringComparer.OrdinalIgnoreCase)
                         .Take(20))
            {
                var siteName = ResolveRuntimeSiteName(sitesById, figure.CurrentSiteId, figure.BirthSiteId);
                var householdName = householdsById.TryGetValue(figure.HouseholdId, out var household)
                    ? household.Name
                    : "-";
                var topSkill = figure.SkillLevels.Count == 0
                    ? "none"
                    : string.Join(", ",
                        figure.SkillLevels
                            .OrderByDescending(entry => entry.Value)
                            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                            .Take(2)
                            .Select(entry => $"{FormatToken(entry.Key)} {entry.Value}"));
                characters.AppendLine(
                    $"- {figure.Name} | {FormatToken(figure.ProfessionId)} | {(figure.IsAlive ? "alive" : "dead")}{(figure.IsFounder ? " | founder" : string.Empty)}");
                characters.AppendLine($"  Site {siteName} | Household {householdName} | Born Y{figure.BirthYear}");
                characters.AppendLine($"  Skills {topSkill}");
            }

            characters.AppendLine();
            characters.AppendLine("Households");
            foreach (var household in snapshot.Households
                         .OrderByDescending(household => household.MemberFigureIds.Count)
                         .ThenBy(household => household.Name, StringComparer.OrdinalIgnoreCase)
                         .Take(16))
            {
                var siteName = sitesById.TryGetValue(household.HomeSiteId, out var householdSite)
                    ? householdSite.Name
                    : household.HomeSiteId;
                characters.AppendLine($"- {household.Name} | Members {household.MemberFigureIds.Count} | Home {siteName}");
            }
        }

        var events = new StringBuilder(1024);
        if (summary.RecentEvents.Length == 0)
        {
            events.Append("No recent events recorded.");
        }
        else
        {
            foreach (var entry in summary.RecentEvents)
                events.AppendLine(entry);
        }

        var subtitle = summary.SimulatedYears > 0
            ? $"{summary.RegionName} | {summary.SimulatedYears} simulated years"
            : summary.RegionName;

        return new StoryInspectorView(
            Title: string.IsNullOrWhiteSpace(summary.RegionName) ? "Story Inspector" : summary.RegionName,
            Subtitle: subtitle,
            Source: "Canonical runtime history",
            Overview: overview.ToString().TrimEnd(),
            Civilizations: civilizations.ToString().TrimEnd(),
            Sites: sites.ToString().TrimEnd(),
            Characters: characters.ToString().TrimEnd(),
            Events: events.ToString().TrimEnd());
    }

    private static StoryInspectorView BuildWorldgenView(
        GeneratedWorldHistory history,
        HistoryYearSnapshot? snapshot,
        int generatedYears,
        int targetYears)
    {
        var civilizationsById = history.Civilizations.ToDictionary(civ => civ.Id, StringComparer.OrdinalIgnoreCase);
        var sitesById = history.Sites.ToDictionary(site => site.Id, StringComparer.OrdinalIgnoreCase);
        var latestPopulations = BuildLatestSitePopulations(history);
        var householdsById = history.Households.ToDictionary(household => household.Id, StringComparer.OrdinalIgnoreCase);

        var overview = new StringBuilder(1024);
        var yearLabel = snapshot is null
            ? $"Finalized story | {history.SimulatedYears} simulated years"
            : $"Year {snapshot.Year}/{Math.Max(targetYears, snapshot.Year)} | Generated {generatedYears}/{Math.Max(targetYears, generatedYears)}";
        overview.AppendLine(yearLabel);
        overview.AppendLine("Story scope");
        overview.AppendLine($"- Civilizations: {history.Civilizations.Count}");
        overview.AppendLine($"- Sites: {history.Sites.Count}");
        overview.AppendLine($"- Households: {history.Households.Count}");
        overview.AppendLine($"- Figures: {history.Figures.Count}");
        overview.AppendLine($"- Roads: {history.Roads.Count}");
        overview.AppendLine($"- Events: {history.Events.Count}");
        if (snapshot is not null)
        {
            overview.AppendLine();
            overview.AppendLine($"Current-year events: {snapshot.Events.Count}");
            overview.AppendLine($"Average prosperity: {snapshot.AverageProsperity:F2}");
            overview.AppendLine($"Average threat: {snapshot.AverageThreat:F2}");
        }

        var civilizations = new StringBuilder(2048);
        if (history.Civilizations.Count == 0)
        {
            civilizations.Append("No civilizations recorded.");
        }
        else
        {
            foreach (var civ in history.Civilizations
                         .OrderByDescending(civ => civ.Territory.Count)
                         .ThenByDescending(civ => civ.Influence)
                         .ThenBy(civ => civ.Name, StringComparer.OrdinalIgnoreCase)
                         .Take(18))
            {
                civilizations.AppendLine($"{civ.Name} ({civ.Id})");
                civilizations.AppendLine(
                    $"  Territory {civ.Territory.Count} | Capital ({civ.Capital.X},{civ.Capital.Y}) | Influence {civ.Influence:F2} | Militarism {civ.Militarism:F2} | Trade {civ.TradeFocus:F2} | {(civ.IsHostile ? "hostile" : "non-hostile")}");
                civilizations.AppendLine();
            }
        }

        var sites = new StringBuilder(3072);
        if (history.Sites.Count == 0)
        {
            sites.Append("No sites recorded.");
        }
        else
        {
            foreach (var site in history.Sites
                         .OrderByDescending(site => latestPopulations.TryGetValue(site.Id, out var population) ? population.Population : 0)
                         .ThenByDescending(site => site.Development)
                         .ThenBy(site => site.Name, StringComparer.OrdinalIgnoreCase)
                         .Take(20))
            {
                latestPopulations.TryGetValue(site.Id, out var population);
                var ownerName = civilizationsById.TryGetValue(site.OwnerCivilizationId, out var owner)
                    ? owner.Name
                    : site.OwnerCivilizationId;
                sites.AppendLine($"{site.Name} ({FormatToken(site.Kind)})");
                sites.AppendLine($"  Owner {ownerName} | Location ({site.Location.X},{site.Location.Y})");
                sites.AppendLine(
                    $"  Population {population?.Population ?? 0} | Households {population?.HouseholdCount ?? 0} | Militia {population?.MilitaryCount ?? 0}");
                sites.AppendLine($"  Development {site.Development:F2} | Security {site.Security:F2}");
                sites.AppendLine();
            }
        }

        var characters = new StringBuilder(4096);
        if (history.Figures.Count == 0)
        {
            characters.Append("No characters recorded.");
        }
        else
        {
            var aliveCount = history.Figures.Count(figure => figure.IsAlive);
            var founderCount = history.Figures.Count(figure => figure.IsFounder);
            characters.AppendLine($"Figures: {history.Figures.Count} total | {aliveCount} alive | {founderCount} founders");
            characters.AppendLine($"Households: {history.Households.Count}");
            characters.AppendLine();
            characters.AppendLine("Figures");

            foreach (var figure in history.Figures
                         .OrderByDescending(figure => figure.IsFounder)
                         .ThenByDescending(figure => figure.IsAlive)
                         .ThenBy(figure => figure.Name, StringComparer.OrdinalIgnoreCase)
                         .Take(20))
            {
                var siteName = ResolveGeneratedSiteName(sitesById, figure.CurrentSiteId, figure.BirthSiteId);
                var householdName = householdsById.TryGetValue(figure.HouseholdId, out var household)
                    ? household.Name
                    : "-";
                var topSkill = figure.SkillLevels.Count == 0
                    ? "none"
                    : string.Join(", ",
                        figure.SkillLevels
                            .OrderByDescending(entry => entry.Value)
                            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                            .Take(2)
                            .Select(entry => $"{FormatToken(entry.Key)} {entry.Value}"));
                characters.AppendLine(
                    $"- {figure.Name} | {FormatToken(figure.ProfessionId)} | {(figure.IsAlive ? "alive" : $"dead Y{figure.DeathYear}")}{(figure.IsFounder ? " | founder" : string.Empty)}");
                characters.AppendLine($"  Site {siteName} | Household {householdName} | Born Y{figure.BirthYear}");
                characters.AppendLine($"  Skills {topSkill}");
            }

            characters.AppendLine();
            characters.AppendLine("Households");
            foreach (var household in history.Households
                         .OrderByDescending(household => household.MemberFigureIds.Count)
                         .ThenBy(household => household.Name, StringComparer.OrdinalIgnoreCase)
                         .Take(16))
            {
                var siteName = sitesById.TryGetValue(household.HomeSiteId, out var householdSite)
                    ? householdSite.Name
                    : household.HomeSiteId;
                characters.AppendLine($"- {household.Name} | Members {household.MemberFigureIds.Count} | Home {siteName}");
            }
        }

        var events = new StringBuilder(2048);
        if (history.Events.Count == 0)
        {
            events.Append("No events recorded.");
        }
        else
        {
            foreach (var evt in history.Events
                         .OrderByDescending(evt => evt.Year)
                         .ThenBy(evt => evt.Type, StringComparer.OrdinalIgnoreCase)
                         .Take(24))
            {
                events.AppendLine($"Y{evt.Year} [{evt.Type}] {evt.Summary}");
            }
        }

        return new StoryInspectorView(
            Title: "Story Inspector",
            Subtitle: snapshot is null
                ? $"Final worldgen story | {history.SimulatedYears} simulated years"
                : $"Worldgen year {snapshot.Year}/{Math.Max(targetYears, snapshot.Year)}",
            Source: snapshot is null ? "Worldgen final history" : "Worldgen snapshot projection",
            Overview: overview.ToString().TrimEnd(),
            Civilizations: civilizations.ToString().TrimEnd(),
            Sites: sites.ToString().TrimEnd(),
            Characters: characters.ToString().TrimEnd(),
            Events: events.ToString().TrimEnd());
    }

    private static Dictionary<string, SitePopulationRecord> BuildLatestSitePopulations(GeneratedWorldHistory history)
    {
        if (history.SitePopulations.Count == 0)
            return new Dictionary<string, SitePopulationRecord>(StringComparer.OrdinalIgnoreCase);

        return history.SitePopulations
            .GroupBy(record => record.SiteId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(record => record.Year).First(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveRuntimeSiteName(
        IReadOnlyDictionary<string, RuntimeHistorySiteSnapshot> sitesById,
        string? currentSiteId,
        string? fallbackSiteId)
    {
        if (!string.IsNullOrWhiteSpace(currentSiteId) && sitesById.TryGetValue(currentSiteId, out var currentSite))
            return currentSite.Name;
        if (!string.IsNullOrWhiteSpace(fallbackSiteId) && sitesById.TryGetValue(fallbackSiteId, out var fallbackSite))
            return fallbackSite.Name;

        return !string.IsNullOrWhiteSpace(currentSiteId)
            ? currentSiteId
            : string.IsNullOrWhiteSpace(fallbackSiteId) ? "-" : fallbackSiteId;
    }

    private static string ResolveGeneratedSiteName(
        IReadOnlyDictionary<string, SiteRecord> sitesById,
        string? currentSiteId,
        string? fallbackSiteId)
    {
        if (!string.IsNullOrWhiteSpace(currentSiteId) && sitesById.TryGetValue(currentSiteId, out var currentSite))
            return currentSite.Name;
        if (!string.IsNullOrWhiteSpace(fallbackSiteId) && sitesById.TryGetValue(fallbackSiteId, out var fallbackSite))
            return fallbackSite.Name;

        return !string.IsNullOrWhiteSpace(currentSiteId)
            ? currentSiteId
            : string.IsNullOrWhiteSpace(fallbackSiteId) ? "-" : fallbackSiteId;
    }

    private static string FormatToken(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Replace('_', ' ');
}
using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Systems;
using Godot;

namespace DwarfFortress.GodotClient.UI;


public partial class DwarfPanel : PanelContainer
{
	private Label? _nameLabel;
	private Label? _summaryLabel;
	private Label? _metaLabel;
	private TabContainer? _tabs;
	private WorldQuerySystem? _query;
	private VBoxContainer? _overviewBox;
	private VBoxContainer? _loreBox;
	private VBoxContainer? _vitalsBox;
	private VBoxContainer? _thoughtsBox;
	private VBoxContainer? _skillsBox;
	private VBoxContainer? _laborsBox;
	private VBoxContainer? _statsBox;
	private VBoxContainer? _eventLogBox;
	private VBoxContainer? _inventoryBox;
	private VBoxContainer? _attributesBox;
	private Action<Vec3i>? _jumpToTile;
	private Func<EventLogLinkTarget, bool>? _jumpToLinkedTarget;
	private string? _currentSelectionKey;

	public override void _Ready()
	{
		_nameLabel = GetNode<Label>("%NameLabel");
		_summaryLabel = GetNode<Label>("%SummaryLabel");
		_metaLabel = GetNode<Label>("%MetaLabel");
		_tabs = GetNode<TabContainer>("%Tabs");
		_overviewBox = GetNode<VBoxContainer>("%OverviewBox");
		_loreBox = GetNode<VBoxContainer>("%LoreBox");
		_vitalsBox = GetNode<VBoxContainer>("%VitalsBox");
		_thoughtsBox = GetNode<VBoxContainer>("%ThoughtsBox");
		_skillsBox = GetNode<VBoxContainer>("%SkillsBox");
		_laborsBox = GetNode<VBoxContainer>("%LaborsBox");
		_statsBox = GetNode<VBoxContainer>("%StatsBox");
		_eventLogBox = GetNode<VBoxContainer>("%EventLogBox");
		_inventoryBox = GetNode<VBoxContainer>("%InventoryBox");
		_attributesBox = GetNode<VBoxContainer>("%AttributesBox");

		Hide();
	}

	public void Setup(GameSimulation simulation)
	{
		_query = simulation.Context.Get<WorldQuerySystem>();
	}

	public void SetTileNavigator(Action<Vec3i> jumpToTile) => _jumpToTile = jumpToTile;
	public void SetLinkedTargetNavigator(Func<EventLogLinkTarget, bool> jumpToLinkedTarget) => _jumpToLinkedTarget = jumpToLinkedTarget;

	public void ShowDwarf(DwarfView dwarf)
	{
		EnsureReady();
		bool selectionChanged = !string.Equals(_currentSelectionKey, $"dwarf:{dwarf.Id}", StringComparison.Ordinal);
		_currentSelectionKey = $"dwarf:{dwarf.Id}";

		_nameLabel!.Text = dwarf.Name;
		_summaryLabel!.Text = $"{FormatToken(dwarf.ProfessionId)}  |  Mood {FormatToken(dwarf.Mood.ToString())}  |  Happiness {dwarf.Happiness:0}%";
		_metaLabel!.Text = $"Position {FormatVec(dwarf.Position)}  |  Health {dwarf.CurrentHealth:0.#}/{dwarf.MaxHealth:0.#}  |  {(dwarf.IsConscious ? "Conscious" : "Unconscious")}";

		PopulateOverview(
			dwarf.Position,
			dwarf.CurrentJob is null ? null : $"Current job: {FormatToken(dwarf.CurrentJob.JobDefId)} ({dwarf.CurrentJob.Status})",
			dwarf.Substances,
			dwarf.Wounds);
		PopulateLore(dwarf);
		PopulateVitals(dwarf.Needs, dwarf.Wounds, dwarf.Substances);
		PopulateThoughts(dwarf.Thoughts);
		PopulateSkills(dwarf.Skills);
		PopulateLabors(dwarf.EnabledLabors);
		PopulateStats(dwarf.Stats, dwarf.Appearance);
		PopulateEventLog(dwarf.EventLog);
		PopulateInventory(dwarf.CarriedItems, dwarf.HauledItem);
		PopulateAttributes(dwarf.Attributes);

		if (selectionChanged)
			_tabs!.CurrentTab = 0;
		Show();
	}

	public void ShowCreature(CreatureView creature)
	{
		EnsureReady();
		bool selectionChanged = !string.Equals(_currentSelectionKey, $"creature:{creature.Id}", StringComparison.Ordinal);
		_currentSelectionKey = $"creature:{creature.Id}";

		_nameLabel!.Text = FormatToken(creature.DefId);
		_summaryLabel!.Text = $"Creature  |  {(creature.IsHostile ? "Hostile" : "Neutral")}  |  Health {creature.CurrentHealth:0.#}/{creature.MaxHealth:0.#}";
		_metaLabel!.Text = $"Position {FormatVec(creature.Position)}  |  {(creature.IsConscious ? "Conscious" : "Unconscious")}";

		PopulateOverview(
			creature.Position,
			null,
			creature.Substances,
			creature.Wounds);
		PopulateLore();
		PopulateVitals(creature.Needs, creature.Wounds, creature.Substances);
		PopulateThoughts(Array.Empty<ThoughtView>());
		PopulateSkills(Array.Empty<SkillView>());
		PopulateLabors(Array.Empty<string>());
		PopulateStats(creature.Stats, appearance: null);
		PopulateEventLog(creature.EventLog);
		PopulateInventory(creature.CarriedItems, creature.HauledItem);
		PopulateAttributes(Array.Empty<DwarfAttributeView>());

		if (selectionChanged)
			_tabs!.CurrentTab = 0;
		Show();
	}

	private void PopulateOverview(Vec3i position, string? currentJob, SubstanceView[] substances, WoundView[] wounds)
	{
		var box = _overviewBox!;
		ResetBox(box);
		AddJumpButton(box, position);
		box.AddChild(CreateBodyLabel($"Position: {FormatVec(position)}"));

		if (!string.IsNullOrWhiteSpace(currentJob))
			box.AddChild(CreateBodyLabel(currentJob));

		box.AddChild(CreateBodyLabel($"Active substances: {(substances.Length == 0 ? "none" : string.Join(", ", substances.Select(FormatSubstance)))}"));
		box.AddChild(CreateBodyLabel($"Wounds: {(wounds.Length == 0 ? "none" : string.Join(", ", wounds.Select(FormatWound)))}"));
	}

	private void PopulateLore(DwarfView? dwarf = null)
	{
		ResetBox(_loreBox);

		if (dwarf?.Provenance is null)
		{
			_loreBox!.AddChild(CreateMutedLabel(dwarf is null
				? "No detailed lore available for this creature."
				: "No historical origin recorded for this dwarf."));
			return;
		}

		var provenance = dwarf.Provenance;
		AddSection(_loreBox!, "Origin",
		[
			$"Historical figure: {FormatNamedValue(provenance.FigureName, provenance.FigureId)}",
			$"Household: {FormatNamedValue(provenance.HouseholdName, provenance.HouseholdId)}",
			$"Civilization: {FormatNamedValue(provenance.CivilizationName, provenance.CivilizationId)}",
			$"Origin site: {FormatNamedValue(provenance.OriginSiteName, provenance.OriginSiteId)}",
			$"Birth site: {FormatNamedValue(provenance.BirthSiteName, provenance.BirthSiteId)}",
			$"Migration wave: {FormatOptionalToken(provenance.MigrationWaveId)}",
			$"World tile: {FormatOptionalCoord(provenance.WorldX, provenance.WorldY)}",
			$"Region tile: {FormatOptionalCoord(provenance.RegionX, provenance.RegionY)}",
			$"World seed: {provenance.WorldSeed}",
		]);

		var lore = _query?.GetLoreSummary();
		if (lore is null)
			return;

		var embarkContextLines = new List<string>
		{
			$"Region: {FormatOptionalText(lore.RegionName)}",
			$"Biome: {FormatOptionalToken(lore.BiomeId)}",
			$"Territory owner: {FormatNamedValue(lore.OwnerCivilizationName, lore.OwnerCivilizationId)}",
			$"Primary site: {FormatNamedValue(lore.PrimarySiteName, lore.PrimarySiteId)}",
			$"Threat: {lore.Threat:0%}",
			$"Prosperity: {lore.Prosperity:0%}",
			$"Simulated years: {lore.SimulatedYears}",
			$"Source: {(lore.UsesCanonicalHistory ? "Canonical history" : "Legacy lore")}",
		};
		if (lore.PrimarySitePopulation is > 0)
			embarkContextLines.Add($"Primary site population: {lore.PrimarySitePopulation.Value}");
		if (lore.PrimarySiteHouseholdCount is > 0)
			embarkContextLines.Add($"Primary site households: {lore.PrimarySiteHouseholdCount.Value}");
		if (lore.PrimarySiteMilitaryCount is > 0)
			embarkContextLines.Add($"Primary site militia: {lore.PrimarySiteMilitaryCount.Value}");

		AddSection(_loreBox!, "Embark Context", embarkContextLines);

		AddSection(_loreBox!, "Recent History",
			lore.RecentEvents.Length == 0
				? ["No recent history entries."]
				: lore.RecentEvents);
	}

	private void PopulateVitals(NeedView[] needs, WoundView[] wounds, SubstanceView[] substances)
	{
		ResetBox(_vitalsBox);
		AddSection(_vitalsBox!, "Needs", needs.Length == 0 ? ["No tracked needs."] : needs.Select(FormatNeed));
		AddSection(_vitalsBox!, "Wounds", wounds.Length == 0 ? ["No wounds."] : wounds.Select(FormatWound));
		AddSection(_vitalsBox!, "Substances", substances.Length == 0 ? ["No active substances."] : substances.Select(FormatSubstance));
	}

	private void PopulateThoughts(ThoughtView[] thoughts)
	{
		ResetBox(_thoughtsBox);
		AddSection(_thoughtsBox!, "Thoughts", thoughts.Length == 0 ? ["No active thoughts."] : thoughts.Select(FormatThought));
	}

	private void PopulateSkills(SkillView[] skills)
	{
		ResetBox(_skillsBox);
		AddSection(_skillsBox!, "Skills", skills.Length == 0 ? ["No tracked skills."] : skills.Select(FormatSkill));
	}

	private void PopulateLabors(string[] labors)
	{
		ResetBox(_laborsBox);
		AddSection(_laborsBox!, "Enabled labors", labors.Length == 0 ? ["No labors listed."] : labors.Select(FormatToken));
	}

	private void PopulateStats(StatView[] stats, DwarfAppearanceView? appearance)
	{
		ResetBox(_statsBox);
		AddSection(_statsBox!, "Stats", stats.Length == 0 ? ["No stats available."] : stats.Select(stat => $"{FormatToken(stat.Id)}: {stat.Value:0.##}"));

		if (appearance is not null)
		{
			AddSection(_statsBox!, "Appearance",
			[
				$"Hair: {FormatToken(appearance.HairType)} ({FormatToken(appearance.HairColor)})",
				$"Beard: {FormatToken(appearance.BeardType)} ({FormatToken(appearance.BeardColor)})",
				$"Eyes: {FormatToken(appearance.EyeType)}",
				$"Face: {FormatToken(appearance.FaceType)}  |  Nose: {FormatToken(appearance.NoseType)}  |  Mouth: {FormatToken(appearance.MouthType)}",
			]);
		}
	}

	private void PopulateEventLog(EventLogEntryView[] eventLog)
	{
		ResetBox(_eventLogBox);

		if (eventLog.Length == 0)
		{
			_eventLogBox!.AddChild(CreateMutedLabel("No recent events."));
			return;
		}

		foreach (var entry in eventLog)
			_eventLogBox!.AddChild(CreateEventEntry(entry));
	}

	private void PopulateInventory(ItemView[] items, ItemView? hauledItem = null)
	{
		ResetBox(_inventoryBox);
		if (items.Length == 0 && hauledItem is null)
		{
			AddSection(_inventoryBox!, "Carried items", ["Nothing carried."]);
			return;
		}

		if (items.Length > 0)
		{
			var totalWeight = items.Sum(i => i.Weight);
			var lines = items.Select(FormatItem).Concat(new[] { $"Inventory weight: {totalWeight:F1} kg" });
			AddSection(_inventoryBox!, "Carried items", lines);
		}
		else
		{
			AddSection(_inventoryBox!, "Carried items", ["Inventory empty."]);
		}

		if (hauledItem is not null)
			AddSection(_inventoryBox!, "Hauled item", [FormatItem(hauledItem)]);
	}

	private void PopulateAttributes(DwarfAttributeView[] attributes)
	{
		ResetBox(_attributesBox);
		if (attributes.Length == 0)
		{
			_attributesBox!.AddChild(CreateMutedLabel("No attributes available."));
			return;
		}
		AddSection(_attributesBox!, "Attributes", attributes.Select(FormatAttribute));
	}

	private static void ResetBox(VBoxContainer? box)
	{
		if (box is null)
			return;

		foreach (Node child in box.GetChildren())
			child.QueueFree();
	}

	private void AddSection(VBoxContainer box, string title, IEnumerable<string> lines)
	{
		box.AddChild(CreateSectionHeader(title));
		foreach (var line in lines)
			box.AddChild(CreateBodyLabel(line));
	}

	private void AddJumpButton(VBoxContainer box, Vec3i position)
	{
		if (_jumpToTile is null)
			return;

		var button = new Button
		{
			Text = $"Jump to {position.X}, {position.Y}, z{position.Z}",
			Alignment = HorizontalAlignment.Left,
			FocusMode = FocusModeEnum.None,
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
		};
		button.Pressed += () => _jumpToTile?.Invoke(position);
		box.AddChild(button);
	}

	private Control CreateEventEntry(EventLogEntryView entry)
	{
		var text = BuildEventEntryText(entry);

		if (_jumpToTile is not null || _jumpToLinkedTarget is not null)
		{
			var button = new Button
			{
				Flat = true,
				Text = text,
				Alignment = HorizontalAlignment.Left,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
				FocusMode = FocusModeEnum.None,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				TooltipText = BuildEventEntryTooltip(entry),
			};
			if (entry.LinkedTarget is { } linkedTarget)
			{
				button.Icon = ResolveEventLinkIcon(linkedTarget);
				button.ExpandIcon = false;
			}

			button.Pressed += () =>
			{
				if (entry.LinkedTarget is { } target && _jumpToLinkedTarget?.Invoke(target) == true)
					return;

				_jumpToTile?.Invoke(entry.Position);
			};
			return button;
		}

		return CreateBodyLabel(text);
	}

	private static string BuildEventEntryText(EventLogEntryView entry)
	{
		var text = string.IsNullOrWhiteSpace(entry.TimeLabel)
			? entry.Message
			: $"{entry.TimeLabel}  {entry.Message}";

		if (entry.LinkedTarget is not { } linkedTarget || string.IsNullOrWhiteSpace(linkedTarget.DisplayName))
			return text;

		return text.Contains(linkedTarget.DisplayName, StringComparison.OrdinalIgnoreCase)
			? text
			: $"{text}  [{linkedTarget.DisplayName}]";
	}

	private static string BuildEventEntryTooltip(EventLogEntryView entry)
	{
		if (entry.LinkedTarget is { } linkedTarget)
			return $"Track {linkedTarget.DisplayName}";

		return $"Jump to {entry.Position.X}, {entry.Position.Y}, z{entry.Position.Z}";
	}

	private static Texture2D? ResolveEventLinkIcon(EventLogLinkTarget linkedTarget)
		=> linkedTarget.Type switch
		{
			EventLogLinkType.Item => PixelArtFactory.GetItem(linkedTarget.DefId, linkedTarget.MaterialId),
			EventLogLinkType.Entity => PixelArtFactory.GetEntity(linkedTarget.DefId),
			_ => null,
		};

	private static Label CreateSectionHeader(string text)
	{
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", 15);
		label.Modulate = new Color(0.90f, 0.86f, 0.72f, 1f);
		return label;
	}

	private static Label CreateBodyLabel(string text)
	{
		return new Label
		{
			Text = text,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
	}

	private static Label CreateMutedLabel(string text)
	{
		var label = CreateBodyLabel(text);
		label.Modulate = new Color(0.72f, 0.72f, 0.72f, 1f);
		return label;
	}

	private void EnsureReady()
	{
		if (_nameLabel is null)
			_Ready();
	}

	private static string FormatNeed(NeedView need) => $"{FormatToken(need.Id)}: {need.Level:0.#}";

	private static string FormatWound(WoundView wound)
		=> $"{FormatToken(wound.BodyPartId)}: {FormatToken(wound.Severity)}{(wound.IsBleeding ? " (bleeding)" : string.Empty)}";

	private static string FormatSubstance(SubstanceView substance)
		=> $"{FormatToken(substance.Id)} {substance.Concentration:0.##}";

	private static string FormatThought(ThoughtView thought)
		=> $"{thought.Description}  |  Mood {thought.HappinessMod:+0.##;-0.##;0}  |  {thought.TimeLeft:0.#}h left";

	private static string FormatSkill(SkillView skill)
		=> $"{FormatToken(skill.Id)}: level {skill.Level}  |  XP {skill.Xp:0.#}/{skill.XpForNextLevel:0.#}";

	private static string FormatItem(ItemView item)
	{
		var stackText = item.StackSize > 1 ? $" x{item.StackSize}" : string.Empty;
		var materialText = string.IsNullOrWhiteSpace(item.MaterialId) ? string.Empty : $" ({FormatToken(item.MaterialId!)})";
		var weightText = item.Weight > 0f ? $" [{item.Weight:F1} kg]" : string.Empty;
		return $"{item.DisplayName}{stackText}{materialText}{weightText}";
	}

	private static string FormatAttribute(DwarfAttributeView attr)
	{
		var filled = new string('\u25CF', attr.Level); // â— filled circle
		var empty = new string('\u25CB', 5 - attr.Level); // â—‹ empty circle
		var labelText = string.IsNullOrWhiteSpace(attr.Label) ? "(normal)" : attr.Label;
		return $"{attr.DisplayName}: {filled}{empty} [{labelText}]";
	}

	private static string FormatToken(string token)
	{
		if (string.IsNullOrWhiteSpace(token))
			return "Unknown";

		var words = token
			.Replace('_', ' ')
			.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant());
		return string.Join(" ", words);
	}

	private static string FormatOptionalToken(string? token)
		=> string.IsNullOrWhiteSpace(token) ? "Unknown" : FormatToken(token);

	private static string FormatOptionalText(string? text)
		=> string.IsNullOrWhiteSpace(text) ? "Unknown" : text;

	private static string FormatNamedValue(string? name, string? id)
	{
		var resolvedName = FormatOptionalText(name);
		if (string.IsNullOrWhiteSpace(id))
			return resolvedName;

		var resolvedId = FormatToken(id);
		return string.Equals(resolvedName, resolvedId, StringComparison.OrdinalIgnoreCase)
			? resolvedName
			: $"{resolvedName} [{resolvedId}]";
	}

	private static string FormatOptionalCoord(int? x, int? y)
		=> x.HasValue && y.HasValue ? $"{x.Value}, {y.Value}" : "Unknown";

	private static string FormatVec(Vec3i value) => $"{value.X}, {value.Y}, z{value.Z}";
}

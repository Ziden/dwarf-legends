using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Systems;
using Godot;

public partial class DwarfPanel : PanelContainer
{
	private Label? _nameLabel;
	private Label? _summaryLabel;
	private Label? _metaLabel;
	private TabContainer? _tabs;
	private VBoxContainer? _overviewBox;
	private VBoxContainer? _vitalsBox;
	private VBoxContainer? _thoughtsBox;
	private VBoxContainer? _skillsBox;
	private VBoxContainer? _laborsBox;
	private VBoxContainer? _statsBox;
	private VBoxContainer? _eventLogBox;
	private VBoxContainer? _inventoryBox;
	private VBoxContainer? _traitsBox;
	private Action<Vec3i>? _jumpToTile;
	private string? _currentSelectionKey;

	public override void _Ready()
	{
		_nameLabel = GetNode<Label>("%NameLabel");
		_summaryLabel = GetNode<Label>("%SummaryLabel");
		_metaLabel = GetNode<Label>("%MetaLabel");
		_tabs = GetNode<TabContainer>("%Tabs");
		_overviewBox = GetNode<VBoxContainer>("%OverviewBox");
		_vitalsBox = GetNode<VBoxContainer>("%VitalsBox");
		_thoughtsBox = GetNode<VBoxContainer>("%ThoughtsBox");
		_skillsBox = GetNode<VBoxContainer>("%SkillsBox");
		_laborsBox = GetNode<VBoxContainer>("%LaborsBox");
		_statsBox = GetNode<VBoxContainer>("%StatsBox");
		_eventLogBox = GetNode<VBoxContainer>("%EventLogBox");
		_inventoryBox = GetNode<VBoxContainer>("%InventoryBox");
		_traitsBox = GetNode<VBoxContainer>("%TraitsBox");

		Hide();
	}

	public void Setup(GameSimulation simulation)
	{
		_ = simulation;
	}

	public void SetTileNavigator(Action<Vec3i> jumpToTile) => _jumpToTile = jumpToTile;

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
		PopulateVitals(dwarf.Needs, dwarf.Wounds, dwarf.Substances);
		PopulateThoughts(dwarf.Thoughts);
		PopulateSkills(dwarf.Skills);
		PopulateLabors(dwarf.EnabledLabors);
		PopulateStats(dwarf.Stats, dwarf.Appearance);
		PopulateEventLog(dwarf.EventLog);
		PopulateInventory(dwarf.CarriedItems);
		PopulateTraits(dwarf.Traits);

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
		PopulateVitals(creature.Needs, creature.Wounds, creature.Substances);
		PopulateThoughts(Array.Empty<ThoughtView>());
		PopulateSkills(Array.Empty<SkillView>());
		PopulateLabors(Array.Empty<string>());
		PopulateStats(creature.Stats, appearance: null);
		PopulateEventLog(creature.EventLog);
		PopulateInventory(creature.CarriedItems);
		PopulateTraits(Array.Empty<TraitView>());

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

	private void PopulateInventory(ItemView[] items)
	{
		ResetBox(_inventoryBox);
		AddSection(_inventoryBox!, "Carried items", items.Length == 0 ? ["Nothing carried."] : items.Select(FormatItem));
	}

	private void PopulateTraits(TraitView[] traits)
	{
		ResetBox(_traitsBox);
		AddSection(_traitsBox!, "Traits", traits.Length == 0 ? ["No traits available."] : traits.Select(FormatTrait));
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
		var text = string.IsNullOrWhiteSpace(entry.TimeLabel)
			? entry.Message
			: $"{entry.TimeLabel}  {entry.Message}";

		if (_jumpToTile is not null)
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
				TooltipText = $"Jump to {entry.Position.X}, {entry.Position.Y}, z{entry.Position.Z}",
			};
			button.Pressed += () => _jumpToTile?.Invoke(entry.Position);
			return button;
		}

		return CreateBodyLabel(text);
	}

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
		return $"{FormatToken(item.DefId)}{stackText}{materialText}";
	}

	private static string FormatTrait(TraitView trait)
		=> string.IsNullOrWhiteSpace(trait.Category)
			? $"{trait.DisplayName}: {trait.Description}"
			: $"[{trait.Category}] {trait.DisplayName}: {trait.Description}";

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

	private static string FormatVec(Vec3i value) => $"{value.X}, {value.Y}, z{value.Z}";
}

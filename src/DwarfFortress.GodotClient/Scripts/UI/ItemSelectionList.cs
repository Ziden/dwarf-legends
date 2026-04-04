using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DwarfFortress.GodotClient.UI;


public sealed record ItemSelectionEntry(
    string Id,
    string Title,
    string Subtitle,
    string Details,
    string Status,
    Color StatusColor,
    Texture2D? Icon,
    string ActionLabel,
    bool IsEnabled,
    Action? OnPressed,
    Texture2D? ActionIcon = null);

public partial class ItemSelectionList : ScrollContainer
{
    private VBoxContainer? _content;
    private readonly Dictionary<string, ItemSelectionEntry> _entries = new();
    private readonly Dictionary<string, EntryRefs> _cards = new();
    private List<string> _order = new();

    public override void _Ready()
    {
        EnsureContent();
    }

    public void SetEntries(IReadOnlyList<ItemSelectionEntry> entries)
    {
        EnsureContent();

        var newOrder = entries.Select(entry => entry.Id).ToList();
        bool rebuild = _order.Count != newOrder.Count || !_order.SequenceEqual(newOrder);

        _entries.Clear();
        foreach (var entry in entries)
            _entries[entry.Id] = entry;

        if (rebuild)
        {
            Rebuild(entries);
            return;
        }

        foreach (var entry in entries)
            UpdateCard(_cards[entry.Id], entry);
    }

    private void Rebuild(IReadOnlyList<ItemSelectionEntry> entries)
    {
        foreach (Node child in _content!.GetChildren())
            child.QueueFree();

        _cards.Clear();
        _order = entries.Select(entry => entry.Id).ToList();

        foreach (var entry in entries)
        {
            var refs = CreateCard(entry);
            _cards[entry.Id] = refs;
            _content.AddChild(refs.Root);
        }
    }

    private EntryRefs CreateCard(ItemSelectionEntry entry)
    {
        var root = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(0, 88),
        };

        var margin = new MarginContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_top", 7);
        margin.AddThemeConstantOverride("margin_bottom", 7);
        root.AddChild(margin);

        var layout = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        layout.AddThemeConstantOverride("separation", 10);
        margin.AddChild(layout);

        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(52, 52),
            ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        layout.AddChild(icon);

        var body = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        body.AddThemeConstantOverride("separation", 2);
        layout.AddChild(body);

        var headerRow = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        headerRow.AddThemeConstantOverride("separation", 10);
        body.AddChild(headerRow);

        var topRow = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        topRow.AddThemeConstantOverride("separation", 2);
        headerRow.AddChild(topRow);

        var title = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
        };
        title.AddThemeFontSizeOverride("font_size", 18);
        topRow.AddChild(title);

        var subtitle = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
            Modulate = new Color(0.84f, 0.84f, 0.84f),
        };
        subtitle.AddThemeFontSizeOverride("font_size", 13);
        topRow.AddChild(subtitle);

        var action = new Button
        {
            CustomMinimumSize = new Vector2(108, 30),
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            ExpandIcon = true,
            IconAlignment = HorizontalAlignment.Left,
        };
        var capturedId = entry.Id;
        action.Pressed += () =>
        {
            if (_entries.TryGetValue(capturedId, out var current))
                current.OnPressed?.Invoke();
        };
        headerRow.AddChild(action);

        var metaRow = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        metaRow.AddThemeConstantOverride("separation", 8);
        body.AddChild(metaRow);

        var status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            ClipText = true,
            CustomMinimumSize = new Vector2(150, 0),
        };
        status.AddThemeFontSizeOverride("font_size", 13);
        metaRow.AddChild(status);

        var details = new Label
        {
            ClipText = true,
            Modulate = new Color(0.74f, 0.74f, 0.74f),
        };
        details.AddThemeFontSizeOverride("font_size", 12);
        body.AddChild(details);

        var refs = new EntryRefs(root, icon, title, subtitle, details, status, action);
        UpdateCard(refs, entry);
        return refs;
    }

    private static void UpdateCard(EntryRefs refs, ItemSelectionEntry entry)
    {
        var hasSubtitle = !string.IsNullOrWhiteSpace(entry.Subtitle);
        var hasDetails = !string.IsNullOrWhiteSpace(entry.Details);
        var hasStatus = !string.IsNullOrWhiteSpace(entry.Status);

        refs.Icon.Texture = entry.Icon;
        refs.Title.Text = entry.Title;
        refs.Subtitle.Text = entry.Subtitle;
        refs.Subtitle.Visible = hasSubtitle;
        refs.Details.Text = entry.Details;
        refs.Details.Visible = hasDetails;
        refs.Status.Text = entry.Status;
        refs.Status.Modulate = entry.StatusColor;
        refs.Status.Visible = hasStatus;
        refs.Action.Text = entry.ActionLabel;
        refs.Action.Icon = entry.ActionIcon;
        refs.Action.Disabled = !entry.IsEnabled;
        refs.Action.Visible = !string.IsNullOrWhiteSpace(entry.ActionLabel) || entry.ActionIcon is not null;
        refs.Root.CustomMinimumSize = new Vector2(0, hasSubtitle || hasDetails || hasStatus ? 88 : 64);
    }

    private void EnsureContent()
    {
        _content ??= GetNodeOrNull<VBoxContainer>("Content");
        if (_content is null)
        {
            _content = new VBoxContainer { Name = "Content" };
            AddChild(_content);
        }

        // Stretch content to ScrollContainer viewport width so cards can fill the full panel.
        _content.AnchorLeft = 0f;
        _content.AnchorRight = 1f;
        _content.OffsetLeft = 0f;
        _content.OffsetRight = 0f;
        _content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _content.AddThemeConstantOverride("separation", 6);
    }

    private sealed record EntryRefs(
        PanelContainer Root,
        TextureRect Icon,
        Label Title,
        Label Subtitle,
        Label Details,
        Label Status,
        Button Action);
}

using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Systems;
using Godot;

/// <summary>Bottom-left fortress announcement log with clickable entries.</summary>
public partial class AnnouncementLog : PanelContainer
{
    private const int MaxMessages = 40;
    private const int MaxVisibleEntries = 14;

    private Label? _headline;
    private VBoxContainer? _entries;
    private ScrollContainer? _scroll;
    private Action<Vec3i>? _jumpToTile;
    private readonly List<FortressAnnouncementView> _fallbackEntries = new();
    private int _fallbackSequence = -1;
    private int _renderedFirstSequence = int.MinValue;
    private int _renderedCount = -1;
    private bool _renderedFallback;

    public override void _Ready()
    {
        _headline = GetNode<Label>("%Headline");
        _entries = GetNode<VBoxContainer>("%Entries");
        _scroll = GetNode<ScrollContainer>("%Scroll");
    }

    public void SetTileNavigator(Action<Vec3i> jumpToTile) => _jumpToTile = jumpToTile;

    public void AddMessage(string text, Color? color = null)
    {
        var severity = ResolveFallbackSeverity(color ?? Colors.White);
        _fallbackEntries.Insert(0, new FortressAnnouncementView(
            Sequence: _fallbackSequence--,
            Message: text,
            Position: Vec3i.Zero,
            HasLocation: false,
            Severity: severity,
            TimeLabel: "Client",
            RepeatCount: 1));

        if (_fallbackEntries.Count > MaxMessages)
            _fallbackEntries.RemoveRange(MaxMessages, _fallbackEntries.Count - MaxMessages);

        Render(_fallbackEntries.ToArray(), isFallback: true);
    }

    public void Refresh(FortressAnnouncementView[] entries)
    {
        if (entries.Length == 0)
        {
            Render(_fallbackEntries.ToArray(), isFallback: true);
            return;
        }

        Render(entries, isFallback: false);
    }

    private void Render(FortressAnnouncementView[] entries, bool isFallback)
    {
        var firstSequence = entries.Length > 0 ? entries[0].Sequence : int.MinValue;
        if (_entries is null || _headline is null || _scroll is null)
            return;

        if (_renderedFirstSequence == firstSequence && _renderedCount == entries.Length && _renderedFallback == isFallback)
            return;

        _renderedFirstSequence = firstSequence;
        _renderedCount = entries.Length;
        _renderedFallback = isFallback;

        foreach (var child in _entries.GetChildren())
            child.QueueFree();

        if (entries.Length == 0)
        {
            _headline.Text = "No recent announcements.";
            _headline.Modulate = new Color(0.78f, 0.78f, 0.74f, 0.92f);
            _entries.AddChild(CreateMutedLabel("Important fortress events will appear here."));
            return;
        }

        var spotlight = entries.FirstOrDefault(entry => entry.Severity >= FortressAnnouncementSeverity.Warning);
        if (spotlight.Sequence == 0)
            spotlight = entries[0];

        _headline.Text = FormatEntryText(spotlight, includeTime: false);
        _headline.Modulate = ResolveSeverityColor(spotlight.Severity);

        foreach (var entry in entries.Take(MaxVisibleEntries))
            _entries.AddChild(CreateEntryControl(entry));

        _scroll.ScrollVertical = 0;
    }

    private Control CreateEntryControl(FortressAnnouncementView entry)
    {
        var text = $"{entry.TimeLabel}  {FormatEntryText(entry, includeTime: false)}";
        if (entry.HasLocation)
        {
            var button = new Button
            {
                Flat = true,
                Text = text,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                Alignment = HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.None,
                TooltipText = $"Jump to {entry.Position.X}, {entry.Position.Y}, z{entry.Position.Z}",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            button.Modulate = ResolveSeverityColor(entry.Severity);
            button.Pressed += () => _jumpToTile?.Invoke(entry.Position);
            return button;
        }

        var label = CreateMutedLabel(text);
        label.Modulate = ResolveSeverityColor(entry.Severity);
        return label;
    }

    private static Label CreateMutedLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
    }

    private static string FormatEntryText(FortressAnnouncementView entry, bool includeTime)
    {
        var prefix = entry.Severity switch
        {
            FortressAnnouncementSeverity.Critical => "Alert: ",
            FortressAnnouncementSeverity.Warning => "Warning: ",
            FortressAnnouncementSeverity.Attention => "Notice: ",
            _ => string.Empty,
        };
        var countSuffix = entry.RepeatCount > 1 ? $"  x{entry.RepeatCount}" : string.Empty;
        var body = $"{prefix}{entry.Message}{countSuffix}";
        return includeTime ? $"{entry.TimeLabel}  {body}" : body;
    }

    private static Color ResolveSeverityColor(FortressAnnouncementSeverity severity)
        => severity switch
        {
            FortressAnnouncementSeverity.Critical => new Color(0.96f, 0.42f, 0.34f, 1f),
            FortressAnnouncementSeverity.Warning => new Color(0.96f, 0.76f, 0.32f, 1f),
            FortressAnnouncementSeverity.Attention => new Color(0.68f, 0.88f, 1f, 1f),
            _ => new Color(0.88f, 0.88f, 0.84f, 1f),
        };

    private static FortressAnnouncementSeverity ResolveFallbackSeverity(Color color)
    {
        if (color.R > 0.8f && color.G < 0.4f)
            return FortressAnnouncementSeverity.Critical;
        if (color.R > 0.8f && color.G > 0.45f)
            return FortressAnnouncementSeverity.Warning;
        return FortressAnnouncementSeverity.Attention;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Systems;
using Godot;

namespace DwarfFortress.GodotClient.UI;


/// <summary>Icon-stack fortress notifications with right-click details and left-click dismiss.</summary>
public partial class AnnouncementLog : PanelContainer
{
    private const int MaxMessages = 40;
    private const int IconButtonSize = 44;
    private const int DetailPopupWidth = 360;
    private const int DetailPopupMinHeight = 156;

    private VBoxContainer? _entries;
    private ScrollContainer? _scroll;
    private PopupPanel? _detailPopup;
    private ColorRect? _detailAccent;
    private TextureRect? _detailIcon;
    private Label? _detailTitle;
    private Label? _detailMessage;
    private Label? _detailMeta;
    private Button? _detailJumpButton;
    private Action<Vec3i>? _jumpToTile;
    private readonly List<FortressAnnouncementView> _fallbackEntries = new();
    private readonly HashSet<int> _dismissedSequences = new();
    private readonly Dictionary<int, Control> _entryControls = new();
    private FortressAnnouncementView[] _liveEntries = Array.Empty<FortressAnnouncementView>();
    private int _fallbackSequence = -1;
    private int? _openSequence;
    private int _renderedTopSequence = int.MinValue;
    private string _renderSignature = string.Empty;
    private Vec3i _detailJumpTarget = Vec3i.Zero;
    private bool _detailHasJumpTarget;

    public override void _Ready()
    {
        _entries = GetNode<VBoxContainer>("%Entries");
        _scroll = GetNode<ScrollContainer>("%Scroll");
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _scroll.MouseFilter = MouseFilterEnum.Ignore;
        _entries.MouseFilter = MouseFilterEnum.Ignore;
        CreateDetailPopup();
        RenderCurrent();
    }

    public override void _ExitTree()
    {
        if (_detailPopup is not null && GodotObject.IsInstanceValid(_detailPopup))
            _detailPopup.QueueFree();

        _detailPopup = null;
        _detailAccent = null;
        _detailIcon = null;
        _detailTitle = null;
        _detailMessage = null;
        _detailMeta = null;
        _detailJumpButton = null;
    }

    public void SetTileNavigator(Action<Vec3i> jumpToTile) => _jumpToTile = jumpToTile;

    public void AddMessage(string text, Color? color = null)
    {
        var severity = ResolveFallbackSeverity(color ?? Colors.White);
        _fallbackEntries.Insert(0, new FortressAnnouncementView(
            Sequence: _fallbackSequence--,
            Kind: FortressAnnouncementKind.Status,
            Message: text,
            Position: Vec3i.Zero,
            HasLocation: false,
            Severity: severity,
            TimeLabel: "Client",
            RepeatCount: 1));

        if (_fallbackEntries.Count > MaxMessages)
            _fallbackEntries.RemoveRange(MaxMessages, _fallbackEntries.Count - MaxMessages);

        RenderCurrent();
    }

    public void Refresh(FortressAnnouncementView[] entries)
    {
        _liveEntries = entries;
        RenderCurrent();
    }

    private void RenderCurrent()
    {
        if (_entries is null || _scroll is null)
            return;

        var sourceEntries = _liveEntries.Length == 0 ? _fallbackEntries.ToArray() : _liveEntries;
        var visibleEntries = sourceEntries
            .Where(entry => !_dismissedSequences.Contains(entry.Sequence))
            .ToArray();
        var renderSignature = BuildRenderSignature(sourceEntries, visibleEntries);

        if (string.Equals(_renderSignature, renderSignature, StringComparison.Ordinal))
        {
            RefreshOpenDetails(visibleEntries);
            return;
        }

        _entryControls.Clear();
        foreach (var child in _entries.GetChildren())
            child.QueueFree();

        if (visibleEntries.Length == 0)
        {
            var emptyText = sourceEntries.Length == 0
                ? "No recent notifications."
                : "All current notifications were dismissed.";
            _entries.AddChild(CreateMutedLabel(emptyText));
            _entries.AddChild(CreateMutedLabel("New fortress events will stack here as icon alerts."));
            HideDetails();
            _renderedTopSequence = int.MinValue;
            _renderSignature = renderSignature;
            return;
        }

        foreach (var entry in visibleEntries)
        {
            var card = CreateEntryIcon(entry);
            _entryControls[entry.Sequence] = card;
            _entries.AddChild(card);
        }

        if (_renderedTopSequence != visibleEntries[0].Sequence)
            _scroll.ScrollVertical = 0;

        _renderedTopSequence = visibleEntries[0].Sequence;
        _renderSignature = renderSignature;
        RefreshOpenDetails(visibleEntries);
    }

    private string BuildRenderSignature(FortressAnnouncementView[] sourceEntries, FortressAnnouncementView[] visibleEntries)
    {
        var builder = new StringBuilder();
        builder.Append(sourceEntries.Length)
            .Append('|')
            .Append(visibleEntries.Length)
            .Append('|');

        foreach (var entry in visibleEntries)
        {
            builder.Append(entry.Sequence)
                .Append(':')
                .Append((int)entry.Kind)
                .Append(':')
                .Append((int)entry.Severity)
                .Append(':')
                .Append(entry.RepeatCount)
                .Append(':')
                .Append(entry.HasLocation ? 1 : 0)
                .Append(':')
                .Append(entry.TimeLabel)
                .Append(':')
                .Append(entry.Message)
                .Append(';');
        }

        return builder.ToString();
    }

    private Control CreateEntryIcon(FortressAnnouncementView entry)
    {
        var root = new PanelContainer
        {
            Name = $"Entry_{entry.Sequence}",
            CustomMinimumSize = new Vector2(IconButtonSize, IconButtonSize),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = BuildTooltipTitle(entry),
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        root.GuiInput += @event =>
        {
            if (HandleEntryInput(entry.Sequence, root, @event))
                GetViewport().SetInputAsHandled();
        };

        var margin = new MarginContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        margin.AddThemeConstantOverride("margin_left", 4);
        margin.AddThemeConstantOverride("margin_right", 4);
        margin.AddThemeConstantOverride("margin_top", 4);
        margin.AddThemeConstantOverride("margin_bottom", 4);
        root.AddChild(margin);

        var layout = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        layout.AddThemeConstantOverride("separation", 4);
        margin.AddChild(layout);

        var accent = new ColorRect
        {
            Color = ResolveSeverityColor(entry.Severity),
            CustomMinimumSize = new Vector2(0, 4),
        };
        layout.AddChild(accent);

        var iconCenter = new CenterContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        layout.AddChild(iconCenter);

        var icon = new TextureRect
        {
            Texture = PixelArtFactory.GetUiIcon(ResolveIconId(entry.Kind)),
            CustomMinimumSize = new Vector2(28, 28),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
        };
        icon.Modulate = ResolveSeverityColor(entry.Severity);
        iconCenter.AddChild(icon);

        return root;
    }

    private void CreateDetailPopup()
    {
        _detailPopup = new PopupPanel();

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        _detailPopup.AddChild(margin);

        var layout = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        layout.AddThemeConstantOverride("separation", 8);
        margin.AddChild(layout);

        var header = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        header.AddThemeConstantOverride("separation", 8);
        layout.AddChild(header);

        _detailAccent = new ColorRect
        {
            CustomMinimumSize = new Vector2(6, 28),
        };
        header.AddChild(_detailAccent);

        _detailIcon = new TextureRect
        {
            CustomMinimumSize = new Vector2(24, 24),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
        };
        header.AddChild(_detailIcon);

        _detailTitle = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        _detailTitle.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(_detailTitle);

        _detailMessage = CreateWrappedLabel();
        _detailMessage.AddThemeFontSizeOverride("font_size", 15);
        layout.AddChild(_detailMessage);

        _detailMeta = CreateWrappedLabel(muted: true);
        layout.AddChild(_detailMeta);

        _detailJumpButton = new Button
        {
            FocusMode = FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            TooltipText = "Center the camera on this notification target",
            Visible = false,
        };
        _detailJumpButton.Pressed += OnDetailJumpPressed;
        layout.AddChild(_detailJumpButton);

        GetTree().Root.CallDeferred(Node.MethodName.AddChild, _detailPopup);
    }

    private void RefreshOpenDetails(FortressAnnouncementView[] visibleEntries)
    {
        if (!_openSequence.HasValue)
            return;

        var entry = visibleEntries.FirstOrDefault(candidate => candidate.Sequence == _openSequence.Value);
        if (entry is null)
        {
            HideDetails();
            return;
        }

        if (!_entryControls.TryGetValue(entry.Sequence, out var sourceControl))
        {
            HideDetails();
            return;
        }

        ShowDetails(entry, sourceControl);
    }

    private bool HandleEntryInput(int sequence, Control? sourceControl, InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mouseButton)
            return false;

        switch (mouseButton.ButtonIndex)
        {
            case MouseButton.Left:
                Dismiss(sequence);
                return true;
            case MouseButton.Right:
                if (!TryGetVisibleEntry(sequence, out var entry))
                    return false;

                ToggleDetails(entry, sourceControl);
                return true;
            default:
                return false;
        }
    }

    private void ToggleDetails(FortressAnnouncementView entry, Control? sourceControl)
    {
        if (_openSequence == entry.Sequence && _detailPopup?.Visible == true)
        {
            HideDetails();
            return;
        }

        _openSequence = entry.Sequence;
        ShowDetails(entry, sourceControl);
    }

    private void ShowDetails(FortressAnnouncementView entry, Control? sourceControl)
    {
        if (_detailPopup is null ||
            _detailAccent is null ||
            _detailIcon is null ||
            _detailTitle is null ||
            _detailMessage is null ||
            _detailMeta is null ||
            _detailJumpButton is null)
        {
            return;
        }

        _detailAccent.Color = ResolveSeverityColor(entry.Severity);
        _detailIcon.Texture = PixelArtFactory.GetUiIcon(ResolveIconId(entry.Kind));
        _detailIcon.Modulate = ResolveSeverityColor(entry.Severity);
        _detailTitle.Text = BuildHeadline(entry);
        _detailTitle.Modulate = ResolveSeverityColor(entry.Severity);
        _detailMessage.Text = entry.Message;
        _detailMeta.Text = BuildMetaText(entry);
        _detailHasJumpTarget = entry.HasLocation;
        _detailJumpTarget = entry.Position;
        _detailJumpButton.Visible = entry.HasLocation;
        _detailJumpButton.Text = $"Jump to {FormatPosition(entry.Position)}";

        var popupHeight = entry.HasLocation ? 196 : DetailPopupMinHeight;
        var anchorRect = sourceControl?.GetGlobalRect() ?? GetGlobalRect();
        _detailPopup.Size = new Vector2I(DetailPopupWidth, popupHeight);
        _detailPopup.PopupOnParent(BuildPopupRect(anchorRect, popupHeight));
    }

    private Rect2I BuildPopupRect(Rect2 anchorRect, int popupHeight)
    {
        var viewportSize = GetViewportRect().Size;
        var x = (int)MathF.Round(anchorRect.End.X + 8f);
        var y = (int)MathF.Round(anchorRect.Position.Y);
        var maxX = Math.Max(8, (int)viewportSize.X - DetailPopupWidth - 8);
        var maxY = Math.Max(8, (int)viewportSize.Y - popupHeight - 8);
        return new Rect2I(Math.Clamp(x, 8, maxX), Math.Clamp(y, 8, maxY), DetailPopupWidth, popupHeight);
    }

    private void HideDetails()
    {
        _openSequence = null;
        _detailHasJumpTarget = false;
        _detailPopup?.Hide();
    }

    private void OnDetailJumpPressed()
    {
        if (!_detailHasJumpTarget)
            return;

        _jumpToTile?.Invoke(_detailJumpTarget);
        HideDetails();
    }

    private bool TryGetVisibleEntry(int sequence, out FortressAnnouncementView entry)
    {
        var sourceEntries = _liveEntries.Length == 0 ? _fallbackEntries.ToArray() : _liveEntries;
        entry = sourceEntries.FirstOrDefault(candidate => candidate.Sequence == sequence && !_dismissedSequences.Contains(candidate.Sequence))!;
        return entry is not null;
    }

    private void Dismiss(int sequence)
    {
        _dismissedSequences.Add(sequence);
        if (_openSequence == sequence)
            HideDetails();

        RenderCurrent();
    }

    internal int[] DebugGetVisibleSequences()
    {
        var sourceEntries = _liveEntries.Length == 0 ? _fallbackEntries.ToArray() : _liveEntries;
        return sourceEntries
            .Where(entry => !_dismissedSequences.Contains(entry.Sequence))
            .Select(entry => entry.Sequence)
            .ToArray();
    }

    internal bool DebugHandleEntryClick(int sequence, MouseButton button)
        => HandleEntryInput(sequence,
            _entryControls.TryGetValue(sequence, out var sourceControl) ? sourceControl : null,
            new InputEventMouseButton { ButtonIndex = button, Pressed = true });

    internal bool DebugIsDetailPopupVisible() => _detailPopup?.Visible == true;

    internal string DebugDetailMessageText => _detailMessage?.Text ?? string.Empty;

    internal int? DebugOpenSequence => _openSequence;

    internal string DebugGetEntryTooltipText(int sequence)
        => _entryControls.TryGetValue(sequence, out var control) ? control.TooltipText : string.Empty;

    internal bool DebugUsesTransparentBackground()
        => GetThemeStylebox("panel") is StyleBoxEmpty;

    private static Label CreateMutedLabel(string text)
    {
        return CreateWrappedLabel(text, muted: true);
    }

    private static Label CreateWrappedLabel(string text = "", bool muted = false)
    {
        var label = new Label
        {
            Text = text,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        if (muted)
            label.Modulate = new Color(0.80f, 0.80f, 0.76f, 0.90f);

        return label;
    }

    private static string BuildMetaText(FortressAnnouncementView entry)
    {
        var meta = entry.TimeLabel;
        if (entry.HasLocation)
            meta += $"  at {FormatPosition(entry.Position)}";
        if (entry.RepeatCount > 1)
            meta += $"  repeated {entry.RepeatCount} times";
        return meta;
    }

    private static string BuildTooltipTitle(FortressAnnouncementView entry)
        => FormatEntryText(entry);

    private static string BuildHeadline(FortressAnnouncementView entry)
    {
        var severity = entry.Severity switch
        {
            FortressAnnouncementSeverity.Critical => "Critical",
            FortressAnnouncementSeverity.Warning => "Warning",
            FortressAnnouncementSeverity.Attention => "Attention",
            _ => "Info",
        };

        return $"{FormatKind(entry.Kind)} - {severity}";
    }

    private static string FormatEntryText(FortressAnnouncementView entry)
    {
        var prefix = entry.Severity switch
        {
            FortressAnnouncementSeverity.Critical => "Alert: ",
            FortressAnnouncementSeverity.Warning => "Warning: ",
            FortressAnnouncementSeverity.Attention => "Notice: ",
            _ => string.Empty,
        };
        var countSuffix = entry.RepeatCount > 1 ? $"  x{entry.RepeatCount}" : string.Empty;
        return $"{prefix}{entry.Message}{countSuffix}";
    }

    private static string FormatPosition(Vec3i position)
        => $"({position.X}, {position.Y}, z{position.Z})";

    private static string FormatKind(FortressAnnouncementKind kind)
        => kind switch
        {
            FortressAnnouncementKind.Status => "Status",
            FortressAnnouncementKind.Discovery => "Discovery",
            FortressAnnouncementKind.Calendar => "Calendar",
            FortressAnnouncementKind.Migration => "Migration",
            FortressAnnouncementKind.WorldEvent => "World Event",
            FortressAnnouncementKind.Threat => "Threat",
            FortressAnnouncementKind.Construction => "Construction",
            FortressAnnouncementKind.Labor => "Labor",
            FortressAnnouncementKind.Mood => "Mood",
            FortressAnnouncementKind.Need => "Need",
            FortressAnnouncementKind.Death => "Death",
            FortressAnnouncementKind.Combat => "Combat",
            FortressAnnouncementKind.Flood => "Flood",
            FortressAnnouncementKind.Wildlife => "Wildlife",
            _ => "Status",
        };

    private static string ResolveIconId(FortressAnnouncementKind kind)
        => kind switch
        {
            FortressAnnouncementKind.Status => UiIconIds.Fortress,
            FortressAnnouncementKind.Discovery => UiIconIds.Book,
            FortressAnnouncementKind.Calendar => UiIconIds.Calendar,
            FortressAnnouncementKind.Migration => UiIconIds.Migration,
            FortressAnnouncementKind.WorldEvent => UiIconIds.Banner,
            FortressAnnouncementKind.Threat => UiIconIds.Threat,
            FortressAnnouncementKind.Construction => UiIconIds.Build,
            FortressAnnouncementKind.Labor => UiIconIds.Pickaxe,
            FortressAnnouncementKind.Mood => UiIconIds.Mood,
            FortressAnnouncementKind.Need => UiIconIds.Need,
            FortressAnnouncementKind.Death => UiIconIds.Death,
            FortressAnnouncementKind.Combat => UiIconIds.Combat,
            FortressAnnouncementKind.Flood => UiIconIds.Flood,
            FortressAnnouncementKind.Wildlife => UiIconIds.Wildlife,
            _ => UiIconIds.Fortress,
        };

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

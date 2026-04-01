using System.Linq;
using DwarfFortress.GameLogic.Systems;
using Godot;

/// <summary>
/// Compact panel that surfaces world-lore context for the current run.
/// </summary>
public partial class WorldLorePanel : PanelContainer
{
    private Label? _titleLabel;
    private Label? _metaLabel;
    private ProgressBar? _threatBar;
    private ProgressBar? _prosperityBar;
    private VBoxContainer? _eventsBox;

    public override void _Ready()
    {
        _titleLabel    = GetNode<Label>("%TitleLabel");
        _metaLabel     = GetNode<Label>("%MetaLabel");
        _threatBar     = GetNode<ProgressBar>("%ThreatBar");
        _prosperityBar = GetNode<ProgressBar>("%ProsperityBar");
        _eventsBox     = GetNode<VBoxContainer>("%EventsBox");

        _threatBar.AddThemeColorOverride("fill_color", new Color(0.9f, 0.25f, 0.22f));
        _prosperityBar.AddThemeColorOverride("fill_color", new Color(0.18f, 0.78f, 0.35f));
    }

    public void Refresh(WorldLoreSummaryView? lore)
    {
        if (lore is null)
        {
            Visible = false;
            return;
        }

        Visible = true;

        _titleLabel!.Text = lore.RegionName;
        _metaLabel!.Text =
            $"{lore.BiomeId.Replace('_', ' ')} | {lore.SimulatedYears} years of history";

        _threatBar!.Value = lore.Threat;
        _threatBar.TooltipText = $"Threat {(lore.Threat * 100f):0}%";

        _prosperityBar!.Value = lore.Prosperity;
        _prosperityBar.TooltipText = $"Prosperity {(lore.Prosperity * 100f):0}%";

        foreach (var child in _eventsBox!.GetChildren())
            child.QueueFree();

        var events = lore.RecentEvents.Take(4).ToArray();
        if (events.Length == 0)
        {
            _eventsBox.AddChild(new Label
            {
                Text = "No recorded events yet.",
                Modulate = new Color(0.7f, 0.7f, 0.7f),
            });
            return;
        }

        foreach (var entry in events)
        {
            _eventsBox.AddChild(new Label
            {
                Text = $" - {entry}",
                AutowrapMode = TextServer.AutowrapMode.Word,
            });
        }
    }
}

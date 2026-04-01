using DwarfFortress.GameLogic.Systems;
using Godot;

/// <summary>Top bar: game clock + current mode + hint text. Speed controls live in ActionBar.</summary>
public partial class TopBar : PanelContainer
{
    private Label? _timeLabel;
    private Label? _hintLabel;

    public override void _Ready()
    {
        _timeLabel = GetNode<Label>("%TimeLabel");
        _hintLabel = GetNode<Label>("%HintLabel");
    }

    public void Refresh(GameTimeView time, InputMode mode, string hint = "")
    {
        _timeLabel!.Text = $"Year {time.Year}  ·  {time.Season}  ·  " +
                           $"Month {time.Month}, Day {time.Day}   {time.Hour:D2}:00";

        var modeText = UiText.ModeLabel(mode);

        _hintLabel!.Text = hint.Length > 0 ? $"[{modeText}]  {hint}" : $"[{modeText}]";
    }
}

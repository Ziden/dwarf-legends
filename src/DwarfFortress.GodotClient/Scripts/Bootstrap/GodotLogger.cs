using DwarfFortress.GameLogic.Core;
using Godot;

namespace DwarfFortress.GodotClient.Bootstrap;

public sealed class GodotLogger : ILogger
{
    public void Info(string message) => GD.Print(message);
    public void Warn(string message) => GD.PushWarning(message);
    public void Error(string message) => GD.PushError(message);
    public void Debug(string message) => GD.PrintRich($"[color=gray]{message}[/color]");
}

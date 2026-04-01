namespace DwarfFortress.GameLogic.Core;

/// <summary>
/// Logging interface injected into GameLogic systems.
/// Production implementation uses GD.Print; test double captures output.
/// </summary>
public interface ILogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
    void Debug(string message);
}

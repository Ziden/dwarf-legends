using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.Tests.Fakes;

/// <summary>
/// Captures log calls for assertion in unit tests.
/// </summary>
public sealed class TestLogger : ILogger
{
    public List<string> InfoMessages  { get; } = new();
    public List<string> WarnMessages  { get; } = new();
    public List<string> ErrorMessages { get; } = new();
    public List<string> DebugMessages { get; } = new();

    public void Info (string msg) => InfoMessages.Add(msg);
    public void Warn (string msg) => WarnMessages.Add(msg);
    public void Error(string msg) => ErrorMessages.Add(msg);
    public void Debug(string msg) => DebugMessages.Add(msg);

    public bool HasError(string fragment) =>
        ErrorMessages.Exists(m => m.Contains(fragment, System.StringComparison.OrdinalIgnoreCase));

    public bool HasWarn(string fragment) =>
        WarnMessages.Exists(m => m.Contains(fragment, System.StringComparison.OrdinalIgnoreCase));
}

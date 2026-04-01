namespace DwarfFortress.GameLogic.Core;

/// <summary>
/// File-reading interface injected into GameLogic systems.
/// Production implementation uses Godot's FileAccess; test double uses an in-memory dictionary.
/// </summary>
public interface IDataSource
{
    /// <summary>Reads the entire text content of a file at the given path.</summary>
    string ReadText(string path);

    /// <summary>
    /// Lists all files in a directory with the given extension.
    /// Returns relative paths suitable for passing back to ReadText.
    /// </summary>
    string[] ListFiles(string directory, string extension = ".json");

    /// <summary>Returns true if the file at the given path exists.</summary>
    bool Exists(string path);
}

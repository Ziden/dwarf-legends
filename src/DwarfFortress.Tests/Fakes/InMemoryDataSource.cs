using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.Tests.Fakes;

/// <summary>
/// In-memory implementation of IDataSource for unit tests.
/// Files are added programmatically before tests run.
/// </summary>
public sealed class InMemoryDataSource : IDataSource
{
    private readonly Dictionary<string, string>      _files = new(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _dirs  = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>Add a virtual file with the given content.</summary>
    public void AddFile(string path, string content)
    {
        _files[path] = content;

        // Register under its directory
        var dir = GetDir(path);
        if (!_dirs.TryGetValue(dir, out var list)) _dirs[dir] = list = new List<string>();
        if (!list.Contains(path)) list.Add(path);
    }

    public string ReadText(string path)
    {
        if (_files.TryGetValue(path, out var content)) return content;
        throw new System.IO.FileNotFoundException($"[InMemoryDataSource] File not found: '{path}'");
    }

    public string[] ListFiles(string directory, string extension = ".json")
    {
        if (!_dirs.TryGetValue(directory, out var list)) return System.Array.Empty<string>();
        return list
            .Where(p => p.EndsWith(extension, System.StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public bool Exists(string path)
        => _files.ContainsKey(path) || _dirs.ContainsKey(path);

    private static string GetDir(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx >= 0 ? path[..idx] : string.Empty;
    }
}

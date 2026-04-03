using System;
using System.IO;
using System.Linq;

namespace DwarfFortress.GameLogic.Core;

/// <summary>
/// IDataSource backed by the real filesystem.
/// Pass the root data folder (e.g. "data" relative to the binary, or an
/// absolute path) and all reads/list operations resolve under it.
///
/// Used by the smoke tests and the future Godot client layer.
/// Unit tests continue to use the fast InMemoryDataSource instead.
/// </summary>
public sealed class FolderDataSource : IDataSource
{
    private readonly string _root;

    /// <param name="root">
    /// Base directory for all data files.  May be relative (resolved against
    /// <see cref="AppContext.BaseDirectory"/>) or absolute.
    /// </param>
    public FolderDataSource(string root)
    {
        _root = Path.IsPathRooted(root)
            ? root
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, root));
    }

    public string ReadText(string path)
    {
        var full = Resolve(path);
        if (!File.Exists(full))
            throw new FileNotFoundException($"[FolderDataSource] File not found: '{path}' (resolved: '{full}')");
        return File.ReadAllText(full);
    }

    public string[] ListFiles(string directory, string extension = ".json", bool recursive = false)
    {
        var full = Resolve(directory);
        if (!Directory.Exists(full)) return Array.Empty<string>();

        var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory
            .GetFiles(full, $"*{extension}", search)
            .Select(f => ToForwardSlash(Path.GetRelativePath(_root, f)))
            .ToArray();
    }

    public bool Exists(string path)
    {
        var full = Resolve(path);
        return File.Exists(full) || Directory.Exists(full);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private string Resolve(string path)
    {
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        var dataPrefix = $"data{Path.DirectorySeparatorChar}";

        if (string.Equals(normalized, "data", StringComparison.OrdinalIgnoreCase))
            return _root;

        if (normalized.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(dataPrefix.Length);

        return Path.Combine(_root, normalized);
    }

    private static string ToForwardSlash(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/');
}

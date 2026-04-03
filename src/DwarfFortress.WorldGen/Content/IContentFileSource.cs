using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DwarfFortress.WorldGen.Content;

public interface IContentFileSource
{
    bool Exists(string path);
    string ReadText(string path);
    IReadOnlyList<string> ListFiles(string directory, string extension = ".json", bool recursive = false);
}

public sealed class DirectoryContentFileSource : IContentFileSource
{
    private readonly string _root;

    public DirectoryContentFileSource(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("Content source root cannot be empty.", nameof(root));

        _root = Path.GetFullPath(root);
    }

    public bool Exists(string path)
    {
        var full = Resolve(path);
        return File.Exists(full) || Directory.Exists(full);
    }

    public string ReadText(string path)
    {
        var full = Resolve(path);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Content source file not found: '{path}' (resolved '{full}').", full);

        return File.ReadAllText(full);
    }

    public IReadOnlyList<string> ListFiles(string directory, string extension = ".json", bool recursive = false)
    {
        var full = Resolve(directory);
        if (!Directory.Exists(full))
            return Array.Empty<string>();

        var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory
            .GetFiles(full, $"*{extension}", search)
            .Select(ToRelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string Resolve(string path)
    {
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        var dataPrefix = $"data{Path.DirectorySeparatorChar}";

        if (string.Equals(normalized, "data", StringComparison.OrdinalIgnoreCase))
            return _root;

        if (normalized.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase) &&
            !Directory.Exists(Path.Combine(_root, "data")))
        {
            normalized = normalized[dataPrefix.Length..];
        }

        return Path.Combine(_root, normalized);
    }

    private string ToRelativePath(string fullPath)
        => Path.GetRelativePath(_root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
}

public sealed class MemoryContentFileSource : IContentFileSource
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    public void AddFile(string path, string content)
        => _files[Normalize(path)] = content;

    public bool Exists(string path)
    {
        var normalized = Normalize(path);
        return _files.ContainsKey(normalized) ||
               _files.Keys.Any(file => file.StartsWith(NormalizeDirectory(normalized), StringComparison.OrdinalIgnoreCase));
    }

    public string ReadText(string path)
    {
        var normalized = Normalize(path);
        if (_files.TryGetValue(normalized, out var content))
            return content;

        throw new FileNotFoundException($"Content source file not found: '{path}'.");
    }

    public IReadOnlyList<string> ListFiles(string directory, string extension = ".json", bool recursive = false)
    {
        var normalizedDir = NormalizeDirectory(directory);
        return _files.Keys
            .Where(path => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Where(path => recursive
                ? path.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase)
                : string.Equals(GetDirectory(path), normalizedDir.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Normalize(string path)
        => path.Replace('\\', '/').TrimStart('/');

    private static string NormalizeDirectory(string path)
    {
        var normalized = Normalize(path).TrimEnd('/');
        return string.IsNullOrEmpty(normalized) ? string.Empty : normalized + "/";
    }

    private static string GetDirectory(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        return lastSlash < 0 ? string.Empty : path[..lastSlash];
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DwarfFortress.WorldGen.Config;

internal static class WorldGenConfigPathResolver
{
    public static IEnumerable<string> EnumerateCandidatePaths(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var roots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
        };

        return roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .SelectMany(EnumerateRootAncestors)
            .Select(root => Path.GetFullPath(Path.Combine(root, relativePath)))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateRootAncestors(string root)
    {
        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(Path.GetFullPath(root));
        }
        catch
        {
            yield break;
        }

        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }
}
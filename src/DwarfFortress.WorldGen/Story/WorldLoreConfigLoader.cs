using System;
using System.IO;
using System.Text.Json;

namespace DwarfFortress.WorldGen.Story;

public static class WorldLoreConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static WorldLoreConfig LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Config path cannot be empty.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException($"World lore config file not found: {path}", path);

        var json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    public static WorldLoreConfig LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Config JSON cannot be empty.", nameof(json));

        var parsed = JsonSerializer.Deserialize<WorldLoreConfig>(json, JsonOptions);
        if (parsed is null)
            throw new InvalidOperationException("Failed to parse world lore config.");

        return WorldLoreConfig.WithDefaults(parsed);
    }
}

using System.Collections.Generic;
using System.Text.Json.Nodes;
using DwarfFortress.WorldGen.Content;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

/// <summary>
/// Maps game def IDs to AtlasTextures cut from real spritesheets.
/// Loaded once at startup from Graphics/sprites/*.json.
///
/// Key format: "tile:{id}" | "item:{id}" | "entity:{id}" | "building:{id}"
///
/// EXTENSIBILITY: To add AI-driven sprite selection in the future, extract
/// ISpriteProvider and replace or decorate this with an AI-backed implementation.
/// Callers (PixelArtFactory) only depend on the TryGet* surface.
/// </summary>
public static class SpriteRegistry
{
    public const string SpritesDir = "res://Graphics/sprites";

    private static readonly Dictionary<string, Texture2D> _cache = new();
    private static readonly Dictionary<string, Texture2D> _sheets = new();
    private static bool _loaded;

    /// <summary>
    /// Load all sprite-mapping JSON files from <paramref name="dir"/>.
    /// Safe to call multiple times: subsequent calls are no-ops.
    /// </summary>
    public static void Load(string dir = SpritesDir)
    {
        if (_loaded) return;
        _loaded = true;

        using var dh = DirAccess.Open(dir);
        if (dh is null)
        {
            GD.PushWarning($"[SpriteRegistry] Sprites directory not found: {dir}  (procedural fallback will be used for all sprites)");
            return;
        }

        dh.ListDirBegin();
        string name;
        while ((name = dh.GetNext()) != "")
        {
            if (name.EndsWith(".json"))
                ParseFile($"{dir}/{name}");
        }
        dh.ListDirEnd();

        GD.Print($"[SpriteRegistry] Loaded {_cache.Count} sprite mapping(s) from {dir}");
    }

    public static bool TryGetTile(string id, out Texture2D? tex) => _cache.TryGetValue($"tile:{id}", out tex);
    public static bool TryGetItem(string id, out Texture2D? tex) => _cache.TryGetValue($"item:{id}", out tex);
    public static bool TryGetEntity(string id, out Texture2D? tex) => _cache.TryGetValue($"entity:{id}", out tex);
    public static bool TryGetBuilding(string id, out Texture2D? tex) => _cache.TryGetValue($"building:{id}", out tex);

    public static void UpsertCreatureVisuals(SharedContentCatalog? content)
    {
        if (content is null)
            return;

        foreach (var creature in content.Creatures.Values)
        {
            var visuals = creature.Visuals;
            if (string.IsNullOrWhiteSpace(visuals?.SpriteSheet) ||
                visuals.SpriteColumn is null ||
                visuals.SpriteRow is null)
            {
                continue;
            }

            UpsertSprite("entity", creature.Id, visuals.SpriteSheet, visuals.SpriteColumn.Value, visuals.SpriteRow.Value, 32, 32);
        }
    }

    private static Texture2D? GetSheet(string path)
    {
        if (_sheets.TryGetValue(path, out var cached)) return cached;

        var tex = GD.Load<Texture2D>(path);
        if (tex is null)
            GD.PushWarning($"[SpriteRegistry] Could not load sheet: {path}");

        _sheets[path] = tex!;
        return tex;
    }

    private static void ParseFile(string jsonPath)
    {
        using var f = FileAccess.Open(jsonPath, FileAccess.ModeFlags.Read);
        if (f is null)
        {
            GD.PushWarning($"[SpriteRegistry] Cannot open: {jsonPath}");
            return;
        }

        JsonNode? root;
        try { root = JsonNode.Parse(f.GetAsText()); }
        catch
        {
            GD.PushError($"[SpriteRegistry] JSON parse error in {jsonPath}");
            return;
        }

        if (root is null)
            return;

        string? defaultSheetPath = root["sheet"]?.GetValue<string>();
        string? category = root["category"]?.GetValue<string>();
        if (category is null)
        {
            GD.PushWarning($"[SpriteRegistry] Missing 'category' in {jsonPath}");
            return;
        }

        int tileW = root["tile_w"]?.GetValue<int>() ?? 32;
        int tileH = root["tile_h"]?.GetValue<int>() ?? 32;

        var entries = root["entries"] as JsonArray;
        if (entries is null)
            return;

        int count = 0;
        foreach (var entry in entries)
        {
            if (entry is null)
                continue;

            string? id = entry["id"]?.GetValue<string>();
            int? col = entry["col"]?.GetValue<int>();
            int? row = entry["row"]?.GetValue<int>();
            if (id is null || col is null || row is null)
                continue;

            string? sheetPath = entry["sheet"]?.GetValue<string>() ?? defaultSheetPath;
            if (string.IsNullOrWhiteSpace(sheetPath))
            {
                GD.PushWarning($"[SpriteRegistry] Missing sheet for '{category}:{id}' in {jsonPath}");
                continue;
            }

            if (UpsertSprite(category, id, sheetPath, col.Value, row.Value, tileW, tileH))
                count++;
        }

        GD.Print($"[SpriteRegistry] '{jsonPath}' -> {count} sprite(s) (category: {category})");
    }

    private static bool UpsertSprite(string category, string id, string sheetPath, int col, int row, int tileW, int tileH)
    {
        var sheet = GetSheet(sheetPath);
        if (sheet is null)
            return false;

        _cache[$"{category}:{id}"] = new AtlasTexture
        {
            Atlas = sheet,
            Region = new Rect2(col * tileW, row * tileH, tileW, tileH),
        };

        return true;
    }
}

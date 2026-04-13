using System.Text.Json;
using System.Text.Json.Serialization;
using DwarfFortress.ContentEditor.Models;

namespace DwarfFortress.ContentEditor.Services;

/// <summary>
/// Reads and writes game definition JSON files from the repo-root data/ directory.
/// </summary>
public class DataService
{
    private readonly string _dataRoot;

    private static readonly JsonSerializerOptions _readOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions _writeOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public DataService(IConfiguration config)
    {
        var relative = config["ContentEditor:DataRoot"] ?? "../../../../../data";
        _dataRoot = ResolveRootPath(relative);
    }

    // ── Items ──────────────────────────────────────────────────────────────

    public List<ItemModel> LoadItems() =>
        Load<List<ItemModel>>("ConfigBundle/items.json") ?? [];

    public void SaveItems(List<ItemModel> items) =>
        Save("ConfigBundle/items.json", items);

    // ── Tiles ──────────────────────────────────────────────────────────────

    public List<TileModel> LoadTiles() =>
        Load<List<TileModel>>("ConfigBundle/tiles.json") ?? [];

    public void SaveTiles(List<TileModel> tiles) =>
        Save("ConfigBundle/tiles.json", tiles);

    // ── Materials ─────────────────────────────────────────────────────────

    public List<MaterialModel> LoadMaterials() =>
        Load<List<MaterialModel>>("ConfigBundle/materials.json") ?? [];

    public void SaveMaterials(List<MaterialModel> materials) =>
        Save("ConfigBundle/materials.json", materials);

    // ── Recipes ───────────────────────────────────────────────────────────

    public List<RecipeModel> LoadRecipes() =>
        Load<List<RecipeModel>>("ConfigBundle/recipes.json") ?? [];

    public void SaveRecipes(List<RecipeModel> recipes) =>
        Save("ConfigBundle/recipes.json", recipes);

    // ── Buildings ─────────────────────────────────────────────────────────

    public List<BuildingModel> LoadBuildings() =>
        Load<List<BuildingModel>>("ConfigBundle/buildings.json") ?? [];

    public void SaveBuildings(List<BuildingModel> buildings) =>
        Save("ConfigBundle/buildings.json", buildings);

    // ── helpers ───────────────────────────────────────────────────────────

    private T? Load<T>(string relPath)
    {
        var full = Path.Combine(_dataRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
            throw new FileNotFoundException($"Data file not found: {full}");
        var json = File.ReadAllText(full);
        return JsonSerializer.Deserialize<T>(json, _readOpts);
    }

    private void Save<T>(string relPath, T value)
    {
        var full = Path.Combine(_dataRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var json = JsonSerializer.Serialize(value, _writeOpts);
        File.WriteAllText(full, json);
    }

    private static string ResolveRootPath(string relative)
    {
        var fromBase = Path.GetFullPath(relative, AppContext.BaseDirectory);
        if (Directory.Exists(fromBase))
            return fromBase;

        return Path.GetFullPath(relative, Directory.GetCurrentDirectory());
    }
}

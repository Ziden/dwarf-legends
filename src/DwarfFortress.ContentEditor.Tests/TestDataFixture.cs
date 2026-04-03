using Microsoft.Extensions.Configuration;
using DwarfFortress.ContentEditor.Services;

namespace DwarfFortress.ContentEditor.Tests;

/// <summary>
/// Creates a temporary data directory populated with minimal JSON fixtures,
/// and a DataService instance pointing at it. Disposed after the test class.
/// </summary>
public sealed class TestDataFixture : IDisposable
{
    public string DataRoot { get; }
    public DataService DataService { get; }

    public const string ItemsJson = """
        [
          { "id": "iron_bar", "displayName": "Iron Bar", "tags": ["metal", "bar"], "weight": 8.0 },
          { "id": "log",      "displayName": "Log",      "tags": ["wood"],         "weight": 10.0 }
        ]
        """;

    public const string TilesJson = """
        [
          { "id": "floor",      "displayName": "Floor",      "isPassable": true,  "isOpaque": false },
          { "id": "stone_wall", "displayName": "Stone Wall",  "isPassable": false, "isOpaque": true, "isMineable": true }
        ]
        """;

    public const string MaterialsJson = """
        [
          { "id": "granite", "displayName": "Granite", "tags": ["stone"],  "hardness": 6.5 },
          { "id": "iron",   "displayName": "Iron",   "tags": ["metal"],  "hardness": 7.5 },
          { "id": "oak",    "displayName": "Oak",    "tags": ["wood"],   "hardness": 3.0 }
        ]
        """;

    public const string RecipesJson = """
        [
          {
            "id": "smelt_iron", "displayName": "Smelt Iron",
            "workshop": "smelter",
            "labor": "smelting",
            "inputs":  [{ "tags": ["ore"], "qty": 1 }],
            "outputs": [{ "itemId": "iron_bar", "qty": 1 }],
            "workTime": 100.0
          }
        ]
        """;

    public const string BuildingsJson = """
        [
          {
            "id": "smelter", "displayName": "Smelter",
            "tags": ["workshop"],
            "footprint": [{ "x": 0, "y": 0, "tile": "floor" }],
            "constructionInputs": [],
            "constructionTime": 200.0,
            "isWorkshop": true
          }
        ]
        """;

    public TestDataFixture()
    {
        DataRoot = Path.Combine(Path.GetTempPath(), $"ce_tests_{Guid.NewGuid():N}");
        WriteFile("ConfigBundle/items.json",     ItemsJson);
        WriteFile("ConfigBundle/tiles.json",     TilesJson);
        WriteFile("ConfigBundle/materials.json", MaterialsJson);
        WriteFile("ConfigBundle/recipes.json",   RecipesJson);
        WriteFile("ConfigBundle/buildings.json", BuildingsJson);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([
                KeyValuePair.Create<string, string?>("ContentEditor:DataRoot", DataRoot)
            ])
            .Build();

        DataService = new DataService(config);
    }

    public void WriteFile(string relative, string json)
    {
        var full = Path.Combine(DataRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json);
    }

    public void Dispose() => Directory.Delete(DataRoot, recursive: true);
}

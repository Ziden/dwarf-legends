using System.Text.Json;
using Microsoft.Extensions.Configuration;
using DwarfFortress.ContentEditor.Models;
using DwarfFortress.ContentEditor.Services;

namespace DwarfFortress.ContentEditor.Tests;

public sealed class DataServiceTests : IDisposable
{
    private readonly TestDataFixture _fixture = new();
    private DataService Svc => _fixture.DataService;

    // ── Items ──────────────────────────────────────────────────────────────

    [Fact]
    public void LoadItems_ReturnsAllItems()
    {
        var items = Svc.LoadItems();
        Assert.Equal(2, items.Count);
        Assert.Contains(items, x => x.Id == "iron_bar");
        Assert.Contains(items, x => x.Id == "log");
    }

    [Fact]
    public void LoadItems_DeserializesTagsList()
    {
        var items = Svc.LoadItems();
        var iron = items.Single(x => x.Id == "iron_bar");
        Assert.Equal(["metal", "bar"], iron.Tags);
    }

    [Fact]
    public void LoadItems_DeserializesFloatProperties()
    {
        var items = Svc.LoadItems();
        Assert.Equal(8.0f, items.Single(x => x.Id == "iron_bar").Weight);
    }

    [Fact]
    public void SaveItems_ThenLoad_RoundTrip()
    {
        var original = Svc.LoadItems();
        original.Add(new ItemModel { Id = "copper_bar", DisplayName = "Copper Bar", Tags = ["metal"], Weight = 7.0f });
        Svc.SaveItems(original);

        var reloaded = Svc.LoadItems();
        Assert.Equal(3, reloaded.Count);
        Assert.Contains(reloaded, x => x.Id == "copper_bar");
    }

    [Fact]
    public void SaveItems_OverwritesExistingEntry()
    {
        var items = Svc.LoadItems();
        var iron = items.Single(x => x.Id == "iron_bar");
        iron.DisplayName = "Iron Ingot";
        Svc.SaveItems(items);

        var reloaded = Svc.LoadItems();
        Assert.Equal("Iron Ingot", reloaded.Single(x => x.Id == "iron_bar").DisplayName);
    }

    [Fact]
    public void SaveItems_PreservesTagsList()
    {
        var items = Svc.LoadItems();
        var iron = items.Single(x => x.Id == "iron_bar");
        iron.Tags = ["metal", "bar", "refined"];
        Svc.SaveItems(items);

        var reloaded = Svc.LoadItems();
        Assert.Equal(["metal", "bar", "refined"], reloaded.Single(x => x.Id == "iron_bar").Tags);
    }

    [Fact]
    public void LoadItems_ThrowsFileNotFound_WhenFileMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([
                KeyValuePair.Create<string, string?>("ContentEditor:DataRoot",
                    Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}"))
            ])
            .Build();
        var svc = new DataService(config);

        Assert.Throws<FileNotFoundException>(() => svc.LoadItems());
    }

    // ── Materials ─────────────────────────────────────────────────────────

    [Fact]
    public void LoadMaterials_ReturnsAllMaterials()
    {
        var mats = Svc.LoadMaterials();
        Assert.Equal(2, mats.Count);
        Assert.Contains(mats, x => x.Id == "iron");
        Assert.Contains(mats, x => x.Id == "oak");
    }

    [Fact]
    public void SaveMaterials_ThenLoad_RoundTrip()
    {
        var mats = Svc.LoadMaterials();
        mats.First().Hardness = 9.9f;
        Svc.SaveMaterials(mats);

        var reloaded = Svc.LoadMaterials();
        Assert.Equal(9.9f, reloaded.First(x => x.Id == "iron").Hardness, precision: 2);
    }

    // ── Tiles ─────────────────────────────────────────────────────────────

    [Fact]
    public void LoadTiles_ReturnsAllTiles()
    {
        var tiles = Svc.LoadTiles();
        Assert.Equal(2, tiles.Count);
        Assert.Contains(tiles, x => x.Id == "floor");
    }

    [Fact]
    public void LoadTiles_DeserializesBoolProperties()
    {
        var tiles = Svc.LoadTiles();
        var wall = tiles.Single(x => x.Id == "stone_wall");
        Assert.False(wall.IsPassable);
        Assert.True(wall.IsOpaque);
        Assert.True(wall.IsMineable);
    }

    [Fact]
    public void SaveTiles_ThenLoad_RoundTrip()
    {
        var tiles = Svc.LoadTiles();
        tiles.Add(new TileModel { Id = "dirt", DisplayName = "Dirt", IsPassable = true });
        Svc.SaveTiles(tiles);

        var reloaded = Svc.LoadTiles();
        Assert.Equal(3, reloaded.Count);
        Assert.Contains(reloaded, x => x.Id == "dirt");
    }

    // ── Recipes ───────────────────────────────────────────────────────────

    [Fact]
    public void LoadRecipes_ReturnsAllRecipes()
    {
        var recipes = Svc.LoadRecipes();
        Assert.Single(recipes);
        var r = recipes[0];
        Assert.Equal("smelt_iron", r.Id);
        Assert.Equal("smelter", r.Workshop);
        Assert.Equal("iron_bar", r.Outputs[0].ItemId);
    }

    [Fact]
    public void SaveRecipes_ThenLoad_RoundTrip()
    {
        var recipes = Svc.LoadRecipes();
        recipes[0].SkillXp = 50;
        Svc.SaveRecipes(recipes);

        var reloaded = Svc.LoadRecipes();
        Assert.Equal(50, reloaded[0].SkillXp);
    }

    // ── Buildings ─────────────────────────────────────────────────────────

    [Fact]
    public void LoadBuildings_ReturnsAllBuildings()
    {
        var buildings = Svc.LoadBuildings();
        Assert.Single(buildings);
        Assert.Equal("smelter", buildings[0].Id);
        Assert.True(buildings[0].IsWorkshop);
    }

    [Fact]
    public void SaveBuildings_ThenLoad_RoundTrip()
    {
        var buildings = Svc.LoadBuildings();
        buildings[0].ConstructionTime = 300f;
        Svc.SaveBuildings(buildings);

        var reloaded = Svc.LoadBuildings();
        Assert.Equal(300f, reloaded[0].ConstructionTime);
    }

    [Fact]
    public void SaveBuildings_Preserves_Full_Building_Schema()
    {
        var buildings = Svc.LoadBuildings();
        var smelter = buildings[0];
        smelter.ResidenceCapacity = 3;
        smelter.ProducedSmokeId = "smoke";
        smelter.AutoStockpileAcceptedTags = ["food", "drink"];
        smelter.ConstructionInputs =
        [
            new BuildingInput { Tags = ["stone"], Qty = 2, MaterialId = "iron" }
        ];
        smelter.DiscoveryInputs =
        [
            new BuildingInput { ItemDefId = "log", Qty = 1 }
        ];
        smelter.Entries =
        [
            new BuildingEntryModel { X = 0, Y = 0, OutwardDirection = "south" }
        ];
        smelter.VisualProfile = new BuildingVisualProfileModel
        {
            Archetype = "workshop",
            Palette = "smelter",
            HideRoofOnHover = true,
        };

        Svc.SaveBuildings(buildings);

        var reloaded = Svc.LoadBuildings()[0];
        Assert.Equal(3, reloaded.ResidenceCapacity);
        Assert.Equal("smoke", reloaded.ProducedSmokeId);
        Assert.Equal(["food", "drink"], reloaded.AutoStockpileAcceptedTags);
        Assert.Equal(["stone"], reloaded.ConstructionInputs[0].Tags);
        Assert.Equal("iron", reloaded.ConstructionInputs[0].MaterialId);
        Assert.Equal("log", reloaded.DiscoveryInputs[0].ItemDefId);
        Assert.Equal("south", reloaded.Entries[0].OutwardDirection);
        Assert.NotNull(reloaded.VisualProfile);
        Assert.Equal("workshop", reloaded.VisualProfile!.Archetype);
        Assert.True(reloaded.VisualProfile.HideRoofOnHover);
    }

    // ── JSON format ───────────────────────────────────────────────────────

    [Fact]
    public void SaveItems_WritesValidJson()
    {
        Svc.SaveItems(Svc.LoadItems());

        var path = Path.Combine(_fixture.DataRoot, "ConfigBundle", "items.json");
        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void LoadItems_WithDefaultConfiguration_UsesRepoDataRoot()
    {
        var svc = new DataService(new ConfigurationBuilder().Build());

        var items = svc.LoadItems();

        Assert.Contains(items, x => x.Id == "iron_bar");
        Assert.Contains(items, x => x.Id == "log");
    }
}

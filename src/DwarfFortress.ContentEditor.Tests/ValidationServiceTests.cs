using DwarfFortress.ContentEditor.Models;
using DwarfFortress.ContentEditor.Services;

namespace DwarfFortress.ContentEditor.Tests;

public sealed class ValidationServiceTests : IDisposable
{
    private readonly TestDataFixture _fixture = new();

    private ValidationService CreateSvc() =>
        new(_fixture.DataService);

    // ── No errors on valid data ────────────────────────────────────────────

    [Fact]
    public void Validate_ReturnsNoErrors_ForValidFixtureData()
    {
        var errors = CreateSvc().Validate();
        Assert.Empty(errors);
    }

    // ── Duplicate ID detection ─────────────────────────────────────────────

    [Fact]
    public void Validate_DetectsItemDuplicateIds()
    {
        var items = _fixture.DataService.LoadItems();
        items.Add(new ItemModel { Id = "iron_bar", DisplayName = "Duplicate" });
        _fixture.DataService.SaveItems(items);

        var errors = CreateSvc().Validate();
        Assert.Contains(errors, e => e.Category == "items" && e.ItemId == "iron_bar" && e.Message == "Duplicate ID");
    }

    [Fact]
    public void Validate_DetectsTileDuplicateIds()
    {
        var tiles = _fixture.DataService.LoadTiles();
        tiles.Add(new TileModel { Id = "floor", DisplayName = "Duplicate Floor" });
        _fixture.DataService.SaveTiles(tiles);

        var errors = CreateSvc().Validate();
        Assert.Contains(errors, e => e.Category == "tiles" && e.ItemId == "floor");
    }

    [Fact]
    public void Validate_DetectsMaterialDuplicateIds()
    {
        var mats = _fixture.DataService.LoadMaterials();
        mats.Add(new MaterialModel { Id = "iron", DisplayName = "Duplicate Iron" });
        _fixture.DataService.SaveMaterials(mats);

        var errors = CreateSvc().Validate();
        Assert.Contains(errors, e => e.Category == "materials" && e.ItemId == "iron");
    }

    // ── Recipe cross-references ────────────────────────────────────────────

    [Fact]
    public void Validate_DetectsMissingWorkshop()
    {
        var recipes = _fixture.DataService.LoadRecipes();
        recipes[0].Workshop = "nonexistent_workshop";
        _fixture.DataService.SaveRecipes(recipes);

        var errors = CreateSvc().Validate();
        Assert.Contains(errors, e =>
            e.Category == "recipes" &&
            e.Message.Contains("nonexistent_workshop"));
    }

    [Fact]
    public void Validate_AllowsEmptyWorkshop()
    {
        var recipes = _fixture.DataService.LoadRecipes();
        recipes[0].Workshop = "";
        _fixture.DataService.SaveRecipes(recipes);

        var errors = CreateSvc().Validate();
        Assert.DoesNotContain(errors, e => e.Category == "recipes" && e.Message.Contains("Workshop"));
    }

    [Fact]
    public void Validate_DetectsMissingOutputItem()
    {
        var recipes = _fixture.DataService.LoadRecipes();
        recipes[0].Outputs.Add(new RecipeOutput { ItemId = "ghost_item", Qty = 1 });
        _fixture.DataService.SaveRecipes(recipes);

        var errors = CreateSvc().Validate();
        Assert.Contains(errors, e =>
            e.Category == "recipes" &&
            e.Message.Contains("ghost_item"));
    }

    [Fact]
    public void Validate_AllowsKnownOutputItem()
    {
        // iron_bar is a known item — recipe output referencing it should be clean
        var errors = CreateSvc().Validate();
        Assert.DoesNotContain(errors, e => e.Message.Contains("iron_bar"));
    }

    [Fact]
    public void Validate_Allows_Derived_Output_With_Material_Selector()
    {
        var recipes = _fixture.DataService.LoadRecipes();
        recipes[0].Outputs[0].ItemId = "";
        recipes[0].Outputs[0].FormRole = "bar";
        recipes[0].Outputs[0].MaterialFrom = "material:iron";
        _fixture.DataService.SaveRecipes(recipes);

        var errors = CreateSvc().Validate();
        Assert.DoesNotContain(errors, e => e.Category == "recipes" && e.ItemId == recipes[0].Id);
    }

    [Fact]
    public void Validate_Detects_Derived_Output_Missing_Material_Selector()
    {
        var recipes = _fixture.DataService.LoadRecipes();
        recipes[0].Outputs[0].ItemId = "";
        recipes[0].Outputs[0].FormRole = "bar";
        recipes[0].Outputs[0].MaterialFrom = "";
        _fixture.DataService.SaveRecipes(recipes);

        var errors = CreateSvc().Validate();
        Assert.Contains(errors, e =>
            e.Category == "recipes" &&
            e.ItemId == recipes[0].Id &&
            e.Message.Contains("requires 'materialFrom'"));
    }

    [Fact]
    public void Validate_Detects_Missing_Recipe_Input_Item_And_Material()
    {
        var recipes = _fixture.DataService.LoadRecipes();
        recipes[0].Inputs[0].ItemId = "ghost_item";
        recipes[0].Inputs[0].MaterialId = "ghost_material";
        _fixture.DataService.SaveRecipes(recipes);

        var errors = CreateSvc().Validate();
        Assert.Contains(errors, e => e.Category == "recipes" && e.Message.Contains("ghost_item"));
        Assert.Contains(errors, e => e.Category == "recipes" && e.Message.Contains("ghost_material"));
    }

    // ── Error accumulation ─────────────────────────────────────────────────

    [Fact]
    public void Validate_ReturnsMultipleErrors_WhenMultipleProblems()
    {
        // Add a duplicate tile ID AND a broken recipe output in one pass
        var tiles = _fixture.DataService.LoadTiles();
        tiles.Add(new TileModel { Id = "floor", DisplayName = "Dup" });
        _fixture.DataService.SaveTiles(tiles);

        var recipes = _fixture.DataService.LoadRecipes();
        recipes[0].Outputs.Add(new RecipeOutput { ItemId = "missing_item", Qty = 1 });
        _fixture.DataService.SaveRecipes(recipes);

        var errors = CreateSvc().Validate();
        Assert.True(errors.Count >= 2);
    }

    [Fact]
    public void Validate_DetectsInvalidWorldgenHistoryFigureHooks()
    {
        _fixture.WriteFile(
            "ConfigBundle/worldgen/worldgen_content.json",
            """
            {
              "geologyProfiles": [
                {
                  "id": "test_geology",
                  "seedSalt": 99,
                  "aquiferDepthFraction": 0.1,
                  "layers": [
                    { "rockTypeId": "granite", "thicknessMin": 2, "thicknessMax": 3 }
                  ]
                }
              ],
              "historyFigures": {
                "professionProfiles": [
                  { "id": "scribe", "laborIds": ["hauling"] }
                ],
                "professionSelectionRules": [
                  { "speciesDefId": "dwarf", "professionIds": ["missing_profession"] }
                ]
              }
            }
            """);

        var errors = CreateSvc().Validate();
        Assert.Contains(errors, e =>
            e.Category == "worldgen" &&
            e.ItemId == "worldgen_content" &&
            e.Message.Contains("missing_profession"));
    }

    public void Dispose() => _fixture.Dispose();
}

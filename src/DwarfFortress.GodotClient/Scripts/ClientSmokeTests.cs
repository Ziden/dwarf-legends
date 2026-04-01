using System;
using System.Linq;
using System.Reflection;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Godot;

public partial class ClientSmokeTests : Node
{
    public override async void _Ready()
    {
        try
        {
            RunDataPathTest();
            RunBootstrapWorldQueryTest();
            RunPixelArtFactoryTest();
            RunWorkshopRecipeFilterTest();
            RunWorkshopProductionQueueTest();
            RunWorkshopPanelShowsOnlyItsRecipesTest();
            await RunGameRootStartupTest();

            GD.Print("[ClientSmokeTests] All smoke tests passed.");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"[ClientSmokeTests] FAILURE: {exception}");
            GetTree().Quit(1);
        }
    }

    private static void RunDataPathTest()
    {
        var dataPath = ClientSimulationFactory.ResolveDataPath();
        Assert(System.IO.Directory.Exists(dataPath), $"Resolved data path does not exist: {dataPath}");
    }

    private static void RunBootstrapWorldQueryTest()
    {
        var simulation = ClientSimulationFactory.CreateSimulation(seed: 7, width: 32, height: 32, depth: 4);
        var map = simulation.Context.Get<WorldMap>();
        var registry = simulation.Context.Get<EntityRegistry>();
        var items = simulation.Context.Get<ItemSystem>();
        var stockpiles = simulation.Context.Get<StockpileManager>();
        var buildings = simulation.Context.Get<BuildingSystem>();
        var queries = simulation.Context.Get<WorldQuerySystem>();
        var time = queries.GetTimeView();

        Assert(CountNonEmptyTiles(map) > 0, "Expected generated world tiles.");
        Assert(registry.CountAlive<Dwarf>() > 0, "Expected starting dwarves.");
        Assert(items.GetAllItems().Any(), "Expected starting items.");
        Assert(stockpiles.GetAll().Any(), "Expected starting stockpiles.");
        Assert(buildings.GetAll().Any(), "Expected starting buildings.");

        simulation.Tick(0.5f);
        var updatedTime = queries.GetTimeView();
        Assert(updatedTime.Hour >= time.Hour, "Expected time to advance or remain stable within the same day.");
        Assert(registry.CountAlive<Dwarf>() > 0, "Unexpected dwarf count change during smoke test tick.");
    }

    private static void RunPixelArtFactoryTest()
    {
        var tile = PixelArtFactory.GetTile("soil");
        var dwarf = PixelArtFactory.GetEntity("dwarf");
        var item = PixelArtFactory.GetItem("log");

        Assert(tile.GetSize() == new Vector2I(PixelArtFactory.Size, PixelArtFactory.Size), "Tile texture size mismatch.");
        Assert(dwarf.GetSize() == new Vector2I(PixelArtFactory.Size, PixelArtFactory.Size), "Entity texture size mismatch.");
        Assert(item.GetSize() == new Vector2I(PixelArtFactory.Size, PixelArtFactory.Size), "Item texture size mismatch.");
        Assert(ReferenceEquals(tile, PixelArtFactory.GetTile("soil")), "Expected cached tile texture instance.");

        var itemIds = typeof(ItemDefIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        foreach (var itemId in itemIds)
        {
            var texture = PixelArtFactory.GetItem(itemId);
            Assert(texture.GetSize() == new Vector2I(PixelArtFactory.Size, PixelArtFactory.Size),
                $"Item texture size mismatch for '{itemId}'.");
        }
    }

    // ── Workshop tests ─────────────────────────────────────────────────────

    /// <summary>Recipes returned for a workshop def must only belong to that workshop.</summary>
    private static void RunWorkshopRecipeFilterTest()
    {
        var sim = ClientSimulationFactory.CreateSimulation(seed: 1, width: 16, height: 16, depth: 2);
        var dm  = sim.Context.Get<DwarfFortress.GameLogic.Data.DataManager>();

        var carpenterRecipes = dm.Recipes.All()
            .Where(r => r.WorkshopDefId == "carpenter_workshop")
            .ToList();
        var smelterRecipes = dm.Recipes.All()
            .Where(r => r.WorkshopDefId == "smelter")
            .ToList();

        Assert(carpenterRecipes.Count > 0,  "Expected at least one recipe for 'carpenter_workshop'.");
        Assert(smelterRecipes.Count > 0,    "Expected at least one recipe for 'smelter'.");
        Assert(!carpenterRecipes.Any(r => r.WorkshopDefId == "smelter"),
            "Carpenter recipes must not contain smelter recipes.");
        Assert(!smelterRecipes.Any(r => r.WorkshopDefId == "carpenter_workshop"),
            "Smelter recipes must not contain carpenter recipes.");
    }

    /// <summary>Dispatching SetProductionOrderCommand queues the recipe for the correct workshop.</summary>
    private static void RunWorkshopProductionQueueTest()
    {
        var sim      = ClientSimulationFactory.CreateSimulation(seed: 1, width: 16, height: 16, depth: 2);
        var workshop = sim.Context.Get<BuildingSystem>().GetAll().FirstOrDefault(b => b.IsWorkshop);

        Assert(workshop is not null, "Expected at least one workshop in the bootstrapped fortress.");

        sim.Context.Commands.Dispatch(new SetProductionOrderCommand(workshop!.Id, "make_plank", 2));

        var queue = sim.Context.Get<RecipeSystem>().GetOrCreateQueue(workshop.Id);
        var order = queue.Peek();

        Assert(order is not null,            "Production queue should not be empty after dispatching an order.");
        Assert(order!.RecipeId == "make_plank", "Queued recipe ID should be 'make_plank'.");
        Assert(order.Remaining  == 2,           "Queued order remaining count should be 2.");
    }

    /// <summary>WorkshopPanel only builds recipe buttons for the selected workshop's def.</summary>
    private void RunWorkshopPanelShowsOnlyItsRecipesTest()
    {
        var sim      = ClientSimulationFactory.CreateSimulation(seed: 1, width: 16, height: 16, depth: 2);
        var workshop = sim.Context.Get<WorldQuerySystem>()
            .GetBuildingView(sim.Context.Get<BuildingSystem>().GetAll().First(b => b.IsWorkshop).Id);

        if (workshop is null)
            throw new InvalidOperationException("Expected at least one workshop for panel test.");

        var panel = GD.Load<PackedScene>("res://Scenes/UI/WorkshopPanel.tscn").Instantiate<WorkshopPanel>();
        AddChild(panel);
        panel.Setup(sim);
        panel.ShowBuilding(workshop!);

        var recipeList = FindItemSelectionList(panel);
        if (recipeList is null)
            throw new InvalidOperationException("WorkshopPanel should contain an ItemSelectionList for recipe entries.");

        var recipeBox = recipeList.GetChildOrNull<VBoxContainer>(0);
        if (recipeBox is null)
            throw new InvalidOperationException("ItemSelectionList should contain a VBoxContainer with recipe cards.");

        var dm = sim.Context.Get<DwarfFortress.GameLogic.Data.DataManager>();
        var expectedIds = dm.Recipes.All()
            .Where(r => r.WorkshopDefId == workshop.BuildingDefId)
            .Select(r => r.Id)
            .ToHashSet();

        int buttonCount = 0;
        foreach (var child in recipeBox.GetChildren())
        {
            if (child is Control card)
            {
                buttonCount += CountButtons(card);
            }
        }

        Assert(buttonCount == expectedIds.Count,
            $"WorkshopPanel should show {expectedIds.Count} recipe button(s) for '{workshop.BuildingDefId}', but found {buttonCount}.");

        panel.QueueFree();
    }

    private static ItemSelectionList? FindItemSelectionList(Control root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is ItemSelectionList list) return list;
            if (child is Control c)
            {
                var found = FindItemSelectionList(c);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static int CountButtons(Control root)
    {
        int count = root is Button ? 1 : 0;
        foreach (var child in root.GetChildren())
            if (child is Control control)
                count += CountButtons(control);
        return count;
    }

    private async System.Threading.Tasks.Task RunGameRootStartupTest()
    {
        var gameRoot = GD.Load<PackedScene>("res://Scenes/Main.tscn").Instantiate<GameRoot>();
        AddChild(gameRoot);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Assert(gameRoot.StartupError is null, $"GameRoot startup failed: {gameRoot.StartupError}");
        Assert(gameRoot.IsSimulationReady, "GameRoot did not finish startup.");

        gameRoot.QueueFree();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static int CountNonEmptyTiles(WorldMap map)
    {
        int count = 0;
        for (int x = 0; x < map.Width; x++)
        for (int y = 0; y < map.Height; y++)
        for (int z = 0; z < map.Depth; z++)
            if (map.GetTile(new Vec3i(x, y, z)).TileDefId != TileDefIds.Empty)
                count++;

        return count;
    }
}
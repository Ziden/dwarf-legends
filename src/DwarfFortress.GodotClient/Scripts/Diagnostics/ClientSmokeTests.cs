using System;
using System.Linq;
using System.Reflection;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using DwarfFortress.GodotClient.Rendering;
using Godot;

namespace DwarfFortress.GodotClient.Diagnostics;

public partial class ClientSmokeTests : Node
{
    private static readonly string[] SmokeFilters = (System.Environment.GetEnvironmentVariable("DF_SMOKE_FILTER") ?? string.Empty)
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public override async void _Ready()
    {
        try
        {
            if (ShouldRun("data"))
                RunDataPathTest();

            if (ShouldRun("bootstrap"))
                RunBootstrapWorldQueryTest();

            if (ShouldRun("pixel-art"))
                RunPixelArtFactoryTest();

            if (ShouldRun("workshop-filter"))
                RunWorkshopRecipeFilterTest();

            if (ShouldRun("workshop-queue"))
                RunWorkshopProductionQueueTest();

            if (ShouldRun("workshop-panel"))
                RunWorkshopPanelShowsOnlyItsRecipesTest();

            if (ShouldRun("dwarf-lore"))
                RunDwarfPanelLoreTabTest();

            if (ShouldRun("tile-inspector"))
                RunTileInspectorGeneralItemTest();

            if (ShouldRun("event-log-jump"))
                await RunEventLogJumpSelectionTest();

            if (ShouldRun("startup"))
                await RunGameRootStartupTest();

            if (ShouldRun("render-mode-residency"))
                await RunGameRootRenderResidencyTest();

            if (ShouldRun("camera-3d-controls"))
                RunWorldCamera3DControllerControlsTest();

            if (ShouldRun("story-inspector"))
                await RunStoryInspectorOpenTest();

            if (ShouldRun("announcement-log"))
                await RunAnnouncementLogUiTest();

            if (ShouldRun("billboard-hover"))
                await RunBillboardHoverSelectionTest();

            if (ShouldRun("resource-billboard-hover"))
                await RunResourceBillboardHoverSelectionTest();

            if (ShouldRun("resource-billboard-area-selection"))
                await RunResourceBillboardAreaSelectionHighlightTest();

            if (ShouldRun("selection-view"))
                await RunSelectionViewHarvestTest();

            if (ShouldRun("selection-view-plants"))
                await RunSelectionViewMixedPlantHarvestTest();

            if (ShouldRun("billboard-pause"))
                await RunBillboardPauseInterpolationTest();

            if (ShouldRun("combat-cue-3d"))
                await RunCombatCue3DSmokeTest();

            GD.Print("[ClientSmokeTests] All smoke tests passed.");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"[ClientSmokeTests] FAILURE: {exception}");
            GetTree().Quit(1);
        }
    }

    private static bool ShouldRun(string testName)
    {
        if (SmokeFilters.Length == 0)
            return true;

        return SmokeFilters.Any(filter => string.Equals(filter, testName, StringComparison.OrdinalIgnoreCase));
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
        var customLog = PixelArtFactory.GetItem("glowwood_log", "glowwood_wood");
        var customOre = PixelArtFactory.GetItem("mithril_ore", "mithril");
        var customBar = PixelArtFactory.GetItem("mithril_bar", "mithril");

        Assert(tile.GetSize() == new Vector2I(PixelArtFactory.Size, PixelArtFactory.Size), "Tile texture size mismatch.");
        Assert(dwarf.GetSize() == new Vector2I(PixelArtFactory.Size, PixelArtFactory.Size), "Entity texture size mismatch.");
        Assert(item.GetSize() == new Vector2I(PixelArtFactory.Size, PixelArtFactory.Size), "Item texture size mismatch.");
        Assert(customLog.GetSize() == new Vector2I(PixelArtFactory.Size, PixelArtFactory.Size), "Custom log texture size mismatch.");
        Assert(customOre.GetSize() == new Vector2I(PixelArtFactory.Size, PixelArtFactory.Size), "Custom ore texture size mismatch.");
        Assert(customBar.GetSize() == new Vector2I(PixelArtFactory.Size, PixelArtFactory.Size), "Custom bar texture size mismatch.");
        Assert(ReferenceEquals(tile, PixelArtFactory.GetTile("soil")), "Expected cached tile texture instance.");
        Assert(ReferenceEquals(customLog, PixelArtFactory.GetItem("glowwood_log", "glowwood_wood")), "Expected cached custom log texture instance.");

        var grassTile = new TileRenderData(TileDefIds.Grass, null);
        TileRenderData? ResolveSurfaceTile(int sx, int sy, int sz)
        {
            if (sx == 1 && sy == 0 && sz == 0)
                return new TileRenderData(TileDefIds.Soil, null);

            return sx == 0 && sy == 0 && sz == 0
                ? grassTile
                : null;
        }

        var terrainSurface = TileSurfaceLibrary.GetOrCreateTexture(grassTile, 0, 0, 0, ResolveSurfaceTile, materialId => GroundMaterialResolver.ResolveGroundTileDefId(materialId));
        var repeatedTerrainSurface = TileSurfaceLibrary.GetOrCreateTexture(grassTile, 0, 0, 0, ResolveSurfaceTile, materialId => GroundMaterialResolver.ResolveGroundTileDefId(materialId));
        var terrainLayer = TileSurfaceLibrary.GetOrCreateArrayLayer(grassTile, 0, 0, 0, ResolveSurfaceTile, materialId => GroundMaterialResolver.ResolveGroundTileDefId(materialId));
        var repeatedTerrainLayer = TileSurfaceLibrary.GetOrCreateArrayLayer(grassTile, 0, 0, 0, ResolveSurfaceTile, materialId => GroundMaterialResolver.ResolveGroundTileDefId(materialId));

        Assert(ReferenceEquals(terrainSurface, repeatedTerrainSurface), "Expected shared terrain surface textures to be cached.");
        Assert(terrainLayer == repeatedTerrainLayer, "Expected shared terrain surface array layers to stay stable.");
        Assert(TileSurfaceLibrary.GetTextureArray() is not null, "Expected terrain surface library to build a texture array for 3D terrain.");

        var oreTile = new TileRenderData(TileDefIds.StoneWall, "granite", OreItemDefId: ItemDefIds.IronOre);
        var hasOreDetailLayer = TerrainDetailOverlayLibrary.TryGetOrCreateArrayLayer(oreTile, 3, 4, 0, out var oreDetailLayer);
        var repeatedOreDetailLayer = TerrainDetailOverlayLibrary.TryGetOrCreateArrayLayer(oreTile, 3, 4, 0, out var repeatedOreLayer);
        var plainTileHasDetailLayer = TerrainDetailOverlayLibrary.TryGetOrCreateArrayLayer(grassTile, 0, 0, 0, out _);

        Assert(hasOreDetailLayer, "Expected ore-bearing terrain to produce a shared terrain detail layer.");
        Assert(repeatedOreDetailLayer && oreDetailLayer == repeatedOreLayer, "Expected terrain detail array layers to stay stable for the same ore tile recipe.");
        Assert(!plainTileHasDetailLayer, "Expected non-ore terrain to skip terrain detail layers.");
        Assert(TerrainDetailOverlayLibrary.GetTextureArray() is not null, "Expected terrain detail overlay library to build a texture array for 3D terrain details.");

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

    // â”€â”€ Workshop tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    private void RunDwarfPanelLoreTabTest()
    {
        var sim = ClientSimulationFactory.CreateSimulation(seed: 7, width: 24, height: 24, depth: 4);
        var queries = sim.Context.Get<WorldQuerySystem>();
        var dwarf = queries.GetDwarfView(sim.Context.Get<EntityRegistry>().GetAlive<Dwarf>().First().Id);

        if (dwarf is null)
            throw new InvalidOperationException("Expected a dwarf view for lore tab smoke test.");

        var panel = GD.Load<PackedScene>("res://Scenes/UI/DwarfPanel.tscn").Instantiate<DwarfPanel>();
        AddChild(panel);
        panel.Setup(sim);
        panel.ShowDwarf(dwarf);

        var tabs = panel.GetNode<TabContainer>("%Tabs");
        var loreBox = panel.GetNode<VBoxContainer>("%LoreBox");
        var loreText = string.Join("\n", CollectText(loreBox));

        Assert(panel.Visible, "Dwarf panel should be visible after selecting a dwarf.");
        Assert(tabs.GetChildCount() >= 10, "Expected dwarf panel to expose the lore tab.");
        Assert(loreBox.GetChildCount() > 0, "Expected lore tab to contain rendered content.");
        Assert(loreText.Contains("Historical figure:", StringComparison.Ordinal), "Expected dwarf lore tab to show historical figure provenance.");
        Assert(loreText.Contains("Civilization:", StringComparison.Ordinal), "Expected dwarf lore tab to show civilization provenance.");
        Assert(loreText.Contains("Embark Context", StringComparison.Ordinal), "Expected dwarf lore tab to show embark context.");

        panel.QueueFree();
    }

    private void RunTileInspectorGeneralItemTest()
    {
        var sim = ClientSimulationFactory.CreateSimulation(seed: 7, width: 24, height: 24, depth: 4);
        var queries = sim.Context.Get<WorldQuerySystem>();
        var item = queries.GetItemView(sim.Context.Get<ItemSystem>().GetAllItems().First().Id);

        if (item is null)
            throw new InvalidOperationException("Expected an item view for tile inspector test.");

        var panel = GD.Load<PackedScene>("res://Scenes/UI/TileInfoPanel.tscn").Instantiate<TileInfoPanel>();
        AddChild(panel);
        panel.Setup(sim);
        panel.ShowItem(item);

        var contentLabel = panel.GetNode<Label>("%ContentLabel");
        var details = contentLabel.Text;

        Assert(panel.Visible || !panel.Visible, "Tile inspector should instantiate without requiring scene-specific visibility state.");
        Assert(details.Contains("Selected Item", StringComparison.Ordinal), "Expected tile inspector to render the selected item section.");
        Assert(details.Contains(item.DisplayName, StringComparison.Ordinal), "Expected tile inspector to include the selected item display name.");
        Assert(details.Contains("Tile:", StringComparison.Ordinal), "Expected tile inspector to include surrounding tile context for selected items.");

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

    private static System.Collections.Generic.IEnumerable<string> CollectText(Node root)
    {
        if (root is Label label && !string.IsNullOrWhiteSpace(label.Text))
            yield return label.Text;
        else if (root is Button button && !string.IsNullOrWhiteSpace(button.Text))
            yield return button.Text;

        foreach (var child in root.GetChildren())
        {
            foreach (var text in CollectText(child))
                yield return text;
        }
    }

    private async System.Threading.Tasks.Task RunEventLogJumpSelectionTest()
    {
        var gameRoot = await StartGameRootAsync();
        var input = gameRoot.GetNode<InputController>("%InputController");

        var simulationField = typeof(GameRoot).GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic);
        if (simulationField?.GetValue(gameRoot) is not GameSimulation simulation)
            throw new InvalidOperationException("Could not access GameRoot simulation for event-log jump test.");

        var registry = simulation.Context.Get<EntityRegistry>();
        var items = simulation.Context.Get<ItemSystem>();
        var jumpMethod = typeof(GameRoot).GetMethod("JumpToLinkedEventTarget", BindingFlags.Instance | BindingFlags.NonPublic);
        if (jumpMethod is null)
            throw new InvalidOperationException("Could not access GameRoot event-log jump handler.");

        var creature = registry.GetAlive<Creature>().First();
        var creatureJumped = (bool)jumpMethod.Invoke(gameRoot, [new EventLogLinkTarget(creature.Id, EventLogLinkType.Entity, creature.DefId, creature.DefId)])!;
        Assert(creatureJumped, "Expected GameRoot to jump to a linked creature target.");
        Assert(input.SelectedCreatureId == creature.Id, "Linked creature jumps should switch the current selection to that creature.");

        var item = items.GetAllItems().First();
        var itemJumped = (bool)jumpMethod.Invoke(gameRoot, [new EventLogLinkTarget(item.Id, EventLogLinkType.Item, item.DefId, item.DefId, item.MaterialId)])!;
        Assert(itemJumped, "Expected GameRoot to jump to a linked item target.");
        Assert(input.SelectedItemId == item.Id, "Linked item jumps should switch the current selection to that item.");

        gameRoot.QueueFree();
    }

    private async System.Threading.Tasks.Task<GameRoot> StartGameRootAsync()
    {
        var gameRoot = GD.Load<PackedScene>("res://Scenes/Main.tscn").Instantiate<GameRoot>();
        AddChild(gameRoot);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        for (var index = 0; index < 10; index++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Assert(gameRoot.StartupError is null, $"GameRoot startup failed: {gameRoot.StartupError}");
        Assert(gameRoot.IsSimulationReady, "GameRoot did not finish startup.");
        return gameRoot;
    }

    private async System.Threading.Tasks.Task<WorldGenViewerRoot> StartWorldGenViewerAsync()
    {
        var viewer = GD.Load<PackedScene>("res://Scenes/WorldGenViewer.tscn").Instantiate<WorldGenViewerRoot>();

        AddChild(viewer);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        for (var index = 0; index < 10; index++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        return viewer;
    }

    private async System.Threading.Tasks.Task RunGameRootStartupTest()
    {
        var gameRoot = await StartGameRootAsync();

        var world3DRoot = gameRoot.GetNodeOrNull<WorldRender3D>("%World3DRoot");
        var mainCamera3D = gameRoot.GetNodeOrNull<Camera3D>("%MainCamera3D");
        Assert(world3DRoot is not null, "Main scene should expose a 3D world root.");
        Assert(mainCamera3D is not null, "Main scene should expose an orthographic Camera3D.");
        Assert(mainCamera3D!.Current, "Main scene should boot directly into the 3D camera.");

        var spriteCounts = world3DRoot!.GetDebugSpriteCounts();
        Assert(spriteCounts.Dwarves > 0, "3D world should create dwarf sprite billboards on startup.");
        Assert(spriteCounts.Items > 0, "3D world should create item-like sprite billboards on startup.");
        Assert(spriteCounts.Plants > 0 || spriteCounts.Trees > 0, "3D world should create vegetation sprite billboards on startup.");
        Assert(world3DRoot.GetDebugChunkMeshCount() > 0, "3D world should create terrain chunk meshes on startup.");
        Assert(world3DRoot.HasDebugStockpileOverlay(), "3D world should create stockpile overlay meshes on startup.");

        var topBar = gameRoot.GetNode<TopBar>("%TopBar");
        var debugButton = topBar.GetNode<Button>("%DebugButton");
        var debugWindow = gameRoot.GetNode<DebugWindow>("%DebugWindow");

        debugButton.EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Assert(debugWindow.Visible, "Debug window should open after pressing the DEBUG button.");

        gameRoot.QueueFree();
    }

    private async System.Threading.Tasks.Task RunStoryInspectorOpenTest()
    {
        var gameRoot = await StartGameRootAsync();
        try
        {
            var topBar = gameRoot.GetNode<TopBar>("%TopBar");
            var debugButton = topBar.GetNode<Button>("%DebugButton");
            var debugWindow = gameRoot.GetNode<DebugWindow>("%DebugWindow");
            var gameplayInspector = gameRoot.GetNode<StoryInspectorPanel>("%StoryInspectorPanel");

            debugButton.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var storyButton = debugWindow.FindChild("StoryButton", recursive: true, owned: false) as Button;
            Assert(storyButton is not null, "Debug window should expose a story inspector button.");

            storyButton!.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Assert(gameplayInspector.Visible, "Gameplay story inspector should open from the debug window.");
            Assert(gameplayInspector.DebugSourceText.Contains("Canonical runtime history", StringComparison.Ordinal), "Gameplay story inspector should identify the canonical runtime source.");
            Assert(gameplayInspector.DebugOverviewText.Contains("Civilizations", StringComparison.Ordinal), "Gameplay story inspector should render story scope details.");
        }
        finally
        {
            gameRoot.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        var viewer = await StartWorldGenViewerAsync();
        try
        {
            var worldgenButton = viewer.GetNode<Button>("%StoryInspectorBtn");
            var worldgenInspector = viewer.GetNode<StoryInspectorPanel>("%StoryInspectorPanel");

            worldgenButton.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Assert(worldgenInspector.Visible, "Worldgen story inspector should open from the worldgen viewer.");
            Assert(worldgenInspector.DebugSourceText.Contains("Worldgen", StringComparison.Ordinal), "Worldgen story inspector should identify the worldgen story source.");
            Assert(worldgenInspector.DebugEventsText.Length > 0, "Worldgen story inspector should render event content.");
        }
        finally
        {
            viewer.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async System.Threading.Tasks.Task RunAnnouncementLogUiTest()
    {
        var panel = GD.Load<PackedScene>("res://Scenes/UI/AnnouncementLog.tscn").Instantiate<AnnouncementLog>();
        AddChild(panel);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        panel.AddMessage("Found a buried hall.", Colors.Goldenrod);
        panel.AddMessage("Goblin raid spotted.", Colors.Red);

        var visibleSequences = panel.DebugGetVisibleSequences();
        Assert(visibleSequences.Length == 2, "Announcement log smoke test should render two icon notifications.");

        var topSequence = visibleSequences[0];
        Assert(panel.DebugGetEntryTooltipText(topSequence).Contains("Alert: Goblin raid spotted.", StringComparison.Ordinal),
            "Announcement log hover tooltip should show the notification title for icon-only alerts.");
        Assert(panel.DebugUsesTransparentBackground(),
            "Announcement log root panel should use a transparent background so the game area stays visible behind it.");
        Assert(panel.DebugHandleEntryClick(topSequence, MouseButton.Right), "Announcement log should open notification details on right click.");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Assert(panel.DebugIsDetailPopupVisible(), "Announcement log should show a detail popup after right clicking an icon notification.");
        Assert(panel.DebugOpenSequence == topSequence, "Announcement log should track the currently opened notification sequence.");
        Assert(panel.DebugDetailMessageText.Contains("Goblin raid spotted.", StringComparison.Ordinal), "Announcement log detail popup should render the clicked notification message.");

        Assert(panel.DebugHandleEntryClick(topSequence, MouseButton.Left), "Announcement log should dismiss notification icons on left click.");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Assert(!panel.DebugIsDetailPopupVisible(), "Dismissing an opened notification should also close its detail popup.");
        Assert(panel.DebugGetVisibleSequences().Length == 1, "Left clicking an icon notification should remove it from the visible stack.");

        panel.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var gameRoot = await StartGameRootAsync();
        try
        {
            var hoverInfo = gameRoot.GetNode<HoverInfoPanel>("%HoverInfoPanel");
            var announcementLog = gameRoot.GetNode<AnnouncementLog>("%AnnouncementLog");

            Assert(announcementLog.GetGlobalRect().Position.Y >= hoverInfo.GetGlobalRect().End.Y + 4f,
                "Announcement log should sit below the hover tile description instead of overlapping it.");
            Assert(announcementLog.DebugUsesTransparentBackground(),
                "Main-scene announcement log should keep a transparent background over the game area.");
        }
        finally
        {
            gameRoot.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async System.Threading.Tasks.Task RunBillboardHoverSelectionTest()
    {
        var gameRoot = await StartGameRootAsync();
        try
        {
            var world3DRoot = gameRoot.GetNode<WorldRender3D>("%World3DRoot");
            var mainCamera3D = gameRoot.GetNode<Camera3D>("%MainCamera3D");
            var input = gameRoot.GetNode<InputController>("%InputController");
            var dwarfPanel = gameRoot.GetNode<DwarfPanel>("%DwarfPanel");
            var tileInfo = gameRoot.GetNode<TileInfoPanel>("%TileInfoPanel");

            Assert(world3DRoot.TryGetDebugBillboardProbe(mainCamera3D, GetViewport(), out var screenPosition, out var expectedTile),
                "3D world should expose at least one visible actor billboard for hover selection.");

            Assert(world3DRoot.TryResolveHoveredBillboardTile(mainCamera3D, GetViewport(), screenPosition, out var hoveredTile),
                "3D billboard picking should resolve a hovered tile from a billboard screen position.");
            Assert(hoveredTile == expectedTile,
                $"Billboard hover should resolve tile {expectedTile}, but resolved {hoveredTile}.");
            Assert(world3DRoot.HasDebugHoveredBillboardOutline(),
                "Hovering a billboard should enable its outline.");

            input.UseExternalHoveredTile(hoveredTile);
            input._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            });
            input._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = false,
            });

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Assert(input.SelectedTile == hoveredTile,
                "Clicking a hovered billboard should route selection through the billboard tile.");
            Assert(input.SelectedDwarfId is not null || input.SelectedCreatureId is not null || input.SelectedItemId is not null,
                "Clicking a hovered billboard should select an entity-like target instead of leaving only a raw tile selection.");

            if (input.SelectedDwarfId is not null || input.SelectedCreatureId is not null)
                Assert(dwarfPanel.Visible, "Dwarf panel should open after selecting a hovered creature billboard.");

            if (input.SelectedItemId is not null)
                Assert(tileInfo.Visible, "Tile info should open in item-inspection mode after selecting a hovered item billboard.");
        }
        finally
        {
            gameRoot.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async System.Threading.Tasks.Task RunBillboardPauseInterpolationTest()
    {
        var gameRoot = await StartGameRootAsync();
        try
        {
            var world3DRoot = gameRoot.GetNode<WorldRender3D>("%World3DRoot");
            var mainCamera3D = gameRoot.GetNode<Camera3D>("%MainCamera3D");
            var actionBar = gameRoot.GetNode<ActionBar>("%ActionBar");
            var simulationField = typeof(GameRoot).GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic);
            var simulation = simulationField?.GetValue(gameRoot) as GameSimulation;

            Assert(simulation is not null, "Billboard pause smoke test needs access to the live simulation.");
            Assert(world3DRoot.TryGetDebugBillboardProbe(mainCamera3D, GetViewport(), out _, out var tile),
                "3D world should expose at least one visible billboard for the pause smoke test.");

            var query = simulation!.Context.Get<WorldQuerySystem>();
            var registry = simulation.Context.Get<EntityRegistry>();
            var map = simulation.Context.Get<WorldMap>();
            var movementPresentation = simulation.Context.Get<MovementPresentationSystem>();
            var tileView = query.QueryTile(new Vec3i(tile.X, tile.Y, 0));
            var targetEntityId = tileView.Dwarves.FirstOrDefault()?.Id ?? tileView.Creatures.FirstOrDefault()?.Id;

            Assert(targetEntityId is not null, "Billboard pause smoke test expected a visible creature-like billboard on the probe tile.");
            var resolvedTargetEntityId = targetEntityId.GetValueOrDefault();
            Assert(registry.TryGetById(resolvedTargetEntityId, out Entity? runtimeEntity) && runtimeEntity is not null,
                "Billboard pause smoke test should resolve the runtime entity for the probe billboard.");

            var oldPos = runtimeEntity!.Components.Get<PositionComponent>().Position;
            var newPos = FindAdjacentPassableTile(map, oldPos);
            Assert(newPos != oldPos, "Billboard pause smoke test needs a traversable adjacent tile to start an interpolated segment.");

            movementPresentation.RecordEntityMovement(runtimeEntity.Id, oldPos, newPos, 0.8f);
            runtimeEntity.Components.Get<PositionComponent>().Position = newPos;
            simulation.Context.EventBus.Emit(new EntityMovedEvent(runtimeEntity.Id, oldPos, newPos));

            Force3DWorldRefresh(gameRoot);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Assert(world3DRoot.TryGetDebugBillboardWorldPosition(runtimeEntity.Id, out var beforePausePosition),
                "Billboard pause smoke test should be able to sample the live billboard position.");

            actionBar.TogglePause();
            for (var index = 0; index < 20; index++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Assert(world3DRoot.TryGetDebugBillboardWorldPosition(runtimeEntity.Id, out var pausedPosition),
                "Billboard pause smoke test should still be able to sample the billboard while paused.");
            Assert(beforePausePosition.DistanceTo(pausedPosition) < 0.0001f,
                "Paused simulation should freeze billboard interpolation instead of letting it keep advancing.");
        }
        finally
        {
            gameRoot.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async System.Threading.Tasks.Task RunCombatCue3DSmokeTest()
    {
        var gameRoot = await StartGameRootAsync();
        try
        {
            var world3DRoot = gameRoot.GetNode<WorldRender3D>("%World3DRoot");
            var mainCamera3D = gameRoot.GetNode<Camera3D>("%MainCamera3D");
            var simulationField = typeof(GameRoot).GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic);
            var simulation = simulationField?.GetValue(gameRoot) as GameSimulation;

            Assert(simulation is not null, "Combat cue smoke test needs access to the live simulation.");
            Assert(world3DRoot.TryGetDebugBillboardProbe(mainCamera3D, GetViewport(), out _, out var tile),
                "Combat cue smoke test expected at least one visible creature billboard.");

            var query = simulation!.Context.Get<WorldQuerySystem>();
            var registry = simulation.Context.Get<EntityRegistry>();
            var map = simulation.Context.Get<WorldMap>();
            var tileView = query.QueryTile(new Vec3i(tile.X, tile.Y, 0));
            var defenderId = tileView.Dwarves.FirstOrDefault()?.Id ?? tileView.Creatures.FirstOrDefault()?.Id;

            Assert(defenderId is not null,
                "Combat cue smoke test expected the billboard probe tile to contain a creature-like target.");

            var resolvedDefenderId = defenderId.GetValueOrDefault();
            var defender = registry.TryGetById(resolvedDefenderId);
            Assert(defender is not null, "Combat cue smoke test should resolve the visible defender entity.");

            var attacker = registry.GetAlive<Dwarf>().FirstOrDefault(dwarf => dwarf.Id != resolvedDefenderId);
            Assert(attacker is not null, "Combat cue smoke test expected a second dwarf to use as the attacker.");

            var defenderPos = defender!.Components.Get<PositionComponent>().Position;
            var attackerPos = FindAdjacentPassableTile(map, defenderPos);
            Assert(attackerPos != defenderPos,
                "Combat cue smoke test needs a passable adjacent tile so the 3D cue has a direction.");

            var attackerOldPos = attacker!.Components.Get<PositionComponent>().Position;
            attacker.Components.Get<PositionComponent>().Position = attackerPos;
            simulation.Context.EventBus.Emit(new EntityMovedEvent(attacker.Id, attackerOldPos, attackerPos));

            Force3DWorldRefresh(gameRoot);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var baselineMaxVisibleCombatCueId = world3DRoot.GetDebugMaxVisibleCombatCueId();

            simulation.Context.EventBus.Emit(new CombatHitEvent(attacker.Id, resolvedDefenderId, 1f, BodyPartIds.Torso));

            Force3DWorldRefresh(gameRoot);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Assert(world3DRoot.GetDebugVisibleCombatCueCount() > 0,
                "Combat hit feedback should expose at least one visible combat cue to the 3D overlay.");
            Assert(world3DRoot.GetDebugMaxVisibleCombatCueId() > baselineMaxVisibleCombatCueId,
                "Combat hit feedback should push a newer combat cue through the 3D overlay state.");
            Assert(world3DRoot.GetDebugCombatCuePlateCount() >= 3,
                "Combat hit feedback should build flash and directional streak plates in the 3D overlay.");
        }
        finally
        {
            gameRoot.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async System.Threading.Tasks.Task RunGameRootRenderResidencyTest()
    {
        var gameRoot = await StartGameRootAsync();
        try
        {
            var world3DRoot = gameRoot.GetNode<WorldRender3D>("%World3DRoot");
            var mapField = typeof(GameRoot).GetField("_map", BindingFlags.Instance | BindingFlags.NonPublic);
            var cameraControllerField = typeof(GameRoot).GetField("_worldCamera3D", BindingFlags.Instance | BindingFlags.NonPublic);
            var map = mapField?.GetValue(gameRoot) as WorldMap;
            var cameraController = cameraControllerField?.GetValue(gameRoot) as WorldCamera3DController;

            Assert(map is not null, "Render residency smoke test needs access to the live WorldMap.");
            Assert(cameraController is not null, "Render residency smoke test needs access to the 3D camera controller.");

            var initialChunkCount = world3DRoot.GetDebugChunkMeshCount();
            Assert(initialChunkCount > 0, "3D world should keep visible chunk meshes resident.");

            cameraController!.JumpToTile(new Vec3i(Math.Max(0, map!.Width - 8), Math.Max(0, map.Height - 8), 0));
            Force3DWorldRefresh(gameRoot);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var farChunkCount = world3DRoot.GetDebugChunkMeshCount();

            cameraController.JumpToTile(new Vec3i(Math.Min(24, map.Width - 1), Math.Min(24, map.Height - 1), 0));
            Force3DWorldRefresh(gameRoot);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var returnChunkCount = world3DRoot.GetDebugChunkMeshCount();

            Assert(farChunkCount <= initialChunkCount, "3D world should evict offscreen chunk meshes instead of accumulating them.");
            Assert(returnChunkCount <= initialChunkCount, "3D chunk residency should stay bounded after moving the camera around.");
        }
        finally
        {
            gameRoot.QueueFree();
        }
    }

    private static void RunWorldCamera3DControllerControlsTest()
    {
        var camera3D = new Camera3D();

        var controller = new WorldCamera3DController();
        controller.Initialize(camera3D);
        controller.SetView(new Vector2(24f, 24f), 24f);

        var startFocus = controller.FocusTile;
        controller.MoveFocus(new Vector2(0f, 1f), 1.0);
        var defaultForwardDelta = controller.FocusTile - startFocus;

        Assert(defaultForwardDelta.Length() > 0.01f, "3D camera forward movement should move the focus tile.");

        controller.SetView(new Vector2(24f, 24f), 24f);
        startFocus = controller.FocusTile;
        controller.RotateYaw(Mathf.Pi * 0.5f);
        controller.MoveFocus(new Vector2(0f, 1f), 1.0);
        var rotatedForwardDelta = controller.FocusTile - startFocus;

        Assert(rotatedForwardDelta.Length() > 0.01f, "3D camera forward movement should still work after rotating the camera.");
        Assert(Mathf.Abs(defaultForwardDelta.Normalized().Dot(rotatedForwardDelta.Normalized())) < 0.2f,
            "3D camera movement should stay relative to the camera angle after yaw rotation.");

        var yawBeforeDrag = controller.YawRadians;
        var pressHandled = controller.HandlePointerInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            ShiftPressed = true,
        });
        var motionHandled = controller.HandlePointerInput(new InputEventMouseMotion
        {
            Relative = new Vector2(32f, 0f),
            ShiftPressed = true,
            ButtonMask = MouseButtonMask.Left,
        });
        var releaseHandled = controller.HandlePointerInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
        });

        Assert(pressHandled && motionHandled && releaseHandled, "3D camera rotation drag should consume Shift-drag pointer input.");
        Assert(!Mathf.IsEqualApprox(controller.YawRadians, yawBeforeDrag), "3D camera rotation drag should change camera yaw.");
        Assert(!controller.IsRotating, "3D camera rotation drag should end when the drag button is released.");
    }

    private async System.Threading.Tasks.Task RunResourceBillboardHoverSelectionTest()
    {
        var gameRoot = await StartGameRootAsync();
        try
        {
            var world3DRoot = gameRoot.GetNode<WorldRender3D>("%World3DRoot");
            var mainCamera3D = gameRoot.GetNode<Camera3D>("%MainCamera3D");
            var input = gameRoot.GetNode<InputController>("%InputController");
            var tileInfo = gameRoot.GetNode<TileInfoPanel>("%TileInfoPanel");
            var simulationField = typeof(GameRoot).GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic);
            var simulation = simulationField?.GetValue(gameRoot) as GameSimulation;

            Assert(simulation is not null, "Resource billboard smoke test needs access to the live simulation.");
            Assert(world3DRoot.TryGetDebugResourceBillboardProbe(mainCamera3D, GetViewport(), out var screenPosition, out var expectedTile),
                "3D world should expose at least one visible resource billboard for hover selection.");

            Assert(world3DRoot.TryResolveHoveredBillboardTarget(mainCamera3D, GetViewport(), screenPosition, out var hoveredTile, out var selectionMode),
                "Resource billboard picking should resolve a hovered tile from a resource billboard screen position.");
            Assert(selectionMode == HoverSelectionMode.RawTile,
                "Resource billboard picking should request raw tile selection so the tile resource view wins over entity priority.");
            Assert(hoveredTile == expectedTile,
                $"Resource billboard hover should resolve tile {expectedTile}, but resolved {hoveredTile}.");
            Assert(world3DRoot.HasDebugHoveredResourceBillboardOutline(),
                "Hovering a resource billboard should enable its outline.");

            var tileResult = simulation!.Context.Get<WorldQuerySystem>().QueryTile(new Vec3i(hoveredTile.X, hoveredTile.Y, 0));
            Assert(tileResult.Tile is not null && (tileResult.Tile.TileDefId == TileDefIds.Tree || tileResult.Tile.PlantDefId is not null),
                "Resource billboard hover should resolve a tile that actually contains a tree or plant resource.");

            input.UseExternalHoveredTile(hoveredTile, selectionMode);
            input._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            });
            input._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = false,
            });

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Assert(input.SelectedTile == hoveredTile,
                "Clicking a hovered resource billboard should select the resource tile.");
            Assert(input.SelectedDwarfId is null && input.SelectedCreatureId is null && input.SelectedBuildingId is null && input.SelectedItemId is null,
                "Clicking a hovered resource billboard should prefer raw tile selection over entity/item priority.");
            Assert(tileInfo.Visible, "Tile info should open after selecting a resource billboard tile.");
            Assert(tileInfo.DebugActionSummaryText.Contains("Harvest", StringComparison.OrdinalIgnoreCase) || tileInfo.DebugActionSummaryText.Length > 0,
                "Selecting a resource tile should expose at least one harvest action in the tile panel.");
        }
        finally
        {
            gameRoot.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async System.Threading.Tasks.Task RunResourceBillboardAreaSelectionHighlightTest()
    {
        var gameRoot = await StartGameRootAsync();
        try
        {
            var world3DRoot = gameRoot.GetNode<WorldRender3D>("%World3DRoot");
            var mainCamera3D = gameRoot.GetNode<Camera3D>("%MainCamera3D");
            var input = gameRoot.GetNode<InputController>("%InputController");
            var simulationField = typeof(GameRoot).GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic);
            var simulation = simulationField?.GetValue(gameRoot) as GameSimulation;

            Assert(simulation is not null, "Resource billboard area selection smoke test needs access to the live simulation.");
            Assert(world3DRoot.TryGetDebugResourceBillboardProbe(mainCamera3D, GetViewport(), out var screenPosition, out var resourceTile),
                "3D world should expose at least one visible resource billboard for area selection.");

            var map = simulation!.Context.Get<WorldMap>();
            var neighborX = resourceTile.X < map.Width - 1 ? resourceTile.X + 1 : resourceTile.X - 1;
            var areaTo = new Vector2I(neighborX, resourceTile.Y);

            Assert(world3DRoot.TryResolveHoveredBillboardTarget(mainCamera3D, GetViewport(), screenPosition, out _, out _),
                "Area selection smoke test should be able to resolve a visible resource billboard before clearing hover.");
            Assert(world3DRoot.HasDebugHoveredResourceBillboardOutline(),
                "Area selection smoke test expects the resource billboard hover outline before clearing it.");
            Assert(!world3DRoot.TryResolveHoveredBillboardTarget(mainCamera3D, GetViewport(), new Vector2(-2048f, -2048f), out _, out _),
                "Resolving an offscreen billboard probe should clear the hovered resource highlight.");
            Assert(!world3DRoot.HasDebugHoveredResourceBillboardOutline(),
                "Moving the billboard probe offscreen should clear the hovered resource outline.");

            input.SelectArea(resourceTile, areaTo);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Assert(input.GetSelectedAreaRect().HasValue, "Area selection should persist after selecting multiple tiles.");
            Assert(world3DRoot.GetDebugEmphasizedResourceBillboardCount() >= 1,
                "Selecting an area containing a tree or plant should highlight at least one resource billboard.");
        }
        finally
        {
            gameRoot.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async System.Threading.Tasks.Task RunSelectionViewHarvestTest()
    {
        var gameRoot = await StartGameRootAsync();
        try
        {
            var input = gameRoot.GetNode<InputController>("%InputController");
            var selectionView = gameRoot.GetNode<SelectionViewPanel>("%SelectionViewPanel");
            var simulationField = typeof(GameRoot).GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic);
            var simulation = simulationField?.GetValue(gameRoot) as GameSimulation;

            Assert(simulation is not null, "Selection view smoke test needs a live simulation.");

            var map = simulation!.Context.Get<WorldMap>();
            var data = simulation.Context.Get<DataManager>();
            var found = false;
            var from = Vector2I.Zero;
            var to = Vector2I.Zero;

            for (var x = 1; x < map.Width - 1 && !found; x++)
            for (var y = 1; y < map.Height - 1 && !found; y++)
            {
                var pos = new Vec3i(x, y, 0);
                if (!PlantHarvesting.TryGetHarvestablePlant(map, data, pos, out _)
                    && map.GetTile(pos).TileDefId != TileDefIds.Tree)
                {
                    continue;
                }

                from = new Vector2I(Math.Max(0, x - 1), Math.Max(0, y - 1));
                to = new Vector2I(Math.Min(map.Width - 1, x + 1), Math.Min(map.Height - 1, y + 1));
                found = true;
            }

            Assert(found, "Selection view smoke test should find at least one harvestable tree or plant on the surface.");

            input.SelectArea(from, to);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Assert(selectionView.Visible, "Selecting multiple tiles should open the selection view panel.");
            Assert(selectionView.DebugSummaryText.Contains("Selection View", StringComparison.Ordinal), "Selection view should render its summary heading.");
            Assert(selectionView.DebugEntriesText.Contains("HarvestPlant", StringComparison.OrdinalIgnoreCase)
                || selectionView.DebugEntriesText.Contains("CutTree", StringComparison.OrdinalIgnoreCase)
                || selectionView.DebugEntriesText.Contains("Mine", StringComparison.OrdinalIgnoreCase),
                "Selection view should expose at least one actionable harvest group when the area contains harvestable resources.");
            Assert(!selectionView.DebugEntriesText.Contains("Ground|", StringComparison.OrdinalIgnoreCase)
                && !selectionView.DebugEntriesText.Contains("Plants|", StringComparison.OrdinalIgnoreCase)
                && !selectionView.DebugEntriesText.Contains("Terrain|", StringComparison.OrdinalIgnoreCase),
                "Selection view should list concrete tile types instead of category headers.");
        }
        finally
        {
            gameRoot.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async System.Threading.Tasks.Task RunSelectionViewMixedPlantHarvestTest()
    {
        var gameRoot = await StartGameRootAsync();
        try
        {
            var input = gameRoot.GetNode<InputController>("%InputController");
            var selectionView = gameRoot.GetNode<SelectionViewPanel>("%SelectionViewPanel");
            var simulationField = typeof(GameRoot).GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic);
            var simulation = simulationField?.GetValue(gameRoot) as GameSimulation;

            Assert(simulation is not null, "Mixed plant selection smoke test needs a live simulation.");

            var map = simulation!.Context.Get<WorldMap>();
            var maturePos = new Vec3i(10, 10, 0);
            var growingPos = new Vec3i(11, 10, 0);

            var matureTile = map.GetTile(maturePos);
            matureTile.TileDefId = TileDefIds.Grass;
            matureTile.IsPassable = true;
            matureTile.PlantDefId = "berry_bush";
            matureTile.PlantGrowthStage = PlantGrowthStages.Mature;
            matureTile.PlantYieldLevel = 1;
            matureTile.PlantSeedLevel = 1;
            map.SetTile(maturePos, matureTile);

            var growingTile = map.GetTile(growingPos);
            growingTile.TileDefId = TileDefIds.Grass;
            growingTile.IsPassable = true;
            growingTile.PlantDefId = "berry_bush";
            growingTile.PlantGrowthStage = PlantGrowthStages.Sprout;
            growingTile.PlantYieldLevel = 0;
            growingTile.PlantSeedLevel = 0;
            map.SetTile(growingPos, growingTile);

            input.SelectArea(new Vector2I(maturePos.X, maturePos.Y), new Vector2I(growingPos.X, growingPos.Y));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Assert(selectionView.Visible, "Selecting multiple plant tiles should open the selection view panel.");
            Assert(selectionView.DebugEntriesText.Contains("HarvestPlant", StringComparison.OrdinalIgnoreCase),
                "A mixed ripe/unripe plant selection should still expose a harvestable plant action.");
        }
        finally
        {
            gameRoot.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static void Force3DWorldRefresh(GameRoot gameRoot)
    {
        var dirtyField = typeof(GameRoot).GetField("_world3DDirty", BindingFlags.Instance | BindingFlags.NonPublic);
        var updateMethod = typeof(GameRoot).GetMethod("Update3DWorldState", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(dirtyField is not null, "Render residency smoke test needs access to GameRoot._world3DDirty.");
        Assert(updateMethod is not null, "Render residency smoke test needs access to GameRoot.Update3DWorldState().");

        dirtyField!.SetValue(gameRoot, true);
        updateMethod!.Invoke(gameRoot, null);
    }

    private static Vec3i FindAdjacentPassableTile(WorldMap map, Vec3i origin)
    {
        foreach (var direction in new[] { Vec3i.East, Vec3i.West, Vec3i.North, Vec3i.South })
        {
            var candidate = origin + direction;
            if (!map.IsInBounds(candidate))
                continue;

            if (map.GetTile(candidate).IsPassable)
                return candidate;
        }

        return origin;
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

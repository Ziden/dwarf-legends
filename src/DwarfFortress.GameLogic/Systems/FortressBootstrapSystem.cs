using System;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.World;
using DwarfFortress.WorldGen.Local;
using DwarfFortress.WorldGen.Maps;

namespace DwarfFortress.GameLogic.Systems;

public record struct FortressStartedEvent(int Seed, int Width, int Height, int Depth, int StartingDwarves, int WorkshopBuildingId);

/// <summary>
/// Single backend entry point for starting a new fortress run.
/// This keeps new-game orchestration in GameLogic rather than the future client.
/// </summary>
public sealed class FortressBootstrapSystem : IGameSystem
{
    public string SystemId => SystemIds.FortressBootstrapSystem;
    public int UpdateOrder => 2;
    public bool IsEnabled { get; set; } = true;

    private GameContext? _ctx;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        ctx.Commands.Register<GenerateWorldCommand>(OnGenerateWorld);
        ctx.Commands.Register<StartFortressCommand>(OnStartFortress);
    }

    public void Tick(float delta) { }
    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    private void OnGenerateWorld(GenerateWorldCommand cmd)
    {
        var lore = _ctx!.TryGet<WorldLoreSystem>();
        lore?.Generate(cmd.Seed, cmd.Width, cmd.Height, cmd.Depth);
        GenerateEmbarkForCommand(cmd.Seed, cmd.Width, cmd.Height, cmd.Depth, lore?.Current?.BiomeId);
    }

    private void OnStartFortress(StartFortressCommand cmd)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        var items = _ctx.Get<ItemSystem>();
        var stockpiles = _ctx.Get<StockpileManager>();
        var buildings = _ctx.Get<BuildingSystem>();

        if (registry.TotalCount > 0 || items.GetAllItems().Any() || stockpiles.GetAll().Any() || buildings.GetAll().Any())
        {
            _ctx.Logger?.Warn("FortressBootstrapSystem: StartFortressCommand expects a fresh simulation.");
            return;
        }

        var lore = _ctx.TryGet<WorldLoreSystem>();
        lore?.Generate(cmd.Seed, cmd.Width, cmd.Height, cmd.Depth);
        var generatedMap = GenerateEmbarkForCommand(cmd.Seed, cmd.Width, cmd.Height, cmd.Depth, lore?.Current?.BiomeId);

        int cx = cmd.Width / 2;
        int cy = cmd.Height / 2;
        var embark = new Vec3i(cx, cy, 0);
        var usedAppearanceSignatures = new HashSet<string>();

        SpawnDwarf("Urist",  embark + Vec3i.West,  usedAppearanceSignatures, LaborIds.Mining,      LaborIds.Hauling);
        SpawnDwarf("Bomrek", embark + Vec3i.East,  usedAppearanceSignatures, LaborIds.WoodCutting, LaborIds.Hauling);
        SpawnDwarf("Domas",  embark + Vec3i.North, usedAppearanceSignatures, LaborIds.Crafting,    LaborIds.Hauling);

        registry.Register(CreateCreatureFromDef(DefIds.Cat, embark + new Vec3i(2, 0, 0), 20f));
        registry.Register(CreateCreatureFromDef(DefIds.Dog, embark + new Vec3i(-2, 0, 0), 30f));
        SpawnGeneratedWildlife(generatedMap, embark);

        // Single 3×3 all-categories stockpile
        _ctx.Commands.Dispatch(new CreateStockpileCommand(embark + new Vec3i(-1, -1, 0), embark + new Vec3i(1, 1, 0), []));
        _ctx.Commands.Dispatch(new PlaceBuildingCommand(BuildingDefIds.CarpenterWorkshop, embark + new Vec3i(3, 1, 0)));

        // Spawn two pre-filled boxes on distinct stockpile slots
        var stockpile = _ctx.Get<StockpileManager>().GetAll().FirstOrDefault();
        if (stockpile is not null)
        {
            var slots = stockpile.AllSlots().Take(2).ToArray();
            if (slots.Length >= 2)
            {
                var foodBox  = new Box(registry.NextId(), slots[0]);
                var drinkBox = new Box(registry.NextId(), slots[1]);
                registry.Register(foodBox);
                registry.Register(drinkBox);

                for (int i = 0; i < Box.DefaultCapacity; i++)
                {
                    var meal = items.CreateItem(ItemDefIds.Meal, MaterialIds.Food, slots[0]);
                    items.StoreItemInBox(meal.Id, foodBox, stockpile.Id);
                }

                for (int i = 0; i < Box.DefaultCapacity; i++)
                {
                    var drink = items.CreateItem(ItemDefIds.Drink, MaterialIds.Drink, slots[1]);
                    items.StoreItemInBox(drink.Id, drinkBox, stockpile.Id);
                }
            }
        }

        int workshopId = buildings.GetAll().FirstOrDefault(b => b.BuildingDefId == BuildingDefIds.CarpenterWorkshop)?.Id ?? -1;
        _ctx.EventBus.Emit(new FortressStartedEvent(cmd.Seed, cmd.Width, cmd.Height, cmd.Depth,
            _ctx.Get<EntityRegistry>().GetAll<Entities.Dwarf>().Count(), workshopId));
    }

    private GeneratedEmbarkMap GenerateEmbarkForCommand(int seed, int width, int height, int depth, string? biomeId)
    {
        var targetMap = _ctx!.Get<WorldMap>();
        var mapGeneration = _ctx.TryGet<MapGenerationService>();
        if (mapGeneration is null)
        {
            var fallbackSettings = new LocalGenerationSettings(width, height, depth, biomeId);
            var fallbackGenerated = EmbarkGenerator.Generate(fallbackSettings, seed);
            WorldGenerator.ApplyGeneratedEmbark(targetMap, fallbackGenerated);
            return fallbackGenerated;
        }

        var localSettings = new LocalGenerationSettings(width, height, depth, biomeId);
        var context = mapGeneration.GenerateAndApplyEmbark(targetMap, seed, localSettings, biomeId);
        var generatedMap = mapGeneration.LastGeneratedLocalMap ?? EmbarkGenerator.Generate(localSettings, seed);

        _ctx.Logger?.Debug(
            $"FortressBootstrapSystem: generated embark via layered mapgen seed={seed}, " +
            $"world=({context.WorldCoord.X},{context.WorldCoord.Y}), " +
            $"region=({context.RegionCoord.RegionX},{context.RegionCoord.RegionY}), " +
            $"macroBiome={context.MacroBiomeId}, regionBiome={context.RegionBiomeVariantId}, " +
            $"effectiveBiome={context.EffectiveBiomeId}.");

        return generatedMap;
    }

    private void SpawnGeneratedWildlife(GeneratedEmbarkMap generatedMap, Vec3i embarkCenter)
    {
        if (generatedMap.CreatureSpawns.Count == 0)
            return;

        var registry = _ctx!.Get<EntityRegistry>();
        var map = _ctx.Get<WorldMap>();
        var data = _ctx.TryGet<DataManager>();
        var occupied = new HashSet<Vec3i>(
            registry.GetAlive<Dwarf>().Select(d => d.Position.Position)
                .Concat(registry.GetAlive<Creature>().Select(c => c.Position.Position)));

        foreach (var spawn in generatedMap.CreatureSpawns)
        {
            var pos = new Vec3i(spawn.X, spawn.Y, spawn.Z);
            if (!map.IsInBounds(pos))
                continue;
            if (occupied.Contains(pos))
                continue;
            if (Math.Abs(pos.X - embarkCenter.X) <= 5 && Math.Abs(pos.Y - embarkCenter.Y) <= 5)
                continue;

            var def = data?.Creatures.GetOrNull(spawn.CreatureDefId);
            var fallbackAquatic =
                string.Equals(spawn.CreatureDefId, DefIds.GiantCarp, StringComparison.OrdinalIgnoreCase) ||
                CreatureDefTagExtensions.IsLikelyAquaticId(spawn.CreatureDefId);
            var requiresSwimming = (def?.IsAquatic() == true) || fallbackAquatic;
            var canSwim = (def?.CanSwim() == true) || fallbackAquatic;
            if (!map.IsTraversable(pos, canSwim, requiresSwimming))
                continue;

            var maxHealth = def?.MaxHealth ?? 50f;
            var isHostile = def?.IsHostile() ?? false;

            registry.Register(CreateCreatureFromDef(spawn.CreatureDefId, pos, maxHealth, isHostile));
            occupied.Add(pos);
        }
    }

    private void SpawnDwarf(string name, Vec3i pos, ISet<string> usedAppearanceSignatures, params string[] labors)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        var dwarf = new Dwarf(registry.NextId(), name, pos);
        dwarf.Appearance.RandomizeDistinct(DwarfAppearanceComponent.CreateSeed(dwarf.Id, name, pos), usedAppearanceSignatures);
        dwarf.Labors.DisableAll();
        dwarf.Labors.EnableAll(labors);
        dwarf.Labors.Enable(LaborIds.Misc);
        RandomizeFoodPreferences(dwarf);
        AssignRandomTraits(dwarf);
        registry.Register(dwarf);
    }

    private void AssignRandomTraits(Dwarf dwarf)
    {
        var data = _ctx!.TryGet<DataManager>();
        if (data is null) return;

        var allTraits = data.Traits.All().ToArray();
        if (allTraits.Length == 0) return;

        var rng = new Random(dwarf.Id ^ dwarf.FirstName.GetHashCode());

        // Always assign at least 1 trait
        var firstIdx = rng.Next(allTraits.Length);
        dwarf.Traits.AddTrait(allTraits[firstIdx].Id);

        // 30% chance for a second trait (different from the first)
        if (rng.NextDouble() < 0.30 && allTraits.Length > 1)
        {
            int secondIdx;
            do { secondIdx = rng.Next(allTraits.Length); } while (secondIdx == firstIdx);
            dwarf.Traits.AddTrait(allTraits[secondIdx].Id);
        }
    }

    private void RandomizeFoodPreferences(Dwarf dwarf)
    {
        var data = _ctx!.TryGet<DataManager>();
        if (data is null) return;

        var foodItems = data.Items.All()
            .Where(i => i.Tags.Contains(TagIds.Food))
            .ToArray();
        if (foodItems.Length < 2) return;

        var rng = new Random(dwarf.Id ^ dwarf.FirstName.GetHashCode());
        var likeIdx = rng.Next(foodItems.Length);
        int dislikeIdx;
        do { dislikeIdx = rng.Next(foodItems.Length); } while (dislikeIdx == likeIdx);

        dwarf.Preferences.LikedFoodId     = foodItems[likeIdx].Id;
        dwarf.Preferences.LikeStrength    = (byte)(128 + rng.Next(128));
        dwarf.Preferences.DislikedFoodId  = foodItems[dislikeIdx].Id;
        dwarf.Preferences.DislikeStrength = (byte)(128 + rng.Next(128));
    }

    private Creature CreateCreatureFromDef(string creatureDefId, Vec3i pos, float fallbackMaxHealth, bool isHostile = false)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        var def = _ctx.TryGet<DataManager>()?.Creatures.GetOrNull(creatureDefId);
        var creature = new Creature(
            registry.NextId(),
            creatureDefId,
            pos,
            def?.MaxHealth ?? fallbackMaxHealth,
            isHostile || (def?.IsHostile() ?? false));
        creature.ApplyBaseStats(def);
        return creature;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.World;
using DwarfFortress.WorldGen.Creatures;
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
        GenerateEmbarkForCommand(cmd.Seed, cmd.Width, cmd.Height, cmd.Depth);
        _ctx!.TryGet<WorldHistoryRuntimeService>()?.RefreshFromLatestGeneration();
        _ctx.TryGet<WorldMacroStateService>()?.RefreshFromHistory();
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

        var generatedMap = GenerateEmbarkForCommand(cmd.Seed, cmd.Width, cmd.Height, cmd.Depth);
        var historyRuntime = _ctx.TryGet<WorldHistoryRuntimeService>();
        historyRuntime?.RefreshFromLatestGeneration();
        _ctx.TryGet<WorldMacroStateService>()?.RefreshFromHistory();

        int cx = cmd.Width / 2;
        int cy = cmd.Height / 2;
        var embark = new Vec3i(cx, cy, 0);
        _ctx.TryGet<FortressLocationSystem>()?.InitializeDefaultLocations(embark);
        var usedAppearanceSignatures = new HashSet<string>();
        var founderProfiles = historyRuntime?.GetStartingDwarfProfiles(3)
            ?? Array.Empty<RuntimeStartingDwarfProfile>();

        SpawnDwarf(founderProfiles.ElementAtOrDefault(0), embark + Vec3i.West, usedAppearanceSignatures, fallbackName: "Urist", fallbackLabors: [LaborIds.Mining, LaborIds.Hauling]);
        SpawnDwarf(founderProfiles.ElementAtOrDefault(1), embark + Vec3i.East, usedAppearanceSignatures, fallbackName: "Bomrek", fallbackLabors: [LaborIds.WoodCutting, LaborIds.Hauling]);
        SpawnDwarf(founderProfiles.ElementAtOrDefault(2), embark + Vec3i.North, usedAppearanceSignatures, fallbackName: "Domas", fallbackLabors: [LaborIds.Crafting, LaborIds.Hauling]);

        SpawnStarterCompanions(embark, cmd.Seed);
        InjectStarterHostileSpawn(generatedMap, embark, cmd.Seed);
        SpawnGeneratedWildlife(generatedMap, embark);

        // Single 3×3 all-categories stockpile
        _ctx.Commands.Dispatch(new CreateStockpileCommand(embark + new Vec3i(-1, -1, 0), embark + new Vec3i(1, 1, 0), []));

        // Seed one starter log so the initial workshop can always be constructed.
        items.CreateItem(ResolveMaterialFormItemDefId(MaterialIds.Wood, WorldGen.Content.ContentFormRoles.Log, ItemDefIds.Log), MaterialIds.Wood, embark + new Vec3i(2, 1, 0));
        TryPlaceStarterWorkshop(embark);

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

    private GeneratedEmbarkMap GenerateEmbarkForCommand(int seed, int width, int height, int depth)
    {
        var targetMap = _ctx!.Get<WorldMap>();
        var mapGeneration = _ctx.TryGet<MapGenerationService>();
        if (mapGeneration is null)
        {
            var fallbackSettings = new LocalGenerationSettings(width, height, depth);
            var fallbackGenerated = EmbarkGenerator.Generate(fallbackSettings, seed);
            WorldGenerator.ApplyGeneratedEmbark(targetMap, fallbackGenerated);
            return fallbackGenerated;
        }

        var localSettings = new LocalGenerationSettings(width, height, depth);
        var context = mapGeneration.GenerateAndApplyEmbark(targetMap, seed, localSettings);
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
            var (canSwim, requiresSwimming) = def?.ResolveTraversal() ?? (false, false);
            if (!map.IsTraversable(pos, canSwim, requiresSwimming))
                continue;

            var maxHealth = def?.MaxHealth ?? 50f;
            var isHostile = def?.IsHostile() ?? false;

            registry.Register(CreateCreatureFromDef(spawn.CreatureDefId, pos, maxHealth, isHostile));
            occupied.Add(pos);
        }
    }

    private void SpawnDwarf(RuntimeStartingDwarfProfile? profile, Vec3i pos, ISet<string> usedAppearanceSignatures, string fallbackName, string[] fallbackLabors)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        var dwarfDef = _ctx.TryGet<DataManager>()?.Creatures.GetOrNull(DefIds.Dwarf);
        var name = string.IsNullOrWhiteSpace(profile?.Name) ? fallbackName : profile.Name;
        var dwarf = new Dwarf(registry.NextId(), name, pos);
        dwarf.ApplyBaseStats(dwarfDef);
        dwarf.Appearance.RandomizeDistinct(DwarfAppearanceComponent.CreateSeed(dwarf.Id, name, pos), usedAppearanceSignatures);
        dwarf.Labors.DisableAll();
        dwarf.Labors.EnableAll(ResolveStartingLabors(profile, fallbackLabors));
        dwarf.Labors.Enable(LaborIds.Misc);
        dwarf.ProfessionId = string.IsNullOrWhiteSpace(profile?.ProfessionId) ? ProfessionIds.Peasant : profile.ProfessionId;
        DwarfAttributeGeneration.Randomize(dwarf, _ctx!.TryGet<DataManager>());
        DwarfAttributeGeneration.ApplyOverrides(dwarf, profile?.AttributeLevels);
        ApplyProvenance(dwarf, profile?.Provenance);
        ApplySkills(dwarf, profile?.SkillLevels);
        ApplyFoodPreferences(dwarf, profile?.LikedFoodId, profile?.DislikedFoodId);
        registry.Register(dwarf);
    }

    private static IEnumerable<string> ResolveStartingLabors(RuntimeStartingDwarfProfile? profile, IReadOnlyCollection<string> fallbackLabors)
    {
        if (profile?.LaborIds is not { Length: > 0 } authoredLabors)
            return fallbackLabors;

        return authoredLabors
            .Concat(fallbackLabors)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void InjectStarterHostileSpawn(GeneratedEmbarkMap generatedMap, Vec3i embarkCenter, int seed)
    {
        if (!TryFindStarterHostileSpawn(generatedMap, embarkCenter, seed, out var spawnPos))
        {
            _ctx!.Logger?.Warn("FortressBootstrapSystem: failed to place starter hostile spawn.");
            return;
        }

        var creatureDefId = ResolveStarterHostileCreatureDefId();
        if (string.IsNullOrWhiteSpace(creatureDefId))
            return;

        generatedMap.AddCreatureSpawn(new CreatureSpawn(creatureDefId, spawnPos.X, spawnPos.Y, spawnPos.Z));
    }

    private bool TryFindStarterHostileSpawn(GeneratedEmbarkMap generatedMap, Vec3i embarkCenter, int seed, out Vec3i spawnPos)
    {
        var map = _ctx!.Get<WorldMap>();
        var registry = _ctx.Get<EntityRegistry>();
        var occupied = new HashSet<Vec3i>(
            registry.GetAlive<Dwarf>().Select(d => d.Position.Position)
                .Concat(registry.GetAlive<Creature>().Select(c => c.Position.Position))
                .Concat(generatedMap.CreatureSpawns.Select(spawn => new Vec3i(spawn.X, spawn.Y, spawn.Z))));
        var candidates = BuildStarterHostileCandidates(embarkCenter);
        if (candidates.Count == 0)
        {
            spawnPos = default;
            return false;
        }

        var startIndex = (seed & int.MaxValue) % candidates.Count;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[(startIndex + i) % candidates.Count];
            if (!map.IsInBounds(candidate))
                continue;
            if (occupied.Contains(candidate))
                continue;
            if (Math.Abs(candidate.X - embarkCenter.X) <= 5 && Math.Abs(candidate.Y - embarkCenter.Y) <= 5)
                continue;
            if (!map.IsTraversable(candidate, canSwim: false, requiresSwimming: false))
                continue;
            if (Pathfinder.FindPath(map, candidate, embarkCenter).Count == 0)
                continue;

            spawnPos = candidate;
            return true;
        }

        spawnPos = default;
        return false;
    }

    private static List<Vec3i> BuildStarterHostileCandidates(Vec3i embarkCenter)
    {
        var candidates = new List<Vec3i>();
        for (var radius = 7; radius <= 10; radius++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                candidates.Add(embarkCenter + new Vec3i(dx, -radius, 0));
                candidates.Add(embarkCenter + new Vec3i(dx, radius, 0));
            }

            for (var dy = -radius + 1; dy < radius; dy++)
            {
                candidates.Add(embarkCenter + new Vec3i(-radius, dy, 0));
                candidates.Add(embarkCenter + new Vec3i(radius, dy, 0));
            }
        }

        return candidates;
    }

    private void TryPlaceStarterWorkshop(Vec3i embark)
    {
        var buildings = _ctx!.Get<BuildingSystem>();
        var preferredOrigins = new[]
        {
            embark + new Vec3i(3, 1, 0),
            embark + new Vec3i(3, -1, 0),
            embark + new Vec3i(-4, 1, 0),
            embark + new Vec3i(-4, -1, 0),
            embark + new Vec3i(1, 3, 0),
            embark + new Vec3i(-1, 3, 0),
            embark + new Vec3i(1, -4, 0),
            embark + new Vec3i(-1, -4, 0),
        };

        foreach (var origin in preferredOrigins)
        {
            _ctx.Commands.Dispatch(new PlaceBuildingCommand(BuildingDefIds.CarpenterWorkshop, origin));
            if (buildings.GetAll().Any(building => building.BuildingDefId == BuildingDefIds.CarpenterWorkshop))
                return;
        }

        _ctx.Logger?.Warn("FortressBootstrapSystem: failed to place starter carpenter workshop.");
    }

    private static void ApplyProvenance(Dwarf dwarf, DwarfProvenanceComponent? provenance)
    {
        if (provenance is null)
            return;

        dwarf.Provenance.WorldSeed = provenance.WorldSeed;
        dwarf.Provenance.FigureId = provenance.FigureId;
        dwarf.Provenance.HouseholdId = provenance.HouseholdId;
        dwarf.Provenance.CivilizationId = provenance.CivilizationId;
        dwarf.Provenance.OriginSiteId = provenance.OriginSiteId;
        dwarf.Provenance.BirthSiteId = provenance.BirthSiteId;
        dwarf.Provenance.MigrationWaveId = provenance.MigrationWaveId;
        dwarf.Provenance.WorldX = provenance.WorldX;
        dwarf.Provenance.WorldY = provenance.WorldY;
        dwarf.Provenance.RegionX = provenance.RegionX;
        dwarf.Provenance.RegionY = provenance.RegionY;
    }

    private void ApplySkills(Dwarf dwarf, IReadOnlyDictionary<string, int>? skillLevels)
    {
        if (skillLevels is null || skillLevels.Count == 0)
            return;

        foreach (var (skillId, level) in skillLevels)
            dwarf.Skills.RestoreSkill(skillId, Math.Max(0, level), 0f);
    }

    private void ApplyFoodPreferences(Dwarf dwarf, string? likedFoodId, string? dislikedFoodId)
    {
        if (string.IsNullOrWhiteSpace(likedFoodId) || string.IsNullOrWhiteSpace(dislikedFoodId) ||
            string.Equals(likedFoodId, dislikedFoodId, StringComparison.OrdinalIgnoreCase))
        {
            RandomizeFoodPreferences(dwarf);
            return;
        }

        dwarf.Preferences.LikedFoodId = likedFoodId;
        dwarf.Preferences.LikeStrength = 200;
        dwarf.Preferences.DislikedFoodId = dislikedFoodId;
        dwarf.Preferences.DislikeStrength = 64;
    }

    private void RandomizeFoodPreferences(Dwarf dwarf)
    {
        var data = _ctx!.TryGet<DataManager>();
        if (data is null) return;

        var foodItems = data.Items.All()
            .Where(i => i.Tags.Contains(TagIds.Food))
            .ToArray();
        if (foodItems.Length < 2) return;

        var rng = new Random(DwarfAppearanceComponent.CreateSeed(dwarf.Id, dwarf.FirstName, dwarf.Position.Position) ^ 104729);
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

    private void SpawnStarterCompanions(Vec3i embark, int seed)
    {
        var data = _ctx!.TryGet<DataManager>();
        var companionIds = data?.ContentQueries?.ResolveCreatureDefIdsByTag(TagIds.Pet) ?? Array.Empty<string>();
        if (companionIds.Count == 0)
            return;

        var ordered = companionIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var startIndex = ordered.Length <= 1 ? 0 : Math.Abs(seed) % ordered.Length;
        var spawnOffsets = new[]
        {
            new Vec3i(2, 0, 0),
            new Vec3i(-2, 0, 0),
        };

        for (var i = 0; i < Math.Min(spawnOffsets.Length, ordered.Length); i++)
        {
            var creatureDefId = ordered[(startIndex + i) % ordered.Length];
            var maxHealth = data?.Creatures.GetOrNull(creatureDefId)?.MaxHealth ?? 30f;
            _ctx.Get<EntityRegistry>().Register(CreateCreatureFromDef(creatureDefId, embark + spawnOffsets[i], maxHealth));
        }
    }

    private string ResolveStarterHostileCreatureDefId()
    {
        var data = _ctx!.TryGet<DataManager>();
        var macroState = _ctx.TryGet<WorldMacroStateService>();
        var fallback = data?.ContentQueries?.ResolveDefaultHostileCreatureDefId();
        return macroState?.GetPrimaryHostileUnitDefId(fallback ?? WorldEventDefaults.PrimaryHostileUnitDefId)
            ?? fallback
            ?? WorldEventDefaults.PrimaryHostileUnitDefId;
    }

    private string ResolveMaterialFormItemDefId(string materialId, string formRole, string fallbackItemDefId)
    {
        var data = _ctx!.TryGet<DataManager>();
        var resolvedItemDefId = data?.ContentQueries?.ResolveMaterialFormItemDefId(materialId, formRole);
        return !string.IsNullOrWhiteSpace(resolvedItemDefId) && data!.Items.Contains(resolvedItemDefId)
            ? resolvedItemDefId
            : fallbackItemDefId;
    }
}

using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

/// <summary>Fired whenever an autonomous behavior executes for an entity.</summary>
public record struct BehaviorFiredEvent(int EntityId, string BehaviorId);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Runs autonomous (non-commanded) behaviors for all alive sapient entities.
/// Behaviors are evaluated in priority order each tick; each behavior has a
/// cooldown so it doesn't fire every single tick.
///
/// Built-in behaviors:
///   - EatFoodBehavior  : hungry creatures seek diet-appropriate food
///   - DrinkWaterBehavior: thirsty creatures seek nearby water and drink
///   - HostilePursuitBehavior: hostile creatures close distance to nearby dwarves
///   - GroomingBehavior : creatures with "groomer" tag periodically lick body parts → ingestion
///   - SocializeBehavior: when Social need is low, seek adjacent dwarf → satisfy Social
///   - TantrumBehavior  : when Mood == Sufferer → destroy items, optionally attack nearby dwarves
///   - WanderBehavior   : creatures without an active job take a random step
///
/// Order 12 — after ContaminationSystem (11), so coatings are up to date before grooming fires.
/// </summary>
public sealed class BehaviorSystem : IGameSystem
{
    public string SystemId    => SystemIds.BehaviorSystem;
    public int    UpdateOrder => 12;
    public bool   IsEnabled   { get; set; } = true;

    private GameContext? _ctx;
    private SimulationProfiler? _profiler;
    private readonly List<IBehavior> _dwarfBehaviors = new();
    private readonly List<IBehavior> _creatureBehaviors = new();

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        _profiler = ctx.Profiler;

        // Register built-in behaviors in execution priority order
        _dwarfBehaviors.Clear();
        _creatureBehaviors.Clear();

        var tantrum = new TantrumBehavior();
        var drinkWater = new DrinkWaterBehavior();
        var eatFood = new EatFoodBehavior();
        var hostilePursuit = new HostilePursuitBehavior();
        var grooming = new GroomingBehavior();
        var socialize = new SocializeBehavior();
        var wander = new WanderBehavior();

        _dwarfBehaviors.Add(tantrum);
        _dwarfBehaviors.Add(grooming);
        _dwarfBehaviors.Add(socialize);

        _creatureBehaviors.Add(drinkWater);
        _creatureBehaviors.Add(eatFood);
        _creatureBehaviors.Add(hostilePursuit);
        _creatureBehaviors.Add(grooming);
        _creatureBehaviors.Add(wander);
    }

    public void Tick(float delta)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        using var behaviorScope = _profiler?.Measure("behavior_system") ?? default;

        using (var dwarfScope = _profiler?.Measure("behavior_dwarves") ?? default)
        {
            foreach (var dwarf in registry.GetAlive<Dwarf>())
                RunBehaviors(dwarf, delta, _dwarfBehaviors);
        }

        using (var creatureScope = _profiler?.Measure("behavior_creatures") ?? default)
        {
            foreach (var creature in registry.GetAlive<Creature>())
                RunBehaviors(creature, delta, _creatureBehaviors);
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    // ── Private ────────────────────────────────────────────────────────────

    private void RunBehaviors(Entity entity, float delta, IReadOnlyList<IBehavior> behaviors)
    {
        if (IsSleeping(entity, _ctx!))
            return;

        for (var index = 0; index < behaviors.Count; index++)
        {
            var behavior = behaviors[index];
            behavior.Tick(entity, delta);
            if (behavior.CanFire(entity, _ctx!) && behavior.ShouldFire(entity, _ctx!))
            {
                behavior.Fire(entity, _ctx!);
                behavior.ResetCooldown(entity, _ctx!);
                _ctx!.EventBus.Emit(new BehaviorFiredEvent(entity.Id, behavior.BehaviorId));
                break; // one behavior per entity per tick
            }
        }
    }

    private static bool IsSleeping(Entity entity, GameContext ctx)
    {
        var sleepSystem = ctx.TryGet<SleepSystem>();
        if (sleepSystem is null)
            return false;

        return entity switch
        {
            Dwarf => sleepSystem.IsSleeping(entity.Id),
            Creature => sleepSystem.IsCreatureSleeping(entity.Id),
            _ => false,
        };
    }
}

// ── Behavior interface ─────────────────────────────────────────────────────

/// <summary>A single autonomous behavior that an entity can execute unprompted.</summary>
public interface IBehavior
{
    string BehaviorId { get; }

    /// <summary>Advance cooldown timer.</summary>
    void Tick(Entity entity, float delta);

    /// <summary>True when cooldown has expired and this behavior is eligible to run.</summary>
    bool CanFire(Entity entity, GameContext ctx);

    /// <summary>True when the entity's state meets the preconditions for the behavior.</summary>
    bool ShouldFire(Entity entity, GameContext ctx);

    /// <summary>Execute the behavior. Side effects go here.</summary>
    void Fire(Entity entity, GameContext ctx);

    void ResetCooldown(Entity entity, GameContext ctx);
}

public abstract class CooldownBehavior : IBehavior
{
    private readonly Dictionary<int, float> _cooldowns = new();
    private readonly float _baseCooldownSeconds;

    protected CooldownBehavior(float cooldownSeconds)
    {
        _baseCooldownSeconds = cooldownSeconds;
    }

    public abstract string BehaviorId { get; }

    public void Tick(Entity entity, float delta)
    {
        if (!_cooldowns.TryGetValue(entity.Id, out var remaining))
            return;

        remaining = Math.Max(0f, remaining - delta);
        if (remaining <= 0f)
            _cooldowns.Remove(entity.Id);
        else
            _cooldowns[entity.Id] = remaining;
    }

    public bool CanFire(Entity entity, GameContext ctx)
        => !_cooldowns.TryGetValue(entity.Id, out var remaining) || remaining <= 0f;

    public void ResetCooldown(Entity entity, GameContext ctx)
    {
        var cooldownSeconds = Math.Max(0.05f, GetCooldownSeconds(entity, ctx));
        var jitterFraction = Math.Max(0f, GetCooldownJitterFraction(entity, ctx));
        if (jitterFraction > 0f)
        {
            var spread = cooldownSeconds * jitterFraction;
            cooldownSeconds += (Random.Shared.NextSingle() * spread * 2f) - spread;
        }

        var appliedCooldown = Math.Max(0.05f, cooldownSeconds);
        _cooldowns[entity.Id] = appliedCooldown;
    }

    protected virtual float GetCooldownSeconds(Entity entity, GameContext ctx)
        => _baseCooldownSeconds;

    protected virtual float GetCooldownJitterFraction(Entity entity, GameContext ctx)
        => 0f;

    public abstract bool ShouldFire(Entity entity, GameContext ctx);

    public abstract void Fire(Entity entity, GameContext ctx);
}

// ── Built-in behaviors ──────────────────────────────────────────────────────

/// <summary>
/// Entities with the "groomer" tag periodically groom themselves:
/// for each coated body part, call ContaminationSystem.IngestBodyPartCoating.
/// This is what makes cats ingest spilled alcohol.
/// </summary>
public sealed class GroomingBehavior : CooldownBehavior
{
    public override string BehaviorId => BehaviorIds.Grooming;
    private const float CooldownSeconds = 30f;

    public GroomingBehavior() : base(CooldownSeconds) { }

    public override bool ShouldFire(Entity entity, GameContext ctx)
    {
        if (!entity.Components.Has<BodyPartComponent>()) return false;
        if (!CanGroom(entity, ctx)) return false;
        var parts = entity.Components.Get<BodyPartComponent>();
        foreach (var part in parts.All)
            if (part.CoatingMaterialId is not null)
                return true;

        return false;
    }

    public override void Fire(Entity entity, GameContext ctx)
    {
        var contamination = ctx.TryGet<ContaminationSystem>();
        if (contamination is null) return;

        var parts = entity.Components.Get<BodyPartComponent>();
        foreach (var part in parts.All)
            if (part.CoatingMaterialId is not null)
                contamination.IngestBodyPartCoating(entity.Id, part.PartId);
    }

    private static bool CanGroom(Entity entity, GameContext ctx)
    {
        if (entity is Dwarf) return true;

        var dm = ctx.TryGet<DataManager>();
        var def = dm?.Creatures.GetOrNull(entity.DefId);
        if (def?.AuthoredCanGroom is bool authoredCanGroom)
            return authoredCanGroom;
        if (def?.IsGroomer() == true) return true;

        if (!entity.Components.Has<BodyPartComponent>()) return false;
        var parts = entity.Components.Get<BodyPartComponent>();
        return parts.TryGet(BodyPartIds.Paws, out _) || parts.TryGet(BodyPartIds.Feet, out _);
    }
}

/// <summary>
/// When Social need drops below 0.4, a dwarf looks for an adjacent live dwarf and
/// "socializes", satisfying both parties' Social need and adding a positive thought.
/// </summary>
public sealed class SocializeBehavior : CooldownBehavior
{
    public override string BehaviorId => BehaviorIds.Socialize;
    private const float CooldownSeconds = 60f;

    public SocializeBehavior() : base(CooldownSeconds) { }

    public override bool ShouldFire(Entity entity, GameContext ctx)
    {
        if (entity is not Dwarf dwarf) return false;
        return dwarf.Needs.Social.Level < 0.4f;
    }

    public override void Fire(Entity entity, GameContext ctx)
    {
        if (entity is not Dwarf self) return;

        var partner = FindPartner(self, ctx);
        if (partner is null) return;

        float satisfaction = 0.3f;
        self.Needs.Social.Satisfy(satisfaction);
        partner.Needs.Social.Satisfy(satisfaction);

        AddThought(self,    ctx, ThoughtIds.Socialized, "Enjoyed some company", 0.08f, 1800f);
        AddThought(partner, ctx, ThoughtIds.Socialized, "Enjoyed some company", 0.08f, 1800f);
    }

    private static void AddThought(Dwarf dwarf, GameContext ctx, string id, string desc, float mod, float duration)
    {
        dwarf.Components.Get<ThoughtComponent>()
             .AddThought(new Thought(id, desc, mod, duration));
    }

    private static Dwarf? FindPartner(Dwarf self, GameContext ctx)
    {
        var origin = self.Position.Position;
        var registry = ctx.Get<EntityRegistry>();
        var spatial = ctx.TryGet<SpatialIndexSystem>();

        if (spatial is not null)
        {
            for (var dx = -2; dx <= 2; dx++)
            for (var dy = -2; dy <= 2; dy++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > 2)
                    continue;

                var candidate = new Vec3i(origin.X + dx, origin.Y + dy, origin.Z);
                foreach (var dwarfId in spatial.GetDwarvesAt(candidate))
                {
                    if (dwarfId == self.Id)
                        continue;

                    if (registry.TryGetById<Dwarf>(dwarfId, out var partner) && partner is not null)
                        return partner;
                }
            }

            return null;
        }

        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            if (dwarf.Id == self.Id)
                continue;
            if (dwarf.Position.Position.ManhattanDistanceTo(origin) <= 2)
                return dwarf;
        }

        return null;
    }
}

/// <summary>
/// When a dwarf's Mood drops to Sufferer and they haven't already snapped,
/// they attack a random adjacent dwarf and/or destroy a random adjacent item.
/// Sets HasSnapped = true to prevent continuous berserk attacks.
/// </summary>
public sealed class TantrumBehavior : CooldownBehavior
{
    public override string BehaviorId => BehaviorIds.Tantrum;
    private const float CooldownSeconds = 10f;

    public TantrumBehavior() : base(CooldownSeconds) { }

    public override bool ShouldFire(Entity entity, GameContext ctx)
    {
        if (entity is not Dwarf dwarf) return false;
        var mood = dwarf.Components.Get<MoodComponent>();
        return mood.Current == Mood.Sufferer && !mood.HasSnapped;
    }

    public override void Fire(Entity entity, GameContext ctx)
    {
        if (entity is not Dwarf dwarf) return;

        var mood = dwarf.Components.Get<MoodComponent>();
        mood.HasSnapped = true;

        var pos      = dwarf.Position.Position;
        var registry = ctx.Get<EntityRegistry>();
        var combat   = ctx.TryGet<CombatSystem>();
        var items    = ctx.TryGet<ItemSystem>();
        var spatial  = ctx.TryGet<SpatialIndexSystem>();

        // Attack a random adjacent dwarf (pick randomly without sorting)
        var nearby = new List<Dwarf>(4);
        if (spatial is not null)
        {
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > 1)
                    continue;

                var candidate = new Vec3i(pos.X + dx, pos.Y + dy, pos.Z);
                foreach (var dwarfId in spatial.GetDwarvesAt(candidate))
                {
                    if (dwarfId == dwarf.Id)
                        continue;

                    if (registry.TryGetById<Dwarf>(dwarfId, out var targetDwarf) && targetDwarf is not null)
                        nearby.Add(targetDwarf);
                }
            }
        }
        else
        {
            foreach (var other in registry.GetAlive<Dwarf>())
            {
                if (other.Id == dwarf.Id)
                    continue;
                if (other.Position.Position.ManhattanDistanceTo(pos) <= 1)
                    nearby.Add(other);
            }
        }

        var target = nearby.Count > 0 ? nearby[Random.Shared.Next(nearby.Count)] : null;

        if (target is not null)
            combat?.AttackEntity(dwarf.Id, target.Id);

        // Destroy a random adjacent item
        Item? nearbyItem = null;
        if (items is not null)
        {
            items.TryGetItemAt(pos, out nearbyItem);
            if (nearbyItem is null)
                items.TryGetItemAt(pos + Vec3i.North, out nearbyItem);
            if (nearbyItem is null)
                items.TryGetItemAt(pos + Vec3i.South, out nearbyItem);
        }

        if (nearbyItem is not null)
            items?.DestroyItem(nearbyItem.Id);

        // Add a "went berserk" thought (negative — snapping doesn't feel good)
        dwarf.Components.Get<ThoughtComponent>()
               .AddThought(new Thought(ThoughtIds.Tantrum, "Lost control in a rage", -0.20f, 7200f));
    }
}

internal static class CreatureTraversalProfile
{
    public static (bool CanSwim, bool RequiresSwimming) Resolve(Entity entity, GameContext ctx)
    {
        if (entity is not Creature creature)
            return (false, false);

        return Resolve(creature, ctx);
    }

    public static (bool CanSwim, bool RequiresSwimming) Resolve(Creature creature, GameContext ctx)
    {
        var def = ctx.TryGet<DataManager>()?.Creatures.GetOrNull(creature.DefId);
        if (def is null)
            return (false, false);

        return def.ResolveTraversal();
    }
}

internal static class AutonomousPacing
{
    public static float ScaleMoveCooldown(
        Entity entity,
        GameContext ctx,
        float baselineSeconds,
        float minSeconds,
        float maxSeconds,
        float urgencyMultiplier = 1f)
    {
        var speed = ResolveSpeed(entity, ctx);
        var effectiveRate = Math.Max(0.2f, speed * Math.Max(0.35f, urgencyMultiplier));
        return Math.Clamp(baselineSeconds / effectiveRate, minSeconds, maxSeconds);
    }

    private static float ResolveSpeed(Entity entity, GameContext ctx)
    {
        if (entity.Components.Has<StatComponent>())
            return Math.Max(0.2f, entity.Components.Get<StatComponent>().Speed.Value);

        if (entity is Creature creature)
            return Math.Max(0.2f, ctx.TryGet<DataManager>()?.Creatures.GetOrNull(creature.DefId)?.BaseSpeed ?? 1f);

        return 1f;
    }
}

internal static class BehaviorSearch
{
    public static readonly Vec3i[] CardinalDirections =
        [Vec3i.North, Vec3i.South, Vec3i.East, Vec3i.West];

    public static bool TryFindNearestReachableTarget(
        WorldMap map,
        Vec3i origin,
        int searchRadius,
        bool canSwim,
        bool requiresSwimming,
        Predicate<Vec3i> isTarget,
        out Vec3i nextStep)
    {
        nextStep = origin;

        var visited = new HashSet<Vec3i> { origin };
        var queue = new Queue<(Vec3i Position, Vec3i FirstStep)>();

        for (var i = 0; i < CardinalDirections.Length; i++)
        {
            var next = origin + CardinalDirections[i];
            if (!IsSearchCandidate(next, origin, searchRadius, map, visited, canSwim, requiresSwimming))
                continue;

            if (isTarget(next))
            {
                nextStep = next;
                return true;
            }

            queue.Enqueue((next, next));
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            for (var i = 0; i < CardinalDirections.Length; i++)
            {
                var next = current.Position + CardinalDirections[i];
                if (!IsSearchCandidate(next, origin, searchRadius, map, visited, canSwim, requiresSwimming))
                    continue;

                if (isTarget(next))
                {
                    nextStep = current.FirstStep;
                    return true;
                }

                queue.Enqueue((next, current.FirstStep));
            }
        }

        return false;
    }

    public static void FillShuffledDirections(Span<Vec3i> buffer)
    {
        for (var i = 0; i < CardinalDirections.Length; i++)
            buffer[i] = CardinalDirections[i];

        for (var i = buffer.Length - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }
    }

    private static bool IsSearchCandidate(
        Vec3i candidate,
        Vec3i origin,
        int searchRadius,
        WorldMap map,
        HashSet<Vec3i> visited,
        bool canSwim,
        bool requiresSwimming)
    {
        if (!map.IsInBounds(candidate) || !visited.Add(candidate))
            return false;

        var dx = Math.Abs(candidate.X - origin.X);
        var dy = Math.Abs(candidate.Y - origin.Y);
        if (dx > searchRadius || dy > searchRadius)
            return false;

        return map.IsTraversable(candidate, canSwim, requiresSwimming);
    }
}

/// <summary>
/// Hungry creatures seek food based on diet tags and satisfy hunger when feeding.
/// Herbivores graze plants, aquatic grazers feed in water, and omnivores/carnivores eat food items.
/// </summary>
public sealed class EatFoodBehavior : CooldownBehavior
{
    public override string BehaviorId => BehaviorIds.EatFood;
    private const float CooldownSeconds = 2f;
    private const float HungerSatisfaction = 0.8f;
    private const int SearchRadius = 14;

    public EatFoodBehavior() : base(CooldownSeconds) { }

    protected override float GetCooldownSeconds(Entity entity, GameContext ctx)
    {
        if (entity is not Creature creature)
            return CooldownSeconds;

        var urgency = 1f + ((1f - creature.Needs.Hunger.Level) * 0.45f);
        return AutonomousPacing.ScaleMoveCooldown(entity, ctx, CooldownSeconds, 0.35f, 3.5f, urgency);
    }

    protected override float GetCooldownJitterFraction(Entity entity, GameContext ctx)
        => 0.18f;

    public override bool ShouldFire(Entity entity, GameContext ctx)
    {
        if (entity is not Creature creature)
            return false;

        return creature.Needs.Hunger.IsCritical;
    }

    public override void Fire(Entity entity, GameContext ctx)
    {
        if (entity is not Creature creature)
            return;

        var map = ctx.Get<WorldMap>();
        var itemSystem = ctx.TryGet<ItemSystem>();
        var dataManager = ctx.TryGet<DataManager>();
        var diet = ResolveDiet(creature, dataManager);
        var edibleItemPositions = ResolveEdibleItemPositions(itemSystem, dataManager, diet);
        var origin = creature.Position.Position;

        if (TryEatAtCurrentPosition(creature, ctx, origin, map, itemSystem, dataManager, diet))
            return;

        if (!TryFindStepTowardFood(creature, ctx, map, origin, diet, edibleItemPositions, dataManager, out var nextStep))
            return;

        EntityMovement.TryMove(ctx, creature, nextStep);

        TryEatAtCurrentPosition(creature, ctx, nextStep, map, itemSystem, dataManager, diet);
    }

    private static bool TryEatAtCurrentPosition(
        Creature creature,
        GameContext ctx,
        Vec3i position,
        WorldMap map,
        ItemSystem? itemSystem,
        DataManager? dataManager,
        CreatureDiet diet)
    {
        if (CanHarvestWildPlant(map, dataManager, position, diet) &&
            PlantHarvesting.TryHarvestPlant(ctx, position, dropHarvestItem: false, dropSeedItem: false, out var plantHarvest))
        {
            creature.Needs.Hunger.Satisfy(HungerSatisfaction);
            ctx.EventBus.Emit(new EntityActivityEvent(creature.Id, $"Ate {plantHarvest.HarvestDisplayName}", position));
            return true;
        }

        if (CanGrazeTile(map, position, diet))
        {
            creature.Needs.Hunger.Satisfy(HungerSatisfaction);
            ctx.EventBus.Emit(new EntityActivityEvent(creature.Id, ResolveGrazingDescription(diet), position));
            return true;
        }

        if (itemSystem is null || dataManager is null)
            return false;

        Item? edibleItem = null;
        foreach (var item in itemSystem.GetItemsAt(position))
        {
            if (!IsEdibleItemForDiet(item, dataManager, diet))
                continue;

            edibleItem = item;
            break;
        }

        if (edibleItem is null)
            return false;

        var itemDisplayName = dataManager.Items.GetOrNull(edibleItem.DefId)?.DisplayName
                              ?? edibleItem.DefId.Replace('_', ' ');
        itemSystem.DestroyItem(edibleItem.Id);
        creature.Needs.Hunger.Satisfy(HungerSatisfaction);
        ctx.EventBus.Emit(new EntityActivityEvent(creature.Id, $"Ate {itemDisplayName}", position));
        return true;
    }

    private static bool TryFindStepTowardFood(
        Creature creature,
        GameContext ctx,
        WorldMap map,
        Vec3i origin,
        CreatureDiet diet,
        ISet<Vec3i> edibleItemPositions,
        DataManager? dataManager,
        out Vec3i nextStep)
    {
        nextStep = origin;
        var (canSwim, requiresSwimming) = CreatureTraversalProfile.Resolve(creature, ctx);

        return BehaviorSearch.TryFindNearestReachableTarget(
            map,
            origin,
            SearchRadius,
            canSwim,
            requiresSwimming,
            candidate =>
                CanGrazeTile(map, candidate, diet) ||
                edibleItemPositions.Contains(candidate) ||
                CanHarvestWildPlant(map, dataManager, candidate, diet),
            out nextStep);
    }

    private static ISet<Vec3i> ResolveEdibleItemPositions(
        ItemSystem? itemSystem,
        DataManager? dataManager,
        CreatureDiet diet)
    {
        var positions = new HashSet<Vec3i>();
        if (itemSystem is null || dataManager is null)
            return positions;

        foreach (var item in itemSystem.GetUsableItems())
        {
            if (!IsEdibleItemForDiet(item, dataManager, diet))
                continue;

            positions.Add(item.Position.Position);
        }

        return positions;
    }

    private static bool IsEdibleItemForDiet(Item item, DataManager dataManager, CreatureDiet diet)
    {
        var itemDef = dataManager.Items.GetOrNull(item.DefId);
        if (itemDef is null)
            return false;

        return diet switch
        {
            CreatureDiet.Herbivore =>
                itemDef.Tags.HasAny(TagIds.Plant, TagIds.Food),
            CreatureDiet.Carnivore =>
                itemDef.Tags.HasAny(TagIds.Meat, TagIds.Corpse, TagIds.Food),
            CreatureDiet.Omnivore =>
                itemDef.Tags.HasAny(TagIds.Food, TagIds.Plant, TagIds.Meat, TagIds.Corpse),
            CreatureDiet.AquaticGrazer =>
                false,
            _ => itemDef.Tags.Contains(TagIds.Food),
        };
    }

    private static bool CanGrazeTile(WorldMap map, Vec3i position, CreatureDiet diet)
    {
        if (!map.IsInBounds(position))
            return false;

        var tile = map.GetTile(position);

        return diet switch
        {
            CreatureDiet.Herbivore =>
                tile.TileDefId is TileDefIds.Grass or TileDefIds.Tree,
            CreatureDiet.AquaticGrazer =>
                (tile.FluidType == FluidType.Water || tile.TileDefId == TileDefIds.Water) && tile.FluidLevel > 0,
            CreatureDiet.Omnivore =>
                tile.TileDefId is TileDefIds.Grass,
            _ => false,
        };
    }

    private static bool CanHarvestWildPlant(WorldMap map, DataManager? dataManager, Vec3i position, CreatureDiet diet)
    {
        if (dataManager is null)
            return false;

        if (diet is not CreatureDiet.Herbivore and not CreatureDiet.Omnivore)
            return false;

        return PlantHarvesting.TryGetHarvestablePlant(map, dataManager, position, out _);
    }

    private static CreatureDiet ResolveDiet(Creature creature, DataManager? dataManager)
    {
        var def = dataManager?.Creatures.GetOrNull(creature.DefId);
        if (def is not null)
            return def.ResolveDiet();

        return CreatureDiet.Omnivore;
    }

    private static string ResolveGrazingDescription(CreatureDiet diet)
        => diet switch
        {
            CreatureDiet.AquaticGrazer => "Fed in water",
            CreatureDiet.Herbivore => "Grazed",
            _ => "Ate",
        };
}

/// <summary>
/// Thirsty creatures move toward nearby water and drink when they can reach it.
/// They can drink while standing on water or from an adjacent tile.
/// </summary>
public sealed class DrinkWaterBehavior : CooldownBehavior
{
    public override string BehaviorId => BehaviorIds.DrinkWater;
    private const float CooldownSeconds = 1f;
    private const float DrinkSatisfaction = 0.9f;
    private const int SearchRadius = 14;

    public DrinkWaterBehavior() : base(CooldownSeconds) { }

    protected override float GetCooldownSeconds(Entity entity, GameContext ctx)
    {
        if (entity is not Creature creature)
            return CooldownSeconds;

        var urgency = 1f + ((1f - creature.Needs.Thirst.Level) * 0.55f);
        return AutonomousPacing.ScaleMoveCooldown(entity, ctx, CooldownSeconds, 0.2f, 2.0f, urgency);
    }

    protected override float GetCooldownJitterFraction(Entity entity, GameContext ctx)
        => 0.14f;

    public override bool ShouldFire(Entity entity, GameContext ctx)
    {
        if (entity is not Creature creature)
            return false;

        return creature.Needs.Thirst.IsCritical;
    }

    public override void Fire(Entity entity, GameContext ctx)
    {
        if (entity is not Creature creature)
            return;

        var map = ctx.Get<WorldMap>();
        var origin = creature.Position.Position;

        if (CanDrinkAt(map, origin))
        {
            creature.Needs.Thirst.Satisfy(DrinkSatisfaction);
            ctx.EventBus.Emit(new EntityActivityEvent(creature.Id, "Drank water", origin));
            return;
        }

        if (!TryFindStepTowardDrinkablePosition(creature, ctx, map, origin, out var nextStep))
            return;

        EntityMovement.TryMove(ctx, creature, nextStep);

        if (CanDrinkAt(map, nextStep))
        {
            creature.Needs.Thirst.Satisfy(DrinkSatisfaction);
            ctx.EventBus.Emit(new EntityActivityEvent(creature.Id, "Drank water", nextStep));
        }
    }

    private static bool TryFindStepTowardDrinkablePosition(
        Creature creature,
        GameContext ctx,
        WorldMap map,
        Vec3i origin,
        out Vec3i nextStep)
    {
        nextStep = origin;
        var (canSwim, requiresSwimming) = CreatureTraversalProfile.Resolve(creature, ctx);

        return BehaviorSearch.TryFindNearestReachableTarget(
            map,
            origin,
            SearchRadius,
            canSwim,
            requiresSwimming,
            candidate => CanDrinkAt(map, candidate),
            out nextStep);
    }

    private static bool CanDrinkAt(WorldMap map, Vec3i position)
    {
        if (IsDrinkableWaterTile(map, position))
            return true;

        for (var i = 0; i < BehaviorSearch.CardinalDirections.Length; i++)
        {
            var dir = BehaviorSearch.CardinalDirections[i];
            if (IsDrinkableWaterTile(map, position + dir))
                return true;
        }

        return false;
    }

    private static bool IsDrinkableWaterTile(WorldMap map, Vec3i position)
    {
        if (!map.IsInBounds(position))
            return false;

        var tile = map.GetTile(position);
        if (tile.FluidType == FluidType.Magma || tile.TileDefId == TileDefIds.Magma)
            return false;

        return (tile.FluidType == FluidType.Water || tile.TileDefId == TileDefIds.Water)
               && tile.FluidLevel > 0;
    }
}

public sealed class HostilePursuitBehavior : CooldownBehavior
{
    public override string BehaviorId => BehaviorIds.HuntDwarves;
    private const float CooldownSeconds = 0.75f;
    private const int SearchRadius = 12;

    public HostilePursuitBehavior() : base(CooldownSeconds) { }

    protected override float GetCooldownSeconds(Entity entity, GameContext ctx)
        => AutonomousPacing.ScaleMoveCooldown(entity, ctx, CooldownSeconds, 0.15f, 1.25f, urgencyMultiplier: 1.35f);

    protected override float GetCooldownJitterFraction(Entity entity, GameContext ctx)
        => 0.12f;

    public override bool ShouldFire(Entity entity, GameContext ctx)
    {
        if (entity is not Creature creature || !creature.IsHostile || creature.Health.IsDead)
            return false;

        var target = FindNearestDwarf(creature, ctx);
        return target is not null && creature.Position.Position.ManhattanDistanceTo(target.Position.Position) > 1;
    }

    public override void Fire(Entity entity, GameContext ctx)
    {
        if (entity is not Creature creature || !creature.IsHostile || creature.Health.IsDead)
            return;

        var target = FindNearestDwarf(creature, ctx);
        if (target is null)
            return;

        var map = ctx.Get<WorldMap>();
        var origin = creature.Position.Position;
        if (origin.ManhattanDistanceTo(target.Position.Position) <= 1)
            return;

        var (canSwim, requiresSwimming) = CreatureTraversalProfile.Resolve(creature, ctx);
        var path = Pathfinder.FindPath(map, origin, target.Position.Position, canSwim, requiresSwimming);
        if (path.Count <= 1)
            return;

        var nextStep = path[1];
        if (nextStep == target.Position.Position)
            return;

        if (!map.IsTraversable(nextStep, canSwim, requiresSwimming))
            return;

        if (IsOccupiedByOtherEntity(ctx.TryGet<SpatialIndexSystem>(), nextStep, creature.Id))
            return;

        EntityMovement.TryMove(ctx, creature, nextStep);
    }

    private static Dwarf? FindNearestDwarf(Creature creature, GameContext ctx)
    {
        var origin = creature.Position.Position;
        var registry = ctx.Get<EntityRegistry>();
        var nearestDistance = int.MaxValue;
        Dwarf? nearest = null;

        var spatial = ctx.TryGet<SpatialIndexSystem>();
        if (spatial is not null)
        {
            var dwarfIds = new List<int>();
            spatial.CollectDwarvesInBounds(
                origin.Z,
                Math.Max(0, origin.X - SearchRadius),
                Math.Max(0, origin.Y - SearchRadius),
                origin.X + SearchRadius,
                origin.Y + SearchRadius,
                dwarfIds);

            for (var i = 0; i < dwarfIds.Count; i++)
            {
                if (!registry.TryGetById<Dwarf>(dwarfIds[i], out var dwarf) || dwarf is null || dwarf.Health.IsDead)
                    continue;

                var distance = origin.ManhattanDistanceTo(dwarf.Position.Position);
                if (distance > SearchRadius || distance >= nearestDistance)
                    continue;

                nearest = dwarf;
                nearestDistance = distance;
            }

            return nearest;
        }

        foreach (var dwarf in registry.GetAlive<Dwarf>())
        {
            if (dwarf.Health.IsDead)
                continue;

            var distance = origin.ManhattanDistanceTo(dwarf.Position.Position);
            if (distance > SearchRadius || distance >= nearestDistance)
                continue;

            nearest = dwarf;
            nearestDistance = distance;
        }

        return nearest;
    }

    private static bool IsOccupiedByOtherEntity(SpatialIndexSystem? spatial, Vec3i position, int entityId)
    {
        if (spatial is null)
            return false;

        foreach (var dwarfId in spatial.GetDwarvesAt(position))
            if (dwarfId != entityId)
                return true;

        foreach (var creatureId in spatial.GetCreaturesAt(position))
            if (creatureId != entityId)
                return true;

        return false;
    }
}

public sealed class WanderBehavior : CooldownBehavior
{
    public override string BehaviorId => BehaviorIds.Wander;
    private const float CooldownSeconds = 5f;

    public WanderBehavior() : base(CooldownSeconds) { }

    protected override float GetCooldownSeconds(Entity entity, GameContext ctx)
        => AutonomousPacing.ScaleMoveCooldown(entity, ctx, CooldownSeconds, 1.2f, 8.0f);

    protected override float GetCooldownJitterFraction(Entity entity, GameContext ctx)
        => 0.35f;

    public override bool ShouldFire(Entity entity, GameContext ctx)
    {
        if (entity is Dwarf)
            return false;

        return entity.Components.Has<PositionComponent>();
    }

    public override void Fire(Entity entity, GameContext ctx)
    {
        var map = ctx.Get<WorldMap>();
        var pos = entity.Components.Get<PositionComponent>().Position;
        var (canSwim, requiresSwimming) = CreatureTraversalProfile.Resolve(entity, ctx);
        Span<Vec3i> shuffled = stackalloc Vec3i[4];
        BehaviorSearch.FillShuffledDirections(shuffled);

        // Aquatic creatures should remain in swimmable tiles.
        if (requiresSwimming && !map.IsSwimmable(pos))
        {
            foreach (var dir in shuffled)
            {
                var next = pos + dir;
                if (!map.IsSwimmable(next))
                    continue;

                EntityMovement.TryMove(ctx, entity, next);
                return;
            }

            return;
        }

        // Pick a random traversable neighbour (Fisher-Yates shuffle on small array)
        foreach (var dir in shuffled)
        {
            var next = pos + dir;
            if (map.IsTraversable(next, canSwim, requiresSwimming))
            {
                EntityMovement.TryMove(ctx, entity, next);
                break;
            }
        }
    }

}

/// <summary>String constants for behavior IDs.</summary>
public static class BehaviorIds
{
    public const string EatFood = "eat_food";
    public const string DrinkWater = "drink_water";
    public const string HuntDwarves = "hunt_dwarves";
    public const string Grooming  = "grooming";
    public const string Socialize = "socialize";
    public const string Tantrum   = "tantrum";
    public const string Wander    = "wander";
}

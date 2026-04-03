using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct WorldEventFiredEvent(string EventDefId, string DisplayName);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Reads WorldEventDefs, evaluates triggers each season/day, 
/// and fires effect blocks via EffectApplicator.
/// Spawn ops (spawn_migrants, spawn_hostiles) are handled here directly
/// since they create entities rather than targeting existing ones.
/// Order 17.
/// </summary>
public sealed class WorldEventManager : IGameSystem
{
    public string SystemId    => SystemIds.WorldEventManager;
    public int    UpdateOrder => 17;
    public bool   IsEnabled   { get; set; } = true;

    // track last fired tick per event (for cooldown)
    private readonly Dictionary<string, float> _lastFired = new();
    private float _totalTime = 0f;

    private static readonly string[] MigrantNames =
    [
        "Aban", "Amost", "Asob", "Bim", "Degel", "Eral", "Iden", "Kiln",
        "Litast", "Medtob", "Meng", "Moldath", "Mosus", "Nish", "Reg",
        "Rigoth", "Sibrek", "Sodel", "Thob", "Tosid", "Udib", "Urvad",
        "Uton", "Vathez", "Vucar", "Zulban", "Zuntir"
    ];

    private static readonly string[] MigrantLabors =
    [
        LaborIds.Mining, LaborIds.WoodCutting, LaborIds.Crafting,
        LaborIds.Hauling, LaborIds.Construction, LaborIds.Cooking,
        LaborIds.Brewing, LaborIds.Masonry, LaborIds.Carpentry
    ];

    private GameContext? _ctx;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        ctx.EventBus.On<SeasonChangedEvent>(e => EvaluateEvents(CreateTriggerContext(e)));
        ctx.EventBus.On<DayStartedEvent>(e => EvaluateEvents(CreateTriggerContext(e)));
    }

    public void Tick(float delta) => _totalTime += delta;

    public void OnSave(SaveWriter w)
    {
        w.Write("wem_total_time",  _totalTime);
        w.Write("wem_last_fired",  _lastFired);
    }

    public void OnLoad(SaveReader r)
    {
        _totalTime = r.TryRead<float>("wem_total_time");
        var saved  = r.TryRead<System.Collections.Generic.Dictionary<string, float>>("wem_last_fired");
        if (saved is not null)
        {
            _lastFired.Clear();
            foreach (var kv in saved)
                _lastFired[kv.Key] = kv.Value;
        }
    }

    // ── Private ────────────────────────────────────────────────────────────

    private void EvaluateEvents(WorldEventTriggerContext triggerContext)
    {
        var dm         = _ctx!.TryGet<DataManager>();
        var applicator = _ctx!.TryGet<EffectApplicator>();
        var registry   = _ctx!.Get<EntityRegistry>();
        var macroState = _ctx!.TryGet<WorldMacroStateService>();

        if (dm is null || applicator is null) return;

        foreach (var evDef in dm.WorldEvents.All())
        {
            bool triggered = evDef.Triggers.Any(t => EvaluateTrigger(t, triggerContext, registry, macroState));
            if (!triggered) continue;

            if (_lastFired.TryGetValue(evDef.Id, out var last) &&
                _totalTime - last < evDef.Cooldown) continue;

            if (!evDef.Repeatable && _lastFired.ContainsKey(evDef.Id)) continue;

            var probability = macroState?.TuneEventProbability(evDef.Id, evDef.Probability) ?? evDef.Probability;
            if (Random.Shared.NextSingle() > probability) continue;

            _lastFired[evDef.Id] = _totalTime;

            foreach (var effect in evDef.Effects)
            {
                if (effect.Op == WorldEventEffectOps.SpawnMigrants)
                    SpawnMigrants(effect, registry, macroState);
                else if (effect.Op == WorldEventEffectOps.SpawnHostiles)
                    SpawnHostiles(effect, registry, macroState);
                else
                    foreach (var entity in ResolveEffectTargets(effect, registry))
                        applicator.Apply(effect, entity.Id);
            }

            _ctx.EventBus.Emit(new WorldEventFiredEvent(evDef.Id, evDef.DisplayName));
            _ctx.Logger?.Info($"World event fired: {evDef.Id}");
        }
    }

    private static WorldEventTriggerContext CreateTriggerContext(DayStartedEvent e)
        => new(
            WorldEventTriggerTypes.DayStart,
            Year: e.Year,
            Month: e.Month,
            Day: e.Day,
            Season: (Season)((Math.Clamp(e.Month, 1, 12) - 1) / 3));

    private static WorldEventTriggerContext CreateTriggerContext(SeasonChangedEvent e)
        => new(
            WorldEventTriggerTypes.SeasonChange,
            Year: e.Year,
            Season: e.Season);

    private static bool EvaluateTrigger(
        Data.Defs.EventTrigger trigger,
        WorldEventTriggerContext context,
        EntityRegistry registry,
        WorldMacroStateService? macroState)
    {
        if (!string.Equals(trigger.Type, context.TriggerType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!MatchesIntParam(trigger.Params, "year", context.Year))
            return false;
        if (!MatchesIntParam(trigger.Params, "month", context.Month))
            return false;
        if (!MatchesIntParam(trigger.Params, "day", context.Day))
            return false;
        if (!MatchesIntRange(trigger.Params, "min_day", "max_day", context.Day))
            return false;
        if (!MatchesSeasonParam(trigger.Params, context.Season))
            return false;
        if (!MatchesIntRange(trigger.Params, "min_population", "max_population", registry.CountAlive<Dwarf>()))
            return false;
        if (!MatchesFloatRange(trigger.Params, "min_prosperity", "max_prosperity", macroState?.Current?.Prosperity))
            return false;
        if (!MatchesFloatRange(trigger.Params, "min_threat", "max_threat", macroState?.Current?.Threat))
            return false;

        return true;
    }

    private IEnumerable<Entity> ResolveEffectTargets(EffectBlock effect, EntityRegistry registry)
    {
        effect.Params.TryGetValue("target", out var target);
        var resolvedTarget = string.IsNullOrWhiteSpace(target)
            ? WorldEventTargetTypes.AllDwarves
            : target;

        return resolvedTarget switch
        {
            WorldEventTargetTypes.AllDwarves => registry.GetAlive<Dwarf>().Cast<Entity>(),
            WorldEventTargetTypes.DwarvesWithProfession => ResolveDwarvesWithProfession(effect, registry),
            WorldEventTargetTypes.DwarvesWithAttribute => ResolveDwarvesWithAttribute(effect, registry),
            WorldEventTargetTypes.DwarvesWithLabor => ResolveDwarvesWithLabor(effect, registry),
            WorldEventTargetTypes.AllCreatures => registry.GetAlive<Creature>().Cast<Entity>(),
            WorldEventTargetTypes.HostileCreatures => registry.GetAlive<Creature>().Where(creature => creature.IsHostile).Cast<Entity>(),
            WorldEventTargetTypes.EntitiesWithFactionRole => ResolveEntitiesWithFactionRole(effect, registry),
            WorldEventTargetTypes.EntitiesWithDef => ResolveEntitiesWithDef(effect, registry),
            _ => WarnAndReturnEmpty(resolvedTarget),
        };
    }

    private IEnumerable<Entity> ResolveDwarvesWithProfession(EffectBlock effect, EntityRegistry registry)
    {
        if (!effect.Params.TryGetValue("profession_id", out var professionId) || string.IsNullOrWhiteSpace(professionId))
            return Array.Empty<Entity>();

        return registry.GetAlive<Dwarf>()
            .Where(dwarf => string.Equals(dwarf.ProfessionId, professionId, StringComparison.OrdinalIgnoreCase))
            .Cast<Entity>();
    }

    private IEnumerable<Entity> ResolveDwarvesWithAttribute(EffectBlock effect, EntityRegistry registry)
    {
        if (!effect.Params.TryGetValue("attribute_id", out var attributeId) || string.IsNullOrWhiteSpace(attributeId))
            return Array.Empty<Entity>();

        var minLevel = TryGetInt(effect.Params, "min_level") ?? TryGetInt(effect.Params, "level") ?? 1;
        var maxLevel = TryGetInt(effect.Params, "max_level") ?? TryGetInt(effect.Params, "level") ?? 5;

        return registry.GetAlive<Dwarf>()
            .Where(dwarf =>
            {
                var level = dwarf.Attributes.GetLevel(attributeId);
                return level >= minLevel && level <= maxLevel;
            })
            .Cast<Entity>();
    }

    private IEnumerable<Entity> ResolveDwarvesWithLabor(EffectBlock effect, EntityRegistry registry)
    {
        if (!effect.Params.TryGetValue("labor_id", out var laborId) || string.IsNullOrWhiteSpace(laborId))
            return Array.Empty<Entity>();

        return registry.GetAlive<Dwarf>()
            .Where(dwarf => dwarf.Labors.IsEnabled(laborId))
            .Cast<Entity>();
    }

    private static IEnumerable<Entity> ResolveEntitiesWithDef(EffectBlock effect, EntityRegistry registry)
    {
        if (!effect.Params.TryGetValue("def_id", out var defId) || string.IsNullOrWhiteSpace(defId))
            return Array.Empty<Entity>();

        return registry.GetAll<Entity>()
            .Where(entity => entity.IsAlive && string.Equals(entity.DefId, defId, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<Entity> ResolveEntitiesWithFactionRole(EffectBlock effect, EntityRegistry registry)
    {
        if (!effect.Params.TryGetValue("role_id", out var roleId) || string.IsNullOrWhiteSpace(roleId))
            return Array.Empty<Entity>();

        var dm = _ctx?.TryGet<DataManager>();
        if (dm is null)
            return Array.Empty<Entity>();

        return registry.GetAll<Entity>()
            .Where(entity => entity.IsAlive)
            .Where(entity => ResolveEntityCreatureDef(entity, dm)?.HasFactionRole(roleId) == true);
    }

    private IEnumerable<Entity> WarnAndReturnEmpty(string target)
    {
        _ctx?.Logger?.Warn($"WorldEventManager: unknown target '{target}'");
        return Array.Empty<Entity>();
    }

    private static bool MatchesIntParam(IReadOnlyDictionary<string, string> parameters, string key, int? actualValue)
    {
        if (!parameters.TryGetValue(key, out var rawValue))
            return true;
        if (!int.TryParse(rawValue, out var expectedValue) || actualValue is null)
            return false;

        return actualValue.Value == expectedValue;
    }

    private static bool MatchesIntRange(IReadOnlyDictionary<string, string> parameters, string minKey, string maxKey, int? actualValue)
    {
        if (!parameters.ContainsKey(minKey) && !parameters.ContainsKey(maxKey))
            return true;
        if (actualValue is null)
            return false;

        var value = actualValue.Value;
        if (parameters.TryGetValue(minKey, out var minRaw) && int.TryParse(minRaw, out var minValue) && value < minValue)
            return false;
        if (parameters.TryGetValue(maxKey, out var maxRaw) && int.TryParse(maxRaw, out var maxValue) && value > maxValue)
            return false;

        return !parameters.TryGetValue(minKey, out var minInvalid) || int.TryParse(minInvalid, out _)
            ? !parameters.TryGetValue(maxKey, out var maxInvalid) || int.TryParse(maxInvalid, out _)
            : false;
    }

    private static bool MatchesFloatRange(IReadOnlyDictionary<string, string> parameters, string minKey, string maxKey, float? actualValue)
    {
        if (!parameters.ContainsKey(minKey) && !parameters.ContainsKey(maxKey))
            return true;
        if (actualValue is null)
            return false;

        var value = actualValue.Value;
        if (parameters.TryGetValue(minKey, out var minRaw) && float.TryParse(minRaw, out var minValue) && value < minValue)
            return false;
        if (parameters.TryGetValue(maxKey, out var maxRaw) && float.TryParse(maxRaw, out var maxValue) && value > maxValue)
            return false;

        return !parameters.TryGetValue(minKey, out var minInvalid) || float.TryParse(minInvalid, out _)
            ? !parameters.TryGetValue(maxKey, out var maxInvalid) || float.TryParse(maxInvalid, out _)
            : false;
    }

    private static bool MatchesSeasonParam(IReadOnlyDictionary<string, string> parameters, Season? actualSeason)
    {
        if (!parameters.TryGetValue("season", out var seasonValue))
            return true;
        if (actualSeason is null)
            return false;

        return string.Equals(actualSeason.Value.ToString(), seasonValue, StringComparison.OrdinalIgnoreCase);
    }

    private static int? TryGetInt(IReadOnlyDictionary<string, string> parameters, string key)
    {
        return parameters.TryGetValue(key, out var rawValue) && int.TryParse(rawValue, out var value)
            ? value
            : null;
    }

    private static CreatureDef? ResolveEntityCreatureDef(Entity entity, DataManager dm)
    {
        return entity switch
        {
            Dwarf => dm.Creatures.GetOrNull(DefIds.Dwarf),
            Creature creature => dm.Creatures.GetOrNull(creature.DefId),
            _ => null,
        };
    }

    private readonly record struct WorldEventTriggerContext(
        string TriggerType,
        int? Year = null,
        int? Month = null,
        int? Day = null,
        Season? Season = null);

    private void SpawnMigrants(EffectBlock effect, EntityRegistry registry, WorldMacroStateService? macroState)
    {
        var map   = _ctx!.TryGet<WorldMap>();
        int count = effect.GetInt("count", 3);
        count     = macroState?.ScaleMigrantCount(count) ?? count;
        int mapW  = map?.Width  ?? 48;
        int mapH  = map?.Height ?? 48;

        // Arrive from the northern edge, spread across the width
        for (int i = 0; i < count; i++)
        {
            var name   = MigrantNames[Random.Shared.Next(MigrantNames.Length)];
            var pos    = new Vec3i(Random.Shared.Next(4, mapW - 4), 1, 0);
            var dwarf  = new Dwarf(registry.NextId(), name, pos);
            dwarf.ApplyBaseStats(_ctx!.TryGet<DataManager>()?.Creatures.GetOrNull(DefIds.Dwarf));
            DwarfAttributeGeneration.Randomize(dwarf, _ctx!.TryGet<DataManager>());

            // Give each migrant one random labor plus hauling
            var labor = MigrantLabors[Random.Shared.Next(MigrantLabors.Length)];
            dwarf.Labors.DisableAll();
            dwarf.Labors.Enable(labor);
            dwarf.Labors.Enable(LaborIds.Hauling);
            dwarf.Labors.Enable(LaborIds.Misc);

            registry.Register(dwarf);
        }
    }

    private void SpawnHostiles(EffectBlock effect, EntityRegistry registry, WorldMacroStateService? macroState)
    {
        var map           = _ctx!.TryGet<WorldMap>();
        int count         = effect.GetInt("count", 2);
        count             = macroState?.ScaleRaidCount(count) ?? count;
        var dynamicFallback = _ctx!.TryGet<DataManager>()?.ContentQueries?.ResolveDefaultHostileCreatureDefId();
        string creatureId = effect.Params.TryGetValue("creature", out var c)
            ? c
            : macroState?.GetPrimaryHostileUnitDefId(dynamicFallback ?? WorldEventDefaults.PrimaryHostileUnitDefId)
                ?? dynamicFallback
                ?? WorldEventDefaults.PrimaryHostileUnitDefId;
        if (string.IsNullOrWhiteSpace(creatureId))
            creatureId = macroState?.GetPrimaryHostileUnitDefId(dynamicFallback ?? WorldEventDefaults.PrimaryHostileUnitDefId)
                ?? dynamicFallback
                ?? WorldEventDefaults.PrimaryHostileUnitDefId;
        if (string.IsNullOrWhiteSpace(creatureId))
            return;

        int mapW          = map?.Width  ?? 48;
        int mapH          = map?.Height ?? 48;

        // Arrive from the southern edge
        for (int i = 0; i < count; i++)
        {
            var pos = new Vec3i(Random.Shared.Next(4, mapW - 4), mapH - 2, 0);
            var def = _ctx!.TryGet<DataManager>()?.Creatures.GetOrNull(creatureId);
            var creature = new Creature(
                registry.NextId(),
                creatureId,
                pos,
                def?.MaxHealth ?? 60f,
                isHostile: true);
            creature.ApplyBaseStats(def);
            registry.Register(creature);
        }
    }
}

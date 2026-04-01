using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
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
        ctx.EventBus.On<SeasonChangedEvent>(_ => EvaluateEvents(WorldEventTriggerTypes.SeasonChange));
        ctx.EventBus.On<DayStartedEvent>(_ => EvaluateEvents(WorldEventTriggerTypes.DayStart));
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

    private void EvaluateEvents(string trigger)
    {
        var dm         = _ctx!.TryGet<DataManager>();
        var applicator = _ctx!.TryGet<EffectApplicator>();
        var registry   = _ctx!.Get<EntityRegistry>();
        var lore       = _ctx!.TryGet<WorldLoreSystem>();

        if (dm is null || applicator is null) return;

        foreach (var evDef in dm.WorldEvents.All())
        {
            bool triggered = evDef.Triggers.Any(t => t.Type == trigger);
            if (!triggered) continue;

            if (_lastFired.TryGetValue(evDef.Id, out var last) &&
                _totalTime - last < evDef.Cooldown) continue;

            if (!evDef.Repeatable && _lastFired.ContainsKey(evDef.Id)) continue;

            var probability = lore?.TuneEventProbability(evDef.Id, evDef.Probability) ?? evDef.Probability;
            if (Random.Shared.NextSingle() > probability) continue;

            _lastFired[evDef.Id] = _totalTime;

            foreach (var effect in evDef.Effects)
            {
                if (effect.Op == WorldEventEffectOps.SpawnMigrants)
                    SpawnMigrants(effect, registry, lore);
                else if (effect.Op == WorldEventEffectOps.SpawnHostiles)
                    SpawnHostiles(effect, registry, lore);
                else
                    foreach (var entity in registry.GetAll<Dwarf>())
                        applicator.Apply(effect, entity.Id);
            }

            _ctx.EventBus.Emit(new WorldEventFiredEvent(evDef.Id, evDef.DisplayName));
            _ctx.Logger?.Info($"World event fired: {evDef.Id}");
        }
    }

    private void SpawnMigrants(EffectBlock effect, EntityRegistry registry, WorldLoreSystem? lore)
    {
        var map   = _ctx!.TryGet<WorldMap>();
        int count = effect.GetInt("count", 3);
        count     = lore?.ScaleMigrantCount(count) ?? count;
        int mapW  = map?.Width  ?? 48;
        int mapH  = map?.Height ?? 48;

        // Arrive from the northern edge, spread across the width
        for (int i = 0; i < count; i++)
        {
            var name   = MigrantNames[Random.Shared.Next(MigrantNames.Length)];
            var pos    = new Vec3i(Random.Shared.Next(4, mapW - 4), 1, 0);
            var dwarf  = new Dwarf(registry.NextId(), name, pos);

            // Give each migrant one random labor plus hauling
            var labor = MigrantLabors[Random.Shared.Next(MigrantLabors.Length)];
            dwarf.Labors.DisableAll();
            dwarf.Labors.Enable(labor);
            dwarf.Labors.Enable(LaborIds.Hauling);
            dwarf.Labors.Enable(LaborIds.Misc);

            registry.Register(dwarf);
        }
    }

    private void SpawnHostiles(EffectBlock effect, EntityRegistry registry, WorldLoreSystem? lore)
    {
        var map           = _ctx!.TryGet<WorldMap>();
        int count         = effect.GetInt("count", 2);
        count             = lore?.ScaleRaidCount(count) ?? count;
        string creatureId = effect.Params.TryGetValue("creature", out var c)
            ? c
            : lore?.GetPrimaryHostileUnitDefId(WorldEventDefaults.PrimaryHostileUnitDefId) ?? WorldEventDefaults.PrimaryHostileUnitDefId;
        if (string.IsNullOrWhiteSpace(creatureId))
            creatureId = lore?.GetPrimaryHostileUnitDefId(WorldEventDefaults.PrimaryHostileUnitDefId) ?? WorldEventDefaults.PrimaryHostileUnitDefId;
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

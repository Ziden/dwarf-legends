using System;
using System.IO;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.WorldGen.Story;

namespace DwarfFortress.GameLogic.Systems;

/// <summary>
/// Stores deterministic macro-world lore generated from the standalone
/// worldgen/story module and exposes helpers for event tuning.
/// </summary>
public sealed class WorldLoreSystem : IGameSystem
{
    public string SystemId => SystemIds.WorldLoreSystem;
    public int UpdateOrder => 3;
    public bool IsEnabled { get; set; } = true;

    private const string SaveKey = "world_lore_state";
    private static readonly string[] ConfigCandidates =
    [
        Path.Combine(AppContext.BaseDirectory, "data", "ConfigBundle", "worldgen", "lore.json"),
        Path.Combine("data", "ConfigBundle", "worldgen", "lore.json"),
    ];

    private WorldLoreState? _state;
    private WorldLoreConfig? _config;
    private GameContext? _ctx;

    public WorldLoreState? Current => _state;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;

        ctx.EventBus.On<RecipeCraftedEvent>(_ => ChangeProsperity(+0.001f));
        ctx.EventBus.On<ItemStoredEvent>(_ => ChangeProsperity(+0.0005f));
        ctx.EventBus.On<JobCompletedEvent>(_ => ChangeProsperity(+0.0005f));
        ctx.EventBus.On<EntityDiedEvent>(e =>
        {
            if (e.IsDwarf)
            {
                ChangeProsperity(-0.02f);
                if (e.Cause is "blood_loss" or "wounds")
                    ChangeThreat(+0.02f);
            }
        });
        ctx.EventBus.On<WorldEventFiredEvent>(e =>
        {
            if (e.EventDefId == WorldEventIds.GoblinRaid)
            {
                ChangeProsperity(-0.03f);
                ChangeThreat(+0.025f);
            }
            else if (e.EventDefId == WorldEventIds.MigrantWave)
            {
                ChangeProsperity(+0.01f);
            }
        });
        ctx.EventBus.On<EntityKilledEvent>(e =>
        {
            var registry = ctx.TryGet<EntityRegistry>();
            if (registry is null) return;
            if (registry.TryGetById<Creature>(e.EntityId, out var creature) && creature?.IsHostile == true)
                ChangeThreat(-0.015f);
        });
    }

    public void Tick(float delta)
    {
        // Slow natural threat decay during peaceful periods (~0.005 drop per ~50 real seconds)
        ChangeThreat(-0.0001f * delta);
    }

    private void ChangeProsperity(float delta)
    {
        if (_state is null) return;
        _state.Prosperity = Math.Clamp(_state.Prosperity + delta, 0f, 1f);
    }

    private void ChangeThreat(float delta)
    {
        if (_state is null) return;
        _state.Threat = Math.Clamp(_state.Threat + delta, 0f, 1f);
    }

    public void OnSave(SaveWriter writer)
    {
        if (_state is not null)
            writer.Write(SaveKey, _state);
    }

    public void OnLoad(SaveReader reader)
        => _state = reader.TryRead<WorldLoreState>(SaveKey);

    public void Generate(int seed, int width, int height, int depth)
    {
        _config ??= TryLoadLoreConfig();
        _state = WorldLoreGenerator.Generate(seed, width, height, depth, _config);
    }

    private static WorldLoreConfig? TryLoadLoreConfig()
    {
        foreach (var path in ConfigCandidates)
        {
            if (!File.Exists(path))
                continue;

            try
            {
                return WorldLoreConfigLoader.LoadFromFile(path);
            }
            catch
            {
                // Fall back to embedded defaults when config is missing or invalid.
            }
        }

        return null;
    }

    public string GetPrimaryHostileUnitDefId(string fallback = WorldEventDefaults.PrimaryHostileUnitDefId)
    {
        if (_state is null) return fallback;

        var hostile = _state.Factions
            .Where(f => f.IsHostile)
            .OrderByDescending(f => f.Militarism * (0.35f + f.Influence))
            .FirstOrDefault();

        return hostile?.PrimaryUnitDefId ?? fallback;
    }

    public int ScaleMigrantCount(int baseCount)
    {
        if (_state is null) return Math.Max(1, baseCount);

        var multiplier = 0.8f + (_state.Prosperity * 0.9f) - (_state.Threat * 0.35f);
        var scaled = (int)MathF.Round(baseCount * multiplier);
        return Math.Max(1, scaled);
    }

    public int ScaleRaidCount(int baseCount)
    {
        if (_state is null) return Math.Max(1, baseCount);

        var multiplier = 0.65f + (_state.Threat * 1.1f) - (_state.Prosperity * 0.25f);
        var scaled = (int)MathF.Round(baseCount * multiplier);
        return Math.Max(1, scaled);
    }

    public float TuneEventProbability(string eventId, float baseProbability)
    {
        if (_state is null) return baseProbability;

        var tuned = eventId switch
        {
            WorldEventIds.GoblinRaid => baseProbability + (_state.Threat * 0.35f) - (_state.Prosperity * 0.20f),
            WorldEventIds.MigrantWave => baseProbability + (_state.Prosperity * 0.35f) - (_state.Threat * 0.20f),
            _ => baseProbability,
        };

        return Math.Clamp(tuned, 0.05f, 0.95f);
    }
}

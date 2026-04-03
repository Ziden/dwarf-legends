using System;
using System.IO;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;
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

    public WorldLoreState? Current => BuildCanonicalProjection() ?? _state;

    public void Initialize(GameContext ctx) => _ctx = ctx;

    public void Tick(float delta) { }

    public void OnSave(SaveWriter writer)
    {
        if (_state is not null && BuildCanonicalProjection() is null)
            writer.Write(SaveKey, _state);
    }

    public void OnLoad(SaveReader reader)
        => _state = reader.TryRead<WorldLoreState>(SaveKey);

    public void Generate(int seed, int width, int height, int depth)
    {
        if (BuildCanonicalProjection() is not null)
        {
            _state = null;
            return;
        }

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
        var macroState = _ctx?.TryGet<WorldMacroStateService>();
        if (macroState is not null)
            return macroState.GetPrimaryHostileUnitDefId(fallback);

        if (Current is not { } state) return fallback;

        var hostile = state.Factions
            .Where(f => f.IsHostile)
            .OrderByDescending(f => f.Militarism * (0.35f + f.Influence))
            .FirstOrDefault();

        return hostile?.PrimaryUnitDefId ?? fallback;
    }

    public int ScaleMigrantCount(int baseCount)
    {
        var macroState = _ctx?.TryGet<WorldMacroStateService>();
        if (macroState is not null)
            return macroState.ScaleMigrantCount(baseCount);

        if (Current is not { } state) return Math.Max(1, baseCount);

        var multiplier = 0.8f + (state.Prosperity * 0.9f) - (state.Threat * 0.35f);
        var scaled = (int)MathF.Round(baseCount * multiplier);
        return Math.Max(1, scaled);
    }

    public int ScaleRaidCount(int baseCount)
    {
        var macroState = _ctx?.TryGet<WorldMacroStateService>();
        if (macroState is not null)
            return macroState.ScaleRaidCount(baseCount);

        if (Current is not { } state) return Math.Max(1, baseCount);

        var multiplier = 0.65f + (state.Threat * 1.1f) - (state.Prosperity * 0.25f);
        var scaled = (int)MathF.Round(baseCount * multiplier);
        return Math.Max(1, scaled);
    }

    public float TuneEventProbability(string eventId, float baseProbability)
    {
        var macroState = _ctx?.TryGet<WorldMacroStateService>();
        if (macroState is not null)
            return macroState.TuneEventProbability(eventId, baseProbability);

        if (Current is not { } state) return baseProbability;

        var tuned = eventId switch
        {
            WorldEventIds.GoblinRaid => baseProbability + (state.Threat * 0.35f) - (state.Prosperity * 0.20f),
            WorldEventIds.MigrantWave => baseProbability + (state.Prosperity * 0.35f) - (state.Threat * 0.20f),
            _ => baseProbability,
        };

        return Math.Clamp(tuned, 0.05f, 0.95f);
    }

    private WorldLoreState? BuildCanonicalProjection()
    {
        var historyRuntime = _ctx?.TryGet<WorldHistoryRuntimeService>();
        var macroState = _ctx?.TryGet<WorldMacroStateService>();
        if (historyRuntime?.CurrentSummary is not { } summary ||
            historyRuntime.Snapshot is null ||
            macroState?.Current is not { } macro)
        {
            return null;
        }

        var snapshot = historyRuntime.Snapshot;
        var worldMap = _ctx?.TryGet<WorldMap>();
        return new WorldLoreState
        {
            Seed = snapshot.EmbarkContext.Seed,
            Width = worldMap?.Width ?? 0,
            Height = worldMap?.Height ?? 0,
            Depth = worldMap?.Depth ?? 0,
            RegionName = summary.RegionName,
            BiomeId = summary.BiomeId,
            Threat = macro.Threat,
            Prosperity = macro.Prosperity,
            SimulatedYears = summary.SimulatedYears,
            Factions = snapshot.Civilizations.Select(civilization => new FactionLoreState
            {
                Id = civilization.Id,
                Name = civilization.Name,
                IsHostile = civilization.IsHostile,
                PrimaryUnitDefId = civilization.PrimaryUnitDefId,
                Influence = civilization.Influence,
                Militarism = civilization.Militarism,
                TradeFocus = civilization.TradeFocus,
            }).ToList(),
            Sites = snapshot.Sites.Select(site => new SiteLoreState
            {
                Id = site.Id,
                Name = site.Name,
                Kind = site.Kind,
                OwnerFactionId = site.OwnerCivilizationId,
                X = site.WorldX,
                Y = site.WorldY,
                Z = 0,
                Summary = BuildSiteSummary(site),
                Status = ResolveSiteStatus(site),
                Development = site.Development,
                Security = site.Security,
            }).ToList(),
            History = BuildHistoricalProjection(summary.RecentEvents),
        };
    }

    private static List<HistoricalEventLoreState> BuildHistoricalProjection(string[] recentEvents)
    {
        var projected = new List<HistoricalEventLoreState>(recentEvents.Length);
        foreach (var entry in recentEvents)
        {
            var text = entry;
            var year = 0;
            if (entry.StartsWith("Y", StringComparison.Ordinal))
            {
                var separator = entry.IndexOf(':');
                if (separator > 1 && int.TryParse(entry.Substring(1, separator - 1), out var parsedYear))
                {
                    year = parsedYear;
                    text = entry[(separator + 1)..].TrimStart();
                }
            }

            projected.Add(new HistoricalEventLoreState
            {
                Year = year,
                Type = "historical_projection",
                Summary = text,
            });
        }

        return projected;
    }

    private static string BuildSiteSummary(RuntimeHistorySiteSnapshot site)
        => $"Population {site.Population}, households {site.HouseholdCount}, military {site.MilitaryCount}.";

    private static string ResolveSiteStatus(RuntimeHistorySiteSnapshot site)
    {
        if (site.Population <= 0)
            return SiteStatusIds.Ruined;
        if (site.Security >= 0.72f && site.MilitaryCount > 0)
            return SiteStatusIds.Fortified;
        if (site.Development >= 0.72f)
            return SiteStatusIds.Growing;
        if (site.Security < 0.28f || site.Development < 0.22f)
            return SiteStatusIds.Declining;
        return SiteStatusIds.Stable;
    }
}

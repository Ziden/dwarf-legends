using System;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Jobs;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.WorldGen.Story;

namespace DwarfFortress.GameLogic.World;

public sealed class WorldMacroStateService : IGameSystem
{
    private const string SaveKey = "world_macro_state";
    private const string LegacyLoreSaveKey = "world_lore_state";

    private GameContext? _ctx;

    public string SystemId => SystemIds.WorldMacroStateService;
    public int UpdateOrder => 3;
    public bool IsEnabled { get; set; } = true;

    public WorldMacroStateSnapshot? Current { get; private set; }

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;

        ctx.EventBus.On<RecipeCraftedEvent>(_ => ChangeProsperity(+0.001f));
        ctx.EventBus.On<ItemStoredEvent>(_ => ChangeProsperity(+0.0005f));
        ctx.EventBus.On<JobCompletedEvent>(_ => ChangeProsperity(+0.0005f));
        ctx.EventBus.On<EntityDiedEvent>(e =>
        {
            if (!e.IsDwarf)
                return;

            ChangeProsperity(-0.02f);
            if (e.Cause is "blood_loss" or "wounds")
                ChangeThreat(+0.02f);
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
            if (registry is null)
                return;

            if (registry.TryGetById<Creature>(e.EntityId, out var creature) && creature?.IsHostile == true)
                ChangeThreat(-0.015f);
        });
    }

    public void Tick(float delta)
    {
        ChangeThreat(-0.0001f * delta);
    }

    public void OnSave(SaveWriter writer)
    {
        if (Current is not null)
            writer.Write(SaveKey, Current);
    }

    public void OnLoad(SaveReader reader)
    {
        Current = reader.TryRead<WorldMacroStateSnapshot>(SaveKey);
        if (Current is null)
        {
            var legacyLore = reader.TryRead<WorldLoreState>(LegacyLoreSaveKey);
            if (legacyLore is not null)
                Current = CreateLegacyMigrationState(legacyLore);
        }

        if (Current is null)
            RefreshFromHistory();
        else
            RecalculateDerivedState();
    }

    public void RefreshFromHistory()
    {
        var historyRuntime = _ctx?.TryGet<WorldHistoryRuntimeService>();
        if (historyRuntime?.CurrentSummary is not { } summary || historyRuntime.Snapshot is null)
        {
            Current = null;
            return;
        }

        var snapshot = historyRuntime.Snapshot;
        var ownerCivilization = historyRuntime.GetCivilization(summary.OwnerCivilizationId);
        var primarySite = historyRuntime.GetSite(summary.PrimarySiteId);

        Current = new WorldMacroStateSnapshot
        {
            WorldSeed = snapshot.EmbarkContext.Seed,
            WorldX = snapshot.EmbarkContext.WorldCoord.X,
            WorldY = snapshot.EmbarkContext.WorldCoord.Y,
            RegionX = snapshot.EmbarkContext.RegionCoord.RegionX,
            RegionY = snapshot.EmbarkContext.RegionCoord.RegionY,
            OwnerCivilizationId = summary.OwnerCivilizationId,
            TerritoryOwnerCivilizationId = snapshot.EmbarkContext.LocalHistory?.TerritoryOwnerCivilizationId,
            PrimarySiteId = summary.PrimarySiteId,
            Threat = DeriveThreat(ownerCivilization, primarySite),
            Prosperity = DeriveProsperity(primarySite),
        };

        RecalculateDerivedState();
    }

    public string GetPrimaryHostileUnitDefId(string fallback = WorldEventDefaults.PrimaryHostileUnitDefId)
    {
        var civilizations = _ctx?.TryGet<WorldHistoryRuntimeService>()?.Snapshot?.Civilizations;
        var dynamicFallback = _ctx?.TryGet<DataManager>()?.ContentQueries?.ResolveDefaultHostileCreatureDefId();
        if (civilizations is not { Count: > 0 })
            return dynamicFallback ?? fallback;

        var hostile = civilizations
            .Where(civilization => civilization.IsHostile)
            .OrderByDescending(civilization => civilization.Militarism * (0.35f + civilization.Influence))
            .FirstOrDefault();

        return hostile?.PrimaryUnitDefId ?? dynamicFallback ?? fallback;
    }

    public int ScaleMigrantCount(int baseCount)
    {
        if (Current is null)
            return Math.Max(1, baseCount);

        var multiplier = 0.8f + (Current.MigrationPull * 0.9f) - (Current.FactionPressure * 0.25f);
        var scaled = (int)MathF.Round(baseCount * multiplier);
        return Math.Max(1, scaled);
    }

    public int ScaleRaidCount(int baseCount)
    {
        if (Current is null)
            return Math.Max(1, baseCount);

        var multiplier = 0.65f + (Current.FactionPressure * 1.05f) - (Current.Prosperity * 0.20f);
        var scaled = (int)MathF.Round(baseCount * multiplier);
        return Math.Max(1, scaled);
    }

    public float TuneEventProbability(string eventId, float baseProbability)
    {
        if (Current is null)
            return baseProbability;

        var tuned = eventId switch
        {
            WorldEventIds.GoblinRaid => baseProbability + (Current.FactionPressure * 0.32f) - (Current.Prosperity * 0.18f),
            WorldEventIds.MigrantWave => baseProbability + (Current.MigrationPull * 0.34f) - (Current.FactionPressure * 0.20f),
            _ => baseProbability,
        };

        return Math.Clamp(tuned, 0.05f, 0.95f);
    }

    private WorldMacroStateSnapshot CreateLegacyMigrationState(WorldLoreState legacyLore)
    {
        var historyRuntime = _ctx?.TryGet<WorldHistoryRuntimeService>();
        var snapshot = historyRuntime?.Snapshot;
        var summary = historyRuntime?.CurrentSummary;
        return new WorldMacroStateSnapshot
        {
            WorldSeed = snapshot?.EmbarkContext.Seed ?? legacyLore.Seed,
            WorldX = snapshot?.EmbarkContext.WorldCoord.X ?? 0,
            WorldY = snapshot?.EmbarkContext.WorldCoord.Y ?? 0,
            RegionX = snapshot?.EmbarkContext.RegionCoord.RegionX ?? 0,
            RegionY = snapshot?.EmbarkContext.RegionCoord.RegionY ?? 0,
            OwnerCivilizationId = summary?.OwnerCivilizationId,
            TerritoryOwnerCivilizationId = snapshot?.EmbarkContext.LocalHistory?.TerritoryOwnerCivilizationId,
            PrimarySiteId = summary?.PrimarySiteId,
            Threat = Math.Clamp(legacyLore.Threat, 0f, 1f),
            Prosperity = Math.Clamp(legacyLore.Prosperity, 0f, 1f),
        };
    }

    private void ChangeProsperity(float delta)
    {
        if (Current is null)
            return;

        Current.Prosperity = Math.Clamp(Current.Prosperity + delta, 0f, 1f);
        RecalculateDerivedState();
    }

    private void ChangeThreat(float delta)
    {
        if (Current is null)
            return;

        Current.Threat = Math.Clamp(Current.Threat + delta, 0f, 1f);
        RecalculateDerivedState();
    }

    private void RecalculateDerivedState()
    {
        if (Current is null)
            return;

        var historyRuntime = _ctx?.TryGet<WorldHistoryRuntimeService>();
        var ownerCivilization = historyRuntime?.GetCivilization(Current.OwnerCivilizationId);
        var primarySite = historyRuntime?.GetSite(Current.PrimarySiteId);
        var security = Math.Clamp(primarySite?.Security ?? 0.5f, 0f, 1f);
        var garrisonRatio = primarySite is null || primarySite.Population <= 0
            ? 0f
            : Math.Clamp(primarySite.MilitaryCount / (float)primarySite.Population, 0f, 1f);

        Current.SiteStability = Math.Clamp(
            (security * 0.55f) +
            (Current.Prosperity * 0.30f) -
            (Current.Threat * 0.30f) +
            (garrisonRatio * 0.15f),
            0f,
            1f);

        Current.FactionPressure = Math.Clamp(
            (Current.Threat * 0.70f) +
            ((ownerCivilization?.Militarism ?? 0f) * 0.20f) +
            (ownerCivilization?.IsHostile == true ? 0.15f : 0f) -
            (Current.Prosperity * 0.15f),
            0f,
            1f);

        Current.MigrationPull = Math.Clamp(
            (Current.Prosperity * 0.70f) +
            (Current.SiteStability * 0.35f) -
            (Current.FactionPressure * 0.40f),
            0f,
            1f);
    }

    private static float DeriveThreat(RuntimeHistoryCivilizationSnapshot? ownerCivilization, RuntimeHistorySiteSnapshot? primarySite)
    {
        var baseThreat = ownerCivilization?.IsHostile == true ? 0.65f : 0.2f;
        var militarism = ownerCivilization?.Militarism ?? 0f;
        var insecurity = 1f - Math.Clamp(primarySite?.Security ?? 0.5f, 0f, 1f);
        var garrisonRatio = primarySite is null || primarySite.Population <= 0
            ? 0f
            : Math.Clamp(primarySite.MilitaryCount / (float)primarySite.Population, 0f, 1f);
        return Math.Clamp(baseThreat + (militarism * 0.25f) + (insecurity * 0.2f) - (garrisonRatio * 0.12f), 0f, 1f);
    }

    private static float DeriveProsperity(RuntimeHistorySiteSnapshot? primarySite)
        => Math.Clamp(primarySite?.Development ?? 0.4f, 0f, 1f);
}

public sealed class WorldMacroStateSnapshot
{
    public int WorldSeed { get; set; }
    public int WorldX { get; set; }
    public int WorldY { get; set; }
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public string? OwnerCivilizationId { get; set; }
    public string? TerritoryOwnerCivilizationId { get; set; }
    public string? PrimarySiteId { get; set; }
    public float Threat { get; set; }
    public float Prosperity { get; set; }
    public float FactionPressure { get; set; }
    public float MigrationPull { get; set; }
    public float SiteStability { get; set; }
}

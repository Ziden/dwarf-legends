using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

/// <summary>A coating was picked up from a tile onto a body part.</summary>
public record struct CoatingPickedUpEvent(int EntityId, string BodyPartId, string MaterialId, float Amount);

/// <summary>A substance was ingested (body part licked / item eaten) adding to body chemistry.</summary>
public record struct SubstanceIngestedEvent(int EntityId, string SubstanceId, float Amount);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The physical-contact pipeline: fluids dry into tile coatings, entities walking
/// over coated tiles pick up material on their feet, grooming behaviors ingest those
/// coatings into the bloodstream.
///
/// This is the system that enables the "cat walks through beer puddle, licks paws,
/// gets drunk" class of emergent interactions.
///
/// Order 11 — runs BEFORE FluidSimulator (order 20).
/// Coating pickup is reactive: we listen to PositionChangedEvent rather than
/// depending on execution order relative to fluid spreading.  Coatings applied
/// this tick will interact with fluid that spreads in the same tick.
/// </summary>
public sealed class ContaminationSystem : IGameSystem
{
    public string SystemId    => SystemIds.ContaminationSystem;
    public int    UpdateOrder => 11;
    public bool   IsEnabled   { get; set; } = true;

    // How much coating an entity picks up per step (0–1 scale per 1.0 coating amount).
    private const float PickupFraction  = 0.25f;

    // Minimum coating needed on a tile to transfer to a body part.
    private const float PickupThreshold = 0.05f;

    // Material IDs that have a substance mapping for body chemistry.
    // Format: materialId → (substanceId, amount per unit of coating ingested)
    private static readonly Dictionary<string, (string SubstanceId, float AmountPerUnit)>
        MaterialToSubstance = new(StringComparer.OrdinalIgnoreCase)
        {
            ["beer"]        = (SubstanceIds.Alcohol,   0.30f),
            ["wine"]        = (SubstanceIds.Alcohol,   0.40f),
            ["rum"]         = (SubstanceIds.Alcohol,   0.50f),
            ["whiskey"]     = (SubstanceIds.Alcohol,   0.60f),
            ["lye_water"]   = (SubstanceIds.Poison,    0.20f),
            ["poison"]      = (SubstanceIds.Poison,    0.40f),
            ["magma"]       = (SubstanceIds.MagmaHeat, 1.00f),
            ["mud"]         = (SubstanceIds.Mud,       0.10f),
            ["blood"]       = (SubstanceIds.Blood,     0.05f),
        };

    private GameContext? _ctx;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        ctx.EventBus.On<TileChangedEvent>(OnTileChanged);
    }

    public void Tick(float delta)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        var map      = _ctx!.Get<WorldMap>();

        TickEntityCoatings(registry, map);
        TickBodyChemistryDecay(registry, delta);
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Transfer a coating from a body part into the entity's body chemistry (ingestion).
    /// Called by BehaviorSystem when an entity grooms itself.
    /// </summary>
    public void IngestBodyPartCoating(int entityId, string partId)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        if (!registry.TryGetById<Entity>(entityId, out var entity) || entity is null) return;
        if (!entity.Components.Has<BodyPartComponent>()) return;
        if (!entity.Components.Has<BodyChemistryComponent>()) return;

        var parts = entity.Components.Get<BodyPartComponent>();
        if (!parts.TryGet(partId, out var part) || part is null) return;
        if (part.CoatingMaterialId is null || part.CoatingAmount <= 0f) return;

        var materialId = part.CoatingMaterialId;
        float amount   = part.CoatingAmount;

        // Transfer coating into body chemistry if this material maps to a substance
        if (MaterialToSubstance.TryGetValue(materialId, out var mapping))
        {
            float dose = amount * mapping.AmountPerUnit;
            entity.Components.Get<BodyChemistryComponent>().AddSubstance(mapping.SubstanceId, dose);
            _ctx.EventBus.Emit(new SubstanceIngestedEvent(entityId, mapping.SubstanceId, dose));
        }

        part.ClearCoating();
    }

    // ── Private ────────────────────────────────────────────────────────────

    private void OnTileChanged(TileChangedEvent e)
    {
        var old = e.OldTile;
        var neo = e.NewTile;

        // Fluid dried up: was fluid before, now has none → leave a coating if it had a material
        if (old.FluidLevel > 0 && neo.FluidLevel == 0 && old.FluidMaterialId is not null)
        {
            var map  = _ctx!.Get<WorldMap>();
            var tile = map.GetTile(e.Pos);

            // Only coat if there's no existing stronger coating
            float depositAmount = Math.Clamp(old.FluidLevel / 7f, 0.1f, 1f);
            if (tile.CoatingMaterialId is null || tile.CoatingAmount < depositAmount)
            {
                tile.CoatingMaterialId = old.FluidMaterialId;
                tile.CoatingAmount     = depositAmount;
                map.SetTile(e.Pos, tile);
            }
        }
    }

    private void TickEntityCoatings(EntityRegistry registry, WorldMap map)
    {
        foreach (var entity in registry.GetAlive<Entity>())
        {
            if (!entity.Components.Has<PositionComponent>()) continue;
            if (!entity.Components.Has<BodyPartComponent>()) continue;

            var pos  = entity.Components.Get<PositionComponent>().Position;
            var tile = map.GetTile(pos);

            if (tile.CoatingMaterialId is null || tile.CoatingAmount < PickupThreshold) continue;

            // Pick up coating on the first available foot part
            var parts    = entity.Components.Get<BodyPartComponent>();
            string? foot = FindFootPart(parts);
            if (foot is null) continue;

            float pickup = tile.CoatingAmount * PickupFraction;
            var   part   = parts.GetOrCreate(foot);
            part.CoatingMaterialId = tile.CoatingMaterialId;
            part.CoatingAmount     = Math.Min(1f, (part.CoatingAmount) + pickup);

            // Reduce tile coating
            tile.CoatingAmount -= pickup;
            if (tile.CoatingAmount <= PickupThreshold)
            {
                tile.CoatingMaterialId = null;
                tile.CoatingAmount     = 0f;
            }
            map.SetTile(pos, tile);

            _ctx!.EventBus.Emit(new CoatingPickedUpEvent(entity.Id, foot, part.CoatingMaterialId!, pickup));
        }
    }

    private void TickBodyChemistryDecay(EntityRegistry registry, float delta)
    {
        foreach (var entity in registry.GetAlive<Entity>())
        {
            if (entity.Components.Has<BodyChemistryComponent>())
                entity.Components.Get<BodyChemistryComponent>().DecayAll(delta);
        }
    }

    private static string? FindFootPart(BodyPartComponent parts)
    {
        foreach (var candidate in BodyPartIds.FootLike)
            if (parts.TryGet(candidate, out _)) return candidate;

        // Fall back to feet (auto-created)
        return BodyPartIds.Feet;
    }
}

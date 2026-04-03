using System;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;

namespace DwarfFortress.GameLogic.Systems;

public record struct EntityDiedEvent(int EntityId, string DefId, string DisplayName, string Cause, Vec3i Position, bool IsDwarf);

/// <summary>
/// Handles non-combat mortality, creates corpse items when creatures die, and advances corpse rot.
/// </summary>
public sealed class DeathSystem : IGameSystem
{
    private const float FatalThirstZeroSeconds = 6f * 60f;

    private GameContext? _ctx;

    public string SystemId => SystemIds.DeathSystem;
    public int UpdateOrder => 10;
    public bool IsEnabled { get; set; } = true;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        ctx.EventBus.On<EntityKilledEvent>(OnEntityKilled);
    }

    public void Tick(float delta)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        TickNeedDeaths(registry.GetAlive<Dwarf>().Cast<Entity>().ToArray());
        TickNeedDeaths(registry.GetAlive<Creature>().Cast<Entity>().ToArray());
        TickCorpseRot(delta);
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    private void TickNeedDeaths(System.Collections.Generic.IEnumerable<Entity> entities)
    {
        foreach (var entity in entities)
        {
            var needs = entity.Components.TryGet<NeedsComponent>();
            if (needs is null)
                continue;

            // Check thirst death
            if (needs.Thirst.TimeAtZeroSeconds >= FatalThirstZeroSeconds)
            {
                var health = entity.Components.TryGet<HealthComponent>();
                if (health is not null && health.CurrentHealth > 0f)
                    health.TakeDamage(health.CurrentHealth);

                _ctx!.Get<EntityRegistry>().Kill(entity.Id, "dehydration");
                continue;
            }

            // Check hunger death
            if (needs.Hunger.TimeAtZeroSeconds >= FatalThirstZeroSeconds)
            {
                var health = entity.Components.TryGet<HealthComponent>();
                if (health is not null && health.CurrentHealth > 0f)
                    health.TakeDamage(health.CurrentHealth);

                _ctx!.Get<EntityRegistry>().Kill(entity.Id, "starvation");
            }
        }
    }

    private void TickCorpseRot(float delta)
    {
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        if (itemSystem is null)
            return;

        foreach (var item in itemSystem.GetAllItems())
            item.Components.TryGet<RotComponent>()?.Tick(delta);
    }

    private void OnEntityKilled(EntityKilledEvent e)
    {
        if (_ctx is null)
            return;

        var registry = _ctx.Get<EntityRegistry>();
        var itemSystem = _ctx.TryGet<ItemSystem>();
        var entity = registry.TryGetById(e.EntityId);
        if (entity is not Dwarf and not Creature)
            return;

        if (entity?.Components.TryGet<PositionComponent>() is not { } position || itemSystem is null)
            return;

        var displayName = ResolveDisplayName(entity);
        var corpse = itemSystem.CreateItem(ItemDefIds.Corpse, string.Empty, position.Position);
        corpse.Components.Add(new CorpseComponent(entity.Id, entity.DefId, displayName, e.Cause));
        corpse.Components.Add(new RotComponent());

        foreach (var carriedItem in itemSystem.GetItemsCarriedBy(entity.Id).ToArray())
            itemSystem.StoreItemInItem(carriedItem.Id, corpse.Id, position.Position);

        if (entity is Creature creature)
            EmitCreatureDeathDrops(creature, itemSystem, corpse, position.Position);

        if (entity is Dwarf)
            _ctx.EventBus.Emit(new DwarfDiedEvent(entity.Id, e.Cause));

        _ctx.EventBus.Emit(new EntityDiedEvent(entity.Id, entity.DefId, displayName, e.Cause, position.Position, entity is Dwarf));
    }

    private string ResolveDisplayName(Entity entity)
    {
        if (entity is Dwarf dwarf)
            return dwarf.FirstName;

        if (entity is Creature creature)
        {
            var dm = _ctx?.TryGet<DataManager>();
            return dm?.Creatures.GetOrNull(creature.DefId)?.DisplayName ?? Humanize(creature.DefId);
        }

        return Humanize(entity.DefId);
    }

    private void EmitCreatureDeathDrops(Creature creature, ItemSystem itemSystem, Item corpse, Vec3i position)
    {
        var def = _ctx?.TryGet<DataManager>()?.Creatures.GetOrNull(creature.DefId);
        if (def?.DeathDrops is not { Count: > 0 })
            return;

        foreach (var drop in def.DeathDrops)
        {
            for (var i = 0; i < drop.Quantity; i++)
            {
                var dropItem = itemSystem.CreateItem(drop.ItemDefId, drop.MaterialId ?? string.Empty, position);
                itemSystem.StoreItemInItem(dropItem.Id, corpse.Id, position);
            }
        }
    }

    private static string Humanize(string value)
        => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Replace('_', ' ');

    private static Need? GetThirstNeed(Entity entity)
        => entity.Components.TryGet<NeedsComponent>()?.Thirst;
}

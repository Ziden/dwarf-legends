using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Entities;

/// <summary>
/// A non-player creature (animal, monster, enemy).
/// Has position, stats, health, and enough anatomy/chemistry to participate
/// in contamination and other emergent systems.
/// </summary>
public sealed class Creature : Entity
{
    public PositionComponent      Position      => Components.Get<PositionComponent>();
    public StatComponent          Stats         => Components.Get<StatComponent>();
    public InventoryComponent     Inventory     => Components.Get<InventoryComponent>();
    public NeedsComponent         Needs         => Components.Get<NeedsComponent>();
    public HealthComponent        Health        => Components.Get<HealthComponent>();
    public BodyPartComponent      BodyParts     => Components.Get<BodyPartComponent>();
    public BodyChemistryComponent BodyChemistry => Components.Get<BodyChemistryComponent>();
    public EmoteComponent         Emotes        => Components.Get<EmoteComponent>();

    public bool IsHostile { get; set; }

    public Creature(int id, string defId, Vec3i spawnPos, float maxHealth, bool isHostile = false)
        : base(id, defId)
    {
        IsHostile = isHostile;
        Components.Add(new PositionComponent(spawnPos));
        Components.Add(new StatComponent());
        Components.Add(new InventoryComponent());
        Components.Add(new NeedsComponent());
        Components.Add(new HealthComponent(maxHealth));

        var bodyParts = new BodyPartComponent();
        bodyParts.Initialize(BodyPartIds.Quadruped);
        Components.Add(bodyParts);
        Components.Add(new BodyChemistryComponent());
        Components.Add(new EmoteComponent());
    }

    public void ApplyBaseStats(CreatureDef? def)
    {
        if (def is null)
            return;

        Stats.Speed.BaseValue = def.BaseSpeed;
        Stats.Strength.BaseValue = def.BaseStrength;
        Stats.Toughness.BaseValue = def.BaseToughness;
        IsHostile = IsHostile || def.IsHostile();
    }
}

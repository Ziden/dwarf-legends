using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Entities;

public static class ProfessionIds
{
    public const string Peasant = "peasant";
}

/// <summary>
/// A playable dwarf in the fortress.
/// Pre-wired with all sapient components during construction.
/// </summary>
public sealed class Dwarf : Entity
{
    public string FirstName   { get; }
    public string NickName    { get; set; }
    public string ProfessionId{ get; set; }

    // ── Quick-access component properties ─────────────────────────────────
    public PositionComponent Position  => Components.Get<PositionComponent>();
    public StatComponent     Stats     => Components.Get<StatComponent>();
    public SkillComponent    Skills    => Components.Get<SkillComponent>();
    public NeedsComponent    Needs     => Components.Get<NeedsComponent>();
    public MoodComponent     Mood      => Components.Get<MoodComponent>();
    public ThoughtComponent  Thoughts  => Components.Get<ThoughtComponent>();
    public LaborComponent    Labors    => Components.Get<LaborComponent>();
    public InventoryComponent Inventory => Components.Get<InventoryComponent>();
    public HealthComponent        Health       => Components.Get<HealthComponent>();
    public BodyPartComponent      BodyParts    => Components.Get<BodyPartComponent>();
    public BodyChemistryComponent BodyChemistry => Components.Get<BodyChemistryComponent>();
    public DwarfAppearanceComponent Appearance => Components.Get<DwarfAppearanceComponent>();
    public NutritionComponent     Nutrition    => Components.Get<NutritionComponent>();
    public PreferenceComponent   Preferences  => Components.Get<PreferenceComponent>();
    public TraitComponent        Traits       => Components.Get<TraitComponent>();
    public EmoteComponent        Emotes       => Components.Get<EmoteComponent>();
    public BodyFatComponent      BodyFat      => Components.Get<BodyFatComponent>();

    public Dwarf(int id, string firstName, Vec3i spawnPos, float maxHealth = 100f)
        : base(id, DefIds.Dwarf)
    {
        FirstName    = firstName;
        NickName     = firstName;
        ProfessionId = ProfessionIds.Peasant;

        // Wire all sapient components
        Components.Add(new PositionComponent(spawnPos));
        Components.Add(new StatComponent());
        Components.Add(new SkillComponent());
        Components.Add(new NeedsComponent());
        Components.Add(new MoodComponent());
        Components.Add(new ThoughtComponent());
        Components.Add(new LaborComponent());
        Components.Add(new InventoryComponent());
        Components.Add(new HealthComponent(maxHealth));
        Components.Add(DwarfAppearanceComponent.CreateDefault(id, firstName, spawnPos));

        // Emergent interaction components
        var bodyParts = new BodyPartComponent();
        bodyParts.Initialize(BodyPartIds.Humanoid);
        Components.Add(bodyParts);
        Components.Add(new BodyChemistryComponent());
        Components.Add(new NutritionComponent());
        Components.Add(new PreferenceComponent());
        Components.Add(new StatusEffectComponent());
        Components.Add(new TraitComponent());
        Components.Add(new EmoteComponent());
        Components.Add(new BodyFatComponent());

        // All labors enabled by default (mirrors base Dwarf Fortress behaviour)
        Components.Get<LaborComponent>().EnableAll(LaborIds.All);
    }
}

/// <summary>Entity def ID string constants. No magic strings in simulation code.</summary>
public static class DefIds
{
    public const string Dwarf   = "dwarf";
    public const string Cat     = "cat";
    public const string Dog     = "dog";
    public const string Goblin  = "goblin";
    public const string Troll   = "troll";
    public const string Elk     = "elk";
    public const string GiantCarp = "giant_carp";
}

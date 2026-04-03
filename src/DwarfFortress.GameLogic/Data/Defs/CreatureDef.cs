using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Data;

namespace DwarfFortress.GameLogic.Data.Defs;

/// <summary>A body part definition for anatomy (used in combat targeting).</summary>
public sealed record BodyPartDef(string Id, string DisplayName, float HitWeight, bool IsVital);

/// <summary>World/story role metadata that can also drive runtime selectors.</summary>
public sealed record CreatureFactionRoleDef(string Id, float Weight = 1.0f);

/// <summary>Data-driven item drops emitted immediately when a creature dies.</summary>
public sealed record CreatureDeathDropDef(string ItemDefId, int Quantity = 1, string? MaterialId = null);

/// <summary>
/// Immutable definition of a creature type (dwarf, goblin, troll, elk, etc.).
/// Loaded from the shared content catalog.
/// </summary>
public sealed record CreatureDef(
    string                      Id,
    string                      DisplayName,
    TagSet                      Tags,
    float                       BaseSpeed     = 1.0f,
    float                       BaseStrength  = 10.0f,
    float                       BaseToughness = 10.0f,
    float                       MaxHealth     = 100f,
    bool                        IsPlayable    = false,  // true = can be a fortress dwarf
    bool                        IsSapient     = false,  // true = has moods/thoughts
    bool                        AuthoredIsHostile = false,
    bool?                       AuthoredCanGroom = null,
    CreatureDiet?               AuthoredDiet  = null,
    CreatureMovementMode?       AuthoredMovementMode = null,
    IReadOnlyList<BodyPartDef>? BodyParts     = null,
    IReadOnlyList<string>?      NaturalLabors = null,   // labors enabled by default
    IReadOnlyList<CreatureDeathDropDef>? DeathDrops = null,
    IReadOnlyList<CreatureFactionRoleDef>? FactionRoles = null);

/// <summary>Preferred feeding model for creature autonomous hunger behavior.</summary>
public enum CreatureDiet
{
    Herbivore,
    Carnivore,
    Omnivore,
    AquaticGrazer,
}

/// <summary>Traversal model for autonomous movement and spawn validation.</summary>
public enum CreatureMovementMode
{
    Land,
    Swimmer,
    Aquatic,
}

public static class CreatureDefTagExtensions
{
    public static bool IsAquatic(this CreatureDef def)
        => def.ResolveTraversal().RequiresSwimming;

    public static bool CanSwim(this CreatureDef def)
        => def.ResolveTraversal().CanSwim;

    public static (bool CanSwim, bool RequiresSwimming) ResolveTraversal(this CreatureDef def)
    {
        if (def.AuthoredMovementMode is CreatureMovementMode authoredMode)
        {
            return authoredMode switch
            {
                CreatureMovementMode.Aquatic => (true, true),
                CreatureMovementMode.Swimmer => (true, false),
                _ => (false, false),
            };
        }

        var isAquatic = def.Tags.HasAny(TagIds.Aquatic, TagIds.Fish);
        var isSwimmer = isAquatic || def.Tags.HasAny(TagIds.Swimmer, TagIds.Amphibious);
        return (isSwimmer, isAquatic);
    }

    public static bool IsGroomer(this CreatureDef def)
        => def.AuthoredCanGroom ?? def.Tags.Contains(TagIds.Groomer);

    public static bool IsHostile(this CreatureDef def)
        => def.AuthoredIsHostile || def.Tags.Contains(TagIds.Hostile);

    public static bool HasFactionRole(this CreatureDef def, string? roleId)
    {
        if (string.IsNullOrWhiteSpace(roleId))
            return false;

        return (def.FactionRoles ?? [])
            .Any(role => string.Equals(role.Id, roleId, System.StringComparison.OrdinalIgnoreCase));
    }

    public static CreatureDiet ResolveDiet(this CreatureDef def)
    {
        if (def.AuthoredDiet is CreatureDiet authoredDiet)
            return authoredDiet;

        if (def.Tags.Contains(TagIds.Herbivore))
            return CreatureDiet.Herbivore;
        if (def.Tags.Contains(TagIds.Carnivore))
            return CreatureDiet.Carnivore;
        if (def.Tags.Contains(TagIds.Omnivore))
            return CreatureDiet.Omnivore;
        if (def.Tags.Contains(TagIds.AquaticGrazer))
            return CreatureDiet.AquaticGrazer;

        if (def.IsAquatic())
            return CreatureDiet.AquaticGrazer;

        if (def.Tags.Contains(TagIds.Grazer))
            return CreatureDiet.Herbivore;

        if (def.Tags.HasAny(TagIds.Hostile, TagIds.Large))
            return CreatureDiet.Carnivore;

        return CreatureDiet.Omnivore;
    }
}

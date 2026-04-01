using System.Collections.Generic;
using DwarfFortress.GameLogic.Data;

namespace DwarfFortress.GameLogic.Data.Defs;

/// <summary>A body part definition for anatomy (used in combat targeting).</summary>
public sealed record BodyPartDef(string Id, string DisplayName, float HitWeight, bool IsVital);

/// <summary>
/// Immutable definition of a creature type (dwarf, goblin, troll, elk, etc.).
/// Loaded from creatures.json.
/// </summary>
public sealed record CreatureDef(
    string                      Id,
    string                      DisplayName,
    TagSet                      Tags,
    float                       BaseSpeed     = 1.0f,
    float                       BaseStrength  = 1.0f,
    float                       BaseToughness = 1.0f,
    float                       MaxHealth     = 100f,
    bool                        IsPlayable    = false,  // true = can be a fortress dwarf
    bool                        IsSapient     = false,  // true = has moods/thoughts
    IReadOnlyList<BodyPartDef>? BodyParts     = null,
    IReadOnlyList<string>?      NaturalLabors = null);  // labors enabled by default

/// <summary>Preferred feeding model for creature autonomous hunger behavior.</summary>
public enum CreatureDiet
{
    Herbivore,
    Carnivore,
    Omnivore,
    AquaticGrazer,
}

public static class CreatureDefTagExtensions
{
    private static readonly string[] AquaticIdMarkers =
    [
        "fish",
        "carp",
        "eel",
        "trout",
        "salmon",
        "shark",
        "ray",
    ];

    public static bool IsAquatic(this CreatureDef def)
        => def.Tags.HasAny(TagIds.Aquatic, TagIds.Fish);

    public static bool CanSwim(this CreatureDef def)
        => def.IsAquatic() || def.Tags.HasAny(TagIds.Swimmer, TagIds.Amphibious);

    public static bool IsGroomer(this CreatureDef def)
        => def.Tags.Contains(TagIds.Groomer);

    public static bool IsHostile(this CreatureDef def)
        => def.Tags.Contains(TagIds.Hostile);

    public static bool IsLikelyAquaticId(string? creatureDefId)
    {
        if (string.IsNullOrWhiteSpace(creatureDefId))
            return false;

        foreach (var marker in AquaticIdMarkers)
        {
            if (creatureDefId.Contains(marker, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static CreatureDiet ResolveDiet(this CreatureDef def)
    {
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

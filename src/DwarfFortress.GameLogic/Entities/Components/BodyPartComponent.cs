using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Entities.Components;

public static class BodyPartIds
{
    public const string Head = "head";
    public const string Torso = "torso";
    public const string LeftArm = "left_arm";
    public const string RightArm = "right_arm";
    public const string LeftLeg = "left_leg";
    public const string RightLeg = "right_leg";
    public const string Feet = "feet";
    public const string Paws = "paws";
    public const string Hooves = "hooves";
    public const string Foot = "foot";

    public static readonly IReadOnlyList<string> Humanoid =
    [
        Head,
        Torso,
        LeftArm,
        RightArm,
        LeftLeg,
        RightLeg,
        Feet,
    ];

    public static readonly IReadOnlyList<string> Quadruped =
    [
        Head,
        Torso,
        Paws,
        Feet,
    ];

    public static readonly IReadOnlyList<string> CombatTargets =
    [
        Head,
        Torso,
        LeftArm,
        RightArm,
        LeftLeg,
        RightLeg,
    ];

    public static readonly IReadOnlyList<string> FootLike =
    [
        Feet,
        Paws,
        Hooves,
        Foot,
    ];
}

/// <summary>Runtime state of a single named body part (e.g. "head", "left_arm", "paws").</summary>
public sealed class BodyPartState
{
    /// <summary>Def ID matching CreatureDef.BodyParts[n].Id (e.g. "paws", "head").</summary>
    public string PartId { get; }

    /// <summary>Material ID of anything currently coating this body part (e.g. "beer", "mud", "blood").</summary>
    public string? CoatingMaterialId { get; set; }

    /// <summary>Amount of coating material (0 = none; 1 = fully coated).</summary>
    public float CoatingAmount { get; set; }

    public BodyPartState(string partId)
    {
        PartId = partId;
    }

    public void ClearCoating()
    {
        CoatingMaterialId = null;
        CoatingAmount     = 0f;
    }
}

/// <summary>
/// Tracks runtime state of all named body parts for an entity.
/// Body parts come from <see cref="DwarfFortress.GameLogic.Data.Defs.CreatureDef.BodyParts"/>;
/// this component holds their mutable runtime state in parallel.
///
/// For dwarves, default parts are seeded in the Dwarf constructor.
/// Creatures get their parts seeded when spawned by EntityRegistry.
/// </summary>
public sealed class BodyPartComponent
{
    private readonly Dictionary<string, BodyPartState> _parts = new();

    /// <summary>Seed body parts from a list of part IDs (typically from CreatureDef).</summary>
    public void Initialize(IEnumerable<string> partIds)
    {
        foreach (var id in partIds)
            _parts[id] = new BodyPartState(id);
    }

    /// <summary>Get or create a body part state by ID.</summary>
    public BodyPartState GetOrCreate(string partId)
    {
        if (!_parts.TryGetValue(partId, out var state))
        {
            state = new BodyPartState(partId);
            _parts[partId] = state;
        }
        return state;
    }

    public bool TryGet(string partId, out BodyPartState? state)
        => _parts.TryGetValue(partId, out state);

    public IEnumerable<BodyPartState> All => _parts.Values;

    public bool HasCoating(string partId)
        => _parts.TryGetValue(partId, out var s) && s.CoatingMaterialId is not null;
}

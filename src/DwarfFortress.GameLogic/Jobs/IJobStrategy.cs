using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.Jobs;

/// <summary>
/// Provides the execution behaviour for a specific job type.
/// Each IJobStrategy maps 1-to-1 with a JobDef ID.
/// </summary>
public interface IJobStrategy
{
    /// <summary>The JobDef ID this strategy handles.</summary>
    string JobDefId { get; }

    /// <summary>
    /// Check whether this job can currently be executed by the given dwarf.
    /// The job may be unexecutable if resources are missing or the target is invalid.
    /// </summary>
    bool CanExecute(Job job, int dwarfId, GameContext ctx);

    /// <summary>
    /// Return the ordered list of action steps the dwarf must perform.
    /// Called once when the job is assigned.
    /// </summary>
    IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx);

    /// <summary>
    /// Called when a job is interrupted (dwarf dies, tile becomes inaccessible, etc.).
    /// Use to release reserved items or undo partial work.
    /// </summary>
    void OnInterrupt(Job job, int dwarfId, GameContext ctx);

    /// <summary>
    /// Called when the job completes successfully.
    /// Spawns items, modifies tiles, awards XP, etc.
    /// </summary>
    void OnComplete(Job job, int dwarfId, GameContext ctx);
}

/// <summary>String constants for job def IDs.</summary>
public static class JobDefIds
{
    public const string EngageHostile = "engage_hostile";
    public const string MineTile   = "mine_tile";
    public const string CutTree    = "cut_tree";
    public const string HarvestPlant = "harvest_plant";
    public const string HaulItem   = "haul_item";
    public const string PlaceBox   = "place_box";
    public const string Construct  = "construct_building";
    public const string Craft      = "craft_item";
    public const string Eat        = "eat";
    public const string Drink      = "drink";
    public const string Sleep      = "sleep";
    public const string Idle       = "idle";
    public const string Train      = "train";
    public const string Patrol     = "patrol";
}

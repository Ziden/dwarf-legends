using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;

namespace DwarfFortress.GameLogic.Jobs;

// ── ActionStep union ────────────────────────────────────────────────────────

/// <summary>Base class for a single atomic step a dwarf performs during a job.</summary>
public abstract record ActionStep;

/// <summary>Move to a world position (or as close as possible).</summary>
public sealed record MoveToStep(Vec3i Target) : ActionStep;

/// <summary>Work at the current position for a fixed time.</summary>
public sealed record WorkAtStep(float Duration, string AnimationHint = "", Vec3i? RequiredPosition = null) : ActionStep;

/// <summary>Pick up an item by entity ID.</summary>
public sealed record PickUpItemStep(int ItemEntityId, ItemCarryMode CarryMode = ItemCarryMode.Inventory) : ActionStep;

/// <summary>Place a carried item at a target position.</summary>
public sealed record PlaceItemStep(int ItemEntityId, Vec3i Target, int ContainerBuildingId = -1) : ActionStep;

/// <summary>Reserved stockpile slot for a specific item during a job.</summary>
public readonly record struct ReservedStockpilePlacement(int ItemEntityId, int StockpileId, Vec3i Slot);

/// <summary>Wait for a fixed time (e.g. eating, drinking, sleeping).</summary>
public sealed record WaitStep(float Duration) : ActionStep;

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A job instance. Immutable definition referenced via DefId; mutable runtime state here.
/// </summary>
public sealed class Job
{
    public int        Id              { get; }
    public string     JobDefId        { get; }
    public Vec3i      TargetPos       { get; }
    /// <summary>ID of the entity this job is associated with (e.g. workshop building ID for craft jobs). -1 = none.</summary>
    public int        EntityId        { get; }
    public JobStatus  Status          { get; set; } = JobStatus.Pending;
    public int        AssignedDwarfId { get; set; } = -1;   // -1 = unassigned
    public float      WorkProgress    { get; set; }          // 0 to WorkTime
    public int        Priority        { get; }

    /// <summary>Optional item entity IDs reserved by this job (inputs for crafting, etc.).</summary>
    public List<int>  ReservedItemIds { get; } = new();
    public int        ReservedStockpileId { get; set; } = -1;
    public Vec3i?     ReservedSlot        { get; set; }
    public List<ReservedStockpilePlacement> ReservedStockpilePlacements { get; } = new();

    public Job(int id, string jobDefId, Vec3i targetPos, int priority = 0, int entityId = -1)
    {
        Id         = id;
        JobDefId   = jobDefId;
        TargetPos  = targetPos;
        Priority   = priority;
        EntityId   = entityId;
    }

    public bool IsAssigned  => AssignedDwarfId >= 0;
    public bool IsComplete  => Status == JobStatus.Complete;
    public bool IsCancelled => Status == JobStatus.Cancelled;

    public override string ToString() => $"Job[{Id}] {JobDefId} @ {TargetPos} ({Status})";
}

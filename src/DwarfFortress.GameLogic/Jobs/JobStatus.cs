namespace DwarfFortress.GameLogic.Jobs;

/// <summary>The lifecycle state of a Job.</summary>
public enum JobStatus
{
    /// <summary>Waiting to be assigned to a dwarf.</summary>
    Pending,

    /// <summary>Assigned to a dwarf, dwarf is pathing or working.</summary>
    InProgress,

    /// <summary>Completed successfully.</summary>
    Complete,

    /// <summary>Failed (target unreachable, resources gone, etc.).</summary>
    Failed,

    /// <summary>Cancelled by the player or a system.</summary>
    Cancelled,
}

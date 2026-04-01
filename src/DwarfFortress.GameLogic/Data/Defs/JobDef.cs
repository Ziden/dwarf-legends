using System.Collections.Generic;
using DwarfFortress.GameLogic.Data;

namespace DwarfFortress.GameLogic.Data.Defs;

/// <summary>
/// Immutable definition of a job type.
/// Concrete behaviour is provided by a matching IJobStrategy implementation.
/// Loaded from jobs.json.
/// </summary>
public sealed record JobDef(
    string  Id,
    string  DisplayName,
    string  RequiredLaborId,   // dwarf must have this labor enabled
    float   WorkTime,          // base ticks to complete
    int     Priority,          // higher = picked first
    TagSet  Tags);

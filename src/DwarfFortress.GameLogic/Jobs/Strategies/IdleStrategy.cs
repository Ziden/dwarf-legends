using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

/// <summary>
/// Dwarf does nothing for a short duration (fallback when no real job exists).
/// </summary>
public sealed class IdleStrategy : IJobStrategy
{
    public string JobDefId => JobDefIds.Idle;

    public bool CanExecute(Job job, int dwarfId, GameContext ctx) => true;

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        return new ActionStep[] { new WaitStep(Duration: 2f) };
    }

    public void OnInterrupt(Job job, int dwarfId, GameContext ctx) { }
    public void OnComplete(Job job, int dwarfId, GameContext ctx) { }
}

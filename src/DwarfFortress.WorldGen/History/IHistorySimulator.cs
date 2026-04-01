using DwarfFortress.WorldGen.Story;
using DwarfFortress.WorldGen.World;

namespace DwarfFortress.WorldGen.History;

public interface IHistorySimulator
{
    GeneratedWorldHistory Simulate(
        GeneratedWorldMap world,
        int seed,
        WorldLoreConfig? config = null,
        int? simulatedYearsOverride = null);

    GeneratedWorldHistoryTimeline SimulateTimeline(
        GeneratedWorldMap world,
        int seed,
        WorldLoreConfig? config = null,
        int? simulatedYearsOverride = null);
}

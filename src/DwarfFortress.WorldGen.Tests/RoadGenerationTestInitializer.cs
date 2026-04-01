using System.Runtime.CompilerServices;
using DwarfFortress.WorldGen.Generation;

namespace DwarfFortress.WorldGen.Tests;

internal static class RoadGenerationTestInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        WorldGenFeatureFlags.EnableRoadGeneration = true;
    }
}

namespace DwarfFortress.WorldGen.Generation;

/// <summary>
/// Global feature switches for world generation behavior.
/// Roads are disabled by default while we focus on validating natural generation layers.
/// </summary>
public static class WorldGenFeatureFlags
{
    public static bool EnableRoadGeneration { get; set; } = false;
}

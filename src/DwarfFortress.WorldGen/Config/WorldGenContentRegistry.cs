using System;
using DwarfFortress.WorldGen.Content;

namespace DwarfFortress.WorldGen.Config;

public static class WorldGenContentRegistry
{
    private static readonly Lazy<WorldGenContentCatalog> Catalog = new(CreateCatalog);

    public static WorldGenContentCatalog Current => Catalog.Value;

    private static WorldGenContentCatalog CreateCatalog()
    {
        var config = WorldGenContentConfigLoader.LoadDefaultOrFallback();
        var sharedContent = SharedContentCatalogLoader.LoadDefaultOrFallback();
        return WorldGenContentCatalog.FromConfig(config, sharedContent);
    }
}

using System;

namespace DwarfFortress.WorldGen.Config;

public static class WorldGenPlantRegistry
{
    private static readonly Lazy<WorldGenPlantCatalog> Catalog = new(WorldGenPlantCatalogLoader.LoadDefaultOrFallback);

    public static WorldGenPlantCatalog Current => Catalog.Value;
}
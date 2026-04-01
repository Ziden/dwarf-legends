using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Maps;
using DwarfFortress.WorldGen.Regions;

namespace DwarfFortress.WorldGen.Tests;

public sealed class RegionSurfaceResolverTests
{
    [Fact]
    public void ResolvePreferredSurfaceTileDefId_UsesCanonicalFallbackForUnknownSurfaceClass()
    {
        var tile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperateWoodland,
            SurfaceClassId: "legacy_surface",
            Slope: 24,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.70f,
            ResourceRichness: 0.45f,
            SoilDepth: 0.68f,
            Groundwater: 0.80f,
            HasRoad: false,
            HasSettlement: false,
            TemperatureBand: 0.58f,
            MoistureBand: 0.76f);

        var tileDefId = RegionSurfaceResolver.ResolvePreferredSurfaceTileDefId(tile, MacroBiomeIds.TemperatePlains);

        Assert.Equal(GeneratedTileDefIds.Mud, tileDefId);
    }

    [Fact]
    public void ResolvePreferredSurfaceTileDefId_MapsKnownSurfaceClassesDirectly()
    {
        var tile = new GeneratedRegionTile(
            BiomeVariantId: RegionBiomeVariantIds.TemperateWoodland,
            SurfaceClassId: RegionSurfaceClassIds.Stone,
            Slope: 12,
            HasRiver: false,
            HasLake: false,
            VegetationDensity: 0.62f,
            ResourceRichness: 0.30f,
            SoilDepth: 0.70f,
            Groundwater: 0.72f,
            HasRoad: false,
            HasSettlement: false,
            TemperatureBand: 0.54f,
            MoistureBand: 0.66f);

        var tileDefId = RegionSurfaceResolver.ResolvePreferredSurfaceTileDefId(tile, MacroBiomeIds.TemperatePlains);

        Assert.Equal(GeneratedTileDefIds.StoneFloor, tileDefId);
    }

    [Fact]
    public void ResolveAnchorSurfaceClassId_FallsBackToBiomeDrivenAnchor()
    {
        var surfaceClassId = RegionSurfaceResolver.ResolveAnchorSurfaceClassId(
            surfaceClassId: null,
            biomeVariantId: RegionBiomeVariantIds.DrySteppe,
            parentMacroBiomeId: MacroBiomeIds.WindsweptSteppe);

        Assert.Equal(RegionSurfaceClassIds.Sand, surfaceClassId);
    }
}
using DwarfFortress.ContentEditor.Services;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Maps;

namespace DwarfFortress.ContentEditor.Tests;

public sealed class MapPreviewServiceTests
{
    private readonly MapPreviewService _svc = new();

    [Fact]
    public void Generate_WithLoreBiome_UsesKnownBiomeAndReturnsMap()
    {
        var run = _svc.Generate(new MapPreviewRequest(
            Seed: 17,
            LocalWidth: 48,
            LocalHeight: 48,
            LocalDepth: 8,
            WorldWidth: 24,
            WorldHeight: 24,
            RegionWidth: 16,
            RegionHeight: 16,
            WorldX: 12,
            WorldY: 12,
            RegionX: 8,
            RegionY: 8,
            BiomeId: null,
            UseLoreBiome: true));

        Assert.True(run.UsedLoreBiome);
        Assert.False(string.IsNullOrWhiteSpace(run.ResolvedBiomeId));
        Assert.Contains(run.ResolvedBiomeId!, _svc.ListBiomes());
        Assert.Equal(48, run.LocalMap.Width);
        Assert.Equal(48, run.LocalMap.Height);
        Assert.Equal(8, run.LocalMap.Depth);
    }

    [Fact]
    public void Generate_WithExplicitBiome_UsesRequestedBiome()
    {
        var run = _svc.Generate(new MapPreviewRequest(
            Seed: 17,
            LocalWidth: 48,
            LocalHeight: 48,
            LocalDepth: 8,
            BiomeId: MacroBiomeIds.Highland,
            UseLoreBiome: false));

        Assert.False(run.UsedLoreBiome);
        Assert.Equal(MacroBiomeIds.Highland, run.ResolvedBiomeId);
    }

    [Fact]
    public void AnalyzeLayer_ReportsTreeCounts_ForForestSurface()
    {
        var run = _svc.Generate(new MapPreviewRequest(
            Seed: 55,
            LocalWidth: 48,
            LocalHeight: 48,
            LocalDepth: 8,
            BiomeId: MacroBiomeIds.ConiferForest,
            UseLoreBiome: false));

        var layer = _svc.AnalyzeLayer(run.LocalMap, z: 0);

        Assert.True(layer.TreeTiles > 0);
        Assert.Contains(layer.TileBreakdown, x => x.TileDefId == GeneratedTileDefIds.Tree && x.Count == layer.TreeTiles);
    }

    [Fact]
    public void AnalyzeLayer_Throws_WhenLayerIsOutOfRange()
    {
        var run = _svc.Generate(new MapPreviewRequest(
            Seed: 5,
            LocalWidth: 32,
            LocalHeight: 32,
            LocalDepth: 6,
            BiomeId: MacroBiomeIds.MistyMarsh,
            UseLoreBiome: false));

        Assert.Throws<ArgumentOutOfRangeException>(() => _svc.AnalyzeLayer(run.LocalMap, z: run.LocalMap.Depth));
    }

    [Fact]
    public void Generate_Clamps_SelectedCoordinates_Into_World_And_Region_Bounds()
    {
        var run = _svc.Generate(new MapPreviewRequest(
            Seed: 99,
            WorldWidth: 20,
            WorldHeight: 18,
            RegionWidth: 14,
            RegionHeight: 12,
            WorldX: 999,
            WorldY: -12,
            RegionX: 999,
            RegionY: -3));

        Assert.Equal(19, run.Request.WorldX);
        Assert.Equal(0, run.Request.WorldY);
        Assert.Equal(13, run.Request.RegionX);
        Assert.Equal(0, run.Request.RegionY);
        Assert.InRange(run.LocalStats.OrePotential, 0f, 1f);
    }
}

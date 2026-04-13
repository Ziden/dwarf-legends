using Bunit;
using Microsoft.Extensions.DependencyInjection;
using DwarfFortress.ContentEditor.Components.Pages;
using DwarfFortress.ContentEditor.Services;

namespace DwarfFortress.ContentEditor.Tests;

public sealed class MapPreviewPageTests
{
    [Fact]
    public void MapPreview_RendersAndRegenerates()
    {
        using var ctx = new TestContext();
        ctx.Services.AddSingleton(new MapPreviewService());

        var cut = ctx.RenderComponent<MapPreview>();

        Assert.Contains("Map Preview", cut.Markup);
        Assert.Contains("World Tile Picker", cut.Markup);
        Assert.Contains("Region Heatmap", cut.Markup);
        Assert.Contains("Layer Stats", cut.Markup);

        cut.FindAll("button")
           .First(b => b.TextContent.Contains("Regenerate", StringComparison.OrdinalIgnoreCase))
           .Click();

        Assert.Contains("Tile Breakdown", cut.Markup);
    }

    [Fact]
    public void MapPreview_WorldTilePicker_ClickUpdatesSelectedWorldCoordinate()
    {
        using var ctx = new TestContext();
        ctx.Services.AddSingleton(new MapPreviewService());

        var cut = ctx.RenderComponent<MapPreview>();
        var before = cut.FindAll(".row.g-3.mb-3 .fw-semibold").First().TextContent.Trim();

        cut.FindAll("button.map-cell")
            .First(b => (b.GetAttribute("title") ?? "").Contains("elev=", StringComparison.Ordinal))
            .Click();

        var after = cut.FindAll(".row.g-3.mb-3 .fw-semibold").First().TextContent.Trim();
        Assert.NotEqual(before, after);
        Assert.Equal("(0, 0)", after);
    }

    [Fact]
    public void MapPreview_RegionHeatmap_ClickUpdatesSelectedRegionCoordinate()
    {
        using var ctx = new TestContext();
        ctx.Services.AddSingleton(new MapPreviewService());

        var cut = ctx.RenderComponent<MapPreview>();
        var before = cut.FindAll(".row.g-3.mb-3 .fw-semibold")[1].TextContent.Trim();

        cut.FindAll("button.map-cell")
            .First(b => (b.GetAttribute("title") ?? "").Contains("veg=", StringComparison.Ordinal))
            .Click();

        var after = cut.FindAll(".row.g-3.mb-3 .fw-semibold")[1].TextContent.Trim();
        Assert.NotEqual(before, after);
        Assert.Equal("(0, 0)", after);
    }
}

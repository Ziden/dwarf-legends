using Bunit;
using Microsoft.Extensions.DependencyInjection;
using DwarfFortress.ContentEditor.Components.Pages;

namespace DwarfFortress.ContentEditor.Tests;

public sealed class ItemsPageTests
{
    [Fact]
    public void ClickingEditButton_ShowsItemEditorForSelectedItem()
    {
        using var fixture = new TestDataFixture();
        using var ctx = new TestContext();
        ctx.Services.AddSingleton(fixture.DataService);

        var cut = ctx.RenderComponent<Items>();

        cut.FindAll("button")
            .First(b => b.TextContent.Trim() == "Edit")
            .Click();

        var idField = cut.Find("input[placeholder='snake_case_id']");

        Assert.Equal("iron_bar", idField.GetAttribute("value"));
        Assert.Contains("Iron Bar", cut.Markup);
    }
}

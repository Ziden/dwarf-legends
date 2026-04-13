using Bunit;
using Microsoft.AspNetCore.Components;
using DwarfFortress.ContentEditor.Components.Pages;
using DwarfFortress.ContentEditor.Models;

namespace DwarfFortress.ContentEditor.Tests;

/// <summary>
/// Unit tests for the ItemEditor sub-component (rendered without a parent page).
/// Verifies the component's own lifecycle, callback wiring and tag parsing.
/// </summary>
public sealed class ItemEditorTests : IDisposable
{
    private readonly TestContext _ctx = new();

    private static ItemModel SampleItem() => new()
    {
        Id = "iron_bar",
        DisplayName = "Iron Bar",
        Tags = ["metal", "bar"],
        Weight = 8.0f,
        BaseValue = 5.0f,
        Stackable = false,
        MaxStack = 0
    };

    // ── Parameter binding ──────────────────────────────────────────────────

    [Fact]
    public void ItemEditor_RendersWithoutException()
    {
        // Previously threw: "Cannot create a component of type 'ItemEditor'
        // because its render mode is not supported" — now fixed.
        var ex = Record.Exception(() =>
            _ctx.RenderComponent<ItemEditor>(
                p => p.Add(c => c.Item, SampleItem())));

        Assert.Null(ex);
    }

    [Fact]
    public void ItemEditor_PreFillsIdField()
    {
        var cut = _ctx.RenderComponent<ItemEditor>(
            p => p.Add(c => c.Item, SampleItem()));

        Assert.Contains("iron_bar", cut.Markup);
    }

    [Fact]
    public void ItemEditor_PreFillsTagsTextField_WithCommaSeparatedTags()
    {
        var cut = _ctx.RenderComponent<ItemEditor>(
            p => p.Add(c => c.Item, SampleItem()));

        // OnParametersSet joins the tags list into "metal, bar"
        Assert.Contains("metal, bar", cut.Markup);
    }

    [Fact]
    public void ItemEditor_ShowsMaxStack_WhenStackableIsTrue()
    {
        var item = SampleItem();
        item.Stackable = true;
        item.MaxStack = 50;

        var cut = _ctx.RenderComponent<ItemEditor>(
            p => p.Add(c => c.Item, item));

        Assert.Contains("Max Stack", cut.Markup);
    }

    [Fact]
    public void ItemEditor_HidesMaxStack_WhenStackableIsFalse()
    {
        var item = SampleItem();
        item.Stackable = false;

        var cut = _ctx.RenderComponent<ItemEditor>(
            p => p.Add(c => c.Item, item));

        Assert.DoesNotContain("Max Stack", cut.Markup);
    }

    // ── OnCancel callback ──────────────────────────────────────────────────

    [Fact]
    public void ItemEditor_ClickCancel_InvokesOnCancelCallback()
    {
        var cancelInvoked = false;
        var cut = _ctx.RenderComponent<ItemEditor>(p =>
        {
            p.Add(c => c.Item, SampleItem());
            p.Add(c => c.OnCancel, EventCallback.Factory.Create(this, () => cancelInvoked = true));
        });

        cut.Find("button:contains('Cancel')").Click();

        Assert.True(cancelInvoked);
    }

    // ── OnSave callback ────────────────────────────────────────────────────

    [Fact]
    public void ItemEditor_Submit_InvokesOnSaveWithCurrentItem()
    {
        ItemModel? saved = null;
        var cut = _ctx.RenderComponent<ItemEditor>(p =>
        {
            p.Add(c => c.Item, SampleItem());
            p.Add(c => c.OnSave, EventCallback.Factory.Create<ItemModel>(this, m => saved = m));
        });

        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.Equal("iron_bar", saved.Id);
    }

    [Fact]
    public void ItemEditor_Submit_SavesModifiedDisplayName()
    {
        ItemModel? saved = null;
        var item = SampleItem();

        var cut = _ctx.RenderComponent<ItemEditor>(p =>
        {
            p.Add(c => c.Item, item);
            p.Add(c => c.OnSave, EventCallback.Factory.Create<ItemModel>(this, m => saved = m));
        });

        // Mutate the bound model directly (simulates user typing into the field)
        item.DisplayName = "Iron Ingot";
        cut.SetParametersAndRender(p => p.Add(c => c.Item, item));

        cut.Find("form").Submit();

        Assert.Equal("Iron Ingot", saved?.DisplayName);
    }

    // ── Tags round-trip ────────────────────────────────────────────────────

    [Fact]
    public void ItemEditor_TagsField_ParsesCommaSeparatedInput_IntoTagsList()
    {
        var item = SampleItem();
        var cut = _ctx.RenderComponent<ItemEditor>(
            p => p.Add(c => c.Item, item));

        // Simulate typing new tags into the tags input
        var tagsInput = cut.Find("input[value*='metal']");
        tagsInput.Change("metal, bar, refined");

        Assert.Equal(["metal", "bar", "refined"], item.Tags);
    }

    [Fact]
    public void ItemEditor_TagsField_StripsWhitespace()
    {
        var item = SampleItem();
        var cut = _ctx.RenderComponent<ItemEditor>(
            p => p.Add(c => c.Item, item));

        var tagsInput = cut.Find("input[value*='metal']");
        tagsInput.Change("  metal ,  bar  ");

        Assert.Equal(["metal", "bar"], item.Tags);
    }

    public void Dispose() => _ctx.Dispose();
}

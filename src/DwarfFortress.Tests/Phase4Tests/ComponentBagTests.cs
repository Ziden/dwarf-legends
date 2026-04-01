using DwarfFortress.GameLogic.Entities;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase4Tests;

// A small test-only component
internal sealed class FooComponent  { public int Value { get; set; } }
internal sealed class BarComponent  { public string Label { get; set; } = ""; }

public sealed class ComponentBagTests
{
    [Fact]
    public void Add_And_Get_Roundtrip()
    {
        var bag = new ComponentBag();
        bag.Add(new FooComponent { Value = 42 });

        var result = bag.Get<FooComponent>();

        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Get_Throws_When_Component_Missing()
    {
        var bag = new ComponentBag();

        Assert.Throws<InvalidOperationException>(() => bag.Get<FooComponent>());
    }

    [Fact]
    public void TryGet_Returns_Null_When_Missing()
    {
        var bag = new ComponentBag();

        Assert.Null(bag.TryGet<FooComponent>());
    }

    [Fact]
    public void TryGet_Returns_Component_When_Present()
    {
        var bag = new ComponentBag();
        bag.Add(new FooComponent { Value = 7 });

        var result = bag.TryGet<FooComponent>();

        Assert.NotNull(result);
        Assert.Equal(7, result!.Value);
    }

    [Fact]
    public void Has_Returns_False_For_Missing_Component()
    {
        var bag = new ComponentBag();

        Assert.False(bag.Has<FooComponent>());
    }

    [Fact]
    public void Has_Returns_True_After_Add()
    {
        var bag = new ComponentBag();
        bag.Add(new FooComponent());

        Assert.True(bag.Has<FooComponent>());
    }

    [Fact]
    public void Remove_Returns_True_And_Removes_Component()
    {
        var bag = new ComponentBag();
        bag.Add(new FooComponent());

        bool removed = bag.Remove<FooComponent>();

        Assert.True(removed);
        Assert.False(bag.Has<FooComponent>());
    }

    [Fact]
    public void Remove_Returns_False_When_Not_Present()
    {
        var bag = new ComponentBag();

        Assert.False(bag.Remove<FooComponent>());
    }

    [Fact]
    public void Double_Add_Throws()
    {
        var bag = new ComponentBag();
        bag.Add(new FooComponent());

        Assert.Throws<InvalidOperationException>(() => bag.Add(new FooComponent()));
    }

    [Fact]
    public void Multiple_Different_Components_Coexist()
    {
        var bag = new ComponentBag();
        bag.Add(new FooComponent { Value = 1 });
        bag.Add(new BarComponent { Label = "hello" });

        Assert.Equal(1,       bag.Get<FooComponent>().Value);
        Assert.Equal("hello", bag.Get<BarComponent>().Label);
    }
}

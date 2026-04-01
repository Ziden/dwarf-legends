using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase2Tests;

public sealed class ModifierStackTests
{
    [Fact]
    public void Flat_Modifier_Adds_To_Base()
    {
        var stack = new ModifierStack();
        stack.Add(new Modifier("test", ModType.Flat, 5f));
        Assert.Equal(15f, stack.Resolve(10f));
    }

    [Fact]
    public void PercentAdd_Modifier_Applied_After_Flat()
    {
        var stack = new ModifierStack();
        stack.Add(new Modifier("flat",  ModType.Flat,       5f));
        stack.Add(new Modifier("pct",   ModType.PercentAdd, 0.5f));  // +50%
        // (10 + 5) * (1 + 0.5) = 22.5
        Assert.Equal(22.5f, stack.Resolve(10f));
    }

    [Fact]
    public void Multiple_PercentMult_Are_Multiplied()
    {
        var stack = new ModifierStack();
        stack.Add(new Modifier("a", ModType.PercentMult, 2f));
        stack.Add(new Modifier("b", ModType.PercentMult, 3f));
        // Formula: base * (1 + 2) * (1 + 3) = 10 * 3 * 4 = 120
        Assert.Equal(120f, stack.Resolve(10f));
    }

    [Fact]
    public void Remove_By_SourceId_Reverts_Effect()
    {
        var stack = new ModifierStack();
        stack.Add(new Modifier("temp", ModType.Flat, 50f));
        Assert.Equal(60f, stack.Resolve(10f));

        stack.Remove("temp");
        Assert.Equal(10f, stack.Resolve(10f));
    }

    [Fact]
    public void Timed_Modifier_Expires_After_Duration()
    {
        var stack = new ModifierStack();
        stack.Add(new Modifier("expiring", ModType.Flat, 10f, Duration: 1f));
        Assert.Equal(20f, stack.Resolve(10f));

        stack.Tick(2f);   // 2 seconds elapsed — duration was 1
        Assert.Equal(10f, stack.Resolve(10f));
    }
}

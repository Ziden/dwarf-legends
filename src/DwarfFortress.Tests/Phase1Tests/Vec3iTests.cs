using DwarfFortress.GameLogic.Core;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase1Tests;

public sealed class Vec3iTests
{
    [Fact]
    public void Addition_Works()
    {
        var a = new Vec3i(1, 2, 3);
        var b = new Vec3i(4, 5, 6);
        Assert.Equal(new Vec3i(5, 7, 9), a + b);
    }

    [Fact]
    public void Subtraction_Works()
    {
        var a = new Vec3i(5, 7, 9);
        var b = new Vec3i(1, 2, 3);
        Assert.Equal(new Vec3i(4, 5, 6), a - b);
    }

    [Fact]
    public void ManhattanDistance_Is_Correct()
    {
        var a = new Vec3i(0, 0, 0);
        var b = new Vec3i(3, 4, 0);
        Assert.Equal(7, a.ManhattanDistanceTo(b));
    }

    [Fact]
    public void Neighbours6_Returns_Six_Distinct_Positions()
    {
        var centre     = new Vec3i(5, 5, 5);
        var neighbours = centre.Neighbours6().ToList();
        Assert.Equal(6, neighbours.Count);
        Assert.All(neighbours, n => Assert.NotEqual(centre, n));
        Assert.Equal(6, neighbours.Distinct().Count());
    }

    [Fact]
    public void Neighbours4_Returns_Four_With_Same_Z()
    {
        var centre     = new Vec3i(5, 5, 5);
        var neighbours = centre.Neighbours4().ToList();
        Assert.Equal(4, neighbours.Count);
        Assert.All(neighbours, n => Assert.Equal(5, n.Z));
    }

    [Fact]
    public void Zero_Is_All_Zeros()
    {
        Assert.Equal(new Vec3i(0, 0, 0), Vec3i.Zero);
    }

    [Fact]
    public void Equality_Is_Value_Based()
    {
        var a = new Vec3i(1, 2, 3);
        var b = new Vec3i(1, 2, 3);
        Assert.Equal(a, b);
        Assert.True(a == b);
    }
}

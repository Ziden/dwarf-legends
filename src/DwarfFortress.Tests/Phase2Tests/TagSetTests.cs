using DwarfFortress.GameLogic.Data;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase2Tests;

public sealed class TagSetTests
{
    [Fact]
    public void HasAll_Returns_True_When_All_Tags_Present()
    {
        var tags = TagSet.From("stone", "hard", "mineable");
        Assert.True(tags.HasAll("stone", "hard"));
    }

    [Fact]
    public void HasAll_Returns_False_For_Missing_Tag()
    {
        var tags = TagSet.From("stone");
        Assert.False(tags.HasAll("stone", "metallic"));
    }

    [Fact]
    public void HasAny_Returns_True_When_One_Tag_Present()
    {
        var tags = TagSet.From("stone");
        Assert.True(tags.HasAny("stone", "metallic"));
    }

    [Fact]
    public void HasAny_Returns_False_When_No_Tag_Present()
    {
        var tags = TagSet.From("stone");
        Assert.False(tags.HasAny("metallic", "organic"));
    }

    [Fact]
    public void Contains_Respects_Case_Insensitivity()
    {
        var tags = TagSet.From("Stone");
        Assert.True(tags.Contains("stone"));
        Assert.True(tags.Contains("STONE"));
    }

    [Fact]
    public void With_Returns_New_TagSet_With_Added_Tag()
    {
        var original = TagSet.From("stone");
        var updated  = original.With("hard");
        Assert.False(original.Contains("hard"));
        Assert.True(updated.Contains("hard"));
    }

    [Fact]
    public void Without_Removes_Tag()
    {
        var tags = TagSet.From("stone", "hard").Without("hard");
        Assert.False(tags.Contains("hard"));
    }

    [Fact]
    public void Two_TagSets_With_Same_Tags_Are_Equal()
    {
        var a = TagSet.From("stone", "hard");
        var b = TagSet.From("hard", "stone");
        Assert.Equal(a, b);
    }
}

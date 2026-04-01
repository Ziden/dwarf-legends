using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Tests;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase2Tests;

public sealed class RegistryTests
{
    [Fact]
    public void Add_And_Get_Returns_Same_Object()
    {
        var registry = new Registry<string>();
        registry.Add("foo", "hello");
        Assert.Equal("hello", registry.Get("foo"));
    }

    [Fact]
    public void Duplicate_Id_Throws()
    {
        var registry = new Registry<string>();
        registry.Add("foo", "first");
        Assert.Throws<InvalidOperationException>(() => registry.Add("foo", "second"));
    }

    [Fact]
    public void GetOrNull_Returns_Null_For_Missing_Id()
    {
        var registry = new Registry<string>();
        Assert.Null(registry.GetOrNull("missing"));
    }

    [Fact]
    public void Contains_Returns_True_After_Add()
    {
        var registry = new Registry<string>();
        registry.Add("bar", "val");
        Assert.True(registry.Contains("bar"));
        Assert.False(registry.Contains("baz"));
    }

    [Fact]
    public void Registry_Is_Sealed_After_DataManager_Initialize()
    {
        // DataManager.Initialize() seals all registries — test that Add is blocked
        var (ctx, _, ds) = TestFixtures.CreateContext();
        TestFixtures.AddCoreData(ds);
        var dm = new DataManager();
        dm.Initialize(ctx);

        Assert.Throws<InvalidOperationException>(() => dm.Materials.Add("test", null!));
    }

    [Fact]
    public void All_Returns_All_Definitions()
    {
        var registry = new Registry<string>();
        registry.Add("a", "1");
        registry.Add("b", "2");
        registry.Add("c", "3");
        Assert.Equal(3, registry.All().Count());
    }
}

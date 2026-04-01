using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Tests;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase2Tests;

public sealed class DataManagerTests
{
    [Fact]
    public void Loads_Materials_From_Json()
    {
        var (ctx, _, ds) = TestFixtures.CreateContext();
        TestFixtures.AddCoreData(ds);

        var dm = new DataManager();
        dm.Initialize(ctx);

        Assert.True(dm.Materials.Contains("granite"));
        var granite = dm.Materials.Get("granite");
        Assert.Equal("Granite", granite.DisplayName);
    }

    [Fact]
    public void Loads_Tiles_From_Json()
    {
        var (ctx, _, ds) = TestFixtures.CreateContext();
        TestFixtures.AddCoreData(ds);

        var dm = new DataManager();
        dm.Initialize(ctx);

        Assert.True(dm.Tiles.Contains("stone_floor"));
        Assert.True(dm.Tiles.Contains("stone_wall"));
    }

    [Fact]
    public void Loads_Items_From_Json()
    {
        var (ctx, _, ds) = TestFixtures.CreateContext();
        TestFixtures.AddCoreData(ds);

        var dm = new DataManager();
        dm.Initialize(ctx);

        Assert.True(dm.Items.Contains("granite_boulder"));
        var boulder = dm.Items.Get("granite_boulder");
        Assert.True(boulder.Tags.Contains("stone"));
    }

    [Fact]
    public void Missing_Directory_Does_Not_Throw()
    {
        var (ctx, logger, _) = TestFixtures.CreateContext();
        // Empty data source — no files configured

        var dm = new DataManager();
        dm.Initialize(ctx);   // should not throw

        Assert.True(logger.WarnMessages.Count > 0);
    }

    [Fact]
    public void Registries_Are_Sealed_After_Initialize()
    {
        var (ctx, _, ds) = TestFixtures.CreateContext();
        TestFixtures.AddCoreData(ds);

        var dm = new DataManager();
        dm.Initialize(ctx);

        Assert.Throws<InvalidOperationException>(() => dm.Materials.Add("new_mat", null!));
    }
}

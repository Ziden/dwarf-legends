using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Tests.Fakes;
using DwarfFortress.GameLogic.World;
using DwarfFortress.WorldGen.Generation;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class ChunkActivationManagerTests
{
    [Fact]
    public void StreamedChunkKey_FromLocalTile_AlignsChunkOrigin_ForPositiveAndNegativeTiles()
    {
        var regionCoord = new RegionCoord(3, 4, 5, 6);

        var positive = StreamedChunkKey.FromLocalTile(regionCoord, new Vec3i(17, 31, 5));
        var negative = StreamedChunkKey.FromLocalTile(regionCoord, new Vec3i(-1, -17, -1));

        Assert.Equal(new Vec3i(16, 16, 4), positive.ChunkOrigin);
        Assert.Equal(new Vec3i(-16, -32, -4), negative.ChunkOrigin);
        Assert.Equal(regionCoord, positive.RegionCoord);
        Assert.Equal(regionCoord, negative.RegionCoord);
    }

    [Fact]
    public void ChunkViewportState_EnumeratesVisibleAndResidentChunks_FromTileBounds()
    {
        var regionCoord = new RegionCoord(7, 8, 9, 10);
        var viewport = new ChunkViewportState(
            regionCoord,
            CurrentZ: 5,
            VisibleTileBounds: new TileBounds(3, 4, 20, 20),
            PrefetchRadiusChunks: 1);

        var visible = viewport.EnumerateVisibleChunkKeys().Select(chunk => chunk.ChunkOrigin).OrderBy(origin => origin.X).ThenBy(origin => origin.Y).ToArray();
        var resident = viewport.EnumerateResidentChunkKeys().Select(chunk => chunk.ChunkOrigin).OrderBy(origin => origin.X).ThenBy(origin => origin.Y).ToArray();

        Assert.Equal(
            [new Vec3i(0, 0, 4), new Vec3i(0, 16, 4), new Vec3i(16, 0, 4), new Vec3i(16, 16, 4)],
            visible);
        Assert.Equal(16, resident.Length);
        Assert.Contains(new Vec3i(-16, -16, 4), resident);
        Assert.Contains(new Vec3i(32, 32, 4), resident);
    }

    [Fact]
    public void ChunkViewportState_HasEquivalentChunkCoverage_WhenTileBoundsStayWithinSameChunkEnvelope()
    {
        var regionCoord = new RegionCoord(7, 8, 9, 10);
        var initial = new ChunkViewportState(
            regionCoord,
            CurrentZ: 5,
            VisibleTileBounds: new TileBounds(3, 4, 20, 20),
            PrefetchRadiusChunks: 1);
        var shifted = new ChunkViewportState(
            regionCoord,
            CurrentZ: 6,
            VisibleTileBounds: new TileBounds(4, 5, 18, 18),
            PrefetchRadiusChunks: 1);

        Assert.True(initial.HasEquivalentChunkCoverage(shifted));
    }

    [Fact]
    public void ChunkViewportState_HasEquivalentChunkCoverage_IsFalse_WhenChunkEnvelopeChanges()
    {
        var regionCoord = new RegionCoord(7, 8, 9, 10);
        var initial = new ChunkViewportState(
            regionCoord,
            CurrentZ: 5,
            VisibleTileBounds: new TileBounds(3, 4, 20, 20),
            PrefetchRadiusChunks: 1);
        var expanded = new ChunkViewportState(
            regionCoord,
            CurrentZ: 5,
            VisibleTileBounds: new TileBounds(15, 4, 20, 20),
            PrefetchRadiusChunks: 1);

        Assert.False(initial.HasEquivalentChunkCoverage(expanded));
    }

    [Fact]
    public void ChunkActivationManager_TracksViewportVisibleAndResidentChunkSets()
    {
        var logger = new TestLogger();
        var dataSource = new InMemoryDataSource();
        var simulation = TestFixtures.CreateSimulation(logger, dataSource, new ChunkActivationManager());
        var manager = simulation.Context.Get<ChunkActivationManager>();
        var regionCoord = new RegionCoord(1, 2, 3, 4);

        simulation.EventBus.Emit(new ChunkViewportChangedEvent(
            new ChunkViewportState(
                regionCoord,
                CurrentZ: 0,
                VisibleTileBounds: new TileBounds(0, 0, 16, 16),
                PrefetchRadiusChunks: 1)));

        var visibleChunk = StreamedChunkKey.FromChunkOrigin(regionCoord, new Vec3i(0, 0, 0));
        var prefetchedChunk = StreamedChunkKey.FromChunkOrigin(regionCoord, new Vec3i(-16, 0, 0));

        Assert.Equal(1, manager.GetDesiredActiveChunkKeys().Count);
        Assert.Equal(9, manager.GetDesiredResidentChunkKeys().Count);
        Assert.True(manager.IsChunkActive(visibleChunk));
        Assert.True(manager.IsChunkResident(prefetchedChunk));
        Assert.True(manager.IsChunkPrefetched(prefetchedChunk));
        Assert.False(manager.IsChunkPrefetched(visibleChunk));
    }

    [Fact]
    public void GameBootstrapper_Build_RegistersChunkActivationManager()
    {
        var logger = new TestLogger();
        var dataSource = new InMemoryDataSource();
        var simulation = GameBootstrapper.Build(logger, dataSource);

        Assert.NotNull(simulation.Context.Get<ChunkActivationManager>());
    }
}

using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Tests.Fakes;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class ChunkPreviewStreamingServiceTests
{
    [Fact]
    public void ChunkPreviewStreamingService_UsesLiveWorldMapTiles_ForActiveLocalCoordinates()
    {
        var simulation = CreateStartedSimulation(seed: 7, width: 24, height: 24, depth: 4);
        var map = simulation.Context.Get<WorldMap>();
        var previewStreaming = simulation.Context.Get<ChunkPreviewStreamingService>();

        var position = new Vec3i(0, 0, 0);
        var tile = map.GetTile(position);
        tile.TileDefId = TileDefIds.Tree;
        tile.MaterialId = MaterialIds.Wood;
        tile.TreeSpeciesId = "apple";
        tile.IsPassable = false;
        map.SetTile(position, tile);

        Assert.True(previewStreaming.TryGetTileForRendering(position, out var resolvedTile));
        Assert.Equal(TileDefIds.Tree, resolvedTile.TileDefId);
        Assert.Equal(MaterialIds.Wood, resolvedTile.MaterialId);
        Assert.Equal("apple", resolvedTile.TreeSpeciesId);
    }

    [Fact]
    public void ChunkPreviewStreamingService_BuildsResidentSnapshots_BeyondTheStartupLocal()
    {
        var simulation = CreateStartedSimulation(seed: 11, width: 24, height: 24, depth: 4);
        var mapGeneration = simulation.Context.Get<MapGenerationService>();
        var previewStreaming = simulation.Context.Get<ChunkPreviewStreamingService>();
        var regionCoord = mapGeneration.LastGeneratedEmbark?.RegionCoord ?? throw new InvalidOperationException("Expected embark region context.");

        previewStreaming.ResetDebugMetrics();

        simulation.EventBus.Emit(new ChunkViewportChangedEvent(
            new ChunkViewportState(
                regionCoord,
                CurrentZ: 0,
                VisibleTileBounds: new TileBounds(24, 0, 24, 24),
                PrefetchRadiusChunks: 0)));

        Assert.True(previewStreaming.TryGetResidentChunkSnapshot(new Vec3i(16, 0, 0), out var stitchedBoundaryChunk));
        Assert.True(previewStreaming.TryGetResidentChunkSnapshot(new Vec3i(32, 0, 0), out var adjacentLocalChunk));

        Assert.True(stitchedBoundaryChunk.TryGetLocalTile(8, 0, 0, out var boundaryTile));
        Assert.True(adjacentLocalChunk.TryGetLocalTile(0, 0, 0, out var adjacentTile));
        Assert.NotEqual(TileData.Empty, boundaryTile);
        Assert.NotEqual(TileData.Empty, adjacentTile);
        Assert.True(stitchedBoundaryChunk.IsPreview);
        Assert.True(adjacentLocalChunk.IsPreview);
        Assert.InRange(previewStreaming.DebugGeneratedLocalRequests, 1, 2);
        Assert.True(previewStreaming.ResidentSnapshotCount >= 2);
    }

    [Fact]
    public void ChunkPreviewStreamingService_ReusesGeneratedLocalCache_ForRepeatedSingleTilePreviewLookups()
    {
        var simulation = CreateStartedSimulation(seed: 13, width: 24, height: 24, depth: 4);
        var previewStreaming = simulation.Context.Get<ChunkPreviewStreamingService>();

        previewStreaming.ResetDebugMetrics();

        var firstPreviewPos = new Vec3i(32, 0, 0);
        var secondPreviewPos = new Vec3i(33, 4, 0);

        Assert.True(previewStreaming.TryGetTileForRendering(firstPreviewPos, out var firstTile));
        Assert.NotEqual(TileData.Empty, firstTile);
        Assert.Equal(1, previewStreaming.DebugGeneratedLocalRequests);

        Assert.True(previewStreaming.TryGetTileForRendering(secondPreviewPos, out var secondTile));
        Assert.NotEqual(TileData.Empty, secondTile);
        Assert.Equal(1, previewStreaming.DebugGeneratedLocalRequests);

        Assert.True(previewStreaming.TryGetTileForRendering(firstPreviewPos, out var repeatedTile));
        Assert.NotEqual(TileData.Empty, repeatedTile);
        Assert.Equal(1, previewStreaming.DebugGeneratedLocalRequests);
        Assert.Equal(0, previewStreaming.ResidentSnapshotCount);
    }

    [Fact]
    public void ChunkPreviewStreamingService_ReusesGeneratedLocalCache_ForAlignedResidentChunkSnapshots()
    {
        var simulation = CreateStartedSimulation(seed: 17, width: 48, height: 48, depth: 8);
        var mapGeneration = simulation.Context.Get<MapGenerationService>();
        var previewStreaming = simulation.Context.Get<ChunkPreviewStreamingService>();
        var regionCoord = mapGeneration.LastGeneratedEmbark?.RegionCoord ?? throw new InvalidOperationException("Expected embark region context.");

        previewStreaming.ResetDebugMetrics();

        simulation.EventBus.Emit(new ChunkViewportChangedEvent(
            new ChunkViewportState(
                regionCoord,
                CurrentZ: 0,
                VisibleTileBounds: new TileBounds(48, 0, 48, 48),
                PrefetchRadiusChunks: 0)));

        Assert.True(previewStreaming.TryGetResidentChunkSnapshot(new Vec3i(48, 0, 0), out var firstPreviewChunk));
        Assert.True(previewStreaming.TryGetResidentChunkSnapshot(new Vec3i(64, 0, 0), out var secondPreviewChunk));

        Assert.True(firstPreviewChunk.IsPreview);
        Assert.True(secondPreviewChunk.IsPreview);
        Assert.Equal(1, previewStreaming.DebugGeneratedLocalRequests);
        Assert.Equal(2, previewStreaming.ResidentSnapshotCount);
    }

    [Fact]
    public void ChunkPreviewStreamingService_ResidentSnapshotVersion_TracksAdjacentLiveChunkChanges()
    {
        var simulation = CreateStartedSimulation(seed: 19, width: 24, height: 24, depth: 4);
        var map = simulation.Context.Get<WorldMap>();
        var mapGeneration = simulation.Context.Get<MapGenerationService>();
        var previewStreaming = simulation.Context.Get<ChunkPreviewStreamingService>();
        var regionCoord = mapGeneration.LastGeneratedEmbark?.RegionCoord ?? throw new InvalidOperationException("Expected embark region context.");

        simulation.EventBus.Emit(new ChunkViewportChangedEvent(
            new ChunkViewportState(
                regionCoord,
                CurrentZ: 0,
                VisibleTileBounds: new TileBounds(24, 0, 24, 24),
                PrefetchRadiusChunks: 0)));

        Assert.True(previewStreaming.TryGetResidentChunkSnapshot(new Vec3i(16, 0, 0), out var stitchedBoundaryChunk));
        var beforeVersion = stitchedBoundaryChunk.Version;

        var boundaryPosition = new Vec3i(15, 0, 0);
        var boundaryTile = map.GetTile(boundaryPosition);
        boundaryTile.IsDesignated = !boundaryTile.IsDesignated;
        map.SetTile(boundaryPosition, boundaryTile);

        Assert.True(previewStreaming.TryGetResidentChunkSnapshot(new Vec3i(16, 0, 0), out var refreshedBoundaryChunk));
        Assert.NotEqual(beforeVersion, refreshedBoundaryChunk.Version);
    }

    private static GameSimulation CreateStartedSimulation(int seed, int width, int height, int depth)
    {
        var logger = new TestLogger();
        var dataSource = new FolderDataSource("data");
        var simulation = GameBootstrapper.Build(logger, dataSource);
        simulation.Context.Commands.Dispatch(new StartFortressCommand(seed, width, height, depth));
        return simulation;
    }
}
namespace DwarfFortress.GodotClient.Rendering;

public static class TerrainRenderStats
{
    private static int _pendingChunkBuildCount;
    private static int _chunksBuiltThisFrame;
    private static int _topVertexCount;
    private static int _sideVertexCount;
    private static int _detailVertexCount;
    private static double _latestChunkBuildMilliseconds;
    private static int _treeInstanceCount;
    private static int _plantInstanceCount;
    private static int _actorBillboardCount;

    public static int TerrainLayerCount => TileSurfaceLibrary.GetArrayLayerCount();
    public static int TerrainLayerCapacity => TileSurfaceLibrary.GetArrayCapacity();
    public static int TerrainArrayRebuildCount => TileSurfaceLibrary.GetArrayRebuildCount();
    public static int TerrainDetailLayerCount => TerrainDetailOverlayLibrary.GetLayerCount();
    public static int TerrainDetailLayerCapacity => TerrainDetailOverlayLibrary.GetCapacity();
    public static int TerrainDetailArrayRebuildCount => TerrainDetailOverlayLibrary.GetArrayRebuildCount();

    public static int PendingChunkBuildCount => _pendingChunkBuildCount;
    public static int ChunksBuiltThisFrame => _chunksBuiltThisFrame;
    public static int TopVertexCount => _topVertexCount;
    public static int SideVertexCount => _sideVertexCount;
    public static int DetailVertexCount => _detailVertexCount;
    public static double LatestChunkBuildMilliseconds => _latestChunkBuildMilliseconds;
    public static int TreeInstanceCount => _treeInstanceCount;
    public static int PlantInstanceCount => _plantInstanceCount;
    public static int ActorBillboardCount => _actorBillboardCount;

    public static void RecordChunkFrame(
        int pendingChunkBuildCount,
        int chunksBuiltThisFrame,
        int topVertexCount,
        int sideVertexCount,
        int detailVertexCount,
        double latestChunkBuildMilliseconds)
    {
        _pendingChunkBuildCount = pendingChunkBuildCount;
        _chunksBuiltThisFrame = chunksBuiltThisFrame;
        _topVertexCount = topVertexCount;
        _sideVertexCount = sideVertexCount;
        _detailVertexCount = detailVertexCount;
        _latestChunkBuildMilliseconds = latestChunkBuildMilliseconds;
    }

    public static void RecordVegetationFrame(int treeInstanceCount, int plantInstanceCount)
    {
        _treeInstanceCount = treeInstanceCount;
        _plantInstanceCount = plantInstanceCount;
    }

    public static void RecordActorBillboards(int actorBillboardCount)
        => _actorBillboardCount = actorBillboardCount;
}

using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Local;
using DwarfFortress.WorldGen.Maps;

namespace DwarfFortress.GameLogic.World;

public sealed class ChunkPreviewStreamingService : IGameSystem
{
    private readonly Dictionary<Vec3i, ChunkTileSnapshot> _residentSnapshots = new();
    private readonly HashSet<Vec3i> _desiredResidentOrigins = new();
    private readonly HashSet<Vec3i> _desiredResidentScratch = new();
    private readonly Dictionary<RegionCoord, GeneratedEmbarkMap> _generatedLocalCache = new();
    private readonly Dictionary<RegionCoord, GeneratedEmbarkMap> _snapshotLocalCache = new();

    private WorldMap? _map;
    private MapGenerationService? _mapGeneration;
    private ChunkViewportCoverage? _currentCoverage;
    private int _debugGeneratedLocalRequests;
    private int _debugSnapshotBuildCount;

    public string SystemId => SystemIds.ChunkPreviewStreamingService;
    public int UpdateOrder => 2;
    public bool IsEnabled { get; set; } = true;

    public int ResidentSnapshotCount => _residentSnapshots.Count;

    public int DebugGeneratedLocalRequests => _debugGeneratedLocalRequests;

    public int DebugSnapshotBuildCount => _debugSnapshotBuildCount;

    public void Initialize(GameContext ctx)
    {
        _map = ctx.Get<WorldMap>();
        _mapGeneration = ctx.Get<MapGenerationService>();
        ctx.EventBus.On<ChunkViewportChangedEvent>(OnViewportChanged);
        ctx.EventBus.On<TileChangedEvent>(OnTileChanged);
    }

    public void Tick(float delta) { }

    public void OnSave(SaveWriter writer) { }

    public void OnLoad(SaveReader reader)
    {
        _residentSnapshots.Clear();
        _desiredResidentOrigins.Clear();
        _desiredResidentScratch.Clear();
        _generatedLocalCache.Clear();
        _currentCoverage = null;
        ResetDebugMetrics();
    }

    public void ResetDebugMetrics()
    {
        _debugGeneratedLocalRequests = 0;
        _debugSnapshotBuildCount = 0;
    }

    public bool TryGetResidentChunkSnapshot(Vec3i chunkOrigin, out ChunkTileSnapshot snapshot)
    {
        if (_residentSnapshots.TryGetValue(chunkOrigin, out snapshot!))
            return true;

        if (_desiredResidentOrigins.Count > 0 && !_desiredResidentOrigins.Contains(chunkOrigin))
        {
            snapshot = null!;
            return false;
        }

        if (!TryResolveStreamingContext(out var context)
            || !TryBuildChunkSnapshot(context, chunkOrigin, out snapshot!))
        {
            snapshot = null!;
            return false;
        }

        _debugSnapshotBuildCount++;
        _residentSnapshots[chunkOrigin] = snapshot;
        return true;
    }

    public bool TryGetTileForRendering(Vec3i worldPosition, out TileData tile)
    {
        var residentChunkOrigin = StreamedChunkKey.ChunkOriginForLocalTile(worldPosition);
        if (_residentSnapshots.TryGetValue(residentChunkOrigin, out var residentSnapshot)
            && residentSnapshot.TryGetLocalTile(
                worldPosition.X - residentChunkOrigin.X,
                worldPosition.Y - residentChunkOrigin.Y,
                worldPosition.Z - residentChunkOrigin.Z,
                out tile))
        {
            return true;
        }

        if (!TryResolveStreamingContext(out var context))
        {
            tile = TileData.Empty;
            return false;
        }

        return TryResolveSingleTile(context, worldPosition, out tile);
    }

    private void OnViewportChanged(ChunkViewportChangedEvent evt)
    {
        var coverage = evt.Viewport.ResolveCoverage();
        if (_currentCoverage.HasValue && _currentCoverage.Value == coverage)
            return;

        if (!TryResolveStreamingContext(out _))
        {
            _residentSnapshots.Clear();
            _desiredResidentOrigins.Clear();
            _desiredResidentScratch.Clear();
            _generatedLocalCache.Clear();
            _currentCoverage = null;
            return;
        }

        _currentCoverage = coverage;
        _desiredResidentScratch.Clear();
        foreach (var chunkKey in evt.Viewport.EnumerateResidentChunkKeys())
            _desiredResidentScratch.Add(chunkKey.ChunkOrigin);

        var staleOrigins = _residentSnapshots.Keys.Where(origin => !_desiredResidentScratch.Contains(origin)).ToArray();
        foreach (var origin in staleOrigins)
            _residentSnapshots.Remove(origin);

        _desiredResidentOrigins.Clear();
        foreach (var origin in _desiredResidentScratch)
            _desiredResidentOrigins.Add(origin);
    }

    private void OnTileChanged(TileChangedEvent evt)
    {
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            var affectedOrigin = StreamedChunkKey.ChunkOriginForLocalTile(new Vec3i(
                evt.Pos.X + dx,
                evt.Pos.Y + dy,
                evt.Pos.Z));
            _residentSnapshots.Remove(affectedOrigin);
        }
    }

    private bool TryResolveStreamingContext(out ChunkStreamingContext context)
    {
        if (_map is null
            || _mapGeneration is null
            || _mapGeneration.LastGeneratedEmbark is not GeneratedEmbarkContext embark)
        {
            context = default;
            return false;
        }

        var localSettings = _mapGeneration.LastLocalGenerationSettings
            ?? new LocalGenerationSettings(_map.Width, _map.Height, _map.Depth);
        var generationSettings = _mapGeneration.LastGenerationSettings ?? MapGenerationSettings.Default;
        if (localSettings.Width <= 0 || localSettings.Height <= 0 || localSettings.Depth <= 0)
        {
            context = default;
            return false;
        }

        context = new ChunkStreamingContext(
            this,
            embark.Seed,
            embark.RegionCoord,
            localSettings,
            LocalGenerationFingerprint.Compute(localSettings),
            generationSettings,
            _map,
            _mapGeneration);
        return true;
    }

    private static bool TryBuildChunkSnapshot(
        ChunkStreamingContext context,
        Vec3i chunkOrigin,
        out ChunkTileSnapshot snapshot)
    {
        if (TryBuildChunkSnapshotFast(context, chunkOrigin, out snapshot))
            return true;

        return TryBuildChunkSnapshotGeneral(context, chunkOrigin, out snapshot);
    }

    private static bool TryBuildChunkSnapshotFast(
        ChunkStreamingContext context,
        Vec3i chunkOrigin,
        out ChunkTileSnapshot snapshot)
    {
        if (!CanUseAlignedChunkFastPath(context.LocalSettings))
        {
            snapshot = null!;
            return false;
        }

        if (chunkOrigin.Z < 0 || chunkOrigin.Z + Chunk.Depth > context.LocalSettings.Depth)
        {
            snapshot = null!;
            return false;
        }

        if (IsChunkFullyWithinActiveLocalBounds(context.LocalSettings, chunkOrigin))
            return TryBuildLiveLocalChunkSnapshot(context, chunkOrigin, out snapshot);

        if (!TryResolveChunkLocalOrigin(context, chunkOrigin, out var regionCoord, out var localOriginX, out var localOriginY))
        {
            snapshot = null!;
            return false;
        }

        if (localOriginX + Chunk.Width > context.LocalSettings.Width
            || localOriginY + Chunk.Height > context.LocalSettings.Height)
        {
            snapshot = null!;
            return false;
        }

        var localMap = context.Owner.GetOrCreateGeneratedLocal(regionCoord, context);
        var tiles = new TileData[Chunk.Width * Chunk.Height * Chunk.Depth];
        var index = 0;
        var hasRenderableTile = false;

        for (var localZ = 0; localZ < Chunk.Depth; localZ++)
        for (var localY = 0; localY < Chunk.Height; localY++)
        for (var localX = 0; localX < Chunk.Width; localX++)
        {
            var tile = WorldGenerator.ToTileData(localMap.GetTile(localOriginX + localX, localOriginY + localY, chunkOrigin.Z + localZ));
            tiles[index++] = tile;
            hasRenderableTile |= tile.TileDefId != TileDefIds.Empty;
        }

        if (!hasRenderableTile)
        {
            snapshot = null!;
            return false;
        }

        snapshot = ChunkTileSnapshot.CreateOwned(chunkOrigin, tiles, ResolveChunkSnapshotVersion(context, chunkOrigin), isPreview: true);
        return true;
    }

    private static bool TryBuildChunkSnapshotGeneral(
        ChunkStreamingContext context,
        Vec3i chunkOrigin,
        out ChunkTileSnapshot snapshot)
    {
        context.Owner._snapshotLocalCache.Clear();
        var tiles = new TileData[Chunk.Width * Chunk.Height * Chunk.Depth];
        var index = 0;
        var hasRenderableTile = false;
        var hasPreviewTile = false;

        for (var localZ = 0; localZ < Chunk.Depth; localZ++)
        for (var localY = 0; localY < Chunk.Height; localY++)
        for (var localX = 0; localX < Chunk.Width; localX++)
        {
            var worldPosition = new Vec3i(chunkOrigin.X + localX, chunkOrigin.Y + localY, chunkOrigin.Z + localZ);
            if (TryResolveTile(context, worldPosition, context.Owner._snapshotLocalCache, out var tile, out var usedGeneratedLocal))
            {
                tiles[index++] = tile;
                hasRenderableTile |= tile.TileDefId != TileDefIds.Empty;
                hasPreviewTile |= usedGeneratedLocal;
                continue;
            }

            tiles[index++] = TileData.Empty;
        }

        if (!hasRenderableTile)
        {
            snapshot = null!;
            return false;
        }

        snapshot = ChunkTileSnapshot.CreateOwned(chunkOrigin, tiles, ResolveChunkSnapshotVersion(context, chunkOrigin), isPreview: hasPreviewTile);
        return true;
    }

    private static bool TryBuildLiveLocalChunkSnapshot(
        ChunkStreamingContext context,
        Vec3i chunkOrigin,
        out ChunkTileSnapshot snapshot)
    {
        var tiles = new TileData[Chunk.Width * Chunk.Height * Chunk.Depth];
        var index = 0;
        var hasRenderableTile = false;

        for (var localZ = 0; localZ < Chunk.Depth; localZ++)
        for (var localY = 0; localY < Chunk.Height; localY++)
        for (var localX = 0; localX < Chunk.Width; localX++)
        {
            var tile = context.Map.GetTile(new Vec3i(chunkOrigin.X + localX, chunkOrigin.Y + localY, chunkOrigin.Z + localZ));
            tiles[index++] = tile;
            hasRenderableTile |= tile.TileDefId != TileDefIds.Empty;
        }

        if (!hasRenderableTile)
        {
            snapshot = null!;
            return false;
        }

        snapshot = ChunkTileSnapshot.CreateOwned(chunkOrigin, tiles, ResolveChunkSnapshotVersion(context, chunkOrigin));
        return true;
    }

    private static bool CanUseAlignedChunkFastPath(LocalGenerationSettings localSettings)
        => localSettings.Width > 0
            && localSettings.Height > 0
            && localSettings.Depth > 0
            && localSettings.Width % Chunk.Width == 0
            && localSettings.Height % Chunk.Height == 0
            && localSettings.Depth % Chunk.Depth == 0;

    private static bool IsChunkFullyWithinActiveLocalBounds(LocalGenerationSettings localSettings, Vec3i chunkOrigin)
        => chunkOrigin.X >= 0
            && chunkOrigin.Y >= 0
            && chunkOrigin.Z >= 0
            && chunkOrigin.X + Chunk.Width <= localSettings.Width
            && chunkOrigin.Y + Chunk.Height <= localSettings.Height
            && chunkOrigin.Z + Chunk.Depth <= localSettings.Depth;

    private static bool TryResolveChunkLocalOrigin(
        ChunkStreamingContext context,
        Vec3i chunkOrigin,
        out RegionCoord regionCoord,
        out int localOriginX,
        out int localOriginY)
    {
        if (!TryResolveLocalCoordinate(context, chunkOrigin, out regionCoord, out localOriginX, out localOriginY))
            return false;

        return localOriginX % Chunk.Width == 0
            && localOriginY % Chunk.Height == 0;
    }

    private static int ResolveChunkSnapshotVersion(ChunkStreamingContext context, Vec3i chunkOrigin)
    {
        var hash = new HashCode();
        hash.Add(context.Seed);
        hash.Add(context.AnchorRegionCoord);
        hash.Add(chunkOrigin);
        hash.Add(context.LocalSettings.Width);
        hash.Add(context.LocalSettings.Height);
        hash.Add(context.LocalSettings.Depth);
        hash.Add(context.LocalSettingsFingerprint);
        hash.Add(context.GenerationSettings.WorldWidth);
        hash.Add(context.GenerationSettings.WorldHeight);
        hash.Add(context.GenerationSettings.RegionWidth);
        hash.Add(context.GenerationSettings.RegionHeight);
        hash.Add(context.GenerationSettings.EnableHistory);
        hash.Add(context.GenerationSettings.EnableHistory ? context.GenerationSettings.SimulatedHistoryYears : 0);

        for (var deltaY = -Chunk.Height; deltaY <= Chunk.Height; deltaY += Chunk.Height)
        for (var deltaX = -Chunk.Width; deltaX <= Chunk.Width; deltaX += Chunk.Width)
        {
            var neighborOrigin = new Vec3i(chunkOrigin.X + deltaX, chunkOrigin.Y + deltaY, chunkOrigin.Z);
            hash.Add(neighborOrigin);
            AddChunkSourceVersionToken(ref hash, context, neighborOrigin);
        }

        return hash.ToHashCode();
    }

    private static void AddChunkSourceVersionToken(ref HashCode hash, ChunkStreamingContext context, Vec3i chunkOrigin)
    {
        if (context.Map.TryGetChunk(chunkOrigin, out var activeChunk) && activeChunk is not null)
        {
            hash.Add(true);
            hash.Add(activeChunk.Version);
        }
        else
        {
            hash.Add(false);
            hash.Add(0);
        }

        var previewRegions = CollectPreviewSourceRegions(context, chunkOrigin, out var hasOutOfWorldCoverage);
        hash.Add(previewRegions.Count);
        hash.Add(hasOutOfWorldCoverage);
        foreach (var regionCoord in previewRegions)
        {
            hash.Add(regionCoord);
            hash.Add(ResolvePreviewRegionSourceVersion(context, regionCoord));
        }
    }

    private static List<RegionCoord> CollectPreviewSourceRegions(
        ChunkStreamingContext context,
        Vec3i chunkOrigin,
        out bool hasOutOfWorldCoverage)
    {
        var previewRegions = new List<RegionCoord>(4);
        hasOutOfWorldCoverage = false;

        for (var localY = 0; localY < Chunk.Height; localY++)
        for (var localX = 0; localX < Chunk.Width; localX++)
        {
            var worldX = chunkOrigin.X + localX;
            var worldY = chunkOrigin.Y + localY;
            if (worldX >= 0
                && worldX < context.LocalSettings.Width
                && worldY >= 0
                && worldY < context.LocalSettings.Height)
            {
                continue;
            }

            if (!TryResolveLocalCoordinate(context, new Vec3i(worldX, worldY, chunkOrigin.Z), out var regionCoord, out _, out _))
            {
                hasOutOfWorldCoverage = true;
                continue;
            }

            if (!previewRegions.Contains(regionCoord))
                previewRegions.Add(regionCoord);
        }

        return previewRegions;
    }

    private static int ResolvePreviewRegionSourceVersion(ChunkStreamingContext context, RegionCoord regionCoord)
    {
        var hash = new HashCode();
        hash.Add(context.Seed);
        hash.Add(context.GenerationSettings.WorldWidth);
        hash.Add(context.GenerationSettings.WorldHeight);
        hash.Add(context.GenerationSettings.RegionWidth);
        hash.Add(context.GenerationSettings.RegionHeight);
        hash.Add(context.GenerationSettings.EnableHistory);
        hash.Add(context.GenerationSettings.EnableHistory ? context.GenerationSettings.SimulatedHistoryYears : 0);
        hash.Add(regionCoord);
        hash.Add(context.LocalSettings.Width);
        hash.Add(context.LocalSettings.Height);
        hash.Add(context.LocalSettings.Depth);
        hash.Add(context.LocalSettingsFingerprint);
        return hash.ToHashCode();
    }

    private static bool TryResolveTile(
        ChunkStreamingContext context,
        Vec3i worldPosition,
        Dictionary<RegionCoord, GeneratedEmbarkMap> localMapCache,
        out TileData tile,
        out bool usedGeneratedLocal)
    {
        usedGeneratedLocal = false;
        if (worldPosition.Z < 0 || worldPosition.Z >= context.LocalSettings.Depth)
        {
            tile = TileData.Empty;
            return false;
        }

        if (worldPosition.X >= 0
            && worldPosition.X < context.LocalSettings.Width
            && worldPosition.Y >= 0
            && worldPosition.Y < context.LocalSettings.Height)
        {
            tile = context.Map.GetTile(worldPosition);
            return true;
        }

        if (!TryResolveLocalCoordinate(context, worldPosition, out var regionCoord, out var localX, out var localY))
        {
            tile = TileData.Empty;
            return false;
        }

        var localMap = context.Owner.GetOrCreateGeneratedLocal(regionCoord, localMapCache, context);
        tile = WorldGenerator.ToTileData(localMap.GetTile(localX, localY, worldPosition.Z));
        usedGeneratedLocal = true;
        return true;
    }

    private bool TryResolveSingleTile(
        ChunkStreamingContext context,
        Vec3i worldPosition,
        out TileData tile)
    {
        if (worldPosition.Z < 0 || worldPosition.Z >= context.LocalSettings.Depth)
        {
            tile = TileData.Empty;
            return false;
        }

        if (worldPosition.X >= 0
            && worldPosition.X < context.LocalSettings.Width
            && worldPosition.Y >= 0
            && worldPosition.Y < context.LocalSettings.Height)
        {
            tile = context.Map.GetTile(worldPosition);
            return true;
        }

        if (!TryResolveLocalCoordinate(context, worldPosition, out var regionCoord, out var localX, out var localY))
        {
            tile = TileData.Empty;
            return false;
        }

        var localMap = GetOrCreateGeneratedLocal(regionCoord, context);
        tile = WorldGenerator.ToTileData(localMap.GetTile(localX, localY, worldPosition.Z));
        return true;
    }

    private GeneratedEmbarkMap GetOrCreateGeneratedLocal(
        RegionCoord regionCoord,
        Dictionary<RegionCoord, GeneratedEmbarkMap> localMapCache,
        ChunkStreamingContext context)
    {
        if (localMapCache.TryGetValue(regionCoord, out var cachedLocal))
            return cachedLocal;

        cachedLocal = GetOrCreateGeneratedLocal(regionCoord, context);
        localMapCache[regionCoord] = cachedLocal;
        return cachedLocal;
    }

    private GeneratedEmbarkMap GetOrCreateGeneratedLocal(
        RegionCoord regionCoord,
        ChunkStreamingContext context)
    {
        if (_generatedLocalCache.TryGetValue(regionCoord, out var cachedLocal))
            return cachedLocal;

        var generatedLocal = context.MapGeneration.GetOrCreateLocal(
            context.Seed,
            regionCoord,
            context.LocalSettings,
            context.GenerationSettings);
        _generatedLocalCache[regionCoord] = generatedLocal;
        _debugGeneratedLocalRequests++;
        return generatedLocal;
    }

    private static bool TryResolveLocalCoordinate(
        ChunkStreamingContext context,
        Vec3i worldPosition,
        out RegionCoord regionCoord,
        out int localX,
        out int localY)
    {
        var localOffsetX = FloorDiv(worldPosition.X, context.LocalSettings.Width);
        var localOffsetY = FloorDiv(worldPosition.Y, context.LocalSettings.Height);
        localX = PositiveMod(worldPosition.X, context.LocalSettings.Width);
        localY = PositiveMod(worldPosition.Y, context.LocalSettings.Height);

        return TryOffsetRegionCoord(
            context.AnchorRegionCoord,
            context.GenerationSettings,
            localOffsetX,
            localOffsetY,
            out regionCoord);
    }

    private static bool TryOffsetRegionCoord(
        RegionCoord anchor,
        MapGenerationSettings settings,
        int deltaRegionX,
        int deltaRegionY,
        out RegionCoord result)
    {
        var absoluteRegionX = (anchor.WorldX * settings.RegionWidth) + anchor.RegionX + deltaRegionX;
        var absoluteRegionY = (anchor.WorldY * settings.RegionHeight) + anchor.RegionY + deltaRegionY;
        var maxRegionX = settings.WorldWidth * settings.RegionWidth;
        var maxRegionY = settings.WorldHeight * settings.RegionHeight;

        if (absoluteRegionX < 0 || absoluteRegionX >= maxRegionX || absoluteRegionY < 0 || absoluteRegionY >= maxRegionY)
        {
            result = default;
            return false;
        }

        var worldX = absoluteRegionX / settings.RegionWidth;
        var worldY = absoluteRegionY / settings.RegionHeight;
        var regionX = absoluteRegionX % settings.RegionWidth;
        var regionY = absoluteRegionY % settings.RegionHeight;
        result = new RegionCoord(worldX, worldY, regionX, regionY);
        return true;
    }

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        var remainder = value % divisor;
        return remainder == 0 || value >= 0 ? quotient : quotient - 1;
    }

    private static int PositiveMod(int value, int divisor)
    {
        var remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }

    private readonly record struct ChunkStreamingContext(
        ChunkPreviewStreamingService Owner,
        int Seed,
        RegionCoord AnchorRegionCoord,
        LocalGenerationSettings LocalSettings,
        int LocalSettingsFingerprint,
        MapGenerationSettings GenerationSettings,
        WorldMap Map,
        MapGenerationService MapGeneration);
}
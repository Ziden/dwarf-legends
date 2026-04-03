using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.World;

// ── Events emitted by WorldMap ─────────────────────────────────────────────

public record struct TileChangedEvent(Vec3i Pos, TileData OldTile, TileData NewTile);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The 3D tile world. Owns all Chunks. Emits TileChangedEvent on every mutation.
/// Systems interact via events — no direct WorldMap references in Tick() code.
/// Tracks dirty tiles for efficient save operations.
/// </summary>
public sealed class WorldMap : IGameSystem
{
    // ── IGameSystem ────────────────────────────────────────────────────────
    public string SystemId    => SystemIds.WorldMap;
    public int    UpdateOrder => 2;
    public bool   IsEnabled   { get; set; } = true;

    // ── Dimensions ────────────────────────────────────────────────────────
    public int Width  { get; private set; }
    public int Height { get; private set; }
    public int Depth  { get; private set; }

    private readonly Dictionary<Vec3i, Chunk> _chunks = new();
    private EventBus? _eventBus;

    // Dirty tile tracking for efficient saves — only serialize changed tiles
    private readonly HashSet<Vec3i> _dirtyTiles = new();

    public WorldMap() { }

    // ── IGameSystem ────────────────────────────────────────────────────────

    public void Initialize(GameContext ctx)
    {
        _eventBus = ctx.EventBus;
    }

    public void Tick(float delta) { }

    public void OnSave(SaveWriter writer)
    {
        writer.Write("width",  Width);
        writer.Write("height", Height);
        writer.Write("depth",  Depth);

        // Use dirty tile tracking for efficient saves — only serialize changed tiles
        // If no dirty tiles tracked (first save), fall back to full scan
        IEnumerable<Vec3i> tilesToSave = _dirtyTiles.Count > 0 ? _dirtyTiles : GetAllTilePositions();
        var tiles = new System.Collections.Generic.List<SavedTile>();

        foreach (var pos in tilesToSave)
        {
            var tile = GetTile(pos);
            if (tile.TileDefId == TileDefIds.Empty) continue;

            tiles.Add(new SavedTile
            {
                X                = pos.X,
                Y                = pos.Y,
                Z                = pos.Z,
                TileDefId        = tile.TileDefId,
                MaterialId       = tile.MaterialId,
                TreeSpeciesId    = tile.TreeSpeciesId,
                PlantDefId       = tile.PlantDefId,
                PlantGrowthStage = tile.PlantGrowthStage,
                PlantGrowthProgressSeconds = tile.PlantGrowthProgressSeconds,
                PlantYieldLevel  = tile.PlantYieldLevel,
                PlantSeedLevel   = tile.PlantSeedLevel,
                FluidType        = (int)tile.FluidType,
                FluidLevel       = tile.FluidLevel,
                IsAquifer        = tile.IsAquifer,
                FluidMaterialId  = tile.FluidMaterialId,
                CoatingMaterialId= tile.CoatingMaterialId,
                CoatingAmount    = tile.CoatingAmount,
                IsDesignated     = tile.IsDesignated,
                IsUnderConstruct = tile.IsUnderConstruction,
                IsPassable       = tile.IsPassable,
            });
        }

        writer.Write("tiles", tiles);
        writer.Write("dirtyTileCount", _dirtyTiles.Count);

        // Clear dirty tiles after save — next save will only track new changes
        _dirtyTiles.Clear();
    }

    // Get all tile positions (fallback for first save when dirty tracking is empty)
    private IEnumerable<Vec3i> GetAllTilePositions()
    {
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        for (int z = 0; z < Depth; z++)
            yield return new Vec3i(x, y, z);
    }

    /// <summary>
    /// Mark a tile as dirty without changing it. Useful for bulk operations.
    /// </summary>
    public void MarkTileDirty(Vec3i pos) => _dirtyTiles.Add(pos);

    public void OnLoad(SaveReader reader)
    {
        int w = reader.Read<int>("width");
        int h = reader.Read<int>("height");
        int d = reader.Read<int>("depth");
        SetDimensions(w, h, d);

        var tiles = reader.TryRead<System.Collections.Generic.List<SavedTile>>("tiles");
        if (tiles is null) return;

        foreach (var t in tiles)
        {
            SetTile(new Vec3i(t.X, t.Y, t.Z), new TileData
            {
                TileDefId         = t.TileDefId,
                MaterialId        = t.MaterialId,
                TreeSpeciesId     = t.TreeSpeciesId,
                PlantDefId        = t.PlantDefId,
                PlantGrowthStage  = t.PlantGrowthStage,
                PlantGrowthProgressSeconds = t.PlantGrowthProgressSeconds,
                PlantYieldLevel   = t.PlantYieldLevel,
                PlantSeedLevel    = t.PlantSeedLevel,
                FluidType         = (FluidType)t.FluidType,
                FluidLevel        = t.FluidLevel,
                IsAquifer         = t.IsAquifer,
                FluidMaterialId   = t.FluidMaterialId,
                CoatingMaterialId = t.CoatingMaterialId,
                CoatingAmount     = t.CoatingAmount,
                IsDesignated      = t.IsDesignated,
                IsUnderConstruction = t.IsUnderConstruct,
                IsPassable        = t.IsPassable,
            });
        }
    }

    // ── Save model ─────────────────────────────────────────────────────────

    private sealed class SavedTile
    {
        public int    X                 { get; set; }
        public int    Y                 { get; set; }
        public int    Z                 { get; set; }
        public string TileDefId         { get; set; } = TileDefIds.Empty;
        public string? MaterialId       { get; set; }
        public string? TreeSpeciesId    { get; set; }
        public string? PlantDefId       { get; set; }
        public byte   PlantGrowthStage  { get; set; }
        public float  PlantGrowthProgressSeconds { get; set; }
        public byte   PlantYieldLevel   { get; set; }
        public byte   PlantSeedLevel    { get; set; }
        public int    FluidType         { get; set; }
        public byte   FluidLevel        { get; set; }
        public bool   IsAquifer         { get; set; }
        public string? FluidMaterialId  { get; set; }
        public string? CoatingMaterialId{ get; set; }
        public float  CoatingAmount     { get; set; }
        public bool   IsDesignated      { get; set; }
        public bool   IsUnderConstruct  { get; set; }
        public bool   IsPassable        { get; set; }
    }

    // ── Map setup ──────────────────────────────────────────────────────────

    /// <summary>
    /// Initialize the map dimensions. Must be called before any tiles are placed.
    /// Allocates all chunks upfront (world gen creates them all on startup).
    /// </summary>
    public void SetDimensions(int width, int height, int depth)
    {
        Width  = width;
        Height = height;
        Depth  = depth;

        _chunks.Clear();
        int chunkCountX = (int)Math.Ceiling((double)width  / Chunk.Width);
        int chunkCountY = (int)Math.Ceiling((double)height / Chunk.Height);
        int chunkCountZ = (int)Math.Ceiling((double)depth  / Chunk.Depth);

        for (int cx = 0; cx < chunkCountX; cx++)
        for (int cy = 0; cy < chunkCountY; cy++)
        for (int cz = 0; cz < chunkCountZ; cz++)
        {
            var origin = new Vec3i(cx * Chunk.Width, cy * Chunk.Height, cz * Chunk.Depth);
            _chunks[origin] = new Chunk(origin);
        }
    }

    // ── Tile access ────────────────────────────────────────────────────────

    /// <summary>Get the tile at the given world position. Returns TileData.Empty if out of bounds.</summary>
    public TileData GetTile(Vec3i pos)
    {
        if (!IsInBounds(pos)) return TileData.Empty;
        var (chunk, lx, ly, lz) = Locate(pos);
        return chunk.Get(lx, ly, lz);
    }

    /// <summary>
    /// Set the tile at the given world position.
    /// Emits TileChangedEvent if the value actually changed.
    /// Tracks the tile as dirty for efficient save operations.
    /// </summary>
    public void SetTile(Vec3i pos, TileData tile)
    {
        if (!IsInBounds(pos))
            throw new ArgumentOutOfRangeException(nameof(pos), $"Position {pos} is out of map bounds.");

        tile = NormalizeTile(tile);

        var (chunk, lx, ly, lz) = Locate(pos);
        var old = chunk.Get(lx, ly, lz);
        if (old.Equals(tile))
            return;

        chunk.Set(lx, ly, lz, tile);

        // Track dirty tile for efficient saves
        _dirtyTiles.Add(pos);

        _eventBus?.Emit(new TileChangedEvent(pos, old, tile));
    }

    /// <summary>Try to set a tile; returns false and does nothing if pos is out of bounds.</summary>
    public bool TrySetTile(Vec3i pos, TileData tile)
    {
        if (!IsInBounds(pos)) return false;
        SetTile(pos, tile);
        return true;
    }

    // ── Queries ────────────────────────────────────────────────────────────

    public bool IsInBounds(Vec3i pos)
        => pos.X >= 0 && pos.X < Width
        && pos.Y >= 0 && pos.Y < Height
        && pos.Z >= 0 && pos.Z < Depth;

    public bool IsPassable(Vec3i pos)
        => IsInBounds(pos) && GetTile(pos).IsPassable;

    public const byte MaxWadeableWaterLevel = 2;
    public const byte MinSwimmableWaterLevel = 3;

    public bool IsWalkable(Vec3i pos)
    {
        if (!IsInBounds(pos))
            return false;

        var tile = GetTile(pos);
        if (!tile.IsPassable)
            return false;

        if (tile.FluidLevel <= 0 || tile.FluidType == FluidType.None)
            return tile.TileDefId != TileDefIds.Magma;

        if (tile.FluidType == FluidType.Magma || tile.TileDefId == TileDefIds.Magma)
            return false;

        if (tile.FluidType == FluidType.Water || tile.TileDefId == TileDefIds.Water)
            return tile.FluidLevel <= MaxWadeableWaterLevel;

        return true;
    }

    public bool IsSwimmable(Vec3i pos)
    {
        if (!IsInBounds(pos))
            return false;

        var tile = GetTile(pos);
        if (!tile.IsPassable)
            return false;
        if (tile.FluidType == FluidType.Magma || tile.TileDefId == TileDefIds.Magma)
            return false;
        if (tile.FluidType != FluidType.Water && tile.TileDefId != TileDefIds.Water)
            return false;

        return tile.FluidLevel >= MinSwimmableWaterLevel;
    }

    public bool IsTraversable(Vec3i pos, bool canSwim, bool requiresSwimming)
    {
        if (requiresSwimming)
            return IsSwimmable(pos);

        if (IsWalkable(pos))
            return true;

        return canSwim && IsSwimmable(pos);
    }

    public void CollectTraversableNeighbors(Vec3i origin, bool canSwim, bool requiresSwimming, List<Vec3i> buffer)
    {
        buffer.Clear();
        if (!IsTraversable(origin, canSwim, requiresSwimming))
            return;

        TryAddHorizontalNeighbor(origin + Vec3i.North, canSwim, requiresSwimming, buffer);
        TryAddHorizontalNeighbor(origin + Vec3i.South, canSwim, requiresSwimming, buffer);
        TryAddHorizontalNeighbor(origin + Vec3i.East, canSwim, requiresSwimming, buffer);
        TryAddHorizontalNeighbor(origin + Vec3i.West, canSwim, requiresSwimming, buffer);
        TryAddVerticalNeighbor(origin, origin + Vec3i.Up, canSwim, requiresSwimming, buffer);
        TryAddVerticalNeighbor(origin, origin + Vec3i.Down, canSwim, requiresSwimming, buffer);
    }

    private void TryAddHorizontalNeighbor(Vec3i target, bool canSwim, bool requiresSwimming, List<Vec3i> buffer)
    {
        if (IsTraversable(target, canSwim, requiresSwimming))
            buffer.Add(target);
    }

    private void TryAddVerticalNeighbor(Vec3i origin, Vec3i target, bool canSwim, bool requiresSwimming, List<Vec3i> buffer)
    {
        if (!IsInBounds(target) || !IsTraversable(target, canSwim, requiresSwimming))
            return;

        var originTile = GetTile(origin);
        var targetTile = GetTile(target);
        if (originTile.TileDefId == TileDefIds.Staircase && targetTile.TileDefId == TileDefIds.Staircase)
            buffer.Add(target);
    }

    /// <summary>All chunks that are currently dirty (need a render snapshot rebuild).</summary>
    public IEnumerable<Chunk> GetDirtyChunks()
    {
        foreach (var chunk in _chunks.Values)
            if (chunk.IsDirty) yield return chunk;
    }

    public IEnumerable<Chunk> AllChunks() => _chunks.Values;

    public bool TryGetChunk(Vec3i origin, out Chunk? chunk)
        => _chunks.TryGetValue(origin, out chunk);

    // ── Private helpers ────────────────────────────────────────────────────

    private static Vec3i ChunkOriginFor(Vec3i pos)
        => new(
            (pos.X / Chunk.Width)  * Chunk.Width,
            (pos.Y / Chunk.Height) * Chunk.Height,
            (pos.Z / Chunk.Depth)  * Chunk.Depth);

    private (Chunk chunk, int lx, int ly, int lz) Locate(Vec3i pos)
    {
        var origin = ChunkOriginFor(pos);
        if (!_chunks.TryGetValue(origin, out var chunk))
            throw new InvalidOperationException(
                $"[WorldMap] No chunk at origin {origin} for position {pos}. " +
                $"Call SetDimensions() before placing tiles.");

        return (chunk, pos.X - origin.X, pos.Y - origin.Y, pos.Z - origin.Z);
    }

    private static TileData NormalizeTile(TileData tile)
    {
        if (tile.FluidLevel == 0)
        {
            tile.FluidType = FluidType.None;
            return tile;
        }

        if (tile.FluidType != FluidType.None)
            return tile;

        tile.FluidType = tile.TileDefId switch
        {
            TileDefIds.Water => FluidType.Water,
            TileDefIds.Magma => FluidType.Magma,
            _ => FluidType.None,
        };

        return tile;
    }
}

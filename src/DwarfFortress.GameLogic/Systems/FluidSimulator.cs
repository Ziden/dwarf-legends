using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct FloodedTileEvent(Vec3i Position, FluidType Fluid, byte Level, FluidType PreviousFluid, byte PreviousLevel);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Cellular-automaton fluid simulation.
/// Each tick, fluids flow from higher to lower levels following gravity.
/// Order 20 — runs after all entity-level systems.
///
/// Optimizations applied:
/// - _dryTicks tracks how many consecutive ticks a tile had no fluid change.
///   Tiles are pruned from the active set after 8 dry ticks to prevent unbounded growth.
/// </summary>
public sealed class FluidSimulator : IGameSystem
{
    public string SystemId    => SystemIds.FluidSimulator;
    public int    UpdateOrder => 20;
    public bool   IsEnabled   { get; set; } = true;

    // Maximum fluid tile updates per tick to keep performance bounded
    private const int MaxUpdatesPerTick = 512;
    // Consecutive dry ticks before pruning a tile from the active set
    private const int DryPruneThreshold = 8;

    private GameContext? _ctx;
    private SimulationProfiler? _profiler;
    private readonly HashSet<Vec3i> _activeTiles = new();
    private readonly Dictionary<Vec3i, int> _dryTicks = new();

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        _profiler = ctx.Profiler;
        ctx.EventBus.On<TileChangedEvent>(OnTileChanged);
    }

    public void Tick(float delta)
    {
        var map = _ctx!.Get<WorldMap>();
        List<Vec3i> toProcess;
        using (_profiler?.Measure("collect_active_tiles") ?? default)
            toProcess = new List<Vec3i>(_activeTiles);
        int count = 0;

        using (_profiler?.Measure("simulate_flows") ?? default)
        {
            foreach (var pos in toProcess)
            {
                if (count++ >= MaxUpdatesPerTick) break;

                var hadChange = SimulateFluid(map, pos);
                if (hadChange)
                {
                    _dryTicks[pos] = 0;
                }
                else
                {
                    var dryCount = _dryTicks.GetValueOrDefault(pos) + 1;
                    if (dryCount >= DryPruneThreshold)
                    {
                        // Prune: tile has been dry for N ticks, no longer needs simulation
                        _dryTicks.Remove(pos);
                        _activeTiles.Remove(pos);
                    }
                    else
                    {
                        _dryTicks[pos] = dryCount;
                    }
                }
            }
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    // ── Private ────────────────────────────────────────────────────────────

    private void OnTileChanged(TileChangedEvent e)
    {
        if (e.NewTile.FluidType != FluidType.None)
            _activeTiles.Add(e.Pos);
        else
            _activeTiles.Remove(e.Pos);
    }

    private bool SimulateFluid(WorldMap map, Vec3i pos)
    {
        var tile = map.GetTile(pos);
        if (tile.FluidType == FluidType.None || tile.FluidLevel == 0)
        {
            _activeTiles.Remove(pos);
            return false;
        }

        // Gravity has priority and can move more mass per step.
        if (TryFlowDown(map, pos, tile))
            return true;

        return TryFlowHorizontally(map, pos, tile);
    }

    private bool TryFlowDown(WorldMap map, Vec3i from, TileData source)
    {
        var to = from + Vec3i.Down;
        if (!TryGetDestination(map, to, source, out var dest))
            return false;

        var capacity = (byte)(7 - dest.FluidLevel);
        var amount = (byte)Math.Min((int)capacity, Math.Min((int)source.FluidLevel, 2));
        return amount > 0 && TransferFluid(map, from, to, source, dest, amount);
    }

    private bool TryFlowHorizontally(WorldMap map, Vec3i from, TileData source)
    {
        var bestTarget = default(Vec3i);
        var bestDest = default(TileData);
        var bestFound = false;
        var bestLevel = byte.MaxValue;

        foreach (var to in from.Neighbours4())
        {
            if (!TryGetDestination(map, to, source, out var dest))
                continue;

            // Keep horizontal fluid mostly level: move only when source is meaningfully higher.
            if (source.FluidLevel <= dest.FluidLevel + 1)
                continue;
            if (!bestFound || dest.FluidLevel < bestLevel)
            {
                bestFound = true;
                bestTarget = to;
                bestDest = dest;
                bestLevel = dest.FluidLevel;
            }
        }

        if (!bestFound)
            return false;

        var gradient = Math.Max(1, source.FluidLevel - bestDest.FluidLevel - 1);
        var amount = (byte)Math.Min(2, gradient);
        var capacity = (byte)(7 - bestDest.FluidLevel);
        amount = (byte)Math.Min(amount, capacity);
        amount = (byte)Math.Min(amount, source.FluidLevel);
        return amount > 0 && TransferFluid(map, from, bestTarget, source, bestDest, amount);
    }

    private static bool TryGetDestination(WorldMap map, Vec3i to, TileData source, out TileData dest)
    {
        dest = default;
        if (!map.IsInBounds(to))
            return false;

        dest = map.GetTile(to);
        if (!dest.IsPassable)
            return false;
        if (dest.FluidType != FluidType.None && dest.FluidType != source.FluidType)
            return false;
        if (dest.FluidLevel >= 7)
            return false;

        return true;
    }

    private bool TransferFluid(WorldMap map, Vec3i from, Vec3i to, TileData source, TileData dest, byte amount)
    {
        if (amount == 0)
            return false;

        var newDest = dest;
        newDest.FluidType  = source.FluidType;
        newDest.FluidLevel = (byte)Math.Min(7, dest.FluidLevel + amount);
        map.SetTile(to, newDest);

        var newSrc = source;
        newSrc.FluidLevel = (byte)Math.Max(0, source.FluidLevel - amount);
        if (newSrc.FluidLevel == 0) newSrc.FluidType = FluidType.None;
        map.SetTile(from, newSrc);

        _activeTiles.Add(to);
        _dryTicks.Remove(to); // Reset dry counter on destination
        _ctx!.EventBus.Emit(new FloodedTileEvent(to, newDest.FluidType, newDest.FluidLevel, dest.FluidType, dest.FluidLevel));
        return true;
    }
}

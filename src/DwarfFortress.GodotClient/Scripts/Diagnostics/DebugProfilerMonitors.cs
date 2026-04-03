using System;
using System.Collections.Generic;
using Godot;
using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GodotClient.Diagnostics;

public sealed class DebugProfilerMonitors
{
    private static readonly StringName LatestTickId = new("DwarfFortress/Simulation Tick");
    private static readonly StringName RecentPeakId = new("DwarfFortress/Simulation Peak (recent)");
    private static readonly StringName SlowestSystemId = new("DwarfFortress/Slowest System Tick");
    private static readonly StringName SlowestSystemShareId = new("DwarfFortress/Slowest System Share");
    private static readonly StringName RenderFrameId = new("DwarfFortress/3D Render Frame");
    private static readonly StringName RenderPeakId = new("DwarfFortress/3D Render Peak (recent)");
    private static readonly StringName RenderEntityPositionsId = new("DwarfFortress/3D Render Entity Positions");
    private static readonly StringName RenderSyncSliceId = new("DwarfFortress/3D Render Sync Slice");
    private static readonly StringName RenderDynamicStateId = new("DwarfFortress/3D Render Dynamic State");
    private static readonly StringName RenderTileSpritesId = new("DwarfFortress/3D Render Tile Sprites");
    private static readonly StringName RenderBillboardsId = new("DwarfFortress/3D Render Billboards");
    private static readonly StringName RenderWaterEffectsId = new("DwarfFortress/3D Render Water FX");

    private const string RenderEntityPositionsSystem = "render_entity_positions";
    private const string RenderSyncSliceSystem = "render3d_sync_slice";
    private const string RenderDynamicStateSystem = "render3d_dynamic_state";
    private const string TileSpritesSpan = "tile_sprites";
    private const string BillboardsSpan = "billboards";
    private const string WaterEffectsSpan = "water_effects";

    private GameSimulation? _simulation;
    private SimulationProfiler? _renderProfiler;

    public void Attach(GameSimulation simulation, SimulationProfiler? renderProfiler = null)
    {
        if (ReferenceEquals(_simulation, simulation) && ReferenceEquals(_renderProfiler, renderProfiler))
            return;

        Detach();
        _simulation = simulation;
        _renderProfiler = renderProfiler;

        RegisterMonitor(LatestTickId, Callable.From(GetLatestTickSeconds), 2);
        RegisterMonitor(RecentPeakId, Callable.From(GetRecentPeakTickSeconds), 2);
        RegisterMonitor(SlowestSystemId, Callable.From(GetSlowestSystemTickSeconds), 2);
        RegisterMonitor(SlowestSystemShareId, Callable.From(GetSlowestSystemShare), 3);
        RegisterMonitor(RenderFrameId, Callable.From(GetLatestRenderFrameSeconds), 2);
        RegisterMonitor(RenderPeakId, Callable.From(GetRecentPeakRenderSeconds), 2);
        RegisterMonitor(RenderEntityPositionsId, Callable.From(GetRenderEntityPositionsSeconds), 2);
        RegisterMonitor(RenderSyncSliceId, Callable.From(GetRenderSyncSliceSeconds), 2);
        RegisterMonitor(RenderDynamicStateId, Callable.From(GetRenderDynamicStateSeconds), 2);
        RegisterMonitor(RenderTileSpritesId, Callable.From(GetRenderTileSpritesSeconds), 2);
        RegisterMonitor(RenderBillboardsId, Callable.From(GetRenderBillboardsSeconds), 2);
        RegisterMonitor(RenderWaterEffectsId, Callable.From(GetRenderWaterEffectsSeconds), 2);
    }

    public void Detach()
    {
        RemoveMonitor(LatestTickId);
        RemoveMonitor(RecentPeakId);
        RemoveMonitor(SlowestSystemId);
        RemoveMonitor(SlowestSystemShareId);
        RemoveMonitor(RenderFrameId);
        RemoveMonitor(RenderPeakId);
        RemoveMonitor(RenderEntityPositionsId);
        RemoveMonitor(RenderSyncSliceId);
        RemoveMonitor(RenderDynamicStateId);
        RemoveMonitor(RenderTileSpritesId);
        RemoveMonitor(RenderBillboardsId);
        RemoveMonitor(RenderWaterEffectsId);
        _simulation = null;
        _renderProfiler = null;
    }

    private void RegisterMonitor(StringName id, Callable callable, int monitorType)
    {
        RemoveMonitor(id);
        Performance.AddCustomMonitor(id, callable, new Godot.Collections.Array(), (Performance.MonitorType)monitorType);
    }

    private static void RemoveMonitor(StringName id)
    {
        if (Performance.HasCustomMonitor(id))
            Performance.RemoveCustomMonitor(id);
    }

    private double GetLatestTickSeconds()
    {
        var latestFrame = _simulation?.Profiler.LatestFrame;
        return latestFrame is null ? 0d : latestFrame.TotalDurationMs / 1000d;
    }

    private double GetRecentPeakTickSeconds()
    {
        var frames = _simulation?.Profiler.GetRecentFrames(120);
        if (frames is null || frames.Length == 0)
            return 0d;

        var peak = 0d;
        foreach (var frame in frames)
            peak = Math.Max(peak, frame.TotalDurationMs);

        return peak / 1000d;
    }

    private double GetSlowestSystemTickSeconds()
    {
        var latestFrame = _simulation?.Profiler.LatestFrame;
        if (latestFrame is null || latestFrame.Systems.Count == 0)
            return 0d;

        var slowest = 0d;
        foreach (var system in latestFrame.Systems)
            slowest = Math.Max(slowest, system.DurationMs);

        return slowest / 1000d;
    }

    private double GetSlowestSystemShare()
    {
        var latestFrame = _simulation?.Profiler.LatestFrame;
        if (latestFrame is null || latestFrame.TotalDurationMs <= 0d || latestFrame.Systems.Count == 0)
            return 0d;

        var slowest = 0d;
        foreach (var system in latestFrame.Systems)
            slowest = Math.Max(slowest, system.DurationMs);

        return slowest / latestFrame.TotalDurationMs;
    }

    private double GetLatestRenderFrameSeconds()
        => GetLatestFrameSeconds(_renderProfiler);

    private double GetRecentPeakRenderSeconds()
        => GetRecentPeakSeconds(_renderProfiler);

    private double GetRenderEntityPositionsSeconds()
        => GetLatestSystemSeconds(_renderProfiler, RenderEntityPositionsSystem);

    private double GetRenderSyncSliceSeconds()
        => GetLatestSystemSeconds(_renderProfiler, RenderSyncSliceSystem);

    private double GetRenderDynamicStateSeconds()
        => GetLatestSystemSeconds(_renderProfiler, RenderDynamicStateSystem);

    private double GetRenderTileSpritesSeconds()
        => GetLatestSpanSeconds(_renderProfiler, RenderDynamicStateSystem, TileSpritesSpan);

    private double GetRenderBillboardsSeconds()
        => GetLatestSpanSeconds(_renderProfiler, RenderDynamicStateSystem, BillboardsSpan);

    private double GetRenderWaterEffectsSeconds()
        => GetLatestSpanSeconds(_renderProfiler, RenderDynamicStateSystem, WaterEffectsSpan);

    private static double GetLatestFrameSeconds(SimulationProfiler? profiler)
    {
        var latestFrame = profiler?.LatestFrame;
        return latestFrame is null ? 0d : latestFrame.TotalDurationMs / 1000d;
    }

    private static double GetRecentPeakSeconds(SimulationProfiler? profiler)
    {
        var frames = profiler?.GetRecentFrames(120);
        if (frames is null || frames.Length == 0)
            return 0d;

        var peak = 0d;
        foreach (var frame in frames)
            peak = Math.Max(peak, frame.TotalDurationMs);

        return peak / 1000d;
    }

    private static double GetLatestSystemSeconds(SimulationProfiler? profiler, string systemId)
    {
        var latestFrame = profiler?.LatestFrame;
        if (latestFrame is null)
            return 0d;

        foreach (var system in latestFrame.Systems)
        {
            if (string.Equals(system.SystemId, systemId, StringComparison.Ordinal))
                return system.DurationMs / 1000d;
        }

        return 0d;
    }

    private static double GetLatestSpanSeconds(SimulationProfiler? profiler, string systemId, string spanName)
    {
        var latestFrame = profiler?.LatestFrame;
        if (latestFrame is null)
            return 0d;

        foreach (var system in latestFrame.Systems)
        {
            if (!string.Equals(system.SystemId, systemId, StringComparison.Ordinal))
                continue;

            return FindSpanDurationMs(system.Spans, spanName) / 1000d;
        }

        return 0d;
    }

    private static double FindSpanDurationMs(IReadOnlyList<ProfilerSpanSample> spans, string spanName)
    {
        foreach (var span in spans)
        {
            if (string.Equals(span.Name, spanName, StringComparison.Ordinal))
                return span.DurationMs;

            var nestedDuration = FindSpanDurationMs(span.Children, spanName);
            if (nestedDuration > 0d)
                return nestedDuration;
        }

        return 0d;
    }
}

using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.World;

public sealed class ChunkActivationManager : IGameSystem
{
    private readonly HashSet<StreamedChunkKey> _visibleChunkKeys = new();
    private readonly HashSet<StreamedChunkKey> _residentChunkKeys = new();
    private ChunkViewportCoverage? _currentCoverage;

    public string SystemId => SystemIds.ChunkActivationManager;
    public int UpdateOrder => 3;
    public bool IsEnabled { get; set; } = true;

    public ChunkViewportState? CurrentViewport { get; private set; }

    public void Initialize(GameContext ctx)
        => ctx.EventBus.On<ChunkViewportChangedEvent>(OnChunkViewportChanged);

    public void Tick(float delta) { }

    public void OnSave(SaveWriter w) { }

    public void OnLoad(SaveReader r)
    {
        CurrentViewport = null;
        _currentCoverage = null;
        _visibleChunkKeys.Clear();
        _residentChunkKeys.Clear();
    }

    public IReadOnlyCollection<StreamedChunkKey> GetDesiredActiveChunkKeys() => _visibleChunkKeys;

    public IReadOnlyCollection<StreamedChunkKey> GetDesiredResidentChunkKeys() => _residentChunkKeys;

    public bool IsChunkActive(StreamedChunkKey chunkKey) => _visibleChunkKeys.Contains(chunkKey);

    public bool IsChunkResident(StreamedChunkKey chunkKey) => _residentChunkKeys.Contains(chunkKey);

    public bool IsChunkPrefetched(StreamedChunkKey chunkKey)
        => _residentChunkKeys.Contains(chunkKey) && !_visibleChunkKeys.Contains(chunkKey);

    private void OnChunkViewportChanged(ChunkViewportChangedEvent e)
    {
        var coverage = e.Viewport.ResolveCoverage();
        if (_currentCoverage.HasValue && _currentCoverage.Value == coverage)
        {
            CurrentViewport = e.Viewport;
            return;
        }

        CurrentViewport = e.Viewport;
        _currentCoverage = coverage;

        _visibleChunkKeys.Clear();
        foreach (var chunkKey in e.Viewport.EnumerateVisibleChunkKeys())
            _visibleChunkKeys.Add(chunkKey);

        _residentChunkKeys.Clear();
        foreach (var chunkKey in e.Viewport.EnumerateResidentChunkKeys())
            _residentChunkKeys.Add(chunkKey);
    }
}
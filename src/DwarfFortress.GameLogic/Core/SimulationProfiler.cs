using System.Diagnostics;

namespace DwarfFortress.GameLogic.Core;

public sealed record ProfilerSpanSample(string Name, double DurationMs, IReadOnlyList<ProfilerSpanSample> Children);

public sealed record ProfilerSystemSample(string SystemId, int UpdateOrder, double DurationMs, IReadOnlyList<ProfilerSpanSample> Spans);

public sealed record ProfilerFrame(long Sequence, float DeltaSeconds, double TotalDurationMs, IReadOnlyList<ProfilerSystemSample> Systems);

public sealed record ProfilerSystemSummary(
    string SystemId,
    int UpdateOrder,
    double LatestDurationMs,
    double AverageDurationMs,
    double MaxDurationMs,
    int SampleCount,
    int SpikeCount);

public sealed class SimulationProfiler
{
    private readonly int _maxFrames;
    private readonly List<ProfilerFrame> _frames = new();
    private readonly Stack<MutableSpan> _spanStack = new();

    private MutableFrame? _currentFrame;
    private MutableSystem? _currentSystem;
    private long _nextSequence = 1;

    public SimulationProfiler(int maxFrames = 600)
    {
        _maxFrames = Math.Max(60, maxFrames);
    }

    public int FrameCount => _frames.Count;

    public ProfilerFrame? LatestFrame => _frames.Count == 0 ? null : _frames[^1];

    public void BeginFrame(float deltaSeconds)
    {
        if (_currentFrame is not null)
            EndFrame();

        _currentFrame = new MutableFrame
        {
            Sequence = _nextSequence++,
            DeltaSeconds = deltaSeconds,
            StartTimestamp = Stopwatch.GetTimestamp(),
        };
        _currentSystem = null;
        _spanStack.Clear();
    }

    public void BeginSystem(string systemId, int updateOrder)
    {
        if (_currentFrame is null)
            return;

        EndSystem();

        var system = new MutableSystem
        {
            SystemId = systemId,
            UpdateOrder = updateOrder,
            StartTimestamp = Stopwatch.GetTimestamp(),
        };
        _currentFrame.Systems.Add(system);
        _currentSystem = system;
        _spanStack.Clear();
    }

    public void EndSystem()
    {
        if (_currentSystem is null)
            return;

        var endTimestamp = Stopwatch.GetTimestamp();
        while (_spanStack.Count > 0)
        {
            var activeSpan = _spanStack.Pop();
            activeSpan.EndTimestamp = endTimestamp;
        }

        _currentSystem.EndTimestamp = endTimestamp;
        _currentSystem = null;
    }

    public void EndFrame()
    {
        if (_currentFrame is null)
            return;

        EndSystem();

        _currentFrame.EndTimestamp = Stopwatch.GetTimestamp();
        _frames.Add(_currentFrame.ToImmutable());
        if (_frames.Count > _maxFrames)
            _frames.RemoveAt(0);

        _currentFrame = null;
        _currentSystem = null;
        _spanStack.Clear();
    }

    public ProfilerScope Measure(string spanName)
    {
        if (_currentSystem is null || string.IsNullOrWhiteSpace(spanName))
            return default;

        var span = new MutableSpan
        {
            Name = spanName,
            StartTimestamp = Stopwatch.GetTimestamp(),
        };

        if (_spanStack.Count > 0)
            _spanStack.Peek().Children.Add(span);
        else
            _currentSystem.Spans.Add(span);

        _spanStack.Push(span);
        return new ProfilerScope(this);
    }

    public ProfilerFrame[] GetRecentFrames(int maxFrames = 120)
    {
        if (_frames.Count == 0 || maxFrames <= 0)
            return Array.Empty<ProfilerFrame>();

        var count = Math.Min(maxFrames, _frames.Count);
        return _frames.Skip(_frames.Count - count).ToArray();
    }

    public ProfilerFrame[] GetSlowFrames(int lookbackFrames = 300, int count = 12)
    {
        if (count <= 0)
            return Array.Empty<ProfilerFrame>();

        return GetRecentFrames(lookbackFrames)
            .OrderByDescending(frame => frame.TotalDurationMs)
            .ThenByDescending(frame => frame.Sequence)
            .Take(count)
            .ToArray();
    }

    public ProfilerFrame? GetFrame(long sequence)
    {
        for (var index = _frames.Count - 1; index >= 0; index--)
        {
            var frame = _frames[index];
            if (frame.Sequence == sequence)
                return frame;
        }

        return null;
    }

    public ProfilerSystemSummary[] GetSystemSummaries(int lookbackFrames = 300)
    {
        var frames = GetRecentFrames(lookbackFrames);
        if (frames.Length == 0)
            return Array.Empty<ProfilerSystemSummary>();

        var durationsBySystem = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var orderBySystem = new Dictionary<string, int>(StringComparer.Ordinal);
        var latestBySystem = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var frame in frames)
        {
            foreach (var system in frame.Systems)
            {
                if (!durationsBySystem.TryGetValue(system.SystemId, out var durations))
                {
                    durations = new List<double>();
                    durationsBySystem[system.SystemId] = durations;
                }

                durations.Add(system.DurationMs);
                orderBySystem[system.SystemId] = system.UpdateOrder;
                latestBySystem[system.SystemId] = system.DurationMs;
            }
        }

        return durationsBySystem
            .Select(entry =>
            {
                var durations = entry.Value;
                var average = durations.Average();
                var max = durations.Max();
                var spikeThreshold = Math.Max(average * 1.5d, average + 0.25d);
                var spikeCount = durations.Count(duration => duration >= spikeThreshold);

                return new ProfilerSystemSummary(
                    entry.Key,
                    orderBySystem[entry.Key],
                    latestBySystem[entry.Key],
                    average,
                    max,
                    durations.Count,
                    spikeCount);
            })
            .OrderByDescending(summary => summary.LatestDurationMs)
            .ThenByDescending(summary => summary.MaxDurationMs)
            .ToArray();
    }

    internal void EndSpan()
    {
        if (_spanStack.Count == 0)
            return;

        var span = _spanStack.Pop();
        span.EndTimestamp = Stopwatch.GetTimestamp();
    }

    public readonly struct ProfilerScope : IDisposable
    {
        private readonly SimulationProfiler? _profiler;

        internal ProfilerScope(SimulationProfiler profiler)
        {
            _profiler = profiler;
        }

        public void Dispose()
        {
            _profiler?.EndSpan();
        }
    }

    private sealed class MutableFrame
    {
        public long Sequence { get; init; }
        public float DeltaSeconds { get; init; }
        public long StartTimestamp { get; init; }
        public long EndTimestamp { get; set; }
        public List<MutableSystem> Systems { get; } = new();

        public ProfilerFrame ToImmutable()
        {
            return new ProfilerFrame(
                Sequence,
                DeltaSeconds,
                ToMilliseconds(EndTimestamp - StartTimestamp),
                Systems.Select(system => system.ToImmutable()).ToArray());
        }
    }

    private sealed class MutableSystem
    {
        public string SystemId { get; init; } = string.Empty;
        public int UpdateOrder { get; init; }
        public long StartTimestamp { get; init; }
        public long EndTimestamp { get; set; }
        public List<MutableSpan> Spans { get; } = new();

        public ProfilerSystemSample ToImmutable()
        {
            return new ProfilerSystemSample(
                SystemId,
                UpdateOrder,
                ToMilliseconds(EndTimestamp - StartTimestamp),
                Spans.Select(span => span.ToImmutable()).ToArray());
        }
    }

    private sealed class MutableSpan
    {
        public string Name { get; init; } = string.Empty;
        public long StartTimestamp { get; init; }
        public long EndTimestamp { get; set; }
        public List<MutableSpan> Children { get; } = new();

        public ProfilerSpanSample ToImmutable()
        {
            return new ProfilerSpanSample(
                Name,
                ToMilliseconds(EndTimestamp - StartTimestamp),
                Children.Select(child => child.ToImmutable()).ToArray());
        }
    }

    private static double ToMilliseconds(long elapsedTicks)
        => elapsedTicks <= 0 ? 0d : elapsedTicks * 1000d / Stopwatch.Frequency;
}
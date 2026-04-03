using System.Threading;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Tests.Fakes;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class SimulationProfilerTests
{
    [Fact]
    public void Tick_Records_Enabled_Systems_In_Update_Order()
    {
        var logger = new TestLogger();
        var dataSource = new InMemoryDataSource();
        var simulation = new GameSimulation(logger, dataSource);

        simulation.RegisterSystem(new ProfilingTestSystem("beta", updateOrder: 20));
        simulation.RegisterSystem(new ProfilingTestSystem("alpha", updateOrder: 5));
        simulation.Initialize();

        simulation.Tick(0.1f);

        var frame = simulation.Profiler.LatestFrame;
        Assert.NotNull(frame);
        Assert.Equal(2, frame!.Systems.Count);
        Assert.Equal("alpha", frame.Systems[0].SystemId);
        Assert.Equal("beta", frame.Systems[1].SystemId);
        Assert.True(frame.TotalDurationMs >= 0d);
    }

    [Fact]
    public void Tick_Captures_Hierarchical_Spans_Within_A_System()
    {
        var logger = new TestLogger();
        var dataSource = new InMemoryDataSource();
        var simulation = new GameSimulation(logger, dataSource);

        simulation.RegisterSystem(new SpanRecordingSystem());
        simulation.Initialize();

        simulation.Tick(0.1f);

        var frame = simulation.Profiler.LatestFrame;
        Assert.NotNull(frame);
        var system = Assert.Single(frame.Systems);
        var outer = Assert.Single(system.Spans);
        Assert.Equal("outer_phase", outer.Name);
        var inner = Assert.Single(outer.Children);
        Assert.Equal("inner_phase", inner.Name);
    }

    [Fact]
    public void Profiler_Keeps_A_Bounded_Rolling_History()
    {
        var profiler = new SimulationProfiler(maxFrames: 60);

        for (var index = 0; index < 80; index++)
        {
            profiler.BeginFrame(0.1f);
            profiler.BeginSystem("test_system", 1);
            Thread.SpinWait(10_000);
            profiler.EndSystem();
            profiler.EndFrame();
        }

        Assert.Equal(60, profiler.FrameCount);
        var frames = profiler.GetRecentFrames(80);
        Assert.Equal(60, frames.Length);
        Assert.Equal(21L, frames[0].Sequence);
        Assert.Equal(80L, frames[^1].Sequence);
    }

    private sealed class ProfilingTestSystem : IGameSystem
    {
        public ProfilingTestSystem(string systemId, int updateOrder)
        {
            SystemId = systemId;
            UpdateOrder = updateOrder;
        }

        public string SystemId { get; }
        public int UpdateOrder { get; }
        public bool IsEnabled { get; set; } = true;

        public void Initialize(GameContext ctx)
        {
        }

        public void Tick(float delta)
        {
            Thread.SpinWait(25_000);
        }

        public void OnSave(SaveWriter w)
        {
        }

        public void OnLoad(SaveReader r)
        {
        }
    }

    private sealed class SpanRecordingSystem : IGameSystem
    {
        private SimulationProfiler? _profiler;

        public string SystemId => "span_system";
        public int UpdateOrder => 1;
        public bool IsEnabled { get; set; } = true;

        public void Initialize(GameContext ctx)
        {
            _profiler = ctx.Profiler;
        }

        public void Tick(float delta)
        {
            using (_profiler?.Measure("outer_phase") ?? default)
            {
                Thread.SpinWait(15_000);
                using (_profiler?.Measure("inner_phase") ?? default)
                    Thread.SpinWait(10_000);
            }
        }

        public void OnSave(SaveWriter w)
        {
        }

        public void OnLoad(SaveReader r)
        {
        }
    }
}
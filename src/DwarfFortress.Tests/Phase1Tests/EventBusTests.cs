using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Tests.Fakes;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase1Tests;

public sealed class EventBusTests
{
    [Fact]
    public void Subscribe_And_Emit_Delivers_Event()
    {
        var bus      = new EventBus();
        var received = new List<TestEvent>();

        bus.On<TestEvent>(e => received.Add(e));
        bus.Emit(new TestEvent(42));

        Assert.Single(received);
        Assert.Equal(42, received[0].Value);
    }

    [Fact]
    public void Unsubscribe_Stops_Delivery()
    {
        var bus     = new EventBus();
        int count   = 0;
        Action<TestEvent> handler = _ => count++;

        bus.On(handler);
        bus.Emit(new TestEvent(0));
        bus.Off(handler);
        bus.Emit(new TestEvent(0));

        Assert.Equal(1, count);
    }

    [Fact]
    public void Multiple_Handlers_All_Receive()
    {
        var bus = new EventBus();
        int a   = 0, b = 0;

        bus.On<TestEvent>(_ => a++);
        bus.On<TestEvent>(_ => b++);
        bus.Emit(new TestEvent(0));

        Assert.Equal(1, a);
        Assert.Equal(1, b);
    }

    [Fact]
    public void Handler_Unsubscribing_During_Emit_Does_Not_Throw()
    {
        var bus = new EventBus();
        Action<TestEvent>? self = null;
        int callCount           = 0;

        self = _ =>
        {
            callCount++;
            bus.Off(self!);
        };
        bus.On(self);
        bus.Emit(new TestEvent(0));
        bus.Emit(new TestEvent(0));   // should not reach handler again

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void SubscriberCount_Returns_Correct_Value()
    {
        var bus = new EventBus();
        Assert.Equal(0, bus.SubscriberCount<TestEvent>());

        Action<TestEvent> h = _ => { };
        bus.On(h);
        Assert.Equal(1, bus.SubscriberCount<TestEvent>());

        bus.Off(h);
        Assert.Equal(0, bus.SubscriberCount<TestEvent>());
    }

    private record struct TestEvent(int Value);
}

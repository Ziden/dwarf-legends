using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Tests.Fakes;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase1Tests;

public sealed class CommandDispatcherTests
{
    private static CommandDispatcher CreateDispatcher()
        => new CommandDispatcher(new TestLogger());

    [Fact]
    public void Dispatch_Calls_Registered_Handler()
    {
        var dispatcher = CreateDispatcher();
        int handled    = 0;
        dispatcher.Register<TestCmd>(_ => handled++);

        dispatcher.Dispatch(new TestCmd(99));

        Assert.Equal(1, handled);
    }

    [Fact]
    public void Dispatch_Unknown_Command_Does_Not_Throw()
    {
        var dispatcher = CreateDispatcher();
        // No handler registered — should silently log and no-op
        dispatcher.Dispatch(new TestCmd(0));
    }

    [Fact]
    public void HasHandler_Returns_True_After_Register()
    {
        var dispatcher = CreateDispatcher();
        Assert.False(dispatcher.HasHandler<TestCmd>());

        dispatcher.Register<TestCmd>(_ => { });
        Assert.True(dispatcher.HasHandler<TestCmd>());
    }

    [Fact]
    public void Command_Value_Passed_To_Handler()
    {
        var dispatcher = CreateDispatcher();
        TestCmd? received = null;
        dispatcher.Register<TestCmd>(cmd => received = cmd);

        dispatcher.Dispatch(new TestCmd(42));

        Assert.NotNull(received);
        Assert.Equal(42, received!.Value);
    }

    private record TestCmd(int Value) : ICommand;
}

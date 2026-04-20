using NSubstitute;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Implementations.Session;
using Xunit;

namespace PersonaEngine.Lib.Tests.Core.Conversation;

public class MicMuteControllerTests
{
    private static (MicMuteController Ctrl, IConversationInputGate Gate, IDisposable Scope) Build()
    {
        var gate = Substitute.For<IConversationInputGate>();
        var scope = Substitute.For<IDisposable>();
        gate.CloseScope(Arg.Any<string>()).Returns(scope);
        return (new MicMuteController(gate), gate, scope);
    }

    [Fact]
    public void SetMuted_True_AcquiresScopeOnce()
    {
        var (ctrl, gate, _) = Build();

        ctrl.SetMuted(true);
        ctrl.SetMuted(true);

        gate.Received(1).CloseScope(Arg.Any<string>());
        Assert.True(ctrl.IsMuted);
    }

    [Fact]
    public void SetMuted_False_DisposesScope()
    {
        var (ctrl, _, scope) = Build();

        ctrl.SetMuted(true);
        ctrl.SetMuted(false);

        scope.Received(1).Dispose();
        Assert.False(ctrl.IsMuted);
    }

    [Fact]
    public void MutedChanged_FiresOnlyOnTransitions()
    {
        var (ctrl, _, _) = Build();
        var count = 0;
        ctrl.MutedChanged += _ => count++;

        ctrl.SetMuted(true);
        ctrl.SetMuted(true);
        ctrl.SetMuted(false);
        ctrl.SetMuted(false);

        Assert.Equal(2, count);
    }

    [Fact]
    public void ConcurrentToggle_IsThreadSafe()
    {
        var (ctrl, _, _) = Build();
        var tasks = Enumerable
            .Range(0, 32)
            .Select(i => Task.Run(() => ctrl.SetMuted(i % 2 == 0)))
            .ToArray();

        Task.WaitAll(tasks);

        ctrl.SetMuted(false);
        Assert.False(ctrl.IsMuted);
    }
}

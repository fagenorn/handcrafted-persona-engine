using Microsoft.Extensions.Logging.Abstractions;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Implementations.Session;
using Xunit;

namespace PersonaEngine.Lib.Tests.Core.Conversation;

public class ConversationInputGateTests
{
    private static IConversationInputGate CreateGate() =>
        new ConversationInputGate(NullLogger<ConversationInputGate>.Instance);

    [Fact]
    public void IsOpen_Initially_True()
    {
        var gate = CreateGate();
        Assert.True(gate.IsOpen);
    }

    [Fact]
    public void CloseScope_ClosesGate_WhileUndisposed()
    {
        var gate = CreateGate();
        using var scope = gate.CloseScope("test reason");
        Assert.False(gate.IsOpen);
    }

    [Fact]
    public void DisposingScope_ReopensGate()
    {
        var gate = CreateGate();
        var scope = gate.CloseScope("test");
        scope.Dispose();
        Assert.True(gate.IsOpen);
    }

    [Fact]
    public void MultipleScopes_ComposeCorrectly_StaysClosedUntilAllDisposed()
    {
        var gate = CreateGate();
        var scopeA = gate.CloseScope("a");
        var scopeB = gate.CloseScope("b");

        Assert.False(gate.IsOpen);

        scopeA.Dispose();
        Assert.False(gate.IsOpen); // still closed by scope B

        scopeB.Dispose();
        Assert.True(gate.IsOpen);
    }

    [Fact]
    public void DoubleDispose_IsNoOp()
    {
        var gate = CreateGate();
        var outer = gate.CloseScope("outer");
        var inner = gate.CloseScope("inner");

        inner.Dispose();
        inner.Dispose(); // second dispose must not decrement again
        inner.Dispose();

        // Outer is still holding; gate should still be closed
        Assert.False(gate.IsOpen);

        outer.Dispose();
        Assert.True(gate.IsOpen);
    }

    [Fact]
    public void CloseScope_NullOrEmptyReason_IsAccepted()
    {
        // Reason is diagnostic only — empty/null shouldn't throw.
        var gate = CreateGate();
        using var scope1 = gate.CloseScope("");
        using var scope2 = gate.CloseScope(null!);
        Assert.False(gate.IsOpen);
    }

    [Fact]
    public void ParallelClose_ThenParallelDispose_EndsOpen()
    {
        var gate = CreateGate();
        const int Parallelism = 64;
        var scopes = new IDisposable[Parallelism];

        Parallel.For(0, Parallelism, i => scopes[i] = gate.CloseScope($"thread-{i}"));
        Assert.False(gate.IsOpen);

        Parallel.For(0, Parallelism, i => scopes[i].Dispose());
        Assert.True(gate.IsOpen);
    }
}

using NSubstitute;
using OpenAI.Chat;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.UI.ControlPanel;
using Xunit;
using LibChatMessage = PersonaEngine.Lib.Core.Conversation.Abstractions.Context.ChatMessage;

namespace PersonaEngine.Lib.Tests.UI;

public class PersonaStateProviderTests
{
    private readonly IConversationOrchestrator _orchestrator =
        Substitute.For<IConversationOrchestrator>();
    private readonly IAudioAmplitudeProvider _amplitudeProvider =
        Substitute.For<IAudioAmplitudeProvider>();

    private PersonaStateProvider CreateProvider() => new(_orchestrator, _amplitudeProvider);

    private void StubFirstActiveSession(IConversationSession session)
    {
        _orchestrator
            .TryGetFirstActiveSession(out Arg.Any<IConversationSession?>())
            .Returns(call =>
            {
                call[0] = session;
                return true;
            });
    }

    [Fact]
    public void State_NoSessions_ReturnsNoSession()
    {
        // Default substitute returns false and null — no stubbing needed.
        var provider = CreateProvider();

        provider.Update(0.016f);

        Assert.Equal(PersonaUiState.NoSession, provider.State);
    }

    [Fact]
    public void State_SessionWithNoPendingTurn_ReturnsIdle()
    {
        var session = Substitute.For<IConversationSession>();
        var context = Substitute.For<IConversationContext>();
        context.PendingTurn.Returns((InteractionTurn?)null);
        session.Context.Returns(context);
        // Without a CurrentState stub, NSubstitute returns the enum's default
        // (Initial) — which the provider treats as "not running" → NoSession.
        // Pin an active state so the under-test Idle/Thinking/Speaking logic runs.
        session.CurrentState.Returns(ConversationState.Idle);

        StubFirstActiveSession(session);

        var provider = CreateProvider();
        provider.Update(0.016f);

        Assert.Equal(PersonaUiState.Idle, provider.State);
    }

    [Fact]
    public void StateJustChanged_OnTransition_IsTrue()
    {
        var session = Substitute.For<IConversationSession>();
        var context = Substitute.For<IConversationContext>();
        context.PendingTurn.Returns((InteractionTurn?)null);
        session.Context.Returns(context);
        session.CurrentState.Returns(ConversationState.Idle);

        StubFirstActiveSession(session);

        var provider = CreateProvider();

        // First update: NoSession (initial default) → Idle
        provider.Update(0.016f);
        Assert.True(provider.StateJustChanged);

        // Second update: Idle → Idle (no change)
        provider.Update(0.016f);
        Assert.False(provider.StateJustChanged);
    }

    [Fact]
    public void AudioAmplitude_DelegatesToProvider()
    {
        _amplitudeProvider.CurrentAmplitude.Returns(0.75f);

        var provider = CreateProvider();
        provider.Update(0.016f);

        Assert.Equal(0.75f, provider.AudioAmplitude);
    }

    [Fact]
    public void IsAudioPlaying_DelegatesToProvider()
    {
        _amplitudeProvider.IsPlaying.Returns(true);

        var provider = CreateProvider();
        provider.Update(0.016f);

        Assert.True(provider.IsAudioPlaying);
    }

    [Fact]
    public void StateElapsed_Accumulates_WhenStateUnchanged()
    {
        var provider = CreateProvider();
        provider.Update(0.1f); // Initial → NoSession transition, elapsed resets then += 0.1
        provider.Update(0.2f); // Same state, elapsed += 0.2

        Assert.True(
            provider.StateElapsed >= 0.25f,
            $"Expected >= 0.25f, got {provider.StateElapsed}"
        );
    }

    [Fact]
    public void TryGetFirstActiveSession_ReturnsFalse_FallsBackToNoSession()
    {
        _orchestrator
            .TryGetFirstActiveSession(out Arg.Any<IConversationSession?>())
            .Returns(call =>
            {
                call[0] = null;
                return false;
            });

        var provider = CreateProvider();
        provider.Update(0.016f);

        Assert.Equal(PersonaUiState.NoSession, provider.State);
    }

    [Fact]
    public void State_PendingTurnWithNoMessages_ReturnsThinking()
    {
        var session = Substitute.For<IConversationSession>();
        var context = Substitute.For<IConversationContext>();

        var pendingTurn = new InteractionTurn(
            Guid.NewGuid(),
            new[] { "assistant" },
            DateTimeOffset.UtcNow,
            null,
            Enumerable.Empty<LibChatMessage>(),
            false
        );

        context.PendingTurn.Returns(pendingTurn);
        session.Context.Returns(context);
        session.CurrentState.Returns(ConversationState.WaitingForLlm);

        StubFirstActiveSession(session);

        var provider = CreateProvider();
        provider.Update(0.016f);

        Assert.Equal(PersonaUiState.Thinking, provider.State);
    }

    [Fact]
    public void State_PendingTurnWithMessages_ReturnsSpeaking()
    {
        var session = Substitute.For<IConversationSession>();
        var context = Substitute.For<IConversationContext>();

        var message = new LibChatMessage(
            Guid.NewGuid(),
            "assistant",
            "Assistant",
            "Hello there",
            DateTimeOffset.UtcNow,
            false,
            ChatMessageRole.Assistant
        );

        var pendingTurn = new InteractionTurn(
            Guid.NewGuid(),
            new[] { "assistant" },
            DateTimeOffset.UtcNow,
            null,
            new[] { message },
            false
        );

        context.PendingTurn.Returns(pendingTurn);
        session.Context.Returns(context);
        session.CurrentState.Returns(ConversationState.Speaking);

        StubFirstActiveSession(session);

        var provider = CreateProvider();
        provider.Update(0.016f);

        Assert.Equal(PersonaUiState.Speaking, provider.State);
    }
}

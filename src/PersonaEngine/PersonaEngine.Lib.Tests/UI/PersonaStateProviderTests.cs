using NSubstitute;
using NSubstitute.ExceptionExtensions;
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

    [Fact]
    public void State_NoSessions_ReturnsNoSession()
    {
        _orchestrator.GetActiveSessionIds().Returns(Enumerable.Empty<Guid>());
        var provider = CreateProvider();

        provider.Update(0.016f);

        Assert.Equal(PersonaUiState.NoSession, provider.State);
    }

    [Fact]
    public void State_SessionWithNoPendingTurn_ReturnsIdle()
    {
        var sessionId = Guid.NewGuid();
        var session = Substitute.For<IConversationSession>();
        var context = Substitute.For<IConversationContext>();
        context.PendingTurn.Returns((InteractionTurn?)null);
        session.Context.Returns(context);

        _orchestrator.GetActiveSessionIds().Returns(new[] { sessionId });
        _orchestrator.GetSession(sessionId).Returns(session);

        var provider = CreateProvider();
        provider.Update(0.016f);

        Assert.Equal(PersonaUiState.Idle, provider.State);
    }

    [Fact]
    public void StateJustChanged_OnTransition_IsTrue()
    {
        var sessionId = Guid.NewGuid();
        var session = Substitute.For<IConversationSession>();
        var context = Substitute.For<IConversationContext>();
        context.PendingTurn.Returns((InteractionTurn?)null);
        session.Context.Returns(context);

        _orchestrator.GetActiveSessionIds().Returns(new[] { sessionId });
        _orchestrator.GetSession(sessionId).Returns(session);

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
        _orchestrator.GetActiveSessionIds().Returns(Enumerable.Empty<Guid>());
        _amplitudeProvider.CurrentAmplitude.Returns(0.75f);

        var provider = CreateProvider();
        provider.Update(0.016f);

        Assert.Equal(0.75f, provider.AudioAmplitude);
    }

    [Fact]
    public void IsAudioPlaying_DelegatesToProvider()
    {
        _orchestrator.GetActiveSessionIds().Returns(Enumerable.Empty<Guid>());
        _amplitudeProvider.IsPlaying.Returns(true);

        var provider = CreateProvider();
        provider.Update(0.016f);

        Assert.True(provider.IsAudioPlaying);
    }

    [Fact]
    public void StateElapsed_Accumulates_WhenStateUnchanged()
    {
        _orchestrator.GetActiveSessionIds().Returns(Enumerable.Empty<Guid>());

        var provider = CreateProvider();
        provider.Update(0.1f); // Initial → NoSession transition, elapsed resets then += 0.1
        provider.Update(0.2f); // Same state, elapsed += 0.2

        Assert.True(
            provider.StateElapsed >= 0.25f,
            $"Expected >= 0.25f, got {provider.StateElapsed}"
        );
    }

    [Fact]
    public void GetSession_Throwing_FallsBackToNoSession()
    {
        var sessionId = Guid.NewGuid();
        _orchestrator.GetActiveSessionIds().Returns(new[] { sessionId });
        _orchestrator.GetSession(sessionId).Throws(new KeyNotFoundException());

        var provider = CreateProvider();
        provider.Update(0.016f);

        Assert.Equal(PersonaUiState.NoSession, provider.State);
    }

    [Fact]
    public void State_PendingTurnWithNoMessages_ReturnsThinking()
    {
        var sessionId = Guid.NewGuid();
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

        _orchestrator.GetActiveSessionIds().Returns(new[] { sessionId });
        _orchestrator.GetSession(sessionId).Returns(session);

        var provider = CreateProvider();
        provider.Update(0.016f);

        Assert.Equal(PersonaUiState.Thinking, provider.State);
    }

    [Fact]
    public void State_PendingTurnWithMessages_ReturnsSpeaking()
    {
        var sessionId = Guid.NewGuid();
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

        _orchestrator.GetActiveSessionIds().Returns(new[] { sessionId });
        _orchestrator.GetSession(sessionId).Returns(session);

        var provider = CreateProvider();
        provider.Update(0.016f);

        Assert.Equal(PersonaUiState.Speaking, provider.State);
    }
}

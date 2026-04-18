namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

/// <summary>
///     Runtime mic-mute controller. Wraps an <see cref="IConversationInputGate" /> scope so
///     that muting the microphone suppresses recognised speech at the conversation-session
///     boundary while leaving the mic / VAD / ASR pipeline running. Scope is released when
///     the user unmutes. Not persisted — always starts unmuted on app launch.
/// </summary>
public interface IMicMuteController
{
    /// <summary><see langword="true" /> while the user has muted the microphone.</summary>
    bool IsMuted { get; }

    /// <summary>
    ///     Idempotently set mute state. Acquires or releases the underlying
    ///     <see cref="IConversationInputGate" /> scope as needed.
    /// </summary>
    void SetMuted(bool muted);

    /// <summary>Fires only on actual transitions. Payload is the new <see cref="IsMuted" /> value.</summary>
    event Action<bool>? MutedChanged;
}

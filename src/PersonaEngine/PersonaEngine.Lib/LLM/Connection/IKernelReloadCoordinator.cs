namespace PersonaEngine.Lib.LLM.Connection;

/// <summary>
///     Bridges conversation-session lifecycle to "is it safe to swap the kernel".
///     Provider has zero knowledge of ConversationSession; coordinator has zero
///     knowledge of kernels.
/// </summary>
public interface IKernelReloadCoordinator
{
    /// <summary>
    ///     Returns <see langword="true" /> when no conversation turn is in-flight and it is safe
    ///     to replace the active <see cref="Microsoft.SemanticKernel.Kernel" /> instance.
    /// </summary>
    bool IsSafeToReloadNow { get; }

    /// <summary>
    ///     Raised when the session transitions to a state that permits a kernel swap
    ///     (i.e. entering <see cref="PersonaEngine.Lib.Core.Conversation.Abstractions.Session.ConversationState.Idle" />).
    ///     Subscribers must be resilient to concurrent or re-entrant calls.
    /// </summary>
    event Action? SafeToReload;
}

using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

namespace PersonaEngine.Lib.LLM.Connection;

/// <summary>
///     Implements <see cref="IKernelReloadCoordinator" /> by observing the aggregated
///     <see cref="IConversationOrchestrator.StateChanged" /> feed and firing
///     <see cref="SafeToReload" /> whenever any session enters
///     <see cref="ConversationState.Idle" />.
///     <para>
///         The coordinator treats <see cref="ConversationState.Initial" /> as the default
///         (safe) state so that a kernel rebuild is never blocked before any session starts.
///         <see cref="IsSafeToReloadNow" /> becomes <see langword="true" /> once
///         <see cref="ConversationState.Idle" /> is reached and remains so until a non-idle
///         state is observed.
///     </para>
/// </summary>
public sealed class KernelReloadCoordinator : IKernelReloadCoordinator, IDisposable
{
    private readonly IConversationOrchestrator _orchestrator;
    private ConversationState _state = ConversationState.Initial;

    /// <summary>
    ///     Initialises the coordinator and subscribes to the orchestrator's
    ///     <see cref="IConversationOrchestrator.StateChanged" /> event.
    /// </summary>
    /// <param name="orchestrator">The orchestrator whose sessions are watched.</param>
    public KernelReloadCoordinator(IConversationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        _orchestrator.StateChanged += OnStateChanged;
    }

    /// <inheritdoc />
    public bool IsSafeToReloadNow => _state == ConversationState.Idle;

    /// <inheritdoc />
    public event Action? SafeToReload;

    /// <inheritdoc />
    public void Dispose() => _orchestrator.StateChanged -= OnStateChanged;

    private void OnStateChanged(ConversationState next)
    {
        var entering = _state != ConversationState.Idle && next == ConversationState.Idle;
        _state = next;
        if (entering)
        {
            SafeToReload?.Invoke();
        }
    }
}

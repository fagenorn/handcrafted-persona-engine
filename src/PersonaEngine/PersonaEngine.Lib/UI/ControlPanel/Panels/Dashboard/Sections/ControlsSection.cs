using Hexa.NET.ImGui;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.UI.ControlPanel.Threading;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

/// <summary>
///     Dashboard row surfacing runtime controls: Cancel (active turn), Retry (error), Mute
///     (toggle). Orchestrator and mute-controller events are marshalled onto the UI thread
///     via <see cref="IUiThreadDispatcher" />.
/// </summary>
public sealed class ControlsSection : IDisposable
{
    private readonly IUiThreadDispatcher _dispatcher;

    private readonly IMicMuteController _muteController;

    private readonly IConversationOrchestrator _orchestrator;

    private bool _disposed;

    private ConversationState _latestState = ConversationState.Initial;

    private bool _muted;

    public ControlsSection(
        IConversationOrchestrator orchestrator,
        IMicMuteController muteController,
        IUiThreadDispatcher dispatcher
    )
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(muteController);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _orchestrator = orchestrator;
        _muteController = muteController;
        _dispatcher = dispatcher;

        _muted = muteController.IsMuted;

        _orchestrator.StateChanged += OnStateChanged;
        _muteController.MutedChanged += OnMutedChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _orchestrator.StateChanged -= OnStateChanged;
        _muteController.MutedChanged -= OnMutedChanged;
    }

    public void Render(float deltaTime)
    {
        _dispatcher.DrainPending();

        ImGuiHelpers.SectionHeader("Controls");

        var cancelEnabled = IsActiveTurn(_latestState);
        var retryEnabled = _latestState == ConversationState.Error;

        RenderButton("Cancel", cancelEnabled, () => _ = _orchestrator.CancelActiveTurnsAsync());
        ImGui.SameLine();
        RenderButton("Retry", retryEnabled, () => _ = _orchestrator.RetryErroredSessionsAsync());
        ImGui.SameLine();
        RenderButton(
            _muted ? "Unmute" : "Mute",
            true,
            () => _muteController.SetMuted(!_muteController.IsMuted)
        );
    }

    private static void RenderButton(string label, bool enabled, Action onClick)
    {
        if (!enabled)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button(label))
        {
            onClick();
        }

        if (!enabled)
        {
            ImGui.EndDisabled();
        }
    }

    private static bool IsActiveTurn(ConversationState state) =>
        state
            is ConversationState.ProcessingInput
                or ConversationState.WaitingForLlm
                or ConversationState.StreamingResponse
                or ConversationState.Speaking;

    private void OnStateChanged(ConversationState state) =>
        _dispatcher.Post(() => _latestState = state);

    private void OnMutedChanged(bool muted) => _dispatcher.Post(() => _muted = muted);
}

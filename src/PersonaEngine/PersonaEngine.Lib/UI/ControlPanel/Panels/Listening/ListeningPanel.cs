using Hexa.NET.ImGui;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Listening.Sections;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Listening;

/// <summary>
///     Orchestrator for the Listening panel. Stacks four sections vertically inside a
///     single scrollable container.
///     <para>
///         While this panel is the active navigation section it holds an
///         <see cref="IConversationInputGate" /> scope closed — the microphone, VAD, and
///         ASR pipelines keep running (so the live meter and Test Microphone remain
///         functional) but recognised-speech events are dropped before reaching the
///         conversation session, so the avatar does not react to calibration utterances.
///     </para>
///     <para>
///         All four child sections hold <c>IOptionsMonitor.OnChange</c> subscriptions,
///         so <see cref="Dispose" /> forwards teardown to them to avoid handler leaks
///         when the control panel is torn down. The activation-lifecycle gate scope in
///         <see cref="OnActivated" /> / <see cref="OnDeactivated" /> is orthogonal to
///         disposal and stays on the <see cref="IActivatablePanel" /> axis.
///     </para>
/// </summary>
public sealed class ListeningPanel : IActivatablePanel, IDisposable
{
    private const string GateReason = "Listening panel active — calibration mode";

    private readonly Sections.MicrophoneDeviceSection _deviceSection;
    private readonly Sections.SpeechDetectionSection _detectionSection;
    private readonly Sections.RecognitionSection _recognitionSection;
    private readonly Sections.InterruptionSection _interruptionSection;
    private readonly IConversationInputGate _inputGate;

    private IDisposable? _gateScope;
    private bool _disposed;

    public ListeningPanel(
        Sections.MicrophoneDeviceSection deviceSection,
        Sections.SpeechDetectionSection detectionSection,
        Sections.RecognitionSection recognitionSection,
        Sections.InterruptionSection interruptionSection,
        IConversationInputGate inputGate
    )
    {
        _deviceSection = deviceSection;
        _detectionSection = detectionSection;
        _recognitionSection = recognitionSection;
        _interruptionSection = interruptionSection;
        _inputGate = inputGate;
    }

    public void OnActivated()
    {
        // Defensive: guarantee we never leak two scopes on this panel. The controller
        // contract guarantees OnDeactivated fires before the next OnActivated, but a
        // misbehaving caller should not produce a leak either.
        _gateScope?.Dispose();
        _gateScope = _inputGate.CloseScope(GateReason);
    }

    public void OnDeactivated()
    {
        _gateScope?.Dispose();
        _gateScope = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _deviceSection.Dispose();
        _detectionSection.Dispose();
        _recognitionSection.Dispose();
        _interruptionSection.Dispose();
    }

    public void Render(float dt)
    {
        using (
            Ui.FillChild(
                "##listening_panel",
                windowFlags: ImGuiWindowFlags.AlwaysVerticalScrollbar,
                padding: 4f
            )
        )
        {
            _deviceSection.Render(dt);
            ImGui.Spacing();
            _detectionSection.Render(dt);
            ImGui.Spacing();
            _recognitionSection.Render(dt);
            ImGui.Spacing();
            _interruptionSection.Render(dt);
        }
    }
}

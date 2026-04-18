using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Sections;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice;

/// <summary>
///     Orchestrator for the Voice panel. Owns nothing beyond its child sections.
///     All sections stack vertically inside a single scrollable container — the whole
///     panel scrolls as one document when content exceeds the available height.
///     <para>
///         <see cref="VoiceModeSelector" />, <see cref="VoiceGallery" />,
///         <see cref="CloneLayerSection" />, and <see cref="AdvancedSection" /> hold
///         <c>IOptionsMonitor.OnChange</c> subscriptions, so <see cref="Dispose" />
///         forwards teardown to them to avoid handler leaks when the control panel
///         is torn down. <see cref="VoiceCard" /> is not disposable.
///     </para>
/// </summary>
public sealed class VoicePanel(
    VoiceModeSelector modeSelector,
    VoiceCard voiceCard,
    VoiceGallery gallery,
    CloneLayerSection cloneLayer,
    AdvancedSection advanced
) : IDisposable
{
    private bool _disposed;

    public void Render(float deltaTime)
    {
        using (Ui.FillChild("##voice_panel", padding: 4f))
        {
            modeSelector.Render(deltaTime);
            var mode = modeSelector.CurrentMode;

            ImGui.Spacing();
            voiceCard.Render(deltaTime, mode);
            ImGui.Spacing();
            gallery.Render(deltaTime, mode);
            ImGui.Spacing();
            cloneLayer.Render(deltaTime, mode);
            ImGui.Spacing();
            advanced.Render(deltaTime, mode);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        modeSelector.Dispose();
        gallery.Dispose();
        cloneLayer.Dispose();
        advanced.Dispose();
    }
}

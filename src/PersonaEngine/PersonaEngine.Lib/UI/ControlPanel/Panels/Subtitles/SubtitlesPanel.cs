using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Subtitles.Sections;
using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Subtitles;

/// <summary>
///     Orchestrator for the Subtitles panel. Owns nothing beyond its child sections.
///     Preview sits at the top so every edit to text style, colors, placement, or
///     canvas updates visibly while the Performer is scrolling.
///     <para>
///         All five child sections are <see cref="IDisposable" />: four hold
///         <c>IOptionsMonitor.OnChange</c> subscriptions, and <see cref="PreviewSection" />
///         additionally owns a <c>SubtitlePreviewRenderer</c> with a GL framebuffer
///         object. <see cref="Dispose" /> forwards teardown to all of them.
///     </para>
/// </summary>
public sealed class SubtitlesPanel(
    PreviewSection preview,
    TextStyleSection textStyle,
    ColorsSection colors,
    PlacementSection placement,
    CanvasSection canvas
) : IDisposable
{
    private bool _disposed;

    public void Initialize(GL gl) => preview.Initialize(gl);

    public void Render(float dt)
    {
        using (Ui.FillChild("##subtitles_panel", padding: 4f))
        {
            preview.Render(dt);
            ImGui.Spacing();
            textStyle.Render(dt);
            ImGui.Spacing();
            colors.Render(dt);
            ImGui.Spacing();
            placement.Render(dt);
            ImGui.Spacing();
            canvas.Render(dt);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        preview.Dispose();
        textStyle.Dispose();
        colors.Dispose();
        placement.Dispose();
        canvas.Dispose();
    }
}

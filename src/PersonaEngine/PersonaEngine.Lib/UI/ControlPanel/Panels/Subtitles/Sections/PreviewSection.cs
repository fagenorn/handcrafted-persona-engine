using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.Rendering.Subtitles;
using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Subtitles.Sections;

/// <summary>
///     Live WYSIWYG preview of subtitle rendering. Delegates all text drawing to
///     <see cref="SubtitlePreviewRenderer" />, which bakes the frame into an
///     offscreen GL FBO using the exact same FontStashSharp pipeline as the live
///     subtitle output. This panel composites the FBO texture into the ImGui draw
///     list and overlays a small font/size caption.
/// </summary>
public sealed class PreviewSection : IDisposable
{
    private const float CanvasPaddingX = 8f;
    private const float CanvasPaddingY = 8f;

    // Fixed canvas height: the preview renders at a constant font size (see
    // SubtitlePreviewRenderer.PreviewFontSize) so we don't scale the canvas to the
    // configured FontSize — that would only mislead about final OBS appearance.
    private const float CanvasHeight = 180f;

    private readonly IDisposable? _changeSubscription;
    private readonly SubtitlePreviewRenderer _previewRenderer;

    private SubtitleOptions _opts;

    public PreviewSection(
        IOptionsMonitor<SubtitleOptions> monitor,
        SubtitlePreviewRenderer previewRenderer
    )
    {
        _previewRenderer = previewRenderer;
        _opts = monitor.CurrentValue;
        _changeSubscription = monitor.OnChange((updated, _) => _opts = updated);
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Initialize(GL gl) => _previewRenderer.Initialize(gl);

    public void Render(float dt)
    {
        using (Ui.Card("##subtitle_preview", padding: 16f))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted("Preview");
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted(
                "Live preview rendered with your chosen TTF, size, colors, and stroke thickness."
            );
            ImGui.PopStyleColor();
            ImGui.Spacing();

            DrawPreviewCanvas();
        }
    }

    private void DrawPreviewCanvas()
    {
        var avail = ImGui.GetContentRegionAvail();
        var canvasSize = new Vector2(avail.X, CanvasHeight);
        var canvasPos = ImGui.GetCursorScreenPos();

        var drawList = ImGui.GetWindowDrawList();

        // Dark backdrop underneath the baked text so stroke and alpha read correctly.
        var backdrop = new Vector4(0.08f, 0.08f, 0.10f, 1f);
        drawList.AddRectFilled(
            canvasPos,
            canvasPos + canvasSize,
            ImGui.ColorConvertFloat4ToU32(backdrop),
            rounding: 6f
        );

        // Bake the preview text into the off-screen FBO. Internal gating makes this a
        // no-op when nothing changed (options unchanged AND size unchanged since last call).
        _previewRenderer.Bake((int)canvasSize.X, (int)canvasSize.Y);

        // Composite the FBO into the ImGui panel. Y is flipped because GL FBOs are Y-up
        // while ImGui is Y-down.
        unsafe
        {
            var texRef = new ImTextureRef(null, new ImTextureID((nuint)_previewRenderer.TextureId));
            ImGui.AddImage(
                drawList,
                texRef,
                canvasPos,
                canvasPos + canvasSize,
                new Vector2(0f, 1f),
                new Vector2(1f, 0f),
                0xFFFFFFFFu
            );
        }

        // Small caption in the corner so the user can confirm the active font + size.
        var caption = $"Font: {StripExt(_opts.Font)} · {_opts.FontSize} pt";
        drawList.AddText(
            canvasPos + new Vector2(CanvasPaddingX, CanvasPaddingY * 0.5f),
            ImGui.ColorConvertFloat4ToU32(Theme.TextSecondary),
            caption
        );

        ImGui.Dummy(canvasSize);
    }

    private static string StripExt(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return "(none)";
        var stem = Path.GetFileNameWithoutExtension(fullName);
        return string.IsNullOrEmpty(stem) ? fullName : stem;
    }
}

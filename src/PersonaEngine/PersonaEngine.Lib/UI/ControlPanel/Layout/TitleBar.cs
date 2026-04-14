using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Custom title bar rendered as the topmost strip of the control panel.
///     Provides app title, minimize/maximize/close buttons, and reports
///     hit-test regions to <see cref="Win32WindowHelper"/> for drag and resize.
/// </summary>
public sealed class TitleBar
{
    public const float Height = 30f;

    private const float ButtonWidth = 46f;
    private const string MinimizeGlyph = "\u2500"; // ─
    private const string MaximizeGlyph = "\u25A1"; // □
    private const string RestoreGlyph = "\u29C9"; // ⧉
    private const string CloseGlyph = "\u2715"; // ✕

    private readonly WindowManager _windowManager;
    private readonly string _title;
    private Win32WindowHelper? _windowHelper;

    public TitleBar(WindowManager windowManager, IOptions<AvatarAppConfig> config)
    {
        _windowManager = windowManager;
        _title = config.Value.Window.Title;
    }

    public void Render(float deltaTime)
    {
        _windowHelper ??= _windowManager.Win32Helper;
        if (_windowHelper is null)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();

        // Bottom border
        var borderY = winPos.Y + winSize.Y - 1f;
        var borderColor = ImGui.ColorConvertFloat4ToU32(Theme.SurfaceBorder);
        ImGui.AddLine(
            drawList,
            new Vector2(winPos.X, borderY),
            new Vector2(winPos.X + winSize.X, borderY),
            borderColor
        );

        // Window control buttons — right-aligned, rendered first so
        // the invisible buttons are laid out before the title text
        var buttonsStartX = winSize.X - ButtonWidth * 3f;

        // Right-click on drag area shows system menu
        ImGui.SetCursorPos(Vector2.Zero);
        ImGui.InvisibleButton("##titlebar_drag", new Vector2(buttonsStartX, winSize.Y));
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            var mousePos = ImGui.GetMousePos();
            _windowHelper.ShowSystemMenu((int)mousePos.X, (int)mousePos.Y);
        }

        RenderWindowButton(
            drawList,
            MinimizeGlyph,
            buttonsStartX,
            winPos.Y,
            winSize.Y,
            Theme.SurfaceHover,
            Theme.TextTertiary,
            Theme.TextPrimary,
            () => _windowHelper.Minimize()
        );

        RenderWindowButton(
            drawList,
            _windowHelper.IsMaximized ? RestoreGlyph : MaximizeGlyph,
            buttonsStartX + ButtonWidth,
            winPos.Y,
            winSize.Y,
            Theme.SurfaceHover,
            Theme.TextTertiary,
            Theme.TextPrimary,
            () => _windowHelper.ToggleMaximize()
        );

        RenderWindowButton(
            drawList,
            CloseGlyph,
            buttonsStartX + ButtonWidth * 2f,
            winPos.Y,
            winSize.Y,
            Theme.Error,
            Theme.TextTertiary,
            Theme.TextPrimary,
            () => _windowHelper.Close()
        );

        // App title — vertically centered, rendered via draw list so it overlays
        // the invisible drag button
        var textH = ImGui.GetTextLineHeight();
        var titleY = winPos.Y + (winSize.Y - textH) * 0.5f;
        var titleColor = ImGui.ColorConvertFloat4ToU32(Theme.TextSecondary);
        ImGui.AddText(drawList, new Vector2(winPos.X + 12f, titleY), titleColor, _title);

        // Update hit-test regions for Win32WindowHelper.
        // All values are window-relative pixels — no DPI conversion needed
        // because GLFW creates DPI-aware windows on Windows 10+.
        _windowHelper.UpdateTitleBarRegion(winSize.Y, buttonsStartX, winSize.X);
    }

    private static void RenderWindowButton(
        ImDrawListPtr drawList,
        string glyph,
        float x,
        float winY,
        float height,
        Vector4 hoverBg,
        Vector4 normalColor,
        Vector4 hoverColor,
        Action onClick
    )
    {
        // Invisible button for interaction — cursor-space (window-relative)
        ImGui.SetCursorPos(new Vector2(x, 0f));
        ImGui.InvisibleButton($"##wb_{glyph}", new Vector2(ButtonWidth, height));

        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        if (hovered)
            ImGuiHelpers.HandCursorOnHover();

        if (ImGui.IsItemClicked())
            onClick();

        // Background highlight on hover — screen-space for draw list
        if (hovered || active)
        {
            var bgAlpha = active ? 0.9f : 0.7f;
            var bgCol = ImGui.ColorConvertFloat4ToU32(hoverBg with { W = bgAlpha });
            var min = new Vector2(ImGui.GetItemRectMin().X, winY);
            var max = new Vector2(ImGui.GetItemRectMax().X, winY + height);
            ImGui.AddRectFilled(drawList, min, max, bgCol);
        }

        // Glyph centered in button — screen-space for draw list
        var textSize = ImGui.CalcTextSize(glyph);
        var itemMin = ImGui.GetItemRectMin();
        var textPos = new Vector2(
            itemMin.X + (ButtonWidth - textSize.X) * 0.5f,
            winY + (height - textSize.Y) * 0.5f
        );
        var textCol = ImGui.ColorConvertFloat4ToU32(hovered ? hoverColor : normalColor);
        ImGui.AddText(drawList, textPos, textCol, glyph);
    }
}

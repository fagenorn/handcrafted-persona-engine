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

        RenderWindowButton(
            drawList,
            "##wb_minimize",
            DrawMinimizeIcon,
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
            "##wb_maximize",
            _windowHelper.IsMaximized ? DrawRestoreIcon : DrawMaximizeIcon,
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
            "##wb_close",
            DrawCloseIcon,
            buttonsStartX + ButtonWidth * 2f,
            winPos.Y,
            winSize.Y,
            Theme.Error,
            Theme.TextTertiary,
            Theme.TextPrimary,
            () => _windowHelper.Close()
        );

        // App title — horizontally centered in the drag area, vertically centered
        var textH = ImGui.GetTextLineHeight();
        var titleY = winPos.Y + (winSize.Y - textH) * 0.5f;
        var titleColor = ImGui.ColorConvertFloat4ToU32(Theme.TextSecondary);
        var titleSize = ImGui.CalcTextSize(_title);
        var titleX = winPos.X + (buttonsStartX - titleSize.X) * 0.5f;
        ImGui.AddText(drawList, new Vector2(titleX, titleY), titleColor, _title);

        // Update hit-test regions for Win32WindowHelper.
        // All values are window-relative pixels — no DPI conversion needed
        // because GLFW creates DPI-aware windows on Windows 10+.
        _windowHelper.UpdateTitleBarRegion(winSize.Y, buttonsStartX, winSize.X);
    }

    private static void RenderWindowButton(
        ImDrawListPtr drawList,
        string id,
        Action<ImDrawListPtr, Vector2, uint> drawIcon,
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
        ImGui.InvisibleButton(id, new Vector2(ButtonWidth, height));

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

        // Icon centered in button — screen-space for draw list
        var itemMin = ImGui.GetItemRectMin();
        var center = new Vector2(itemMin.X + ButtonWidth * 0.5f, winY + height * 0.5f);
        var iconCol = ImGui.ColorConvertFloat4ToU32(hovered ? hoverColor : normalColor);
        drawIcon(drawList, center, iconCol);
    }

    private static void DrawMinimizeIcon(ImDrawListPtr dl, Vector2 center, uint color)
    {
        const float halfW = 5f;
        dl.AddLine(
            new Vector2(center.X - halfW, center.Y),
            new Vector2(center.X + halfW, center.Y),
            color,
            1.2f
        );
    }

    private static void DrawMaximizeIcon(ImDrawListPtr dl, Vector2 center, uint color)
    {
        const float halfSz = 5f;
        ImGui.AddRect(
            dl,
            new Vector2(center.X - halfSz, center.Y - halfSz),
            new Vector2(center.X + halfSz, center.Y + halfSz),
            color,
            0f,
            0,
            1.2f
        );
    }

    private static void DrawRestoreIcon(ImDrawListPtr dl, Vector2 center, uint color)
    {
        const float sz = 8f;
        const float offset = 2f;
        const float halfSz = sz * 0.5f;

        // Back (upper-right) rectangle
        ImGui.AddRect(
            dl,
            new Vector2(center.X - halfSz + offset, center.Y - halfSz - offset),
            new Vector2(center.X + halfSz + offset, center.Y + halfSz - offset),
            color,
            0f,
            0,
            1.2f
        );

        // Fill behind front rectangle to cover overlapping back rect edges
        ImGui.AddRectFilled(
            dl,
            new Vector2(center.X - halfSz - 1f, center.Y - halfSz + offset - 1f),
            new Vector2(center.X + halfSz - offset + 1f, center.Y + halfSz + 1f),
            ImGui.ColorConvertFloat4ToU32(Theme.TitleBarBg)
        );

        // Front (lower-left) rectangle
        ImGui.AddRect(
            dl,
            new Vector2(center.X - halfSz, center.Y - halfSz + offset),
            new Vector2(center.X + halfSz - offset, center.Y + halfSz),
            color,
            0f,
            0,
            1.2f
        );
    }

    private static void DrawCloseIcon(ImDrawListPtr dl, Vector2 center, uint color)
    {
        const float halfSz = 5f;
        dl.AddLine(
            new Vector2(center.X - halfSz, center.Y - halfSz),
            new Vector2(center.X + halfSz, center.Y + halfSz),
            color,
            1.2f
        );
        dl.AddLine(
            new Vector2(center.X + halfSz, center.Y - halfSz),
            new Vector2(center.X - halfSz, center.Y + halfSz),
            color,
            1.2f
        );
    }
}

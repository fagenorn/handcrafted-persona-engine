using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     Warm purple-pink color palette and ImGui style configuration for the control panel.
/// </summary>
public static class Theme
{
    // ── Backgrounds ─────────────────────────────────────────────────────────────
    public static readonly Vector4 Background = ColorFromHex(0x1E1A2E);
    public static readonly Vector4 Surface = ColorFromHex(0x2A2440);
    public static readonly Vector4 SurfaceHover = ColorFromHex(0x352E4D);
    public static readonly Vector4 ActiveSelected = ColorFromHex(0x3D3558);
    public static readonly Vector4 InputBackground = ColorFromHex(0x231E36);
    public static readonly Vector4 SidebarBackground = ColorFromHex(0x1A1628);

    // ── Accents ──────────────────────────────────────────────────────────────────
    public static readonly Vector4 AccentPrimary = ColorFromHex(0xE8A0BF); // soft pink
    public static readonly Vector4 AccentSecondary = ColorFromHex(0xB68FD0); // lavender

    // ── Text ─────────────────────────────────────────────────────────────────────
    public static readonly Vector4 TextPrimary = ColorFromHex(0xF0E6F6);
    public static readonly Vector4 TextSecondary = ColorFromHex(0x9B8FB0);

    // ── Semantic ─────────────────────────────────────────────────────────────────
    public static readonly Vector4 Success = ColorFromHex(0x7EC8A0);
    public static readonly Vector4 Warning = ColorFromHex(0xE8C170);
    public static readonly Vector4 Error = ColorFromHex(0xE07070);

    /// <summary>Applies the theme to the current ImGui style.</summary>
    public static void Apply()
    {
        var style = ImGui.GetStyle();

        // ── Rounding ──────────────────────────────────────────────────────────
        style.WindowRounding = 8f;
        style.FrameRounding = 6f;
        style.ChildRounding = 6f;
        style.PopupRounding = 6f;
        style.ScrollbarRounding = 12f;
        style.GrabRounding = 6f;
        style.TabRounding = 6f;

        // ── Spacing ───────────────────────────────────────────────────────────
        style.WindowPadding = new Vector2(16f, 16f);
        style.FramePadding = new Vector2(8f, 4f);
        style.ItemSpacing = new Vector2(10f, 8f);
        style.ItemInnerSpacing = new Vector2(6f, 4f);
        style.ScrollbarSize = 12f;
        style.GrabMinSize = 10f;

        // ── Borders ───────────────────────────────────────────────────────────
        style.WindowBorderSize = 0f;
        style.ChildBorderSize = 1f;
        style.FrameBorderSize = 1f;

        // ── Disabled ──────────────────────────────────────────────────────────
        style.DisabledAlpha = 0.35f;

        // ── Colors ────────────────────────────────────────────────────────────
        var c = style.Colors;

        c[(int)ImGuiCol.WindowBg] = Background;
        c[(int)ImGuiCol.ChildBg] = Surface with { W = 0.4f };
        c[(int)ImGuiCol.PopupBg] = Surface;

        c[(int)ImGuiCol.FrameBg] = InputBackground;
        c[(int)ImGuiCol.FrameBgHovered] = SurfaceHover;
        c[(int)ImGuiCol.FrameBgActive] = ActiveSelected;

        c[(int)ImGuiCol.TitleBg] = SidebarBackground;
        c[(int)ImGuiCol.TitleBgActive] = Background;
        c[(int)ImGuiCol.TitleBgCollapsed] = SidebarBackground with { W = 0.6f };

        c[(int)ImGuiCol.MenuBarBg] = SidebarBackground;

        c[(int)ImGuiCol.ScrollbarBg] = Background with { W = 0f };
        c[(int)ImGuiCol.ScrollbarGrab] = Surface;
        c[(int)ImGuiCol.ScrollbarGrabHovered] = SurfaceHover;
        c[(int)ImGuiCol.ScrollbarGrabActive] = ActiveSelected;

        c[(int)ImGuiCol.CheckMark] = AccentPrimary;
        c[(int)ImGuiCol.SliderGrab] = AccentSecondary;
        c[(int)ImGuiCol.SliderGrabActive] = AccentPrimary;

        c[(int)ImGuiCol.Button] = Surface;
        c[(int)ImGuiCol.ButtonHovered] = SurfaceHover;
        c[(int)ImGuiCol.ButtonActive] = ActiveSelected;

        c[(int)ImGuiCol.Header] = Surface;
        c[(int)ImGuiCol.HeaderHovered] = SurfaceHover;
        c[(int)ImGuiCol.HeaderActive] = ActiveSelected;

        c[(int)ImGuiCol.Separator] = SurfaceHover;
        c[(int)ImGuiCol.SeparatorHovered] = AccentSecondary with { W = 0.6f };
        c[(int)ImGuiCol.SeparatorActive] = AccentPrimary;

        c[(int)ImGuiCol.ResizeGrip] = Surface;
        c[(int)ImGuiCol.ResizeGripHovered] = AccentSecondary with { W = 0.6f };
        c[(int)ImGuiCol.ResizeGripActive] = AccentPrimary;

        c[(int)ImGuiCol.Tab] = Surface;
        c[(int)ImGuiCol.TabHovered] = SurfaceHover;
        c[(int)ImGuiCol.TabSelected] = ActiveSelected;
        c[(int)ImGuiCol.TabDimmed] = Background;
        c[(int)ImGuiCol.TabDimmedSelected] = Surface;

        c[(int)ImGuiCol.Text] = TextPrimary;
        c[(int)ImGuiCol.TextDisabled] = TextSecondary;
        c[(int)ImGuiCol.TextSelectedBg] = AccentSecondary with { W = 0.4f };

        c[(int)ImGuiCol.Border] = SurfaceHover with { W = 0.5f };
        c[(int)ImGuiCol.BorderShadow] = Vector4.Zero;

        c[(int)ImGuiCol.PlotLines] = AccentSecondary;
        c[(int)ImGuiCol.PlotLinesHovered] = AccentPrimary;
        c[(int)ImGuiCol.PlotHistogram] = AccentSecondary;
        c[(int)ImGuiCol.PlotHistogramHovered] = AccentPrimary;

        c[(int)ImGuiCol.TableHeaderBg] = SidebarBackground;
        c[(int)ImGuiCol.TableBorderStrong] = SurfaceHover;
        c[(int)ImGuiCol.TableBorderLight] = Surface;
        c[(int)ImGuiCol.TableRowBg] = Vector4.Zero;
        c[(int)ImGuiCol.TableRowBgAlt] = Surface with { W = 0.05f };

        c[(int)ImGuiCol.DragDropTarget] = Warning with { W = 0.9f };
        c[(int)ImGuiCol.NavWindowingHighlight] = TextPrimary with { W = 0.7f };
        c[(int)ImGuiCol.NavWindowingDimBg] = Background with { W = 0.2f };
        c[(int)ImGuiCol.ModalWindowDimBg] = Background with { W = 0.35f };
    }

    /// <summary>Converts a 0xRRGGBB hex literal to a normalized <see cref="Vector4"/> (alpha = 1).</summary>
    private static Vector4 ColorFromHex(uint hex) =>
        new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, 1f);
}

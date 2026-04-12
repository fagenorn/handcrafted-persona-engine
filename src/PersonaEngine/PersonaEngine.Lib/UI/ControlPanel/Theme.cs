using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     Warm purple-pink color palette and ImGui style configuration for the control panel.
/// </summary>
public static class Theme
{
    // ── Backgrounds ─────────────────────────────────────────────────────────────
    public static readonly Vector4 Base = ColorFromHex(0x16121F);
    public static readonly Vector4 Surface1 = ColorFromHex(0x1E1A2E);
    public static readonly Vector4 Surface2 = ColorFromHex(0x262040);
    public static readonly Vector4 Surface3 = ColorFromHex(0x2E2848);
    public static readonly Vector4 SurfaceHover = ColorFromHex(0x3A3358);
    public static readonly Vector4 ActiveSelected = ColorFromHex(0x443C65);
    public static readonly Vector4 SurfaceBorder = ColorFromHex(0x2D2845);

    // ── Accents ──────────────────────────────────────────────────────────────────
    public static readonly Vector4 AccentPrimary = ColorFromHex(0xE8A0BF); // soft pink
    public static readonly Vector4 AccentSecondary = ColorFromHex(0xB68FD0); // lavender

    // ── Text ─────────────────────────────────────────────────────────────────────
    public static readonly Vector4 TextPrimary = ColorFromHex(0xF0E6F6);
    public static readonly Vector4 TextSecondary = ColorFromHex(0xA89CC0);
    public static readonly Vector4 TextTertiary = ColorFromHex(0x6E6485);

    // ── Semantic ─────────────────────────────────────────────────────────────────
    public static readonly Vector4 Success = ColorFromHex(0x82D4A8);
    public static readonly Vector4 Warning = ColorFromHex(0xF0CA78);
    public static readonly Vector4 Error = ColorFromHex(0xE87878);

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
        style.WindowPadding = new Vector2(20f, 20f);
        style.FramePadding = new Vector2(10f, 6f);
        style.ItemSpacing = new Vector2(12f, 10f);
        style.ItemInnerSpacing = new Vector2(8f, 6f);
        style.ScrollbarSize = 14f;
        style.GrabMinSize = 10f;

        // ── Borders ───────────────────────────────────────────────────────────
        style.WindowBorderSize = 0f;
        style.ChildBorderSize = 1f;
        style.FrameBorderSize = 1f;

        // ── Disabled ──────────────────────────────────────────────────────────
        style.DisabledAlpha = 0.35f;

        // ── Colors ────────────────────────────────────────────────────────────
        var c = style.Colors;

        c[(int)ImGuiCol.WindowBg] = Base;
        c[(int)ImGuiCol.ChildBg] = Surface2 with { W = 0.4f };
        c[(int)ImGuiCol.PopupBg] = Surface2;

        c[(int)ImGuiCol.FrameBg] = Surface3;
        c[(int)ImGuiCol.FrameBgHovered] = SurfaceHover;
        c[(int)ImGuiCol.FrameBgActive] = ActiveSelected;

        c[(int)ImGuiCol.TitleBg] = Surface1;
        c[(int)ImGuiCol.TitleBgActive] = Base;
        c[(int)ImGuiCol.TitleBgCollapsed] = Surface1 with { W = 0.6f };

        c[(int)ImGuiCol.MenuBarBg] = Surface1;

        c[(int)ImGuiCol.ScrollbarBg] = Base with { W = 0f };
        c[(int)ImGuiCol.ScrollbarGrab] = SurfaceHover;
        c[(int)ImGuiCol.ScrollbarGrabHovered] = AccentSecondary with { W = 0.3f };
        c[(int)ImGuiCol.ScrollbarGrabActive] = ActiveSelected;

        c[(int)ImGuiCol.CheckMark] = AccentPrimary;
        c[(int)ImGuiCol.SliderGrab] = AccentSecondary;
        c[(int)ImGuiCol.SliderGrabActive] = AccentPrimary;

        c[(int)ImGuiCol.Button] = Surface2;
        c[(int)ImGuiCol.ButtonHovered] = SurfaceHover;
        c[(int)ImGuiCol.ButtonActive] = ActiveSelected;

        c[(int)ImGuiCol.Header] = Surface2;
        c[(int)ImGuiCol.HeaderHovered] = SurfaceHover;
        c[(int)ImGuiCol.HeaderActive] = ActiveSelected;

        c[(int)ImGuiCol.Separator] = SurfaceBorder;
        c[(int)ImGuiCol.SeparatorHovered] = AccentSecondary with { W = 0.6f };
        c[(int)ImGuiCol.SeparatorActive] = AccentPrimary;

        c[(int)ImGuiCol.ResizeGrip] = Surface2;
        c[(int)ImGuiCol.ResizeGripHovered] = AccentSecondary with { W = 0.6f };
        c[(int)ImGuiCol.ResizeGripActive] = AccentPrimary;

        c[(int)ImGuiCol.Tab] = Surface2;
        c[(int)ImGuiCol.TabHovered] = SurfaceHover;
        c[(int)ImGuiCol.TabSelected] = ActiveSelected;
        c[(int)ImGuiCol.TabDimmed] = Base;
        c[(int)ImGuiCol.TabDimmedSelected] = Surface2;

        c[(int)ImGuiCol.Text] = TextPrimary;
        c[(int)ImGuiCol.TextDisabled] = TextSecondary;
        c[(int)ImGuiCol.TextSelectedBg] = AccentSecondary with { W = 0.4f };

        c[(int)ImGuiCol.Border] = SurfaceBorder;
        c[(int)ImGuiCol.BorderShadow] = Vector4.Zero;

        c[(int)ImGuiCol.PlotLines] = AccentSecondary;
        c[(int)ImGuiCol.PlotLinesHovered] = AccentPrimary;
        c[(int)ImGuiCol.PlotHistogram] = AccentSecondary;
        c[(int)ImGuiCol.PlotHistogramHovered] = AccentPrimary;

        c[(int)ImGuiCol.TableHeaderBg] = Surface1;
        c[(int)ImGuiCol.TableBorderStrong] = SurfaceBorder;
        c[(int)ImGuiCol.TableBorderLight] = SurfaceBorder with { W = 0.5f };
        c[(int)ImGuiCol.TableRowBg] = Vector4.Zero;
        c[(int)ImGuiCol.TableRowBgAlt] = Surface2 with { W = 0.05f };

        c[(int)ImGuiCol.DragDropTarget] = Warning with { W = 0.9f };
        c[(int)ImGuiCol.NavWindowingHighlight] = TextPrimary with { W = 0.7f };
        c[(int)ImGuiCol.NavWindowingDimBg] = Base with { W = 0.2f };
        c[(int)ImGuiCol.ModalWindowDimBg] = Base with { W = 0.35f };
    }

    /// <summary>Converts a 0xRRGGBB hex literal to a normalized <see cref="Vector4"/> (alpha = 1).</summary>
    private static Vector4 ColorFromHex(uint hex) =>
        new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, 1f);
}

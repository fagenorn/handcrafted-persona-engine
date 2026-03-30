using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Implements UI theming management
/// </summary>
public class UiThemeManager : IUiThemeManager
{
    private UiTheme _currentTheme = new();

    public void ApplyTheme()
    {
        var style = ImGui.GetStyle();

        style.Colors[(int)ImGuiCol.Text] = _currentTheme.TextColor;
        style.Colors[(int)ImGuiCol.TextDisabled] = _currentTheme.TextDisabledColor;
        style.Colors[(int)ImGuiCol.WindowBg] = _currentTheme.WindowBgColor;
        style.Colors[(int)ImGuiCol.ChildBg] = _currentTheme.ChildBgColor;
        style.Colors[(int)ImGuiCol.PopupBg] = _currentTheme.PopupBgColor;
        style.Colors[(int)ImGuiCol.Border] = _currentTheme.BorderColor;
        style.Colors[(int)ImGuiCol.BorderShadow] = _currentTheme.BorderShadowColor;
        style.Colors[(int)ImGuiCol.FrameBg] = _currentTheme.FrameBgColor;
        style.Colors[(int)ImGuiCol.FrameBgHovered] = _currentTheme.FrameBgHoveredColor;
        style.Colors[(int)ImGuiCol.FrameBgActive] = _currentTheme.FrameBgActiveColor;
        style.Colors[(int)ImGuiCol.TitleBg] = _currentTheme.TitleBgColor;
        style.Colors[(int)ImGuiCol.TitleBgActive] = _currentTheme.TitleBgActiveColor;
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = _currentTheme.TitleBgCollapsedColor;
        style.Colors[(int)ImGuiCol.MenuBarBg] = _currentTheme.MenuBarBgColor;
        style.Colors[(int)ImGuiCol.ScrollbarBg] = _currentTheme.ScrollbarBgColor;
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = _currentTheme.ScrollbarGrabColor;
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = _currentTheme.ScrollbarGrabHoveredColor;
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = _currentTheme.ScrollbarGrabActiveColor;
        style.Colors[(int)ImGuiCol.CheckMark] = _currentTheme.CheckMarkColor;
        style.Colors[(int)ImGuiCol.SliderGrab] = _currentTheme.SliderGrabColor;
        style.Colors[(int)ImGuiCol.SliderGrabActive] = _currentTheme.SliderGrabActiveColor;
        style.Colors[(int)ImGuiCol.Button] = _currentTheme.ButtonColor;
        style.Colors[(int)ImGuiCol.ButtonHovered] = _currentTheme.ButtonHoveredColor;
        style.Colors[(int)ImGuiCol.ButtonActive] = _currentTheme.ButtonActiveColor;
        style.Colors[(int)ImGuiCol.Header] = _currentTheme.HeaderColor;
        style.Colors[(int)ImGuiCol.HeaderHovered] = _currentTheme.HeaderHoveredColor;
        style.Colors[(int)ImGuiCol.HeaderActive] = _currentTheme.HeaderActiveColor;
        style.Colors[(int)ImGuiCol.Separator] = _currentTheme.SeparatorColor;
        style.Colors[(int)ImGuiCol.SeparatorHovered] = _currentTheme.SeparatorHoveredColor;
        style.Colors[(int)ImGuiCol.SeparatorActive] = _currentTheme.SeparatorActiveColor;
        style.Colors[(int)ImGuiCol.ResizeGrip] = _currentTheme.ResizeGripColor;
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = _currentTheme.ResizeGripHoveredColor;
        style.Colors[(int)ImGuiCol.ResizeGripActive] = _currentTheme.ResizeGripActiveColor;
        style.Colors[(int)ImGuiCol.Tab] = _currentTheme.TabColor;
        style.Colors[(int)ImGuiCol.TabHovered] = _currentTheme.TabHoveredColor;
        style.Colors[(int)ImGuiCol.TabSelected] = _currentTheme.TabActiveColor;
        style.Colors[(int)ImGuiCol.TabDimmed] = _currentTheme.TabUnfocusedColor;
        style.Colors[(int)ImGuiCol.TabDimmedSelected] = _currentTheme.TabUnfocusedActiveColor;
        style.Colors[(int)ImGuiCol.PlotLines] = _currentTheme.PlotLinesColor;
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = _currentTheme.PlotLinesHoveredColor;
        style.Colors[(int)ImGuiCol.PlotHistogram] = _currentTheme.PlotHistogramColor;
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = _currentTheme.PlotHistogramHoveredColor;
        style.Colors[(int)ImGuiCol.TableHeaderBg] = _currentTheme.TableHeaderBgColor;
        style.Colors[(int)ImGuiCol.TableBorderStrong] = _currentTheme.TableBorderStrongColor;
        style.Colors[(int)ImGuiCol.TableBorderLight] = _currentTheme.TableBorderLightColor;
        style.Colors[(int)ImGuiCol.TableRowBg] = _currentTheme.TableRowBgColor;
        style.Colors[(int)ImGuiCol.TableRowBgAlt] = _currentTheme.TableRowBgAltColor;
        style.Colors[(int)ImGuiCol.TextSelectedBg] = _currentTheme.TextSelectedBgColor;
        style.Colors[(int)ImGuiCol.DragDropTarget] = _currentTheme.DragDropTargetColor;
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] =
            _currentTheme.NavWindowingHighlightColor;
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = _currentTheme.NavWindowingDimBgColor;
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = _currentTheme.ModalWindowDimBgColor;

        // Set style properties
        style.Alpha = _currentTheme.Alpha;
        style.DisabledAlpha = _currentTheme.DisabledAlpha;
        style.WindowRounding = _currentTheme.WindowRounding;
        style.WindowBorderSize = _currentTheme.WindowBorderSize;
        style.WindowMinSize = _currentTheme.WindowMinSize;
        style.WindowTitleAlign = _currentTheme.WindowTitleAlign;

        // Handle WindowMenuButtonPosition
        if (_currentTheme.WindowMenuButtonPosition == "Left")
        {
            style.WindowMenuButtonPosition = ImGuiDir.Left;
        }
        else if (_currentTheme.WindowMenuButtonPosition == "Right")
        {
            style.WindowMenuButtonPosition = ImGuiDir.Right;
        }

        style.ChildRounding = _currentTheme.ChildRounding;
        style.ChildBorderSize = _currentTheme.ChildBorderSize;
        style.PopupRounding = _currentTheme.PopupRounding;
        style.PopupBorderSize = _currentTheme.PopupBorderSize;
        style.FrameRounding = _currentTheme.FrameRounding;
        style.FrameBorderSize = _currentTheme.FrameBorderSize;
        style.ItemSpacing = _currentTheme.ItemSpacing;
        style.ItemInnerSpacing = _currentTheme.ItemInnerSpacing;
        style.CellPadding = _currentTheme.CellPadding;
        style.IndentSpacing = _currentTheme.IndentSpacing;
        style.ColumnsMinSpacing = _currentTheme.ColumnsMinSpacing;
        style.ScrollbarSize = _currentTheme.ScrollbarSize;
        style.ScrollbarRounding = _currentTheme.ScrollbarRounding;
        style.GrabMinSize = _currentTheme.GrabMinSize;
        style.GrabRounding = _currentTheme.GrabRounding;
        style.TabRounding = _currentTheme.TabRounding;
        style.TabBorderSize = _currentTheme.TabBorderSize;
        style.TabCloseButtonMinWidthSelected = _currentTheme.TabCloseButtonMinWidthSelected;
        style.TabCloseButtonMinWidthUnselected = _currentTheme.TabCloseButtonMinWidthUnselected;

        // Handle ColorButtonPosition
        if (_currentTheme.ColorButtonPosition == "Left")
        {
            style.ColorButtonPosition = ImGuiDir.Left;
        }
        else if (_currentTheme.ColorButtonPosition == "Right")
        {
            style.ColorButtonPosition = ImGuiDir.Right;
        }

        style.ButtonTextAlign = _currentTheme.ButtonTextAlign;
        style.SelectableTextAlign = _currentTheme.SelectableTextAlign;
        style.WindowPadding = _currentTheme.WindowPadding;
        style.FramePadding = _currentTheme.FramePadding;
    }

    public void SetTheme(UiTheme theme)
    {
        _currentTheme = theme;
        ApplyTheme();
    }

    public UiTheme GetCurrentTheme()
    {
        return _currentTheme;
    }
}

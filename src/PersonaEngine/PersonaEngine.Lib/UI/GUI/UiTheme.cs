using System.Numerics;

namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     UI theme configuration data
/// </summary>
public class UiTheme
{
    public Vector4 TextColor { get; set; } = new(1.0f, 1.0f, 1.0f, 1.0f);

    public float WindowRounding { get; set; } = 10.0f;

    public float FrameRounding { get; set; } = 5.0f;

    public Vector2 WindowPadding { get; set; } = new(8.0f, 8.0f);

    public Vector2 FramePadding { get; set; } = new(5.0f, 3.5f);

    public Vector2 ItemSpacing { get; set; } = new(5.0f, 4.0f);

    // All style properties from TOML
    public float Alpha { get; set; } = 1.0f;

    public float DisabledAlpha { get; set; } = 0.1000000014901161f;

    public float WindowBorderSize { get; set; } = 0.0f;

    public Vector2 WindowMinSize { get; set; } = new(30.0f, 30.0f);

    public Vector2 WindowTitleAlign { get; set; } = new(0.5f, 0.5f);

    public string WindowMenuButtonPosition { get; set; } = "Right";

    public float ChildRounding { get; set; } = 5.0f;

    public float ChildBorderSize { get; set; } = 1.0f;

    public float PopupRounding { get; set; } = 10.0f;

    public float PopupBorderSize { get; set; } = 0.0f;

    public float FrameBorderSize { get; set; } = 0.0f;

    public Vector2 ItemInnerSpacing { get; set; } = new(5.0f, 5.0f);

    public Vector2 CellPadding { get; set; } = new(4.0f, 2.0f);

    public float IndentSpacing { get; set; } = 5.0f;

    public float ColumnsMinSpacing { get; set; } = 5.0f;

    public float ScrollbarSize { get; set; } = 15.0f;

    public float ScrollbarRounding { get; set; } = 9.0f;

    public float GrabMinSize { get; set; } = 15.0f;

    public float GrabRounding { get; set; } = 5.0f;

    public float TabRounding { get; set; } = 5.0f;

    public float TabBorderSize { get; set; } = 0.0f;

    public float TabCloseButtonMinWidthSelected { get; set; } = 0.0f;

    public float TabCloseButtonMinWidthUnselected { get; set; } = 0.0f;

    public string ColorButtonPosition { get; set; } = "Right";

    public Vector2 ButtonTextAlign { get; set; } = new(0.5f, 0.5f);

    public Vector2 SelectableTextAlign { get; set; } = new(0.0f, 0.0f);

    // All colors from TOML
    public Vector4 TextDisabledColor { get; set; } = new(1.0f, 1.0f, 1.0f, 0.3605149984359741f);

    public Vector4 WindowBgColor { get; set; } = new(0.098f, 0.098f, 0.098f, 1.0f);

    public Vector4 ChildBgColor { get; set; } = new(1.0f, 0.0f, 0.0f, 0.0f);

    public Vector4 PopupBgColor { get; set; } = new(0.098f, 0.098f, 0.098f, 1.0f);

    public Vector4 BorderColor { get; set; } = new(0.424f, 0.380f, 0.573f, 0.54935622215271f);

    public Vector4 BorderShadowColor { get; set; } = new(0.0f, 0.0f, 0.0f, 0.0f);

    public Vector4 FrameBgColor { get; set; } = new(0.157f, 0.157f, 0.157f, 1.0f);

    public Vector4 FrameBgHoveredColor { get; set; } =
        new(0.380f, 0.424f, 0.573f, 0.5490196347236633f);

    public Vector4 FrameBgActiveColor { get; set; } =
        new(0.620f, 0.576f, 0.769f, 0.5490196347236633f);

    public Vector4 TitleBgColor { get; set; } = new(0.098f, 0.098f, 0.098f, 1.0f);

    public Vector4 TitleBgActiveColor { get; set; } = new(0.098f, 0.098f, 0.098f, 1.0f);

    public Vector4 TitleBgCollapsedColor { get; set; } = new(0.259f, 0.259f, 0.259f, 0.0f);

    public Vector4 MenuBarBgColor { get; set; } = new(0.0f, 0.0f, 0.0f, 0.0f);

    public Vector4 ScrollbarBgColor { get; set; } = new(0.157f, 0.157f, 0.157f, 0.0f);

    public Vector4 ScrollbarGrabColor { get; set; } = new(0.157f, 0.157f, 0.157f, 1.0f);

    public Vector4 ScrollbarGrabHoveredColor { get; set; } = new(0.235f, 0.235f, 0.235f, 1.0f);

    public Vector4 ScrollbarGrabActiveColor { get; set; } = new(0.294f, 0.294f, 0.294f, 1.0f);

    public Vector4 CheckMarkColor { get; set; } = new(0.294f, 0.294f, 0.294f, 1.0f);

    public Vector4 SliderGrabColor { get; set; } = new(0.620f, 0.576f, 0.769f, 0.5490196347236633f);

    public Vector4 SliderGrabActiveColor { get; set; } =
        new(0.816f, 0.773f, 0.965f, 0.5490196347236633f);

    public Vector4 ButtonColor { get; set; } = new(0.620f, 0.576f, 0.769f, 0.5490196347236633f);

    public Vector4 ButtonHoveredColor { get; set; } =
        new(0.737f, 0.694f, 0.886f, 0.5490196347236633f);

    public Vector4 ButtonActiveColor { get; set; } =
        new(0.816f, 0.773f, 0.965f, 0.5490196347236633f);

    public Vector4 HeaderColor { get; set; } = new(0.620f, 0.576f, 0.769f, 0.5490196347236633f);

    public Vector4 HeaderHoveredColor { get; set; } =
        new(0.737f, 0.694f, 0.886f, 0.5490196347236633f);

    public Vector4 HeaderActiveColor { get; set; } =
        new(0.816f, 0.773f, 0.965f, 0.5490196347236633f);

    public Vector4 SeparatorColor { get; set; } = new(0.620f, 0.576f, 0.769f, 0.5490196347236633f);

    public Vector4 SeparatorHoveredColor { get; set; } =
        new(0.737f, 0.694f, 0.886f, 0.5490196347236633f);

    public Vector4 SeparatorActiveColor { get; set; } =
        new(0.816f, 0.773f, 0.965f, 0.5490196347236633f);

    public Vector4 ResizeGripColor { get; set; } = new(0.620f, 0.576f, 0.769f, 0.5490196347236633f);

    public Vector4 ResizeGripHoveredColor { get; set; } =
        new(0.737f, 0.694f, 0.886f, 0.5490196347236633f);

    public Vector4 ResizeGripActiveColor { get; set; } =
        new(0.816f, 0.773f, 0.965f, 0.5490196347236633f);

    public Vector4 TabColor { get; set; } = new(0.620f, 0.576f, 0.769f, 0.5490196347236633f);

    public Vector4 TabHoveredColor { get; set; } = new(0.737f, 0.694f, 0.886f, 0.5490196347236633f);

    public Vector4 TabActiveColor { get; set; } = new(0.816f, 0.773f, 0.965f, 0.5490196347236633f);

    public Vector4 TabUnfocusedColor { get; set; } = new(0.0f, 0.451f, 1.0f, 0.0f);

    public Vector4 TabUnfocusedActiveColor { get; set; } = new(0.133f, 0.259f, 0.424f, 0.0f);

    public Vector4 PlotLinesColor { get; set; } = new(0.294f, 0.294f, 0.294f, 1.0f);

    public Vector4 PlotLinesHoveredColor { get; set; } =
        new(0.737f, 0.694f, 0.886f, 0.5490196347236633f);

    public Vector4 PlotHistogramColor { get; set; } =
        new(0.620f, 0.576f, 0.769f, 0.5490196347236633f);

    public Vector4 PlotHistogramHoveredColor { get; set; } =
        new(0.737f, 0.694f, 0.886f, 0.5490196347236633f);

    public Vector4 TableHeaderBgColor { get; set; } = new(0.188f, 0.188f, 0.200f, 1.0f);

    public Vector4 TableBorderStrongColor { get; set; } =
        new(0.424f, 0.380f, 0.573f, 0.5490196347236633f);

    public Vector4 TableBorderLightColor { get; set; } =
        new(0.424f, 0.380f, 0.573f, 0.2918455004692078f);

    public Vector4 TableRowBgColor { get; set; } = new(0.0f, 0.0f, 0.0f, 0.0f);

    public Vector4 TableRowBgAltColor { get; set; } = new(1.0f, 1.0f, 1.0f, 0.03433477878570557f);

    public Vector4 TextSelectedBgColor { get; set; } =
        new(0.737f, 0.694f, 0.886f, 0.5490196347236633f);

    public Vector4 DragDropTargetColor { get; set; } = new(1.0f, 1.0f, 0.0f, 0.8999999761581421f);

    public Vector4 NavWindowingHighlightColor { get; set; } =
        new(1.0f, 1.0f, 1.0f, 0.699999988079071f);

    public Vector4 NavWindowingDimBgColor { get; set; } =
        new(0.8f, 0.8f, 0.8f, 0.2000000029802322f);

    public Vector4 ModalWindowDimBgColor { get; set; } = new(0.8f, 0.8f, 0.8f, 0.3499999940395355f);
}

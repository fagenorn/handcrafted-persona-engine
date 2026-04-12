using System.Numerics;
using PersonaEngine.Lib.UI.ControlPanel;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>Predefined style bundles for standard layout regions.</summary>
public static class Styles
{
    public static readonly Style None = default;

    public static readonly Style StatusBar = new(
        padding: new Vector2(8f, 0f),
        itemSpacing: new Vector2(8f, 0f),
        childBg: Theme.SidebarBackground);

    public static readonly Style Sidebar = new(
        padding: new Vector2(8f, 8f),
        itemSpacing: new Vector2(8f, 6f),
        childBg: Theme.SidebarBackground);

    public static readonly Style Content = new(
        padding: new Vector2(16f, 16f),
        itemSpacing: new Vector2(10f, 8f));

    public static readonly Style ControlBar = new(
        padding: new Vector2(8f, 0f),
        itemSpacing: new Vector2(8f, 0f),
        childBg: Theme.SidebarBackground);
}

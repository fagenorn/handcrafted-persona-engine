using System.Numerics;
using PersonaEngine.Lib.UI.ControlPanel;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>Predefined style bundles for standard layout regions.</summary>
public static class Styles
{
    public static readonly Style None = default;

    public static readonly Style StatusBar = new(
        padding: new Vector2(16f, 6f),
        itemSpacing: new Vector2(12f, 0f),
        childBg: Theme.Surface2
    );

    public static readonly Style Sidebar = new(
        padding: new Vector2(12f, 12f),
        itemSpacing: new Vector2(10f, 10f),
        childBg: Theme.Surface1
    );

    public static readonly Style Content = new(
        padding: new Vector2(24f, 20f),
        itemSpacing: new Vector2(12f, 10f),
        childBg: Theme.Base
    );

    public static readonly Style ControlBar = new(
        padding: new Vector2(12f, 6f),
        itemSpacing: new Vector2(12f, 0f),
        childBg: Theme.Surface1
    );
}

using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     Top-level <see cref="IRenderComponent" /> that orchestrates the full control panel layout:
///     status bar, navigation sidebar, content area, and control bar.
/// </summary>
public sealed class ControlPanelComponent : IRenderComponent
{
    private const float SidebarWidth = 170f;

    private const float StatusBarHeight = 40f;
    private const float ControlBarHeight = 44f;
    private const float SavedIndicatorDuration = 2f;

    private readonly ControlBar _controlBar;
    private readonly IConfigWriter _configWriter;
    private readonly Navigation _navigation = new();
    private readonly Dictionary<NavSection, Action> _panelRenderers = new();
    private readonly StatusBar _statusBar;

    public ControlPanelComponent(
        StatusBar statusBar,
        ControlBar controlBar,
        IConfigWriter configWriter,
        Dashboard dashboard,
        Voice voice,
        Personality personality,
        Listening listening,
        Avatar avatar,
        Subtitles subtitles,
        RouletteWheelPanel rouletteWheelPanel,
        ScreenAwareness screenAwareness,
        Streaming streaming,
        LlmConnection llmConnection,
        Application application
    )
    {
        _statusBar = statusBar;
        _controlBar = controlBar;
        _configWriter = configWriter;

        RegisterPanel(NavSection.Dashboard, dashboard.Render);
        RegisterPanel(NavSection.Voice, voice.Render);
        RegisterPanel(NavSection.Personality, personality.Render);
        RegisterPanel(NavSection.Listening, listening.Render);
        RegisterPanel(NavSection.Avatar, avatar.Render);
        RegisterPanel(NavSection.Subtitles, subtitles.Render);
        RegisterPanel(NavSection.RouletteWheel, rouletteWheelPanel.Render);
        RegisterPanel(NavSection.ScreenAwareness, screenAwareness.Render);
        RegisterPanel(NavSection.Streaming, streaming.Render);
        RegisterPanel(NavSection.LlmConnection, llmConnection.Render);
        RegisterPanel(NavSection.Application, application.Render);
    }

    public bool UseSpout => false;

    public string SpoutTarget => string.Empty;

    public int Priority => 0;

    public void Initialize(GL gl, IView view, IInputContext input) { }

    public void Update(float deltaTime) { }

    public void Resize() { }

    public void Dispose() { }

    /// <summary>
    ///     Registers a renderer for the given navigation section.
    ///     Call this before the first frame to wire up panel content.
    /// </summary>
    public void RegisterPanel(NavSection section, Action renderer) =>
        _panelRenderers[section] = renderer;

    public void Render(float deltaTime)
    {
        using (Ui.Window("##ControlPanel"))
        {
            using (Ui.Row(Sz.Fixed(StatusBarHeight), Styles.StatusBar))
                _statusBar.Render(deltaTime);

            using (
                var split = Ui.HSplit(
                    "main",
                    Sz.Fill(),
                    initialLeft: SidebarWidth,
                    minLeft: 100f,
                    minRight: 300f,
                    leftStyle: Styles.Sidebar,
                    rightStyle: Styles.Content
                )
            )
            {
                using (split.Left())
                    _navigation.Render();
                using (split.Right())
                    RenderActivePanel();
            }

            using (Ui.Row(Sz.Fixed(ControlBarHeight), Styles.ControlBar))
                _controlBar.Render();
        }

        RenderSavedIndicator();
    }

    private void RenderActivePanel()
    {
        if (_panelRenderers.TryGetValue(_navigation.ActiveSection, out var renderer))
        {
            renderer();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted("Coming soon...");
            ImGui.PopStyleColor();
        }
    }

    private void RenderSavedIndicator()
    {
        if (_configWriter.LastSaveTime is not { } saveTime)
            return;

        var elapsed = (float)(DateTime.UtcNow - saveTime).TotalSeconds;
        if (elapsed >= SavedIndicatorDuration)
            return;

        var alpha = 1f - elapsed / SavedIndicatorDuration;
        var color = Theme.Success with { W = alpha };
        var col = ImGui.ColorConvertFloat4ToU32(color);

        var viewport = ImGui.GetMainViewport();
        var drawList = ImGui.GetForegroundDrawList();

        const string text = "Saved";
        var textSize = ImGui.CalcTextSize(text);

        const float margin = 12f;
        var pos = new Vector2(
            viewport.Pos.X + viewport.Size.X - textSize.X - margin,
            viewport.Pos.Y + margin
        );

        ImGui.AddText(drawList, pos, col, text);
    }
}

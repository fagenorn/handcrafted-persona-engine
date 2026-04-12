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
    public const float SidebarWidth = 140f;

    private const float StatusBarHeight = 30f;
    private const float ControlBarHeight = 40f;
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
        var viewport = ImGui.GetMainViewport();

        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);

        // Zero padding + rounding for the fullscreen host window only.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);

        var windowFlags =
            ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoNavFocus
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse;

        ImGui.Begin("##ControlPanel", windowFlags);

        // Pop both — back to theme defaults.  From here we control everything
        // explicitly: zero ItemSpacing for the tiling grid, per-child padding.
        ImGui.PopStyleVar(2);

        // Zero item spacing so children tile edge-to-edge with no gaps.
        // This makes the height/width math exact: no hidden spacing to account for.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var availableHeight = ImGui.GetContentRegionAvail().Y;
        var contentHeight = MathF.Max(0f, availableHeight - StatusBarHeight - ControlBarHeight);

        // ── Status bar (full width, fixed height, minimal padding) ──────────
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 0f));
        _statusBar.Render(availableWidth);
        ImGui.PopStyleVar();

        // ── Navigation sidebar + content area side by side ──────────────────
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 8f));
        _navigation.Render(contentHeight);
        ImGui.PopStyleVar();

        ImGui.SameLine();

        // Content area — scrollable child with comfortable padding and normal
        // item spacing so panels lay out naturally inside.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 16f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10f, 8f));

        var contentWidth = MathF.Max(0f, availableWidth - SidebarWidth);
        if (ImGui.BeginChild("##Content", new Vector2(contentWidth, contentHeight)))
        {
            RenderActivePanel();
        }

        ImGui.EndChild();
        ImGui.PopStyleVar(2); // ItemSpacing(10,8) + WindowPadding(16,16)

        // ── Control bar (full width, fixed height, minimal padding) ─────────
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 0f));
        _controlBar.Render(availableWidth);
        ImGui.PopStyleVar();

        ImGui.PopStyleVar(); // ItemSpacing(Zero)

        RenderSavedIndicator();

        ImGui.End();
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

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

    private const float StatusBarHeight = 46f;
    private const float ControlBarHeight = 44f;
    private const float SavedIndicatorDuration = 2f;

    // Staggered transition config
    private const float PanelFadeInBase = 0.18f;
    private const float PanelFadeInStagger = 0.04f;
    private const float PanelFadeInMaxTotal = 0.40f;
    private const float PanelFadeOutDuration = 0.10f;
    private const float PanelSlideOffset = 8f;

    private readonly AmbientRenderer _ambientRenderer;
    private readonly ControlBar _controlBar;
    private readonly IConfigWriter _configWriter;
    private readonly Navigation _navigation = new();
    private readonly Dictionary<NavSection, Action<float>> _panelRenderers = new();
    private readonly StatusBar _statusBar;
    private readonly PersonaStateProvider _stateProvider;

    private NavSection _lastSection;
    private OneShotAnimation _panelTransition;
    private OneShotAnimation _savedPop;
    private DateTime? _lastSavedTime;

    public ControlPanelComponent(
        StatusBar statusBar,
        ControlBar controlBar,
        IConfigWriter configWriter,
        PersonaStateProvider stateProvider,
        AmbientRenderer ambientRenderer,
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
        _stateProvider = stateProvider;
        _ambientRenderer = ambientRenderer;

        RegisterPanel(NavSection.Dashboard, dt => dashboard.Render(dt));
        RegisterPanel(NavSection.Voice, dt => voice.Render(dt));
        RegisterPanel(NavSection.Personality, dt => personality.Render(dt));
        RegisterPanel(NavSection.Listening, dt => listening.Render(dt));
        RegisterPanel(NavSection.Avatar, dt => avatar.Render(dt));
        RegisterPanel(NavSection.Subtitles, dt => subtitles.Render(dt));
        RegisterPanel(NavSection.RouletteWheel, dt => rouletteWheelPanel.Render(dt));
        RegisterPanel(NavSection.ScreenAwareness, dt => screenAwareness.Render(dt));
        RegisterPanel(NavSection.Streaming, dt => streaming.Render(dt));
        RegisterPanel(NavSection.LlmConnection, dt => llmConnection.Render(dt));
        RegisterPanel(NavSection.Application, dt => application.Render(dt));
    }

    public bool UseSpout => false;

    public string SpoutTarget => string.Empty;

    public int Priority => 0;

    public void Initialize(GL gl, IView view, IInputContext input) { }

    public void Update(float deltaTime)
    {
        _stateProvider.Update(deltaTime);
        _ambientRenderer.Update(deltaTime);
    }

    public void Resize() { }

    public void Dispose() { }

    /// <summary>
    ///     Registers a renderer for the given navigation section.
    ///     Call this before the first frame to wire up panel content.
    /// </summary>
    public void RegisterPanel(NavSection section, Action<float> renderer) =>
        _panelRenderers[section] = renderer;

    public void Render(float deltaTime)
    {
        using (Ui.Window("##ControlPanel"))
        {
            // Render ambient background behind all content
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            var bgDrawList = ImGui.GetWindowDrawList();
            _ambientRenderer.RenderBackground(bgDrawList, winPos, winSize);

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
                    _navigation.Render(deltaTime, _stateProvider);
                using (split.Right())
                    RenderActivePanel(deltaTime);
            }

            using (Ui.Row(Sz.Fixed(ControlBarHeight), Styles.ControlBar))
                _controlBar.Render(deltaTime);
        }

        RenderSavedIndicator(deltaTime);
    }

    private void RenderActivePanel(float deltaTime)
    {
        var current = _navigation.ActiveSection;

        if (current != _lastSection)
        {
            _panelTransition.Start(PanelFadeInBase);
            _lastSection = current;
        }

        _panelTransition.Update(deltaTime);

        var t = _panelTransition.IsActive ? Easing.EaseOutCubic(_panelTransition.Progress) : 1f;

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, t);

        var offsetY = (1f - t) * PanelSlideOffset;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offsetY);

        if (_panelRenderers.TryGetValue(current, out var renderer))
        {
            renderer(deltaTime);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted("Coming soon...");
            ImGui.PopStyleColor();
        }

        ImGui.PopStyleVar();
    }

    private void RenderSavedIndicator(float dt)
    {
        if (_configWriter.LastSaveTime is not { } saveTime)
            return;

        var elapsed = (float)(DateTime.UtcNow - saveTime).TotalSeconds;
        if (elapsed >= SavedIndicatorDuration)
            return;

        // Detect new save for pop animation
        if (_lastSavedTime != saveTime)
        {
            _lastSavedTime = saveTime;
            _savedPop.Start(0.2f);
        }

        _savedPop.Update(dt);

        var alpha = 1f - elapsed / SavedIndicatorDuration;

        // Scale pop: uses EaseOutBack (overshoot) during pop animation
        var scale = 1f;
        if (_savedPop.IsActive)
        {
            var popT = Easing.EaseOutBack(_savedPop.Progress);
            scale = 1f + 0.1f * (1f - popT);
        }

        var color = Theme.Success with { W = alpha };
        var col = ImGui.ColorConvertFloat4ToU32(color);

        var viewport = ImGui.GetMainViewport();
        var drawList = ImGui.GetForegroundDrawList();

        const string text = "Saved";
        var textSize = ImGui.CalcTextSize(text);

        const float margin = 12f;
        var pos = new Vector2(
            viewport.Pos.X + viewport.Size.X - textSize.X * scale - margin,
            viewport.Pos.Y + margin
        );

        // Spark particle drifting upward during pop
        if (_savedPop.IsActive)
        {
            var sparkT = _savedPop.Progress;
            var sparkY = pos.Y - sparkT * 12f;
            var sparkAlpha = alpha * (1f - sparkT);
            var sparkCol = ImGui.ColorConvertFloat4ToU32(
                Theme.Success with
                {
                    W = sparkAlpha * 0.6f,
                }
            );
            ImGui.AddCircleFilled(
                drawList,
                new Vector2(pos.X + textSize.X * 0.5f, sparkY),
                2f,
                sparkCol
            );
        }

        // Offset position to keep the scaled text visually centered
        if (scale > 1.001f)
        {
            var scaledSize = textSize * scale;
            var offset = (scaledSize - textSize) * 0.5f;
            pos -= offset;
        }

        ImGui.AddText(drawList, pos, col, text);
    }
}

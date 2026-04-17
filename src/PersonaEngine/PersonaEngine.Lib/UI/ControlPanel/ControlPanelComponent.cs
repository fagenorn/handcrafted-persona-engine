using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Avatar;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Listening;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Personality;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Subtitles;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice;
using PersonaEngine.Lib.UI.ControlPanel.Visuals;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Dashboard = PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Dashboard;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     Top-level <see cref="IRenderComponent" /> that orchestrates the full control panel layout:
///     status bar, navigation sidebar, content area, and control bar.
/// </summary>
public sealed class ControlPanelComponent : IRenderComponent
{
    private const float SidebarWidth = 170f;

    private const float TitleBarHeight = TitleBar.Height;
    private const float StatusBarHeight = 46f;
    private const float ControlBarHeight = 44f;

    // Staggered transition config
    private const float PanelFadeInBase = 0.18f;
    private const float PanelFadeInStagger = 0.04f;
    private const float PanelFadeInMaxTotal = 0.40f;
    private const float PanelFadeOutDuration = 0.10f;
    private const float PanelSlideOffset = 8f;

    private readonly AmbientRenderer _ambientRenderer;
    private readonly ControlBar _controlBar;
    private readonly TitleBar _titleBar;
    private readonly WindowFrameGlow _windowFrameGlow;
    private readonly IConfigWriter _configWriter;
    private readonly SubtitlesPanel _subtitlesPanel;
    private readonly INavRequestBus _navBus;
    private readonly Navigation _navigation = new();
    private readonly Dictionary<NavSection, PanelRegistration> _panelRegistrations = new();

    private readonly record struct PanelRegistration(
        Action<float> Render,
        IActivatablePanel? Lifecycle
    );

    private readonly StatusBar _statusBar;
    private readonly PersonaStateProvider _stateProvider;

    private NavSection? _lastSection;
    private OneShotAnimation _panelTransition;

    // Saved toast animation state
    private DateTime? _lastKnownSaveTime;
    private float _toastElapsed;
    private bool _toastVisible;
    private OneShotAnimation _toastPulse;

    private const float ToastSlideInDuration = 0.2f;
    private const float ToastVisibleDuration = 1.6f;
    private const float ToastSlideOutDuration = 0.3f;
    private const float ToastTotalDuration =
        ToastSlideInDuration + ToastVisibleDuration + ToastSlideOutDuration;

    public ControlPanelComponent(
        StatusBar statusBar,
        ControlBar controlBar,
        TitleBar titleBar,
        IConfigWriter configWriter,
        PersonaStateProvider stateProvider,
        AmbientRenderer ambientRenderer,
        WindowFrameGlow windowFrameGlow,
        Dashboard dashboard,
        VoicePanel voicePanel,
        PersonalityPanel personalityPanel,
        ListeningPanel listeningPanel,
        AvatarPanel avatarPanel,
        SubtitlesPanel subtitlesPanel,
        OverlayPanel overlayPanel,
        RouletteWheelPanel rouletteWheelPanel,
        ScreenAwareness screenAwareness,
        Streaming streaming,
        LlmConnection llmConnection,
        Application application,
        INavRequestBus navBus
    )
    {
        _statusBar = statusBar;
        _controlBar = controlBar;
        _titleBar = titleBar;
        _configWriter = configWriter;
        _stateProvider = stateProvider;
        _ambientRenderer = ambientRenderer;
        _windowFrameGlow = windowFrameGlow;
        _subtitlesPanel = subtitlesPanel;
        _navBus = navBus;

        RegisterPanel(NavSection.Dashboard, dt => dashboard.Render(dt));
        RegisterPanel(NavSection.LlmConnection, dt => llmConnection.Render(dt));
        RegisterPanel(NavSection.Personality, dt => personalityPanel.Render(dt));
        RegisterPanel(NavSection.Listening, dt => listeningPanel.Render(dt), listeningPanel);
        RegisterPanel(NavSection.Voice, dt => voicePanel.Render(dt));
        RegisterPanel(NavSection.Avatar, dt => avatarPanel.Render(dt));
        RegisterPanel(NavSection.Subtitles, dt => subtitlesPanel.Render(dt));
        RegisterPanel(NavSection.Overlay, dt => overlayPanel.Render(dt));
        RegisterPanel(NavSection.Streaming, dt => streaming.Render(dt));
        RegisterPanel(NavSection.RouletteWheel, dt => rouletteWheelPanel.Render(dt));
        RegisterPanel(NavSection.ScreenAwareness, dt => screenAwareness.Render(dt));
        RegisterPanel(NavSection.Application, dt => application.Render(dt));

        _navBus.Requested += OnNavRequested;
    }

    private void OnNavRequested(NavSection section) => _navigation.SetActiveSection(section);

    public bool UseSpout => false;

    public string SpoutTarget => string.Empty;

    public int Priority => 0;

    public void Initialize(GL gl, IView view, IInputContext input)
    {
        _ambientRenderer.Initialize(gl);
        _subtitlesPanel.Initialize(gl);
    }

    public void Update(float deltaTime)
    {
        _stateProvider.Update(deltaTime);
        _ambientRenderer.Update(deltaTime);
    }

    public void Resize() { }

    public void Dispose()
    {
        _navBus.Requested -= OnNavRequested;

        if (
            _lastSection is { } active
            && _panelRegistrations.TryGetValue(active, out var reg)
            && reg.Lifecycle is { } lifecycle
        )
        {
            lifecycle.OnDeactivated();
        }

        _ambientRenderer.Dispose();
    }

    /// <summary>
    ///     Registers a renderer for the given navigation section. Optionally associates a
    ///     lifecycle-aware panel object so <see cref="IActivatablePanel.OnActivated" /> /
    ///     <see cref="IActivatablePanel.OnDeactivated" /> fire on section transitions.
    /// </summary>
    public void RegisterPanel(
        NavSection section,
        Action<float> renderer,
        IActivatablePanel? lifecycle = null
    ) => _panelRegistrations[section] = new PanelRegistration(renderer, lifecycle);

    public void Render(float deltaTime)
    {
        using (Ui.Window("##ControlPanel"))
        {
            using (Ui.Row(Sz.Fixed(TitleBarHeight), Styles.TitleBar))
                _titleBar.Render(deltaTime);

            using (Ui.Row(Sz.Fixed(StatusBarHeight), Styles.StatusBar))
                _statusBar.Render(deltaTime, _stateProvider);

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
                using (var rightScope = split.Right())
                {
                    _ambientRenderer.RenderBackground(
                        rightScope.OuterDrawList,
                        rightScope.OuterPos,
                        rightScope.OuterSize,
                        deltaTime
                    );
                    RenderActivePanel(deltaTime);
                }
            }

            using (Ui.Row(Sz.Fixed(ControlBarHeight), Styles.ControlBar))
                _controlBar.Render(deltaTime);
        }

        RenderSavedIndicator(deltaTime);

        // Rendered last on the foreground drawlist so it sits above everything.
        _windowFrameGlow.Render(deltaTime);
    }

    private void RenderActivePanel(float deltaTime)
    {
        var current = _navigation.ActiveSection;

        if (_lastSection is not { } previous || previous != current)
        {
            HandleSectionTransition(_lastSection, current);
            _panelTransition.Start(PanelFadeInBase);
            _lastSection = current;
        }

        _panelTransition.Update(deltaTime);

        var t = _panelTransition.IsActive ? Easing.EaseOutCubic(_panelTransition.Progress) : 1f;

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, t);

        var offsetY = (1f - t) * PanelSlideOffset;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offsetY);

        if (_panelRegistrations.TryGetValue(current, out var registration))
        {
            registration.Render(deltaTime);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted("Coming soon...");
            ImGui.PopStyleColor();
        }

        ImGui.PopStyleVar();
    }

    private void HandleSectionTransition(NavSection? previous, NavSection current)
    {
        if (
            previous is { } prev
            && _panelRegistrations.TryGetValue(prev, out var prevReg)
            && prevReg.Lifecycle is { } prevLifecycle
        )
        {
            prevLifecycle.OnDeactivated();
        }

        if (
            _panelRegistrations.TryGetValue(current, out var currReg)
            && currReg.Lifecycle is { } currLifecycle
        )
        {
            currLifecycle.OnActivated();
        }
    }

    private void RenderSavedIndicator(float dt)
    {
        var saveTime = _configWriter.LastSaveTime;

        // Detect new save
        if (saveTime is { } currentSave && currentSave != _lastKnownSaveTime)
        {
            _lastKnownSaveTime = currentSave;

            if (_toastVisible)
            {
                // Rapid successive save: extend the visible window and pulse
                // Clamp elapsed back so we get the full "Visible" duration ahead of us,
                // but never reset to < slide-in time (keep toast on screen).
                _toastElapsed = MathF.Min(_toastElapsed, ToastSlideInDuration);
                _toastPulse.Start(0.18f);
            }
            else
            {
                // Fresh toast
                _toastElapsed = 0f;
                _toastVisible = true;
            }
        }

        if (!_toastVisible)
            return;

        _toastElapsed += dt;
        _toastPulse.Update(dt);

        if (_toastElapsed >= ToastTotalDuration)
        {
            _toastVisible = false;
            return;
        }

        // Phase calculations
        float slideIn; // 0→1 slide-in progress
        float alpha; // overall alpha
        if (_toastElapsed < ToastSlideInDuration)
        {
            slideIn = Easing.EaseOutCubic(_toastElapsed / ToastSlideInDuration);
            alpha = slideIn;
        }
        else if (_toastElapsed < ToastSlideInDuration + ToastVisibleDuration)
        {
            slideIn = 1f;
            alpha = 1f;
        }
        else
        {
            slideIn = 1f;
            var outT =
                (_toastElapsed - ToastSlideInDuration - ToastVisibleDuration)
                / ToastSlideOutDuration;
            outT = Math.Clamp(outT, 0f, 1f);
            alpha = 1f - Easing.EaseOutCubic(outT);
            slideIn = 1f - 0.3f * Easing.EaseOutCubic(outT); // slight slide-out
        }

        // Pulse for rapid save
        var pulseScale = 1f;
        if (_toastPulse.IsActive)
        {
            var popT = Easing.EaseOutBack(_toastPulse.Progress);
            pulseScale = 1f + 0.08f * (1f - popT);
        }

        var viewport = ImGui.GetMainViewport();
        var drawList = ImGui.GetForegroundDrawList();

        const string label = "Saved";
        const float paddingX = 12f;
        const float paddingY = 7f;
        const float dotRadius = 3.5f;
        const float dotGap = 8f;

        var textSize = ImGui.CalcTextSize(label);
        var cardW = (paddingX * 2f + dotRadius * 2f + dotGap + textSize.X) * pulseScale;
        var cardH = (paddingY * 2f + MathF.Max(textSize.Y, dotRadius * 2f)) * pulseScale;

        const float rightMargin = 14f;
        const float offscreenOffset = 40f;

        // slideIn controls horizontal position: 0 = offscreen right, 1 = at rest
        var targetX = viewport.Pos.X + viewport.Size.X - cardW - rightMargin;
        var offX = (1f - slideIn) * offscreenOffset;
        // Vertically centered inside the status bar band (top StatusBarHeight pixels of viewport)
        var cardY = viewport.Pos.Y + TitleBarHeight + (StatusBarHeight - cardH) * 0.5f;
        var cardMin = new Vector2(targetX + offX, cardY);
        var cardMax = cardMin + new Vector2(cardW, cardH);

        // Card background
        var bgCol = ImGui.ColorConvertFloat4ToU32(Theme.Surface2 with { W = alpha * 0.96f });
        var borderCol = ImGui.ColorConvertFloat4ToU32(Theme.Success with { W = alpha * 0.55f });

        ImGui.AddRectFilled(drawList, cardMin, cardMax, bgCol, 6f);
        ImGui.AddRect(drawList, cardMin, cardMax, borderCol, 6f, 0, 1f);

        // Success dot (left side)
        var dotCenter = new Vector2(cardMin.X + paddingX + dotRadius, cardMin.Y + cardH * 0.5f);
        var dotCol = ImGui.ColorConvertFloat4ToU32(Theme.Success with { W = alpha });
        var dotGlowCol = ImGui.ColorConvertFloat4ToU32(Theme.Success with { W = alpha * 0.4f });
        ImGui.AddCircleFilled(drawList, dotCenter, dotRadius * 2f, dotGlowCol, 16);
        ImGui.AddCircleFilled(drawList, dotCenter, dotRadius, dotCol, 16);

        // Label text
        var textCol = ImGui.ColorConvertFloat4ToU32(Theme.TextPrimary with { W = alpha });
        var textPos = new Vector2(
            dotCenter.X + dotRadius + dotGap,
            cardMin.Y + (cardH - textSize.Y) * 0.5f
        );
        ImGui.AddText(drawList, textPos, textCol, label);
    }
}

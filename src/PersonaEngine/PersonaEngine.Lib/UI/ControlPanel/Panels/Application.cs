using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     Application panel: window size, title, and display mode configuration.
/// </summary>
public sealed class Application(
    IOptionsMonitor<AvatarAppConfig> appOptions,
    IConfigWriter configWriter
)
{
    private WindowConfiguration _window = null!;
    private bool _initialized;

    private AnimatedFloat _fullscreenKnob;

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _window = CloneWindow(appOptions.CurrentValue.Window);
        _fullscreenKnob = new AnimatedFloat(_window.Fullscreen ? 1f : 0f);
        _initialized = true;
    }

    private static WindowConfiguration CloneWindow(WindowConfiguration src) =>
        new()
        {
            Width = src.Width,
            Height = src.Height,
            Title = src.Title,
            Fullscreen = src.Fullscreen,
        };

    public void Render(float deltaTime)
    {
        EnsureInitialized();
        RenderWindow(deltaTime);
    }

    // ── Window ───────────────────────────────────────────────────────────────────

    private void RenderWindow(float dt)
    {
        ImGuiHelpers.SectionHeader("Window");

        // Resolution
        {
            var width = _window.Width;
            var height = _window.Height;

            ImGuiHelpers.SettingLabel("Resolution", "Render window resolution.");

            if (ImGuiHelpers.ResolutionPicker("WindowRes", ref width, ref height))
            {
                _window.Width = width;
                _window.Height = height;
                configWriter.Write(CloneWindow(_window));
            }
        }

        // Title
        {
            var title = _window.Title;

            ImGuiHelpers.SettingLabel("Title", "The window title shown in the taskbar.");

            if (ImGui.InputText("##WindowTitle", ref title, 256))
            {
                _window.Title = title;
                configWriter.Write(CloneWindow(_window));
            }
        }

        // Fullscreen
        {
            var fullscreen = _window.Fullscreen;

            ImGuiHelpers.SettingLabel("Fullscreen", "Run the application in fullscreen mode.");

            if (ImGuiHelpers.ToggleSwitch("##Fullscreen", ref fullscreen, ref _fullscreenKnob, dt))
            {
                _window.Fullscreen = fullscreen;
                configWriter.Write(CloneWindow(_window));
            }
        }
    }
}

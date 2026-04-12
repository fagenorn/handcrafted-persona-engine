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

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _window = CloneWindow(appOptions.CurrentValue.Window);
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

    public void Render()
    {
        EnsureInitialized();
        RenderWindow();
    }

    // ── Window ───────────────────────────────────────────────────────────────────

    private void RenderWindow()
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

            if (ImGui.Checkbox("##Fullscreen", ref fullscreen))
            {
                _window.Fullscreen = fullscreen;
                configWriter.Write(CloneWindow(_window));
            }
        }
    }
}

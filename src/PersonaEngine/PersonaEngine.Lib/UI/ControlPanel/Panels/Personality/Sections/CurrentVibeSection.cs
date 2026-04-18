using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Personality.Sections;

/// <summary>
///     Freeform textarea for setting the session-level mood / situation context.
///     Maps to <see cref="ConversationContextOptions.CurrentContext" />.
/// </summary>
public sealed class CurrentVibeSection : IDisposable
{
    private const int VibeBufferSize = 4096;

    private readonly IConfigWriter _configWriter;
    private readonly IDisposable? _changeSubscription;

    private ConversationContextOptions _opts;
    private string _vibeBuffer = string.Empty;
    private bool _initialized;

    public CurrentVibeSection(
        IOptionsMonitor<ConversationContextOptions> monitor,
        IConfigWriter configWriter
    )
    {
        _configWriter = configWriter;
        _opts = monitor.CurrentValue;
        _changeSubscription = monitor.OnChange(
            (updated, _) =>
            {
                _opts = updated;
                if (!_initialized)
                    return;
                _vibeBuffer = updated.CurrentContext ?? string.Empty;
            }
        );
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt)
    {
        if (!_initialized)
        {
            _vibeBuffer = _opts.CurrentContext ?? string.Empty;
            _initialized = true;
        }

        using (Ui.Card("##current_vibe", padding: 12f))
        {
            // Header
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted("Current Vibe");
            ImGui.PopStyleColor();

            // Description
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted("Set the mood for the current session");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            // Textarea
            ImGui.SetNextItemWidth(-1f);
            if (
                ImGui.InputTextMultiline(
                    "##vibe",
                    ref _vibeBuffer,
                    VibeBufferSize,
                    new System.Numerics.Vector2(0f, 80f)
                )
            )
            {
                _opts = _opts with { CurrentContext = _vibeBuffer };
                _configWriter.Write(_opts);
            }
        }
    }
}

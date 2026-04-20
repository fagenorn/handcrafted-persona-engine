using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Personality.Sections;

/// <summary>
///     Displays the active prompt source (file or custom) and lets the user switch
///     between them. In custom mode, provides a multiline editor for the system prompt.
/// </summary>
public sealed class PromptSourceSection : IDisposable
{
    private const int PromptBufferSize = 8192;

    private readonly IConfigWriter _configWriter;
    private readonly IDisposable? _changeSubscription;

    private ConversationContextOptions _opts;
    private string _promptBuffer = string.Empty;
    private bool _initialized;

    public PromptSourceSection(
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
                _promptBuffer = updated.SystemPrompt ?? string.Empty;
            }
        );
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt)
    {
        if (!_initialized)
        {
            _promptBuffer = _opts.SystemPrompt ?? string.Empty;
            _initialized = true;
        }

        using (Ui.Card("##prompt_source", padding: 12f))
        {
            // Header
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted("Character Prompt");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            var hasFile = !string.IsNullOrWhiteSpace(_opts.SystemPromptFile);
            var isCustomMode = _opts.UseCustomPrompt;
            var isFileMode = hasFile && !isCustomMode;

            // Source toggle chips
            if (hasFile)
            {
                if (ImGuiHelpers.Chip("File", isFileMode))
                {
                    SwitchToFileMode();
                }

                ImGui.SameLine(0f, 6f);
            }

            if (ImGuiHelpers.Chip("Custom", isCustomMode))
            {
                if (!isCustomMode)
                    SwitchToCustomMode();
            }

            ImGui.Spacing();

            // Content based on mode
            if (isFileMode)
            {
                RenderFileMode();
            }
            else
            {
                RenderCustomMode();
            }
        }
    }

    private void RenderFileMode()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted($"Using file: {_opts.SystemPromptFile}");
        ImGui.PopStyleColor();
    }

    private void RenderCustomMode()
    {
        ImGui.SetNextItemWidth(-1f);
        if (
            ImGui.InputTextMultiline(
                "##system_prompt",
                ref _promptBuffer,
                PromptBufferSize,
                new System.Numerics.Vector2(0f, 300f)
            )
        )
        {
            _opts = _opts with { SystemPrompt = _promptBuffer };
            _configWriter.Write(_opts);
        }
    }

    private void SwitchToFileMode()
    {
        _opts = _opts with { UseCustomPrompt = false };
        _configWriter.Write(_opts);
    }

    private void SwitchToCustomMode()
    {
        _opts = _opts with { UseCustomPrompt = true };
        _configWriter.Write(_opts);
    }
}

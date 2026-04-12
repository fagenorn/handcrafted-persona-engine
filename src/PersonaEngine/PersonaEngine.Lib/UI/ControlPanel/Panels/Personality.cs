using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     Personality panel: system prompt, current context, and conversation topics.
/// </summary>
public sealed class Personality(
    IOptionsMonitor<ConversationContextOptions> contextOptions,
    IConfigWriter configWriter
)
{
    private const int SystemPromptBufferSize = 8192;
    private const int CurrentContextBufferSize = 4096;
    private const int TopicsBufferSize = 2048;

    private ConversationContextOptions _context;

    private string _systemPromptBuffer = string.Empty;
    private string _currentContextBuffer = string.Empty;
    private string _topicsBuffer = string.Empty;
    private bool _initialized;

    public void Render(float deltaTime)
    {
        EnsureInitialized();
        RenderSystemPrompt();
        RenderCurrentContext();
        RenderTopics();
    }

    // ── Initialization ───────────────────────────────────────────────────────────

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _context = contextOptions.CurrentValue;
        _systemPromptBuffer = _context.SystemPrompt ?? string.Empty;
        _currentContextBuffer = _context.CurrentContext ?? string.Empty;
        _topicsBuffer = _context.Topics is { Count: > 0 }
            ? string.Join(", ", _context.Topics)
            : string.Empty;

        _initialized = true;
    }

    // ── System Prompt ────────────────────────────────────────────────────────────

    private void RenderSystemPrompt()
    {
        ImGuiHelpers.SectionHeader("System Prompt");

        if (!string.IsNullOrWhiteSpace(_context.SystemPromptFile))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted($"Also loading from file: {_context.SystemPromptFile}");
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        ImGuiHelpers.SettingLabel(
            "Prompt",
            "The system prompt sent at the start of every conversation."
        );
        ImGui.NewLine();

        ImGui.SetNextItemWidth(-1f);

        if (
            ImGui.InputTextMultiline(
                "##SystemPrompt",
                ref _systemPromptBuffer,
                SystemPromptBufferSize,
                new System.Numerics.Vector2(0f, 200f)
            )
        )
        {
            _context = _context with { SystemPrompt = _systemPromptBuffer };
            configWriter.Write(_context);
        }
    }

    // ── Current Context ──────────────────────────────────────────────────────────

    private void RenderCurrentContext()
    {
        ImGuiHelpers.SectionHeader("Current Context");

        ImGuiHelpers.SettingLabel(
            "Context",
            "Additional context injected into the conversation for the current session."
        );
        ImGui.NewLine();

        ImGui.SetNextItemWidth(-1f);

        if (
            ImGui.InputTextMultiline(
                "##CurrentContext",
                ref _currentContextBuffer,
                CurrentContextBufferSize,
                new System.Numerics.Vector2(0f, 80f)
            )
        )
        {
            _context = _context with { CurrentContext = _currentContextBuffer };
            configWriter.Write(_context);
        }
    }

    // ── Topics ───────────────────────────────────────────────────────────────────

    private void RenderTopics()
    {
        ImGuiHelpers.SectionHeader("Topics");

        ImGuiHelpers.SettingLabel(
            "Topics",
            "Comma-separated list of topics the persona should focus on."
        );

        if (ImGui.InputText("##Topics", ref _topicsBuffer, TopicsBufferSize))
        {
            var topics = _topicsBuffer
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            _context = _context with { Topics = topics };
            configWriter.Write(_context);
        }
    }
}

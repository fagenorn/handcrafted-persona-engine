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

    private string _systemPromptBuffer = string.Empty;
    private string _currentContextBuffer = string.Empty;
    private string _topicsBuffer = string.Empty;
    private bool _buffersInitialized;

    public void Render()
    {
        EnsureBuffersInitialized();
        RenderSystemPrompt();
        RenderCurrentContext();
        RenderTopics();
    }

    // ── Buffer initialization ────────────────────────────────────────────────────

    private void EnsureBuffersInitialized()
    {
        if (_buffersInitialized)
            return;

        var ctx = contextOptions.CurrentValue;
        _systemPromptBuffer = ctx.SystemPrompt ?? string.Empty;
        _currentContextBuffer = ctx.CurrentContext ?? string.Empty;
        _topicsBuffer = ctx.Topics is { Count: > 0 } ? string.Join(", ", ctx.Topics) : string.Empty;

        _buffersInitialized = true;
    }

    // ── System Prompt ────────────────────────────────────────────────────────────

    private void RenderSystemPrompt()
    {
        ImGuiHelpers.SectionHeader("System Prompt");

        var ctx = contextOptions.CurrentValue;

        if (!string.IsNullOrWhiteSpace(ctx.SystemPromptFile))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted($"Also loading from file: {ctx.SystemPromptFile}");
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
            configWriter.Write(ctx with { SystemPrompt = _systemPromptBuffer });
        }
    }

    // ── Current Context ──────────────────────────────────────────────────────────

    private void RenderCurrentContext()
    {
        ImGuiHelpers.SectionHeader("Current Context");

        var ctx = contextOptions.CurrentValue;

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
            configWriter.Write(ctx with { CurrentContext = _currentContextBuffer });
        }
    }

    // ── Topics ───────────────────────────────────────────────────────────────────

    private void RenderTopics()
    {
        ImGuiHelpers.SectionHeader("Topics");

        var ctx = contextOptions.CurrentValue;

        ImGuiHelpers.SettingLabel(
            "Topics",
            "Comma-separated list of topics the persona should focus on."
        );

        if (ImGui.InputText("##Topics", ref _topicsBuffer, TopicsBufferSize))
        {
            var topics = _topicsBuffer
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            configWriter.Write(ctx with { Topics = topics });
        }
    }
}

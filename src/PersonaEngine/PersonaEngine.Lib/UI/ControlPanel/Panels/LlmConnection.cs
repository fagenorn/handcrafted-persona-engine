using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     LLM Connection panel: text and vision LLM endpoint, model, and API key configuration.
/// </summary>
public sealed class LlmConnection(
    IOptionsMonitor<LlmOptions> llmOptions,
    IConfigWriter configWriter
)
{
    private LlmOptions _llm;
    private bool _initialized;

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _llm = llmOptions.CurrentValue;
        _initialized = true;
    }

    public void Render(float deltaTime)
    {
        EnsureInitialized();
        RenderTextLlm();
        RenderVisionLlm();
    }

    // ── Text LLM ─────────────────────────────────────────────────────────────────

    private void RenderTextLlm()
    {
        ImGuiHelpers.SectionHeader("Text LLM");

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(
            "The primary language model used to generate the avatar's conversational responses. "
                + "Must be an OpenAI-compatible API endpoint."
        );
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        ImGui.Spacing();

        // Endpoint
        {
            var endpoint = _llm.TextEndpoint;

            ImGuiHelpers.SettingLabel("Endpoint", "Base URL of the OpenAI-compatible API.");

            if (ImGui.InputText("##TextEndpoint", ref endpoint, 512))
            {
                _llm = _llm with { TextEndpoint = endpoint };
                configWriter.Write(_llm);
            }
        }

        // Model
        {
            var model = _llm.TextModel;

            ImGuiHelpers.SettingLabel("Model", "Model identifier to use for text generation.");

            if (ImGui.InputText("##TextModel", ref model, 256))
            {
                _llm = _llm with { TextModel = model };
                configWriter.Write(_llm);
            }
        }

        // API Key
        {
            var apiKey = _llm.TextApiKey;

            ImGuiHelpers.SettingLabel("API Key", "Authentication key for the text LLM endpoint.");

            if (ImGui.InputText("##TextApiKey", ref apiKey, 512, ImGuiInputTextFlags.Password))
            {
                _llm = _llm with { TextApiKey = apiKey };
                configWriter.Write(_llm);
            }
        }
    }

    // ── Vision LLM ───────────────────────────────────────────────────────────────

    private void RenderVisionLlm()
    {
        ImGuiHelpers.SectionHeader("Vision LLM");

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(
            "The vision-capable language model used for screen-awareness features. "
                + "Must be an OpenAI-compatible API endpoint that accepts image inputs."
        );
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        ImGui.Spacing();

        // Endpoint
        {
            var endpoint = _llm.VisionEndpoint;

            ImGuiHelpers.SettingLabel("Endpoint", "Base URL of the vision-capable API.");

            if (ImGui.InputText("##VisionEndpoint", ref endpoint, 512))
            {
                _llm = _llm with { VisionEndpoint = endpoint };
                configWriter.Write(_llm);
            }
        }

        // Model
        {
            var model = _llm.VisionModel;

            ImGuiHelpers.SettingLabel("Model", "Model identifier to use for image understanding.");

            if (ImGui.InputText("##VisionModel", ref model, 256))
            {
                _llm = _llm with { VisionModel = model };
                configWriter.Write(_llm);
            }
        }

        // API Key
        {
            var apiKey = _llm.VisionApiKey;

            ImGuiHelpers.SettingLabel("API Key", "Authentication key for the vision LLM endpoint.");

            if (ImGui.InputText("##VisionApiKey", ref apiKey, 512, ImGuiInputTextFlags.Password))
            {
                _llm = _llm with { VisionApiKey = apiKey };
                configWriter.Write(_llm);
            }
        }
    }
}

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.LlmConnection.Sections;

using Hexa.NET.ImGui;
using PersonaEngine.Lib.LLM.Connection;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;

/// <summary>
///     Row-level helpers shared between <see cref="TextLlmSection" /> and
///     <see cref="VisionLlmSection" />. Each of the three rows — Endpoint,
///     Model, API Key — was a ~20-line private method duplicated verbatim on
///     both sections; collapsing them here keeps the section classes focused on
///     the channel-specific pieces (header, toggle, probe wiring, snapshot
///     writes).
/// </summary>
/// <remarks>
///     These are thin wrappers that hide the
///     <c>GetCursorPosY</c>/<c>SettingLabel</c>/<c>SettingEndRow</c> framing
///     around each shared widget. Callers pass buffers by <see langword="ref" />
///     and receive a <c>next</c> value only when the user actually edited the
///     input, matching the convention used by the widgets themselves.
/// </remarks>
public static class LlmChannelSection
{
    /// <summary>
    ///     Renders the Endpoint row (label + <see cref="EndpointPickerRow" />).
    /// </summary>
    /// <param name="idPrefix">Unique scope (e.g. "Text", "Vision").</param>
    /// <param name="tooltip">Tooltip text shown on the label's help cursor.</param>
    /// <param name="buffer">Caller-owned endpoint URL buffer; updated in place on edit.</param>
    /// <param name="next">The new URL when the user typed or picked a preset; otherwise <see langword="null" />.</param>
    public static void EndpointRow(
        string idPrefix,
        string tooltip,
        ref string buffer,
        out string? next
    )
    {
        var rowY = ImGui.GetCursorPosY();
        ImGuiHelpers.SettingLabel("Endpoint", tooltip);

        EndpointPickerRow.Render(
            idPrefix + "Endpoint",
            EndpointPickerRow.DefaultPresets,
            buffer,
            out next
        );

        if (next is not null)
        {
            buffer = next;
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    /// <summary>
    ///     Renders the Model row (label + <see cref="ScannedModelPicker" />).
    /// </summary>
    /// <param name="tooltip">Tooltip text shown on the label's help cursor.</param>
    /// <param name="probeStatus">Most recent probe status for the channel.</param>
    /// <param name="availableModels">Model ids returned by the endpoint probe.</param>
    /// <param name="buffer">Caller-owned saved model id buffer; updated in place on edit.</param>
    /// <param name="onRequestReprobe">Cached callback fired when the user picks a new model.</param>
    /// <param name="next">The new model id when the user picked one; otherwise <see langword="null" />.</param>
    public static void ModelRow(
        string tooltip,
        LlmProbeStatus probeStatus,
        IReadOnlyList<string> availableModels,
        ref string buffer,
        Action onRequestReprobe,
        out string? next
    )
    {
        var rowY = ImGui.GetCursorPosY();
        ImGuiHelpers.SettingLabel("Model", tooltip);

        ScannedModelPicker.Render(
            probeStatus,
            availableModels,
            buffer,
            out next,
            onRequestReprobe: onRequestReprobe
        );

        if (next is not null)
        {
            buffer = next;
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    /// <summary>
    ///     Renders the API Key row (label + <see cref="ApiKeyRow" />).
    /// </summary>
    /// <param name="idPrefix">Unique scope (e.g. "Text", "Vision").</param>
    /// <param name="buffer">Caller-owned API key buffer; updated in place on edit.</param>
    /// <param name="showKey">Caller-owned reveal flag; flipped by the Show/Hide toggle.</param>
    /// <param name="endpoint">The channel's current endpoint URL — used to emit the OpenAI-key warning.</param>
    /// <param name="next">The new key when the user typed; otherwise <see langword="null" />.</param>
    public static void ApiKeyRow(
        string idPrefix,
        ref string buffer,
        ref bool showKey,
        string endpoint,
        out string? next
    )
    {
        var rowY = ImGui.GetCursorPosY();
        ImGuiHelpers.SettingLabel(
            "API Key",
            "Authentication token. Leave blank for local endpoints."
        );

        Shared.ApiKeyRow.Render(idPrefix + "Key", ref buffer, ref showKey, endpoint, out next);

        ImGuiHelpers.SettingEndRow(rowY);
    }
}

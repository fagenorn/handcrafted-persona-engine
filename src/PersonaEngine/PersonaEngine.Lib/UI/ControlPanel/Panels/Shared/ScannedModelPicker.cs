namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;

using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.LLM.Connection;

/// <summary>
///     Model picker that is always a dropdown (no free-text fallback in the happy path).
///     Disabled placeholders explain why the list is empty. An Advanced disclosure reveals
///     a plain input for private / gated model names — state is section-local, not
///     persisted. <see cref="LlmProbeStatus.Reachable" /> and
///     <see cref="LlmProbeStatus.ModelMissing" /> are treated identically here: both mean
///     "endpoint is up, model list available".
/// </summary>
public static class ScannedModelPicker
{
    private static readonly Vector4 WarningColor = new(1f, 0.65f, 0.3f, 1f);

    /// <summary>
    ///     Immutable projection of probe status + model list into displayable picker state.
    /// </summary>
    /// <param name="Placeholder">
    ///     Text shown in the combo when the picker is disabled. <see langword="null" />
    ///     when the picker is enabled (the current model name is shown instead).
    /// </param>
    /// <param name="Disabled">
    ///     <see langword="true" /> when the combo should be greyed out and non-interactive.
    /// </param>
    /// <param name="Warning">
    ///     Optional inline warning shown below the combo, e.g. when the saved model id is
    ///     not in the list returned by the endpoint. <see langword="null" /> when no warning
    ///     is needed.
    /// </param>
    public readonly record struct PickerState(string? Placeholder, bool Disabled, string? Warning);

    /// <summary>
    ///     Pure state-table projection — placeholder text, disabled flag, and inline
    ///     warning derived from the probe status, model-list size, whether the saved model
    ///     id is present in the list, and the saved model id itself.
    /// </summary>
    /// <param name="probe">Most recent probe result for the LLM channel.</param>
    /// <param name="availableCount">
    ///     Number of model ids returned by the endpoint. Ignored for non-reachable
    ///     statuses.
    /// </param>
    /// <param name="savedInList">
    ///     Whether the currently saved model id appears in the available list.
    ///     Defaults to <see langword="true" /> (no warning shown).
    /// </param>
    /// <param name="saved">
    ///     The currently saved model id. Only used in the warning message when
    ///     <paramref name="savedInList" /> is <see langword="false" />.
    /// </param>
    public static PickerState ComputeState(
        LlmProbeStatus probe,
        int availableCount,
        bool savedInList = true,
        string? saved = null
    )
    {
        return probe switch
        {
            LlmProbeStatus.Probing => new PickerState("Scanning\u2026", true, null),
            LlmProbeStatus.Reachable or LlmProbeStatus.ModelMissing when availableCount == 0 =>
                new PickerState("No models reported", true, null),
            LlmProbeStatus.Reachable or LlmProbeStatus.ModelMissing when !savedInList =>
                new PickerState(
                    null,
                    false,
                    $"'{saved}' not served by this endpoint \u2014 pick one below"
                ),
            LlmProbeStatus.Reachable or LlmProbeStatus.ModelMissing => new PickerState(
                null,
                false,
                null
            ),
            _ => new PickerState("Verify endpoint to list models", true, null),
        };
    }

    /// <summary>
    ///     Renders the combo + optional warning + Advanced disclosure. Sets
    ///     <paramref name="next" /> to the newly-picked model when the user selects one
    ///     or types into the Advanced input. <paramref name="advancedOpen" /> tracks
    ///     whether the Advanced disclosure is expanded (section-local UI state).
    /// </summary>
    /// <param name="probeStatus">Most recent probe status for the LLM channel.</param>
    /// <param name="availableModels">Model ids returned by the endpoint probe.</param>
    /// <param name="current">Currently saved model id.</param>
    /// <param name="next">
    ///     Set to the newly-selected model id when the user interacts with the picker;
    ///     otherwise <see langword="null" />.
    /// </param>
    /// <param name="onRequestReprobe">
    ///     Callback invoked when a new model is selected so the caller can trigger a
    ///     fresh probe.
    /// </param>
    /// <param name="advancedOpen">
    ///     Section-local state tracking whether the Advanced disclosure is expanded.
    /// </param>
    public static void Render(
        LlmProbeStatus probeStatus,
        IReadOnlyList<string> availableModels,
        string current,
        out string? next,
        Action onRequestReprobe,
        ref bool advancedOpen
    )
    {
        ArgumentNullException.ThrowIfNull(availableModels);
        ArgumentNullException.ThrowIfNull(onRequestReprobe);
        next = null;

        var savedInList = false;
        foreach (var model in availableModels)
        {
            if (string.Equals(model, current, StringComparison.OrdinalIgnoreCase))
            {
                savedInList = true;
                break;
            }
        }

        var state = ComputeState(probeStatus, availableModels.Count, savedInList, current);

        if (state.Disabled)
        {
            ImGui.BeginDisabled();
        }

        var preview = state.Disabled ? state.Placeholder ?? string.Empty : current;
        if (ImGui.BeginCombo("Model", preview))
        {
            foreach (var model in availableModels)
            {
                var selected = string.Equals(model, current, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(model, selected))
                {
                    next = model;
                    onRequestReprobe();
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        if (state.Disabled)
        {
            ImGui.EndDisabled();
        }

        if (state.Warning is not null)
        {
            ImGui.TextColored(WarningColor, state.Warning);
        }

        if (ImGui.CollapsingHeader("Advanced: custom model name"))
        {
            advancedOpen = true;
            var buffer = current ?? string.Empty;
            if (ImGui.InputText("##advanced_custom_model", ref buffer, 256))
            {
                next = buffer;
            }
        }
        else
        {
            advancedOpen = false;
        }
    }
}

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;

using Hexa.NET.ImGui;

/// <summary>
///     Masked API key input + trailing reveal toggle. Draws the input with
///     <see cref="ImGuiInputTextFlags.Password" /> by default, pairs it with a
///     "Show"/"Hide" <see cref="ImGuiHelpers.SubtleButton" /> on the same row, and
///     surfaces a tertiary-colored warning when an OpenAI endpoint is configured
///     with a blank key. The input reserves horizontal space for the trailing
///     button so it never overflows the card. Caller-owned state (<paramref name="buffer" />,
///     <paramref name="showKey" />) flows in by <see langword="ref" />; the row
///     reports the new text via <paramref name="next" /> only when the user edits
///     the input, matching the convention used by <see cref="EndpointPickerRow" />
///     and <see cref="ScannedModelPicker" />.
/// </summary>
public static class ApiKeyRow
{
    // SubtleButton uses FramePadding(10, 5) so width ≈ text + 20.
    private const float ButtonHorizontalPadding = 20f;

    // Minimum input width so a narrow card can't collapse the field into a sliver.
    private const float MinInputWidth = 60f;

    private const string OpenAiHost = "api.openai.com";

    private const string OpenAiWarning = "OpenAI requires an API key.";

    /// <summary>
    ///     Renders the masked input + reveal toggle row.
    /// </summary>
    /// <param name="id">
    ///     Stable identifier used for both ImGui labels on this row. Must be unique
    ///     across sibling rows so ImGui widget state doesn't bleed between them.
    /// </param>
    /// <param name="buffer">
    ///     Caller-owned input buffer. Mutated in place when the user types; also
    ///     reported via <paramref name="next" />.
    /// </param>
    /// <param name="showKey">
    ///     Caller-owned reveal flag. Toggled when the user clicks the Show/Hide
    ///     button; flows both ways so the caller can persist it across disposal.
    /// </param>
    /// <param name="endpoint">
    ///     Endpoint URL for the channel this key belongs to. Used to decide whether
    ///     to show the "OpenAI requires an API key" warning.
    /// </param>
    /// <param name="next">
    ///     Set to the new key value when the user types; otherwise
    ///     <see langword="null" />.
    /// </param>
    public static void Render(
        string id,
        ref string buffer,
        ref bool showKey,
        string endpoint,
        out string? next
    )
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(endpoint);
        next = null;

        // Reserve horizontal space for the Hide/Show button so it doesn't overflow
        // the card.
        var visibleBtnText = showKey ? "Hide" : "Show";
        var style = ImGui.GetStyle();
        var buttonW = ImGui.CalcTextSize(visibleBtnText).X + ButtonHorizontalPadding;
        var avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetNextItemWidth(MathF.Max(MinInputWidth, avail - buttonW - style.ItemSpacing.X));

        var flags = showKey ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;

        ImGui.PushID(id);
        try
        {
            if (ImGui.InputText("##key", ref buffer, 512, flags))
            {
                next = buffer;
            }

            ImGui.SameLine();
            if (ImGuiHelpers.SubtleButton(visibleBtnText))
            {
                showKey = !showKey;
            }
        }
        finally
        {
            ImGui.PopID();
        }

        if (
            endpoint.Contains(OpenAiHost, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(buffer)
        )
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted(OpenAiWarning);
            ImGui.PopStyleColor();
        }
    }
}

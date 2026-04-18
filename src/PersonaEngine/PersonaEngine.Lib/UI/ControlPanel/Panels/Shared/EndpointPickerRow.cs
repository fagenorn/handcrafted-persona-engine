namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;

using Hexa.NET.ImGui;

/// <summary>
///     Endpoint picker: single-line text input + "Presets ▾" combo on the same row. The
///     input reserves space for the trailing combo via <see cref="ImGui.SetNextItemWidth" />
///     so the combo never falls off the right edge of the card. Uses the same
///     <see cref="ImGui.BeginCombo" /> + <see cref="ImGui.Selectable" /> widget pattern as
///     <see cref="ScannedModelPicker" /> for API consistency — the "Presets" preview
///     string acts as a discoverable label (users edit the URL freely, so we never show
///     a selected-preset value there). Caller mutates configuration only when
///     <paramref name="next" /> is non-null.
/// </summary>
public static class EndpointPickerRow
{
    /// <summary>
    ///     Default preset list shared by every LLM connection section (text + vision).
    ///     OpenAI public endpoint + the two locally-hosted OpenAI-compatible runtimes.
    /// </summary>
    public static readonly (string Label, string Url)[] DefaultPresets =
    [
        ("OpenAI", "https://api.openai.com/v1"),
        ("LM Studio", "http://localhost:1234/v1"),
        ("Ollama", "http://localhost:11434/v1"),
    ];

    /// <summary>
    ///     Returns the preset label whose URL matches <paramref name="current" />
    ///     ignoring case and a single trailing slash. <see langword="null" /> when no
    ///     preset matches.
    /// </summary>
    public static string? MatchPreset(
        IReadOnlyList<(string Label, string Url)> presets,
        string current
    )
    {
        ArgumentNullException.ThrowIfNull(presets);
        var normalized = Normalize(current);
        foreach (var (label, url) in presets)
        {
            if (string.Equals(Normalize(url), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return label;
            }
        }

        return null;
    }

    /// <summary>
    ///     Renders the endpoint input + presets combo. Sets <paramref name="next" /> to
    ///     the new URL when the user edits the text or picks a preset; otherwise leaves
    ///     it <see langword="null" />.
    /// </summary>
    public static void Render(
        string label,
        IReadOnlyList<(string Label, string Url)> presets,
        string current,
        out string? next
    )
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(presets);
        next = null;

        ImGui.PushID(label);
        try
        {
            // Reserve horizontal space for the presets combo so the input doesn't push
            // it past the card's right edge. SettingLabel has already set the next item
            // width to -1; we overwrite that with an explicit budget. The combo width is
            // the preview text + FramePadding on each side + the arrow button
            // (~FrameHeight wide) — matching how ImGui itself lays out a combo.
            const string previewText = "Presets";
            var style = ImGui.GetStyle();
            var comboW =
                ImGui.CalcTextSize(previewText).X
                + style.FramePadding.X * 2f
                + ImGui.GetFrameHeight();
            var avail = ImGui.GetContentRegionAvail().X;
            var inputW = MathF.Max(60f, avail - comboW - style.ItemSpacing.X);
            ImGui.SetNextItemWidth(inputW);

            var buffer = current ?? string.Empty;
            if (ImGui.InputText($"##{label}_endpoint", ref buffer, 512))
            {
                next = buffer;
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(comboW);
            if (ImGui.BeginCombo("##presets", previewText))
            {
                foreach (var (presetLabel, url) in presets)
                {
                    if (ImGui.Selectable(presetLabel))
                    {
                        next = url;
                    }
                }

                ImGui.EndCombo();
            }
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static string Normalize(string? url) => (url ?? string.Empty).Trim().TrimEnd('/');
}

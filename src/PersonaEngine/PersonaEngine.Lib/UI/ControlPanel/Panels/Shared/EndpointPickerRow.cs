namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;

using Hexa.NET.ImGui;

/// <summary>
///     Endpoint picker: single-line text input with a compact Presets popup. When the
///     current value matches a preset (case-insensitive, trailing-slash tolerant) its
///     label is shown as a hint; otherwise renders a subtle "Custom" hint. The caller
///     mutates configuration only when <paramref name="next" /> is non-null.
/// </summary>
public static class EndpointPickerRow
{
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
    ///     Renders the endpoint input + presets popup + hint. Sets <paramref name="next" />
    ///     to the new URL when the user edited the text or picked a preset; otherwise leaves
    ///     it <see langword="null" />.
    /// </summary>
    public static void Render(
        string label,
        string tooltip,
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
            var buffer = current ?? string.Empty;
            if (ImGui.InputText($"##{label}_endpoint", ref buffer, 512))
            {
                next = buffer;
            }

            ImGui.SameLine();
            if (ImGui.Button("Presets \u25BE"))
            {
                ImGui.OpenPopup($"{label}_presets_popup");
            }

            if (ImGui.BeginPopup($"{label}_presets_popup"))
            {
                foreach (var (presetLabel, url) in presets)
                {
                    if (ImGui.MenuItem(presetLabel))
                    {
                        next = url;
                    }
                }

                ImGui.EndPopup();
            }

            var match = MatchPreset(presets, current ?? string.Empty);
            var hint = match ?? "Custom";
            ImGui.TextDisabled(hint);

            if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(tooltip);
            }
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static string Normalize(string? url) => (url ?? string.Empty).Trim().TrimEnd('/');
}

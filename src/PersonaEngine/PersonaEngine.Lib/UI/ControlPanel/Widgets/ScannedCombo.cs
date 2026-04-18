using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;

namespace PersonaEngine.Lib.UI.ControlPanel;

public static partial class ImGuiHelpers
{
    /// <summary>
    ///     Renders a combo + Refresh button pair backed by a <see cref="ScannedNamePicker" />.
    ///     The combo auto-shows a "(none)" disabled placeholder when the picker is empty,
    ///     keeps the saved name selected even when it's not in the scan, and calls
    ///     <paramref name="onRefresh" /> when the user clicks Refresh.
    /// </summary>
    /// <param name="id">ImGui id suffix; the helper prefixes "##" internally.</param>
    /// <param name="picker">The scan-state owner. Callers <b>must</b> call
    ///     <see cref="ScannedNamePicker.Refresh" /> at least once before first render.</param>
    /// <param name="savedName">The currently persisted name (from options).</param>
    /// <param name="selected">When the method returns <see langword="true"/>, contains
    ///     the clean name (suffix stripped) the user picked.</param>
    /// <param name="onRefresh">Invoked when Refresh is clicked. Implementations typically
    ///     call <see cref="ScannedNamePicker.Refresh(string?)" /> with the current saved name.</param>
    /// <param name="refreshTooltip">Tooltip for the Refresh button.</param>
    /// <param name="refreshButtonWidth">Width of the Refresh button in pixels.</param>
    /// <returns><see langword="true"/> when the user selected a different entry.</returns>
    internal static bool ScannedCombo(
        string id,
        ScannedNamePicker picker,
        string savedName,
        out string selected,
        Action onRefresh,
        string refreshTooltip,
        float refreshButtonWidth = 100f
    )
    {
        selected = savedName;
        var changed = false;

        var selectedIndex = picker.IndexOf(savedName);

        // Shorten the combo to leave room for the Refresh button on the same line.
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - (refreshButtonWidth + 10f));

        if (picker.Choices.Count > 0)
        {
            var choices = picker.Choices is string[] arr ? arr : picker.Choices.ToArray();
            if (ImGui.Combo($"##{id}", ref selectedIndex, choices, choices.Length))
            {
                var stripped = ScannedNamePicker.StripSuffix(choices[selectedIndex]);
                if (!string.Equals(stripped, savedName, StringComparison.Ordinal))
                {
                    selected = stripped;
                    changed = true;
                }
            }
        }
        else
        {
            // Nothing to pick — disabled "(none)" placeholder keeps row layout stable.
            ImGui.BeginDisabled();
            var empty = 0;
            var placeholder = new[] { "(none)" };
            ImGui.Combo($"##{id}", ref empty, placeholder, placeholder.Length);
            ImGui.EndDisabled();
        }

        HandCursorOnHover();
        ImGui.SameLine();

        if (ImGui.Button($"Refresh##{id}", new Vector2(refreshButtonWidth, 0f)))
        {
            onRefresh();
        }

        HandCursorOnHover();
        Tooltip(refreshTooltip);

        return changed;
    }
}

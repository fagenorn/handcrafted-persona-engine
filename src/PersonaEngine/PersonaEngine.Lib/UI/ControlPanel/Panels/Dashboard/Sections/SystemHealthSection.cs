using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

/// <summary>
///     Cosmetic system health cards (Microphone, LLM, TTS, Spout).
///     All indicators are currently static — the status bar provides live conversation state.
/// </summary>
public sealed class SystemHealthSection
{
    private const float CardHeight = 88f;

    private static readonly (string Name, string StatusText)[] HealthCards =
    [
        ("Microphone", "OK"),
        ("LLM", "OK"),
        ("TTS", "OK"),
        ("Spout", "OK"),
    ];

    public void Render(float dt)
    {
        ImGuiHelpers.SectionHeader("System Health");

        using var cols = Ui.EqualCols(HealthCards.Length, CardHeight, gap: 12f);

        for (var i = 0; i < HealthCards.Length; i++)
        {
            if (!cols.NextCol())
                continue;

            using (Ui.Card(id: $"##health_{HealthCards[i].Name}", padding: 15f))
            {
                RenderHealthCard(HealthCards[i].Name, HealthCards[i].StatusText);
            }
        }
    }

    private static void RenderHealthCard(string name, string statusText)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(name);
        ImGui.PopStyleColor();

        ImGui.SameLine(0f, 8f);
        ImGuiHelpers.StatusDot(Theme.Success);

        ImGui.TextUnformatted(statusText);
    }
}

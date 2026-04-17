using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

/// <summary>
///     Dashboard health-strip: four cosmetic cards (Microphone / LLM / TTS /
///     Spout) summarising subsystem status at a glance. Currently hard-coded
///     placeholders — live wiring will happen as each subsystem gains a health
///     probe. The Overlay presence control lives in its own
///     <see cref="PresenceStripSection" /> above this row.
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

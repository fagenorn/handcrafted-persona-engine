using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.Overlay;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

/// <summary>
///     Dashboard health-strip: four cosmetic cards (Microphone / LLM / TTS /
///     Spout) followed by a live Overlay quick-toggle. Cosmetic cards are
///     placeholders — the status bar provides real conversation state.
/// </summary>
public sealed class SystemHealthSection(OverlayHost overlayHost)
{
    private const float CardHeight = 88f;

    private static readonly (string Name, string StatusText)[] HealthCards =
    [
        ("Microphone", "OK"),
        ("LLM", "OK"),
        ("TTS", "OK"),
        ("Spout", "OK"),
    ];

    // Monotonically increasing — drives the StatusPill pulse animation.
    private float _elapsed;

    public void Render(float dt)
    {
        _elapsed += dt;

        ImGuiHelpers.SectionHeader("System Health");

        // One extra column for the live Overlay card.
        using var cols = Ui.EqualCols(HealthCards.Length + 1, CardHeight, gap: 12f);

        for (var i = 0; i < HealthCards.Length; i++)
        {
            if (!cols.NextCol())
                continue;

            using (Ui.Card(id: $"##health_{HealthCards[i].Name}", padding: 15f))
            {
                RenderHealthCard(HealthCards[i].Name, HealthCards[i].StatusText);
            }
        }

        if (cols.NextCol())
        {
            using (Ui.Card(id: "##health_Overlay", padding: 15f))
            {
                RenderOverlayCard();
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

    private void RenderOverlayCard()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted("Overlay");
        ImGui.PopStyleColor();

        ImGui.SameLine(0f, 8f);
        var desired = overlayHost.DesiredEnabled;
        if (ImGui.Checkbox("##DashOverlayEnabled", ref desired))
        {
            overlayHost.SetEnabled(desired);
        }
        ImGuiHelpers.HandCursorOnHover();

        StatusPill.Render(overlayHost.Status, _elapsed, overlayHost.LastError);
    }
}

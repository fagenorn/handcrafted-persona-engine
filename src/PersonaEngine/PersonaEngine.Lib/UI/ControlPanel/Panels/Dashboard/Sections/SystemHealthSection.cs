using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.Health;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

/// <summary>
///     Dashboard health strip. One <see cref="Ui.Card" /> per registered
///     <see cref="ISubsystemHealthProbe" />, coloured dot + label + click-through
///     navigation via <see cref="INavRequestBus" />. Registration order in DI
///     defines column order (Mic → LLM → TTS → Spout).
/// </summary>
public sealed class SystemHealthSection : IDisposable
{
    private const float CardHeight = 88f;

    private readonly INavRequestBus _nav;
    private readonly IReadOnlyList<ISubsystemHealthProbe> _probes;

    public SystemHealthSection(IEnumerable<ISubsystemHealthProbe> probes, INavRequestBus nav)
    {
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(nav);

        _probes = probes.ToArray();
        _nav = nav;

        foreach (var p in _probes)
        {
            p.StatusChanged += OnProbeChanged;
        }
    }

    public void Render(float dt)
    {
        ImGuiHelpers.SectionHeader("System Health");

        if (_probes.Count == 0)
        {
            ImGui.TextUnformatted("No health probes registered.");
            return;
        }

        using var cols = Ui.EqualCols(_probes.Count, CardHeight, gap: 12f);

        for (var i = 0; i < _probes.Count; i++)
        {
            if (!cols.NextCol())
            {
                continue;
            }

            var probe = _probes[i];
            using (Ui.Card(id: $"##health_{probe.Name}", padding: 15f))
            {
                RenderCard(probe);
            }
        }
    }

    public void Dispose()
    {
        foreach (var p in _probes)
        {
            p.StatusChanged -= OnProbeChanged;
        }
    }

    private void RenderCard(ISubsystemHealthProbe probe)
    {
        var status = probe.Current;
        var (dotColor, labelColor) = Palette(status.Health);

        // Capture the card's content rect for the click-through overlay.
        var cardMin = ImGui.GetItemRectMin();
        var cardMax = ImGui.GetItemRectMax();

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(probe.Name);
        ImGui.PopStyleColor();

        ImGui.SameLine(0f, 8f);
        ImGuiHelpers.StatusDot(dotColor);

        ImGui.PushStyleColor(ImGuiCol.Text, labelColor);
        ImGui.TextUnformatted(status.Label);
        ImGui.PopStyleColor();

        var size = cardMax - cardMin;
        ImGui.SetCursorScreenPos(cardMin);
        ImGui.InvisibleButton($"##nav_{probe.Name}", size);
        ImGuiHelpers.HandCursorOnHover();

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            _nav.Request(probe.TargetPanel);
        }

        if (!string.IsNullOrEmpty(status.Detail) && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(status.Detail);
        }
    }

    private static (Vector4 Dot, Vector4 Label) Palette(SubsystemHealth h) =>
        h switch
        {
            SubsystemHealth.Healthy => (Theme.Success, Theme.TextPrimary),
            SubsystemHealth.Degraded => (Theme.Warning, Theme.TextPrimary),
            SubsystemHealth.Failed => (Theme.Error, Theme.TextPrimary),
            SubsystemHealth.Unknown => (Theme.TextTertiary, Theme.TextTertiary),
            SubsystemHealth.Disabled => (Theme.TextTertiary, Theme.TextTertiary),
            _ => (Theme.TextTertiary, Theme.TextTertiary),
        };

    private void OnProbeChanged(SubsystemStatus _)
    {
        // ImGui is immediate-mode: we redraw every frame anyway.
        // The event hook exists so future code (toast, animation, audio cue)
        // has a transition edge to latch onto.
    }
}

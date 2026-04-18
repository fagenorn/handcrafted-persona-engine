using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.Health;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

/// <summary>
///     Dashboard health strip. One <see cref="Ui.Card" /> per registered
///     <see cref="ISubsystemHealthProbe" />, coloured dot + label + click-through
///     navigation via <see cref="INavRequestBus" />. Registration order in DI
///     defines column order (Mic → LLM → TTS).
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

        // Claim the hit region first so the entire card is clickable. We're
        // inside the Ui.Card child scope here; GetItemRectMin/Max would
        // return the last item from the OUTER scope, so we use the current
        // cursor / available content region instead.
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();

        // Skip the hit region on frames where the column has zero width/height
        // (e.g. first frame before layout settles) — InvisibleButton asserts on
        // zero-size.
        if (avail.X > 0f && avail.Y > 0f)
        {
            ImGui.InvisibleButton($"##nav_{probe.Name}", avail);
            ImGuiHelpers.HandCursorOnHover();

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                _nav.Request(probe.TargetPanel);
            }

            if (!string.IsNullOrEmpty(status.Detail) && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(status.Detail);
            }

            // Reset cursor to the origin so the visual content draws on top of
            // the invisible hit region.
            ImGui.SetCursorScreenPos(origin);
        }

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(probe.Name);
        ImGui.PopStyleColor();

        ImGui.SameLine(0f, 8f);
        ImGuiHelpers.StatusDot(dotColor);

        ImGui.PushStyleColor(ImGuiCol.Text, labelColor);
        ImGui.TextUnformatted(status.Label);
        ImGui.PopStyleColor();
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

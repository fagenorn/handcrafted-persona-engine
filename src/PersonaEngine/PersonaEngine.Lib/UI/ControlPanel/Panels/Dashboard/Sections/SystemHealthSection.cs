using Hexa.NET.ImGui;
using PersonaEngine.Lib.Health;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

/// <summary>
///     Dashboard health strip. One <see cref="Ui.Card" /> per registered
///     <see cref="ISubsystemHealthProbe" />, coloured dot + label + click-through
///     navigation via <see cref="INavRequestBus" />. Registration order in DI
///     defines column order (Mic → LLM → TTS).
/// </summary>
public sealed class SystemHealthSection : IDisposable
{
    // Offset from the cursor origin to the chip's visible RIGHT edge. The
    // chip's broadcast tint paints padX(8) to the LEFT of the cursor and
    // padX(8) + dot diameter(10) + gap(6) = 24 to the right of the cursor
    // (plus the label width). So `newCursor + 24 + labelW` is where the
    // visible right edge lands — that's what we right-align to the content
    // region right edge so the chip's visible padding matches the title's
    // visible left padding inside the Ui.Card.
    private const float ChipRightEdgeOffsetFromCursor = 24f;

    // Guaranteed breathing room between the title's right edge and the chip's
    // visible left edge (chip tint extends 8 px left of cursor). Subtracted
    // from the label budget so a long status label truncates before it can
    // crowd the title, even when the card is narrow.
    private const float MinTitleChipGap = 8f;

    private readonly INavRequestBus _nav;
    private readonly IReadOnlyList<ISubsystemHealthProbe> _probes;

    // Pre-built per-probe ImGui child ids — "##health_{probe.Name}". Computing
    // these once avoids a fresh string interpolation per probe per frame.
    private readonly string[] _cardIds;

    // Monotonic animation clock — owned here so the chip's live-ring phase is
    // stable-precision (float loses enough precision at UTC-epoch magnitudes
    // that `elapsed % 1.6f` stops varying).
    private float _elapsed;

    private bool _disposed;

    public SystemHealthSection(IEnumerable<ISubsystemHealthProbe> probes, INavRequestBus nav)
    {
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(nav);

        _probes = probes.ToArray();
        _nav = nav;

        _cardIds = new string[_probes.Count];
        for (var i = 0; i < _probes.Count; i++)
        {
            _cardIds[i] = $"##health_{_probes[i].Name}";
        }

        foreach (var p in _probes)
        {
            p.StatusChanged += OnProbeChanged;
        }
    }

    public void Render(float dt)
    {
        _elapsed += dt;
        ImGuiHelpers.SectionHeader("System Health");

        if (_probes.Count == 0)
        {
            ImGui.TextUnformatted("No health probes registered.");
            return;
        }

        // Grid (auto-height table) lets each card size to its own content and
        // equalises to the tallest card, instead of forcing a hardcoded height
        // on Ui.EqualCols. Ui.Card uses AutoResizeY so the card grows around
        // label + chip + optional detail line without spawning a scrollbar.
        using var grid = Ui.Grid("##health_grid", _probes.Count);
        grid.Row();

        for (var i = 0; i < _probes.Count; i++)
        {
            grid.Col();

            var probe = _probes[i];
            using (Ui.Card(id: _cardIds[i], padding: 15f))
            {
                RenderCard(probe);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var p in _probes)
        {
            p.StatusChanged -= OnProbeChanged;
        }
    }

    private void RenderCard(ISubsystemHealthProbe probe)
    {
        var status = probe.Current;

        // Row 1: probe name (left) + status chip (right-aligned).
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted(probe.Name);
        ImGui.PopStyleColor();

        ImGui.SameLine();
        var rowAvail = ImGui.GetContentRegionAvail().X;
        // Label budget: whatever's left in the row minus the chip's right-of-cursor
        // chrome and a guaranteed gap so the chip never crowds the title.
        var labelBudget = Math.Max(0f, rowAvail - ChipRightEdgeOffsetFromCursor - MinTitleChipGap);
        // Measure the label the chip will ACTUALLY draw (post-truncation) — otherwise
        // the cursor offset below over-shoots and the chip floats left of the edge.
        var displayLabel = SubsystemStatusChip.TruncateLabel(status.Label, labelBudget);
        var labelW = ImGui.CalcTextSize(displayLabel).X;
        ImGui.SetCursorPosX(
            ImGui.GetCursorPosX() + (rowAvail - (labelW + ChipRightEdgeOffsetFromCursor))
        );
        SubsystemStatusChip.Render(status, _elapsed, maxLabelWidth: labelBudget);

        // Whole-card hit region: we can't use InvisibleButton up front the way
        // EqualCols did, because Ui.Card is AutoResizeY and GetContentRegionAvail().Y
        // is unbounded. Instead, check hover on the current (child) window after
        // content has been laid out — its rect is the card's final size.
        if (ImGui.IsWindowHovered())
        {
            ImGuiHelpers.HandCursorOnHover();

            if (!string.IsNullOrEmpty(status.Detail))
            {
                ImGui.SetTooltip(status.Detail);
            }

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _nav.Request(probe.TargetPanel);
            }
        }
    }

    private void OnProbeChanged(SubsystemStatus _)
    {
        // ImGui is immediate-mode: we redraw every frame anyway.
        // The event hook exists so future code (toast, animation, audio cue)
        // has a transition edge to latch onto.
    }
}

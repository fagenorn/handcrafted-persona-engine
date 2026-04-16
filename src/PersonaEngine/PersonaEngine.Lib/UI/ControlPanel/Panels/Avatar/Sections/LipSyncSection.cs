using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Avatar.Sections;

/// <summary>
///     Lip-sync engine card. The Style chips pick between the VBridger (phoneme-based,
///     "Simple") and Audio2Face (ML-based, "Realistic") engines. When Audio2Face is
///     active, two sub-settings appear below: Quality (Accurate / Fast chips, mapping
///     to BVLS / PGD solver types) and Use GPU (ToggleSwitch). Quality comes first
///     because it's the user-facing fidelity knob; Use GPU is the performance lever.
///     <para>
///         <see cref="Audio2FaceOptions.Identity" /> is intentionally not exposed —
///         see the Avatar panel redesign spec (Non-Goals).
///     </para>
/// </summary>
public sealed class LipSyncSection : IDisposable
{
    private const string EngineSimple = "VBridger";
    private const string EngineRealistic = "Audio2Face";
    private const string SolverAccurate = "BVLS";
    private const string SolverFast = "PGD";
    private const float ChipGap = 6f;

    private readonly IConfigWriter _configWriter;
    private readonly IDisposable? _changeSubscription;

    private LipSyncOptions _lipSync;
    private AnimatedFloat _useGpuKnob;
    private bool _initialized;

    public LipSyncSection(IOptionsMonitor<LipSyncOptions> monitor, IConfigWriter configWriter)
    {
        _configWriter = configWriter;
        _lipSync = monitor.CurrentValue;
        _changeSubscription = monitor.OnChange(
            (updated, _) =>
            {
                _lipSync = updated;
                if (!_initialized)
                    return;
                _useGpuKnob.Target = updated.Audio2Face.UseGpu ? 1f : 0f;
            }
        );
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt)
    {
        if (!_initialized)
        {
            _useGpuKnob = new AnimatedFloat(_lipSync.Audio2Face.UseGpu ? 1f : 0f);
            _initialized = true;
        }

        using (Ui.Card("##lipsync", padding: 12f))
        {
            // Header
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted("Lip Sync");
            ImGui.PopStyleColor();

            // Description
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted("How the avatar's mouth follows your speech");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            RenderStyleRow();

            if (string.Equals(_lipSync.Engine, EngineRealistic, StringComparison.OrdinalIgnoreCase))
            {
                RenderQualityRow();
                RenderUseGpuRow(dt);
            }
        }
    }

    // ── Style chips (Simple / Realistic) ──────────────────────────────────────

    private void RenderStyleRow()
    {
        var rowY = ImGui.GetCursorPosY();

        ImGuiHelpers.SettingLabel(
            "Style",
            "Simple is fast and works everywhere. Realistic uses a neural model for richer mouth shapes."
        );

        var isSimple = string.Equals(
            _lipSync.Engine,
            EngineSimple,
            StringComparison.OrdinalIgnoreCase
        );
        var isRealistic = string.Equals(
            _lipSync.Engine,
            EngineRealistic,
            StringComparison.OrdinalIgnoreCase
        );

        if (ImGuiHelpers.Chip("Simple", isSimple))
        {
            if (!isSimple)
                WriteEngine(EngineSimple);
        }

        ImGui.SameLine(0f, ChipGap);

        if (ImGuiHelpers.Chip("Realistic", isRealistic))
        {
            if (!isRealistic)
                WriteEngine(EngineRealistic);
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    private void WriteEngine(string engine)
    {
        _lipSync = _lipSync with { Engine = engine };
        _configWriter.Write(_lipSync);
    }

    // ── Use GPU toggle ────────────────────────────────────────────────────────

    private void RenderUseGpuRow(float dt)
    {
        var rowY = ImGui.GetCursorPosY();

        ImGuiHelpers.SettingLabel(
            "Use GPU",
            "Run lip sync on your graphics card. Faster, but needs a capable GPU."
        );

        var useGpu = _lipSync.Audio2Face.UseGpu;
        if (ImGuiHelpers.ToggleSwitch("##a2f_use_gpu", ref useGpu, ref _useGpuKnob, dt))
        {
            _lipSync = _lipSync with { Audio2Face = _lipSync.Audio2Face with { UseGpu = useGpu } };
            _configWriter.Write(_lipSync);
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    // ── Quality chips (Accurate / Fast) ───────────────────────────────────────

    private void RenderQualityRow()
    {
        var rowY = ImGui.GetCursorPosY();

        ImGuiHelpers.SettingLabel(
            "Quality",
            "Accurate gives the best-looking mouth shapes. Fast trades a bit of detail for lower latency."
        );

        var solver = _lipSync.Audio2Face.SolverType;
        var isAccurate = string.Equals(solver, SolverAccurate, StringComparison.OrdinalIgnoreCase);
        var isFast = string.Equals(solver, SolverFast, StringComparison.OrdinalIgnoreCase);

        if (ImGuiHelpers.Chip("Accurate", isAccurate))
        {
            if (!isAccurate)
                WriteSolver(SolverAccurate);
        }

        ImGui.SameLine(0f, ChipGap);

        if (ImGuiHelpers.Chip("Fast", isFast))
        {
            if (!isFast)
                WriteSolver(SolverFast);
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    private void WriteSolver(string solver)
    {
        _lipSync = _lipSync with { Audio2Face = _lipSync.Audio2Face with { SolverType = solver } };
        _configWriter.Write(_lipSync);
    }
}

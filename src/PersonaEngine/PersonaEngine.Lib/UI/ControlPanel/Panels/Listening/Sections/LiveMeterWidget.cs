using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.ASR.VAD;
using PersonaEngine.Lib.Audio;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Listening.Sections;

/// <summary>
///     Two-stack live meter: amplitude bar + trailing sparkline on top, VAD probability
///     trace with threshold line on bottom. Pure ImDrawList draw, zero per-frame allocs.
/// </summary>
public sealed class LiveMeterWidget
{
    private const float WidgetHeight = 120f;
    private const float Gap = 6f;
    private const float UiSmoothingRate = 15f;

    private readonly IMicrophoneAmplitudeProvider _micAmp;
    private readonly IVadProbabilityProvider _vadProb;

    private float _displayAmp;

    public LiveMeterWidget(IMicrophoneAmplitudeProvider micAmp, IVadProbabilityProvider vadProb)
    {
        _micAmp = micAmp;
        _vadProb = vadProb;
    }

    public void Render(float dt, float currentThreshold, bool isThresholdActive)
    {
        // Extra UI-side smoothing for a visually mellow amplitude bar.
        var alpha = 1f - MathF.Exp(-UiSmoothingRate * dt);
        _displayAmp += (_micAmp.CurrentAmplitude - _displayAmp) * alpha;

        var avail = ImGui.GetContentRegionAvail();
        var size = new Vector2(avail.X, WidgetHeight);
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        // Background panel
        var bg = origin;
        var bgMax = origin + size;
        ImGui.AddRectFilled(
            drawList,
            bg,
            bgMax,
            ImGui.ColorConvertFloat4ToU32(Theme.Surface3),
            ImGui.GetStyle().FrameRounding
        );

        // Split into two halves with a gap
        var halfH = (size.Y - Gap) * 0.5f;
        var topMin = origin + new Vector2(0f, 0f);
        var topMax = origin + new Vector2(size.X, halfH);
        var botMin = origin + new Vector2(0f, halfH + Gap);
        var botMax = origin + size;

        RenderAmplitudeRow(drawList, topMin, topMax);
        RenderProbabilityRow(drawList, botMin, botMax, currentThreshold, isThresholdActive);

        ImGui.Dummy(size);
    }

    private void RenderAmplitudeRow(ImDrawListPtr drawList, Vector2 min, Vector2 max)
    {
        var width = max.X - min.X;
        var height = max.Y - min.Y;
        var barWidth = MathF.Floor(width * 0.30f);
        var barPadding = 6f;

        // Amplitude pill bar
        var barMin = min + new Vector2(barPadding, barPadding);
        var barMax = new Vector2(min.X + barWidth - barPadding, max.Y - barPadding);
        var fillHeight = (barMax.X - barMin.X) * Math.Clamp(_displayAmp * 4f, 0f, 1f);
        var filledMax = new Vector2(barMin.X + fillHeight, barMax.Y);
        var barColor = LerpColor(
            Theme.TextTertiary,
            Theme.AccentPrimary,
            Math.Clamp(_displayAmp * 4f, 0f, 1f)
        );

        ImGui.AddRectFilled(
            drawList,
            barMin,
            barMax,
            ImGui.ColorConvertFloat4ToU32(Theme.Surface1),
            (barMax.Y - barMin.Y) * 0.5f
        );
        ImGui.AddRectFilled(
            drawList,
            barMin,
            filledMax,
            ImGui.ColorConvertFloat4ToU32(barColor),
            (barMax.Y - barMin.Y) * 0.5f
        );

        // Sparkline in the remaining width
        var sparkMin = new Vector2(min.X + barWidth + barPadding, min.Y + barPadding);
        var sparkMax = new Vector2(max.X - barPadding, max.Y - barPadding);
        DrawSparkline(
            drawList,
            sparkMin,
            sparkMax,
            _micAmp.History,
            _micAmp.HistoryHead,
            Theme.AccentSecondary,
            1.5f,
            0f,
            1f
        );
    }

    private void RenderProbabilityRow(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float threshold,
        bool isThresholdActive
    )
    {
        var padding = 6f;
        var innerMin = min + new Vector2(padding, padding);
        var innerMax = max - new Vector2(padding, padding);
        var height = innerMax.Y - innerMin.Y;

        // Sparkline of VAD probability
        DrawSparkline(
            drawList,
            innerMin,
            innerMax,
            _vadProb.History,
            _vadProb.HistoryHead,
            _vadProb.CurrentProbability > threshold ? Theme.AccentPrimary : Theme.TextTertiary,
            _vadProb.CurrentProbability > threshold ? 2f : 1.25f,
            0f,
            1f
        );

        // Threshold line
        var thresholdY = innerMax.Y - height * Math.Clamp(threshold, 0f, 1f);
        var lineColor = isThresholdActive
            ? Theme.AccentPrimary
            : Theme.TextTertiary with
            {
                W = 0.7f,
            };
        ImGui.AddLine(
            drawList,
            new Vector2(innerMin.X, thresholdY),
            new Vector2(innerMax.X, thresholdY),
            ImGui.ColorConvertFloat4ToU32(lineColor),
            isThresholdActive ? 2f : 1f
        );
    }

    private static void DrawSparkline(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        ReadOnlySpan<float> history,
        int head,
        Vector4 color,
        float thickness,
        float valueMin,
        float valueMax
    )
    {
        if (history.Length < 2)
            return;

        var width = max.X - min.X;
        var height = max.Y - min.Y;
        var range = MathF.Max(0.0001f, valueMax - valueMin);
        var step = width / (history.Length - 1);
        var col = ImGui.ColorConvertFloat4ToU32(color);

        Vector2 previous = default;
        for (var i = 0; i < history.Length; i++)
        {
            // Oldest sample is at head; newest at (head - 1 + N) % N
            var idx = (head + i) % history.Length;
            var v = Math.Clamp(history[idx], valueMin, valueMax);
            var t = (v - valueMin) / range;
            var point = new Vector2(min.X + step * i, max.Y - height * t);
            if (i > 0)
            {
                ImGui.AddLine(drawList, previous, point, col, thickness);
            }
            previous = point;
        }
    }

    private static Vector4 LerpColor(Vector4 a, Vector4 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Vector4(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            a.W + (b.W - a.W) * t
        );
    }
}

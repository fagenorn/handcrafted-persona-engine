using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.ASR.VAD;
using PersonaEngine.Lib.Audio;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Listening.Sections;

/// <summary>
///     Live calibration meter for the Listening panel. Shows a scrolling trace of the
///     Silero VAD probability with above-threshold shading, a draggable threshold line
///     labelled "Sensitivity", a state banner ("Silent" / "Picking up sound" /
///     "Listening to you"), and a compact mic-alive amplitude pip.
///     <para>
///         The trace samples <see cref="IVadProbabilityProvider.CurrentProbability" />
///         once per render frame and applies an asymmetric envelope follower (instant
///         attack, slow release) so speech onset is sharp while the decay flows smoothly.
///         Points use wall-clock timestamps for X positioning, producing continuous
///         scrolling with no discrete ring-buffer jumps.
///     </para>
/// </summary>
public sealed class LiveMeterWidget
{
    private const float WidgetHeight = 176f;
    private const float BannerHeight = 34f;
    private const float BannerGap = 6f;
    private const float MicPipSmoothingRate = 15f;
    private const float TimeWindow = 4f;

    // Asymmetric envelope on VAD probability: instant attack preserves sharp speech
    // onset; slow release creates a flowing tail instead of a hard drop.
    private const float AttackTime = 0.005f;
    private const float ReleaseTime = 0.250f;

    // Power-of-two ring capacity lets us use bitmask wrap (& CapacityMask) instead
    // of modulo. 512 ≫ any plausible per-frame sampling rate across TimeWindow; old
    // entries past the window are filtered at draw time, not evicted.
    private const int SampleCapacity = 512;
    private const int CapacityMask = SampleCapacity - 1;

    private readonly IMicrophoneAmplitudeProvider _micAmp;
    private readonly IVadProbabilityProvider _vadProb;

    private float _displayMicAmp;
    private float _envelope;
    private float _elapsed;
    private bool _isDraggingThreshold;

    // Fixed-capacity ring: _samples[(_head - _count + i) & CapacityMask] yields
    // entries oldest→newest for i in [0, _count). Writes land at _head, then advance.
    private readonly TraceSample[] _samples = new TraceSample[SampleCapacity];
    private int _head;
    private int _count;

    public LiveMeterWidget(IMicrophoneAmplitudeProvider micAmp, IVadProbabilityProvider vadProb)
    {
        _micAmp = micAmp;
        _vadProb = vadProb;
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    ///     Renders the meter. <paramref name="threshold" /> is bidirectional — the user
    ///     can drag the threshold line to adjust it. Returns <see langword="true" /> when
    ///     the user modified the value this frame.
    /// </summary>
    public bool Render(float dt, ref float threshold, bool isExternallyActive)
    {
        AdvanceMicSmoothing(dt);
        UpdateTrace(dt);

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();

        // Banner — drawn via draw list; Dummy reserves layout space.
        var bannerMin = ImGui.GetCursorScreenPos();
        var bannerSize = new Vector2(availableWidth, BannerHeight);
        ImGui.Dummy(bannerSize);
        RenderStateBanner(drawList, bannerMin, bannerMin + bannerSize, threshold);

        ImGui.Dummy(new Vector2(0f, BannerGap));

        // Trace area — Dummy reserves layout; draw list renders on top.
        var traceHeight = WidgetHeight - BannerHeight - BannerGap;
        var traceOrigin = ImGui.GetCursorScreenPos();
        var traceSize = new Vector2(availableWidth, traceHeight);
        ImGui.Dummy(traceSize);

        return RenderTraceWithDraggableThreshold(
            drawList,
            traceOrigin,
            traceOrigin + traceSize,
            ref threshold,
            isExternallyActive
        );
    }

    // ── Data update ─────────────────────────────────────────────────────────

    private void AdvanceMicSmoothing(float dt)
    {
        var target = _micAmp.CurrentAmplitude;
        var alpha = 1f - MathF.Exp(-MicPipSmoothingRate * dt);
        _displayMicAmp += (target - _displayMicAmp) * alpha;
    }

    private void UpdateTrace(float dt)
    {
        _elapsed += dt;

        // Asymmetric envelope: snap up on speech onset, flow down on speech end.
        var rawProb = _vadProb.CurrentProbability;
        var coeff =
            rawProb > _envelope
                ? 1f - MathF.Exp(-dt / AttackTime)
                : 1f - MathF.Exp(-dt / ReleaseTime);
        _envelope += (rawProb - _envelope) * coeff;

        // Ring-buffer write: overwrite at head, advance with wrap. When full, the
        // oldest sample is silently overwritten — it's outside TimeWindow anyway,
        // and the draw path skips entries older than the cutoff.
        _samples[_head] = new TraceSample(_elapsed, _envelope);
        _head = (_head + 1) & CapacityMask;
        if (_count < SampleCapacity)
            _count++;
    }

    /// <summary>Sample at chronological index <paramref name="i" /> (0 = oldest, <see cref="_count" /> - 1 = newest).</summary>
    private TraceSample GetSample(int i) =>
        _samples[(_head - _count + i) & CapacityMask];

    /// <summary>First chronological index whose timestamp is within the visible window.</summary>
    private int FirstVisibleIndex()
    {
        var cutoff = _elapsed - TimeWindow;
        var start = 0;
        while (start < _count && GetSample(start).Time < cutoff)
            start++;
        return start;
    }

    // ── Banner ──────────────────────────────────────────────────────────────

    private void RenderStateBanner(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float threshold
    )
    {
        ImGui.AddRectFilled(drawList, min, max, ImGui.ColorConvertFloat4ToU32(Theme.Surface1), 8f);

        var (label, color) = ClassifyState(threshold);

        var padX = 14f;
        var dotRadius = 5f;
        var dotCenter = new Vector2(min.X + padX + dotRadius, (min.Y + max.Y) * 0.5f);
        ImGui.AddCircleFilled(drawList, dotCenter, dotRadius, ImGui.ColorConvertFloat4ToU32(color));
        ImGui.AddCircleFilled(
            drawList,
            dotCenter,
            dotRadius * 2.2f,
            ImGui.ColorConvertFloat4ToU32(color with { W = 0.25f })
        );

        var textSize = ImGui.CalcTextSize(label);
        var textPos = new Vector2(
            dotCenter.X + dotRadius + 10f,
            (min.Y + max.Y) * 0.5f - textSize.Y * 0.5f
        );
        drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(Theme.TextPrimary), label);

        RenderMicPip(drawList, max);
    }

    private void RenderMicPip(ImDrawListPtr drawList, Vector2 bannerMax)
    {
        const int pipCount = 6;
        const float pipWidth = 4f;
        const float pipGap = 2f;
        const float pipTallest = 16f;
        const float pipShortest = 4f;
        const float padX = 14f;

        var totalWidth = pipCount * pipWidth + (pipCount - 1) * pipGap;
        var startX = bannerMax.X - padX - totalWidth;
        var centerY = bannerMax.Y - BannerHeight * 0.5f;

        var active = Math.Clamp(_displayMicAmp * pipCount * 2.2f, 0f, pipCount);
        for (var i = 0; i < pipCount; i++)
        {
            var t = (i + 1) / (float)pipCount;
            var pipHeight = pipShortest + (pipTallest - pipShortest) * t;
            var x = startX + i * (pipWidth + pipGap);
            var yMin = centerY - pipHeight * 0.5f;
            var yMax = centerY + pipHeight * 0.5f;

            var litAmount = Math.Clamp(active - i, 0f, 1f);
            var col = Vector4.Lerp(Theme.Surface3, Theme.AccentPrimary, litAmount);
            ImGui.AddRectFilled(
                drawList,
                new Vector2(x, yMin),
                new Vector2(x + pipWidth, yMax),
                ImGui.ColorConvertFloat4ToU32(col),
                1.5f
            );
        }
    }

    private (string Label, Vector4 Color) ClassifyState(float threshold)
    {
        if (_vadProb.CurrentProbability > threshold)
            return ("Listening to you", Theme.AccentPrimary);

        if (_displayMicAmp > 0.02f)
            return ("Picking up sound", Theme.AccentSecondary);

        return ("Silent", Theme.TextTertiary);
    }

    // ── Trace + draggable threshold ─────────────────────────────────────────

    private bool RenderTraceWithDraggableThreshold(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        ref float threshold,
        bool isExternallyActive
    )
    {
        ImGui.AddRectFilled(drawList, min, max, ImGui.ColorConvertFloat4ToU32(Theme.Surface3), 8f);

        const float inset = 8f;
        var plotMin = min + new Vector2(inset, inset);
        var plotMax = max - new Vector2(inset, inset);
        var plotWidth = plotMax.X - plotMin.X;
        var plotHeight = plotMax.Y - plotMin.Y;
        var thresholdY = plotMax.Y - plotHeight * Math.Clamp(threshold, 0f, 1f);

        // Drag interaction — positioned explicitly so InvisibleButton doesn't
        // consume layout space (the parent Dummy already reserved it).
        const float grabHalfHeight = 10f;
        var cursorBefore = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(plotMin);
        _ = ImGui.InvisibleButton("##meter_drag", plotMax - plotMin);
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();
        ImGui.SetCursorScreenPos(cursorBefore);

        var thresholdChanged = false;
        var mousePos = ImGui.GetMousePos();
        var nearLine = hovered && MathF.Abs(mousePos.Y - thresholdY) <= grabHalfHeight;

        if (held && (nearLine || _isDraggingThreshold))
        {
            _isDraggingThreshold = true;
            var normalized = Math.Clamp((plotMax.Y - mousePos.Y) / plotHeight, 0.1f, 0.95f);
            if (MathF.Abs(normalized - threshold) > 1e-4f)
            {
                threshold = normalized;
                thresholdChanged = true;
                thresholdY = plotMax.Y - plotHeight * threshold;
            }
        }

        if (!held)
            _isDraggingThreshold = false;

        if (nearLine || _isDraggingThreshold)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);

        // Draw trace + fill inside a clip rect; threshold line outside so its
        // label can extend beyond the plot area if needed.
        ImGui.PushClipRect(drawList, plotMin, plotMax, true);
        DrawAboveThresholdFill(
            drawList,
            plotMin,
            plotMax,
            plotWidth,
            plotHeight,
            threshold,
            thresholdY
        );
        DrawTrace(drawList, plotMin, plotMax, plotWidth, plotHeight, threshold);
        ImGui.PopClipRect(drawList);

        var isHot = isExternallyActive || _isDraggingThreshold || nearLine;
        DrawThresholdLine(drawList, plotMin, plotMax, thresholdY, isHot);

        return thresholdChanged;
    }

    // ── Trace drawing ───────────────────────────────────────────────────────

    private float TimeToX(float sampleTime, float plotLeft, float plotWidth)
    {
        var age = _elapsed - sampleTime;
        return plotLeft + plotWidth * (1f - age / TimeWindow);
    }

    private void DrawTrace(
        ImDrawListPtr drawList,
        Vector2 plotMin,
        Vector2 plotMax,
        float plotWidth,
        float plotHeight,
        float threshold
    )
    {
        if (_count < 2)
            return;

        var aboveCol = ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary);
        var belowCol = ImGui.ColorConvertFloat4ToU32(Theme.TextSecondary);

        var start = FirstVisibleIndex();
        if (_count - start < 2)
            return;

        Vector2 previous = default;
        TraceSample prevSample = default;
        for (var i = start; i < _count; i++)
        {
            var s = GetSample(i);
            var x = TimeToX(s.Time, plotMin.X, plotWidth);
            var y = plotMax.Y - plotHeight * Math.Clamp(s.Value, 0f, 1f);
            var point = new Vector2(x, y);

            if (i > start)
            {
                var speaking = prevSample.Value > threshold || s.Value > threshold;
                ImGui.AddLine(drawList, previous, point, speaking ? aboveCol : belowCol, 1.75f);
            }

            previous = point;
            prevSample = s;
        }
    }

    private void DrawAboveThresholdFill(
        ImDrawListPtr drawList,
        Vector2 plotMin,
        Vector2 plotMax,
        float plotWidth,
        float plotHeight,
        float threshold,
        float thresholdY
    )
    {
        if (_count < 2)
            return;

        var fillCol = ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary with { W = 0.18f });

        var start = FirstVisibleIndex();
        for (var i = start; i < _count; i++)
        {
            var s = GetSample(i);
            if (s.Value <= threshold)
                continue;

            var x0 = TimeToX(s.Time, plotMin.X, plotWidth);
            var x1 =
                i + 1 < _count
                    ? TimeToX(GetSample(i + 1).Time, plotMin.X, plotWidth)
                    : plotMin.X + plotWidth;

            var y = plotMax.Y - plotHeight * Math.Clamp(s.Value, 0f, 1f);
            ImGui.AddRectFilled(drawList, new Vector2(x0, y), new Vector2(x1, thresholdY), fillCol);
        }
    }

    // ── Threshold line ──────────────────────────────────────────────────────

    private static void DrawThresholdLine(
        ImDrawListPtr drawList,
        Vector2 plotMin,
        Vector2 plotMax,
        float thresholdY,
        bool isHot
    )
    {
        var col = isHot ? Theme.AccentPrimary : Theme.TextSecondary with { W = 0.75f };
        var thickness = isHot ? 2f : 1.25f;

        if (isHot)
        {
            ImGui.AddLine(
                drawList,
                new Vector2(plotMin.X, thresholdY),
                new Vector2(plotMax.X, thresholdY),
                ImGui.ColorConvertFloat4ToU32(col),
                thickness
            );
        }
        else
        {
            const float dashLen = 6f;
            const float gapLen = 4f;
            var x = plotMin.X;
            var colU = ImGui.ColorConvertFloat4ToU32(col);
            while (x < plotMax.X)
            {
                var end = MathF.Min(x + dashLen, plotMax.X);
                ImGui.AddLine(
                    drawList,
                    new Vector2(x, thresholdY),
                    new Vector2(end, thresholdY),
                    colU,
                    thickness
                );
                x = end + gapLen;
            }
        }

        var label = "Sensitivity";
        var labelSize = ImGui.CalcTextSize(label);
        var labelPos = new Vector2(plotMin.X + 6f, thresholdY - labelSize.Y - 2f);
        var labelCol = isHot ? Theme.AccentPrimary : Theme.TextTertiary;
        drawList.AddText(labelPos, ImGui.ColorConvertFloat4ToU32(labelCol), label);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private readonly record struct TraceSample(float Time, float Value);
}

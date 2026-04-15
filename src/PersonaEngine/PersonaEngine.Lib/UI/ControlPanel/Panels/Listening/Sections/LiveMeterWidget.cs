using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.ASR.VAD;
using PersonaEngine.Lib.Audio;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Listening.Sections;

/// <summary>
///     Live calibration meter for the Listening panel. Shows a single primary trace of the
///     Silero VAD probability with above-threshold shading (literal visual of "this is when
///     the avatar would react"), a draggable threshold line labelled "Sensitivity", a state
///     banner at the top ("Silent" / "Picking up sound" / "Listening to you"), and a
///     compact mic-alive amplitude pip on the right.
///     <para>
///         Scrolling uses sub-slot interpolation: between sample arrivals the trace scrolls
///         smoothly on the UI render thread, producing fluid motion regardless of whether
///         the underlying provider updates at 160 Hz (amplitude) or 31 Hz (VAD probability).
///     </para>
/// </summary>
public sealed class LiveMeterWidget
{
    private const float WidgetHeight = 176f;
    private const float BannerHeight = 34f;
    private const float BannerGap = 6f;

    // UI-side smoothing for the mic-alive pip only — the primary VAD trace is shown raw
    // so the threshold relationship is direct.
    private const float MicPipSmoothingRate = 15f;

    // EMA weight for push-interval estimation. Lower = smoother but slower to adapt.
    private const float IntervalEmaAlpha = 0.2f;

    private readonly IMicrophoneAmplitudeProvider _micAmp;
    private readonly IVadProbabilityProvider _vadProb;

    private float _displayMicAmp;

    // Scroll-interpolation state — one per trace source.
    private TraceScroll _vadScroll = TraceScroll.Initial(1f / 31f);

    // Draggable threshold line state.
    private bool _isDraggingThreshold;

    public LiveMeterWidget(IMicrophoneAmplitudeProvider micAmp, IVadProbabilityProvider vadProb)
    {
        _micAmp = micAmp;
        _vadProb = vadProb;
    }

    /// <summary>
    ///     Renders the meter. The <paramref name="threshold" /> parameter is bidirectional —
    ///     the user can drag the threshold line on the meter to adjust it directly. Returns
    ///     <see langword="true" /> when the user modified the value this frame.
    /// </summary>
    /// <param name="dt">Frame delta time (seconds).</param>
    /// <param name="threshold">
    ///     The VAD threshold in <c>[0, 1]</c>. May be modified if the user drags the
    ///     threshold line.
    /// </param>
    /// <param name="isExternallyActive">
    ///     <see langword="true" /> when the "Voice Sensitivity" slider below the meter is
    ///     actively being dragged — causes the meter's threshold line to brighten even when
    ///     the user isn't dragging the line itself.
    /// </param>
    public bool Render(float dt, ref float threshold, bool isExternallyActive)
    {
        AdvanceMicSmoothing(dt);
        _vadScroll.Update(dt, _vadProb.HistoryHead, _vadProb.History.Length);

        var origin = ImGui.GetCursorScreenPos();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var size = new Vector2(availableWidth, WidgetHeight);
        var drawList = ImGui.GetWindowDrawList();

        var bannerMin = origin;
        var bannerMax = origin + new Vector2(size.X, BannerHeight);
        var traceMin = origin + new Vector2(0f, BannerHeight + BannerGap);
        var traceMax = origin + size;

        RenderStateBanner(drawList, bannerMin, bannerMax, threshold);

        var thresholdChanged = RenderTraceWithDraggableThreshold(
            drawList,
            traceMin,
            traceMax,
            ref threshold,
            isExternallyActive
        );

        ImGui.Dummy(size);
        return thresholdChanged;
    }

    private void AdvanceMicSmoothing(float dt)
    {
        var target = _micAmp.CurrentAmplitude;
        var alpha = 1f - MathF.Exp(-MicPipSmoothingRate * dt);
        _displayMicAmp += (target - _displayMicAmp) * alpha;
    }

    // ── Banner ──────────────────────────────────────────────────────────────

    private void RenderStateBanner(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float threshold
    )
    {
        var rounding = 8f;
        var bg = ImGui.ColorConvertFloat4ToU32(Theme.Surface1);
        ImGui.AddRectFilled(drawList, min, max, bg, rounding);

        var (label, color) = ClassifyState(threshold);

        var padX = 14f;
        var dotRadius = 5f;
        var dotCenter = new Vector2(min.X + padX + dotRadius, (min.Y + max.Y) * 0.5f);
        var dotCol = ImGui.ColorConvertFloat4ToU32(color);
        ImGui.AddCircleFilled(drawList, dotCenter, dotRadius, dotCol);
        // Soft glow
        var glowCol = ImGui.ColorConvertFloat4ToU32(color with { W = 0.25f });
        ImGui.AddCircleFilled(drawList, dotCenter, dotRadius * 2.2f, glowCol);

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

        var padX = 14f;
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
            var col = LerpColor(Theme.Surface3, Theme.AccentPrimary, litAmount);
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
        // Derived purely from current-frame signals; no historical logic.
        // - Probability well above threshold → "Listening to you"
        // - Probability below threshold but mic picking up real signal → "Picking up sound"
        // - Otherwise → "Silent"
        var prob = _vadProb.CurrentProbability;
        var amp = _displayMicAmp;

        if (prob > threshold)
        {
            return ("Listening to you", Theme.AccentPrimary);
        }

        const float NoiseFloor = 0.02f;
        if (amp > NoiseFloor)
        {
            return ("Picking up sound", Theme.AccentSecondary);
        }

        return ("Silent", Theme.TextTertiary);
    }

    // ── Primary trace + draggable threshold ──────────────────────────────────

    private bool RenderTraceWithDraggableThreshold(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        ref float threshold,
        bool isExternallyActive
    )
    {
        var rounding = 8f;
        ImGui.AddRectFilled(
            drawList,
            min,
            max,
            ImGui.ColorConvertFloat4ToU32(Theme.Surface3),
            rounding
        );

        var inset = 8f;
        var plotMin = min + new Vector2(inset, inset);
        var plotMax = max - new Vector2(inset, inset);
        var plotWidth = plotMax.X - plotMin.X;
        var plotHeight = plotMax.Y - plotMin.Y;

        // Threshold line position.
        var thresholdY = plotMax.Y - plotHeight * Math.Clamp(threshold, 0f, 1f);

        // Drag region spans the full trace area — grabbing anywhere near the line works.
        // We hit-test a ±10px band around the current threshold line.
        const float grabHalfHeight = 10f;
        _ = ImGui.InvisibleButton(
            "##meter_drag",
            new Vector2(plotMax.X - min.X, plotMax.Y - min.Y)
        );
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

        var thresholdChanged = false;
        var mousePos = ImGui.GetMousePos();
        var nearLine = hovered && MathF.Abs(mousePos.Y - thresholdY) <= grabHalfHeight;

        if (held && (nearLine || _isDraggingThreshold))
        {
            _isDraggingThreshold = true;
            // Map mouse Y to threshold value. Invert because top of plot = 1.0.
            var normalized = Math.Clamp((plotMax.Y - mousePos.Y) / plotHeight, 0.1f, 0.95f);
            if (MathF.Abs(normalized - threshold) > 1e-4f)
            {
                threshold = normalized;
                thresholdChanged = true;
                thresholdY = plotMax.Y - plotHeight * threshold;
            }
        }
        if (!held)
        {
            _isDraggingThreshold = false;
        }

        if (nearLine || _isDraggingThreshold)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);
        }

        // Above-threshold fill — quads from trace points down to threshold line, in accent color.
        DrawAboveThresholdFill(drawList, plotMin, plotMax, threshold, thresholdY);

        // The probability trace itself.
        DrawTrace(drawList, plotMin, plotMax, threshold);

        // Threshold line (drawn last so it sits on top).
        var isHot = isExternallyActive || _isDraggingThreshold || nearLine;
        DrawThresholdLine(drawList, plotMin, plotMax, thresholdY, isHot);

        return thresholdChanged;
    }

    private void DrawTrace(
        ImDrawListPtr drawList,
        Vector2 plotMin,
        Vector2 plotMax,
        float threshold
    )
    {
        var history = _vadProb.History;
        if (history.Length < 2)
            return;

        var width = plotMax.X - plotMin.X;
        var height = plotMax.Y - plotMin.Y;
        var step = width / (history.Length - 1);
        var offset = _vadScroll.SubSlotOffset;
        var head = _vadProb.HistoryHead;

        var aboveCol = ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary);
        var belowCol = ImGui.ColorConvertFloat4ToU32(Theme.TextSecondary);

        Vector2 previous = default;
        for (var i = 0; i < history.Length; i++)
        {
            var idx = (head + i) % history.Length;
            var v = Math.Clamp(history[idx], 0f, 1f);
            var point = new Vector2(plotMin.X + step * (i - offset), plotMax.Y - height * v);
            if (i > 0)
            {
                // Segment color: accent if either endpoint is above threshold, muted below.
                var highSegment =
                    history[(head + i - 1) % history.Length] > threshold || v > threshold;
                ImGui.AddLine(drawList, previous, point, highSegment ? aboveCol : belowCol, 1.75f);
            }
            previous = point;
        }
    }

    private void DrawAboveThresholdFill(
        ImDrawListPtr drawList,
        Vector2 plotMin,
        Vector2 plotMax,
        float threshold,
        float thresholdY
    )
    {
        var history = _vadProb.History;
        if (history.Length < 2)
            return;

        var width = plotMax.X - plotMin.X;
        var height = plotMax.Y - plotMin.Y;
        var step = width / (history.Length - 1);
        var offset = _vadScroll.SubSlotOffset;
        var head = _vadProb.HistoryHead;

        var fillCol = ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary with { W = 0.18f });

        // Render as a series of thin vertical strips from threshold line down to the trace
        // where the trace is above threshold. Cheap, no polygon triangulation needed.
        for (var i = 0; i < history.Length; i++)
        {
            var idx = (head + i) % history.Length;
            var v = Math.Clamp(history[idx], 0f, 1f);
            if (v <= threshold)
                continue;

            var x0 = plotMin.X + step * (i - offset);
            var x1 = plotMin.X + step * (i + 1 - offset);
            var y = plotMax.Y - height * v;

            // Clip to plot bounds.
            if (x1 < plotMin.X || x0 > plotMax.X)
                continue;
            x0 = MathF.Max(x0, plotMin.X);
            x1 = MathF.Min(x1, plotMax.X);

            ImGui.AddRectFilled(drawList, new Vector2(x0, y), new Vector2(x1, thresholdY), fillCol);
        }
    }

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

        // Dashed when cold, solid when hot — dashes communicate "adjustable".
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

        // "Sensitivity" label near the left edge, hovering above the line.
        var label = "Sensitivity";
        var labelSize = ImGui.CalcTextSize(label);
        var labelPos = new Vector2(plotMin.X + 6f, thresholdY - labelSize.Y - 2f);
        var labelCol = isHot ? Theme.AccentPrimary : Theme.TextTertiary;
        drawList.AddText(labelPos, ImGui.ColorConvertFloat4ToU32(labelCol), label);
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

    /// <summary>
    ///     Tracks sub-slot scroll offset for a single history-based trace so the draw code
    ///     can render between sample arrivals at pixel resolution.
    /// </summary>
    private struct TraceScroll
    {
        private int _lastKnownHead;
        private float _elapsedSinceUpdate;
        private float _estimatedInterval;
        private float _subSlotOffset;
        private bool _initialized;

        public static TraceScroll Initial(float defaultInterval) =>
            new()
            {
                _estimatedInterval = defaultInterval,
                _elapsedSinceUpdate = 0f,
                _subSlotOffset = 0f,
                _initialized = false,
                _lastKnownHead = 0,
            };

        public float SubSlotOffset => _subSlotOffset;

        public void Update(float dt, int currentHead, int historyLength)
        {
            if (historyLength <= 0)
            {
                _subSlotOffset = 0f;
                return;
            }

            if (!_initialized)
            {
                _lastKnownHead = currentHead;
                _initialized = true;
            }

            _elapsedSinceUpdate += dt;

            if (currentHead != _lastKnownHead)
            {
                var slotsAdvanced = (currentHead - _lastKnownHead + historyLength) % historyLength;
                if (slotsAdvanced == 0)
                    slotsAdvanced = historyLength; // full wraparound — treat as full window

                var observedInterval =
                    slotsAdvanced > 0 ? _elapsedSinceUpdate / slotsAdvanced : _estimatedInterval;

                // Clamp observed to sane bounds — guards against very first frame after
                // app start when providers may have been running long before the UI opened.
                observedInterval = Math.Clamp(observedInterval, 1f / 240f, 1f / 4f);

                _estimatedInterval =
                    _estimatedInterval * (1f - IntervalEmaAlpha)
                    + observedInterval * IntervalEmaAlpha;
                _elapsedSinceUpdate = 0f;
                _subSlotOffset = MathF.Max(0f, _subSlotOffset - slotsAdvanced);
                _lastKnownHead = currentHead;
            }

            // Advance sub-slot offset by the fraction of an interval that has passed.
            var fraction = _elapsedSinceUpdate / _estimatedInterval;
            _subSlotOffset = Math.Clamp(fraction, 0f, 1.25f);
        }
    }
}

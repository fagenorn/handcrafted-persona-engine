using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
/// The persona's status + voice indicator. A pulsing orb that doubles as a
/// state dot (Listening/Thinking) and a HAL-style voice visualizer (Speaking).
/// On loud syllables it emits expanding rings, like sound waves. Invisible
/// when no session exists.
/// </summary>
/// <remarks>
/// Renders at a given screen position; does not decide where it lives.
/// Callers (e.g. <c>StatusBar</c>) own positioning.
/// </remarks>
public sealed class PresenceOrb
{
    // Core disc: always rendered when visible.
    private const float CoreBaseRadius = 3.5f;
    private const float CoreAmplitudeGain = 5f;

    // Inner halo: bright ring around the core.
    private const float InnerHaloPadding = 4f;
    private const float InnerHaloAmplitudeGain = 7f;

    // Outer halo: softest, largest glow.
    private const float OuterHaloPadding = 6f;
    private const float OuterHaloAmplitudeGain = 9f;

    // Soft-knee compression for amplitude → pulse. A power curve gives quiet
    // sounds more visual presence and tames the ceiling on loud peaks so the
    // orb doesn't sit pinned at max during loud speech.
    private const float CompressionExponent = 0.65f;
    private const float CompressionGain = 1.4f;

    // Per-layer smoothing rates. The core tracks amplitude almost instantly;
    // each outer layer trails progressively, producing a visible propagation
    // ripple from center outward instead of three discs scaling in lockstep.
    private const float CoreSmoothing = 30f;
    private const float InnerSmoothing = 14f;
    private const float OuterSmoothing = 7f;
    private const float VisibilitySmoothing = 6f;

    // Very subtle slow heartbeat for non-speaking active states — barely
    // perceptible but signals "alive".
    private const float HeartbeatHz = 0.5f;
    private const float HeartbeatAmplitude = 0.04f;

    // Pulse rings: emanate outward when amplitude crosses a peak threshold,
    // creating discrete "syllable" events on top of the continuous pulse.
    private const int MaxRings = 5;
    private const float RingDurationSeconds = 0.7f;
    private const float RingPeakThreshold = 0.45f;
    private const float RingCooldownMin = 0.06f;
    private const float RingCooldownMax = 0.28f;
    private const float RingMaxSpawnDelay = 0.18f;

    // Per-layer amplitude state. Each layer smooths the same source amplitude
    // at its own rate, so outer layers visibly lag the core during transients.
    private float _coreAmplitude;
    private float _innerAmplitude;
    private float _outerAmplitude;

    private float _previousPulse;
    private float _ringCooldown;
    private float _visibility;
    private float _elapsed;

    private readonly PulseRing[] _rings = new PulseRing[MaxRings];

    /// <summary>Maximum on-screen radius this orb may occupy, for layout reservation.</summary>
    public static float MaxRadius =>
        CoreBaseRadius + CoreAmplitudeGain
        + InnerHaloPadding + InnerHaloAmplitudeGain
        + OuterHaloPadding + OuterHaloAmplitudeGain;

    public void Update(float dt, PersonaStateProvider state)
    {
        _elapsed += dt;
        _ringCooldown = MathF.Max(0f, _ringCooldown - dt);

        var rawAmp = state.IsAudioPlaying ? state.AudioAmplitude : 0f;

        // Each layer smooths toward the same source at a different rate.
        _coreAmplitude += (rawAmp - _coreAmplitude) * (1f - MathF.Exp(-CoreSmoothing * dt));
        _innerAmplitude += (rawAmp - _innerAmplitude) * (1f - MathF.Exp(-InnerSmoothing * dt));
        _outerAmplitude += (rawAmp - _outerAmplitude) * (1f - MathF.Exp(-OuterSmoothing * dt));

        var targetVisibility = state.State == PersonaUiState.NoSession ? 0f : 1f;
        _visibility += (targetVisibility - _visibility) * (1f - MathF.Exp(-VisibilitySmoothing * dt));

        // Advance / spawn pulse rings only during speaking state. Each ring
        // has its own spawn delay; lifetime begins ticking once the delay
        // expires, so rings appear at irregular times even when triggered
        // back-to-back.
        for (var i = 0; i < _rings.Length; i++)
        {
            ref var ring = ref _rings[i];
            if (!ring.Active) continue;

            if (ring.SpawnDelay > 0f)
            {
                ring.SpawnDelay -= dt;
                continue;
            }

            ring.Lifetime += dt / ring.Duration;
            if (ring.Lifetime >= 1f)
                ring.Active = false;
        }

        if (state.State == PersonaUiState.Speaking)
        {
            var pulse = CompressedPulse(_coreAmplitude);
            if (
                pulse >= RingPeakThreshold
                && _previousPulse < RingPeakThreshold
                && _ringCooldown <= 0f
            )
            {
                SpawnRing();
                // Randomize cooldown so successive triggers fire at
                // irregular intervals.
                _ringCooldown =
                    RingCooldownMin
                    + (float)_ringRng.NextDouble() * (RingCooldownMax - RingCooldownMin);
            }
            _previousPulse = pulse;
        }
        else
        {
            _previousPulse = 0f;
        }
    }

    public void Render(ImDrawListPtr drawList, Vector2 center, PersonaUiState state)
    {
        if (_visibility < 0.01f)
            return;

        var color = GetStateColor(state);

        // Per-layer pulse values. In Speaking, each layer follows its own
        // smoothed amplitude (core leads, outer lags). Otherwise all layers
        // share the heartbeat.
        float corePulse, innerPulse, outerPulse;
        if (state == PersonaUiState.Speaking)
        {
            corePulse = CompressedPulse(_coreAmplitude);
            innerPulse = CompressedPulse(_innerAmplitude);
            outerPulse = CompressedPulse(_outerAmplitude);
        }
        else
        {
            var t = 0.5f + 0.5f * MathF.Sin(_elapsed * HeartbeatHz * MathF.Tau);
            corePulse = innerPulse = outerPulse = t * HeartbeatAmplitude;
        }

        // Pulse rings — drawn first, behind the orb body. Each ring has its
        // own start radius, duration, and thickness, set at spawn time.
        for (var i = 0; i < _rings.Length; i++)
        {
            ref readonly var ring = ref _rings[i];
            if (!ring.Active || ring.SpawnDelay > 0f) continue;

            var t = ring.Lifetime;
            var ringRadius = ring.StartRadius + t * (ring.MaxRadius - ring.StartRadius);
            var fade = (1f - t) * (1f - t);
            var ringAlpha = _visibility * fade * 0.55f;
            var thickness = ring.StartThickness * (1f - t * 0.5f);

            ImGui.AddCircle(
                drawList,
                center,
                ringRadius,
                ImGui.ColorConvertFloat4ToU32(color with { W = ringAlpha }),
                32,
                thickness
            );
        }

        // Outer halo — drawn first so inner layers sit on top. Uses the slow
        // amplitude trace, so it visibly trails the core during transients.
        var outerRadius =
            CoreBaseRadius + CoreAmplitudeGain * corePulse
            + InnerHaloPadding + InnerHaloAmplitudeGain * innerPulse
            + OuterHaloPadding + OuterHaloAmplitudeGain * outerPulse;
        var outerAlpha = _visibility * (0.10f + outerPulse * 0.20f);
        ImGui.AddCircleFilled(
            drawList,
            center,
            outerRadius,
            ImGui.ColorConvertFloat4ToU32(color with { W = outerAlpha }),
            32
        );

        // Inner halo — uses inner trace, slightly delayed from core.
        var innerRadius =
            CoreBaseRadius + CoreAmplitudeGain * corePulse
            + InnerHaloPadding + InnerHaloAmplitudeGain * innerPulse;
        var innerAlpha = _visibility * (0.30f + innerPulse * 0.30f);
        ImGui.AddCircleFilled(
            drawList,
            center,
            innerRadius,
            ImGui.ColorConvertFloat4ToU32(color with { W = innerAlpha }),
            32
        );

        // Core — bright solid disc, driven by the fastest amplitude trace.
        var coreRadius = CoreBaseRadius + CoreAmplitudeGain * corePulse;
        var coreAlpha = _visibility * (0.85f + corePulse * 0.15f);
        ImGui.AddCircleFilled(
            drawList,
            center,
            coreRadius,
            ImGui.ColorConvertFloat4ToU32(color with { W = coreAlpha }),
            32
        );
    }

    /// <summary>
    /// Soft-knee compression of raw RMS amplitude into the visual pulse range
    /// [0, 1]. Quiet sounds get amplified more, peaks taper toward 1.
    /// </summary>
    private static float CompressedPulse(float amp)
    {
        if (amp <= 0f) return 0f;
        return MathF.Min(1f, MathF.Pow(amp, CompressionExponent) * CompressionGain);
    }

    private readonly Random _ringRng = new();

    private void SpawnRing()
    {
        // Each ring gets randomized parameters so rapid-succession rings don't
        // look like identical clones — varied speed, distance, and weight feel
        // more like organic sound waves.
        var startOffset = (float)(_ringRng.NextDouble() * 4f); // 0–4px
        var maxRadius = MaxRadius * (1.05f + (float)_ringRng.NextDouble() * 0.45f); // 105–150% of orb max
        var duration = RingDurationSeconds * (0.75f + (float)_ringRng.NextDouble() * 0.5f); // ±25%
        var thickness = 1.6f + (float)_ringRng.NextDouble() * 1.2f; // 1.6–2.8px

        // Random spawn delay — the ring is "queued" but doesn't start
        // expanding until the delay elapses, decoupling visual appearance
        // from the trigger moment.
        var spawnDelay = (float)_ringRng.NextDouble() * RingMaxSpawnDelay;

        var ring = new PulseRing
        {
            Active = true,
            Lifetime = 0f,
            SpawnDelay = spawnDelay,
            StartRadius = CoreBaseRadius + 3f + startOffset,
            MaxRadius = maxRadius,
            Duration = duration,
            StartThickness = thickness,
        };

        for (var i = 0; i < _rings.Length; i++)
        {
            if (!_rings[i].Active)
            {
                _rings[i] = ring;
                return;
            }
        }

        // No free slot — recycle the oldest (highest lifetime).
        var oldestIdx = 0;
        var oldestLifetime = -1f;
        for (var i = 0; i < _rings.Length; i++)
        {
            if (_rings[i].Lifetime > oldestLifetime)
            {
                oldestLifetime = _rings[i].Lifetime;
                oldestIdx = i;
            }
        }
        _rings[oldestIdx] = ring;
    }

    private static Vector4 GetStateColor(PersonaUiState state) =>
        state switch
        {
            PersonaUiState.Speaking => Theme.AccentPrimary,
            PersonaUiState.Thinking => Theme.Warning,
            PersonaUiState.Idle => Theme.Success,
            _ => Theme.TextSecondary,
        };

    private struct PulseRing
    {
        public bool Active;
        public float SpawnDelay; // seconds before lifetime begins
        public float Lifetime; // 0..1 normalized to Duration
        public float StartRadius;
        public float MaxRadius;
        public float Duration; // seconds
        public float StartThickness;
    }
}

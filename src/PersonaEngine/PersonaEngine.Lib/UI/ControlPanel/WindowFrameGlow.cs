using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     Animated glowing border drawn along the window edge.
///     Breathes softly when idle, warms toward pink and pulses with audio amplitude
///     when the persona is speaking. Matches the DWM rounded corner radius so the
///     border aligns with the native window curvature.
/// </summary>
public sealed class WindowFrameGlow
{
    // Visual constants
    private const float CornerRadius = 8f;
    private const float InnerInset = 1f;
    private const float OuterGlowThickness = 2f;
    private const float InnerLineThickness = 1f;

    // Breathing oscillator — ~4s cycle, gentle ±0.05 amplitude around 0f.
    // Added to the base alpha at render time.
    private readonly SineOscillator _breathing = new(
        center: 0f,
        amplitude: 0.05f,
        frequencyHz: 0.25f
    );

    private readonly PersonaStateProvider _stateProvider;

    // Animated color blends smoothly between persona states (lavender ↔ pink).
    private AnimatedColor _tint = new(GetStateColor(PersonaUiState.Idle), speed: 2.5f);

    // Exponentially-smoothed audio amplitude (matches StatusBar's pattern).
    private float _smoothedAmplitude;

    private float _elapsed;

    public WindowFrameGlow(PersonaStateProvider stateProvider)
    {
        _stateProvider = stateProvider;
    }

    public void Render(float deltaTime)
    {
        _elapsed += deltaTime;

        // Smooth state-driven color transitions
        _tint.Target = GetStateColor(_stateProvider.State);
        _tint.Update(deltaTime);

        // Smooth audio amplitude (same coefficient as StatusBar for consistency)
        var targetAmplitude = _stateProvider.IsAudioPlaying ? _stateProvider.AudioAmplitude : 0f;
        _smoothedAmplitude +=
            (targetAmplitude - _smoothedAmplitude) * (1f - MathF.Exp(-12f * deltaTime));

        // Base alpha: speaking pulses with voice, idle/thinking has steady presence.
        // Breathing sine adds subtle life regardless of state.
        var baseAlpha = _stateProvider.State switch
        {
            PersonaUiState.Speaking => 0.20f + _smoothedAmplitude * 0.35f,
            PersonaUiState.Thinking => 0.22f,
            _ => 0.15f,
        };
        var alpha = MathF.Max(0f, baseAlpha + _breathing.Sample(_elapsed));

        var viewport = ImGui.GetMainViewport();
        var min = viewport.Pos + new Vector2(InnerInset, InnerInset);
        var max = viewport.Pos + viewport.Size - new Vector2(InnerInset, InnerInset);

        var drawList = ImGui.GetForegroundDrawList();

        // Soft outer glow — thicker, lower alpha
        var glowColor = ImGui.ColorConvertFloat4ToU32(_tint.Current with { W = alpha * 0.55f });
        ImGui.AddRect(drawList, min, max, glowColor, CornerRadius, 0, OuterGlowThickness);

        // Crisp inner line — thin, higher alpha
        var lineColor = ImGui.ColorConvertFloat4ToU32(_tint.Current with { W = alpha });
        ImGui.AddRect(drawList, min, max, lineColor, CornerRadius, 0, InnerLineThickness);
    }

    private static Vector4 GetStateColor(PersonaUiState state) =>
        state switch
        {
            PersonaUiState.Speaking => Theme.AccentPrimary,
            _ => Theme.AccentSecondary,
        };
}

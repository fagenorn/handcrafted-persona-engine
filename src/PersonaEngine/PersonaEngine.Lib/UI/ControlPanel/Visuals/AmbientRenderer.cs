using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel;
using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.ControlPanel.Visuals;

/// <summary>
/// Renders ambient background effects: two smooth radial warmth glows (via
/// a pre-rendered gradient texture) and sparse floating particles.
/// </summary>
public sealed class AmbientRenderer : IDisposable
{
    private readonly PersonaStateProvider _stateProvider;
    private readonly ParticleSystem _particles = new(12);
    private float _elapsed;

    // Position drift per glow (normalized 0..1 of panel bounds).
    // Biased toward opposite corners with narrow amplitudes — motion is subtle
    // and the two glows rarely overlap. Frequencies are incommensurate so the
    // combined drift never repeats a visible pattern.
    private readonly SineOscillator _glow1X = new(0.28f, 0.06f, frequencyHz: 0.07f);
    private readonly SineOscillator _glow1Y = new(0.32f, 0.05f, frequencyHz: 0.055f);
    private readonly SineOscillator _glow2X = new(0.72f, 0.06f, frequencyHz: 0.083f);
    private readonly SineOscillator _glow2Y = new(0.68f, 0.05f, frequencyHz: 0.061f);

    // Animated tints — smoothly transition between persona-state colors.
    private AnimatedColor _tint1 = new(Vector4.Zero, speed: 2f);
    private AnimatedColor _tint2 = new(Vector4.Zero, speed: 2f);

    // Smoothed audio amplitude for reactive intensity.
    private float _smoothedAmplitude;

    private RadialGradientTexture? _gradient;

    public AmbientRenderer(PersonaStateProvider stateProvider)
    {
        _stateProvider = stateProvider;
    }

    public void Initialize(GL gl)
    {
        _gradient ??= RadialGradientTexture.Create(gl, size: 256);
    }

    /// <summary>
    /// Advances time-based state that does not depend on panel geometry:
    /// <c>_elapsed</c>, tint animations, and smoothed audio amplitude.
    /// Particle simulation is stepped in <see cref="RenderBackground"/> where
    /// the panel bounds are known.
    /// </summary>
    public void Update(float dt)
    {
        _elapsed += dt;

        // Resolve per-state target tints (RGB = color, A = base alpha).
        var (target1, target2) = GetTargetTints(_stateProvider.State);
        _tint1.Target = target1;
        _tint2.Target = target2;
        _tint1.Update(dt);
        _tint2.Update(dt);

        // Smooth the raw per-chunk RMS so glow intensity tracks voice dynamics
        // continuously rather than stepping between chunk boundaries.
        var targetAmp = _stateProvider.IsAudioPlaying ? _stateProvider.AudioAmplitude : 0f;
        _smoothedAmplitude += (targetAmp - _smoothedAmplitude) * (1f - MathF.Exp(-10f * dt));
    }

    /// <summary>
    /// Steps the particle simulation with the current panel bounds and draws
    /// the ambient layer (warmth glows + particles). <paramref name="dt"/> is
    /// the frame delta; it is only used to advance the particle population
    /// here, since spawn positions depend on <paramref name="size"/>.
    /// </summary>
    public void RenderBackground(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float dt)
    {
        _particles.Update(dt, _stateProvider.State, size);

        if (_gradient is not null)
            RenderWarmthGlows(drawList, origin, size);

        RenderParticles(drawList, origin, size);
    }

    private void RenderWarmthGlows(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
    {
        // Intensity is constant + reactive to audio amplitude. No sine-based
        // breathing — that reads as mechanical pulsing. When the persona is
        // speaking, the glows brighten with the voice; when idle, they stay
        // steady.
        var audioBoost = 1f + _smoothedAmplitude * 0.6f;

        var tint1 = _tint1.Current;
        tint1.W *= audioBoost;

        var tint2 = _tint2.Current;
        tint2.W *= audioBoost * 0.8f;

        DrawGlowQuad(
            drawList,
            center: origin
                + new Vector2(_glow1X.Sample(_elapsed) * size.X, _glow1Y.Sample(_elapsed) * size.Y),
            radius: size.X * 0.40f,
            tint: tint1
        );

        DrawGlowQuad(
            drawList,
            center: origin
                + new Vector2(_glow2X.Sample(_elapsed) * size.X, _glow2Y.Sample(_elapsed) * size.Y),
            radius: size.X * 0.35f,
            tint: tint2
        );
    }

    private void DrawGlowQuad(ImDrawListPtr drawList, Vector2 center, float radius, Vector4 tint)
    {
        if (tint.W <= 0.001f || _gradient is null)
            return;

        var min = center - new Vector2(radius, radius);
        var max = center + new Vector2(radius, radius);
        var col = ImGui.ColorConvertFloat4ToU32(tint);

        unsafe
        {
            var texRef = new ImTextureRef(null, new ImTextureID((nuint)_gradient.TextureId));

            ImGui.AddImage(
                drawList,
                texRef,
                min,
                max,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                col
            );
        }
    }

    private void RenderParticles(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
    {
        foreach (ref readonly var p in _particles.Particles)
        {
            if (!p.IsAlive || p.Alpha <= 0f)
                continue;

            var screenPos = origin + p.Position;
            if (
                screenPos.X < origin.X - 10f
                || screenPos.X > origin.X + size.X + 10f
                || screenPos.Y < origin.Y - 10f
                || screenPos.Y > origin.Y + size.Y + 10f
            )
                continue;

            var alpha = p.Alpha * 0.25f;
            var col = ImGui.ColorConvertFloat4ToU32(p.Color with { W = alpha });
            var outerCol = ImGui.ColorConvertFloat4ToU32(p.Color with { W = alpha * 0.3f });

            ImGui.AddCircleFilled(drawList, screenPos, p.Radius * 2f, outerCol, 16);
            ImGui.AddCircleFilled(drawList, screenPos, p.Radius, col, 16);
        }
    }

    /// <summary>
    /// Maps persona state to (tint1, tint2) target colors. RGB is the warmth
    /// color; W is the base alpha for that glow.
    /// </summary>
    private static (Vector4 Tint1, Vector4 Tint2) GetTargetTints(PersonaUiState state)
    {
        return state switch
        {
            PersonaUiState.Speaking => (
                Theme.AccentPrimary with
                {
                    W = 0.085f,
                },
                Theme.AccentSecondary with
                {
                    W = 0.085f,
                }
            ),
            PersonaUiState.Thinking => (
                Theme.AccentSecondary with
                {
                    W = 0.065f,
                },
                Theme.AccentPrimary with
                {
                    W = 0.065f,
                }
            ),
            _ => (
                Theme.AccentSecondary with
                {
                    W = 0.055f,
                },
                Theme.AccentSecondary with
                {
                    W = 0.055f,
                }
            ),
        };
    }

    public void Dispose()
    {
        _gradient?.Dispose();
        _gradient = null;
    }
}

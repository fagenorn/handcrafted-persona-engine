using System.Numerics;
using Hexa.NET.ImGui;
using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
/// Renders ambient background effects: two smooth radial warmth glows (via
/// a pre-rendered gradient texture) and sparse floating particles.
/// </summary>
public sealed class AmbientRenderer : IDisposable
{
    private readonly PersonaStateProvider _stateProvider;
    private readonly ParticleSystem _particles = new(12);
    private float _elapsed;

    // Breathe intensity (alpha oscillation) per glow.
    private readonly SineOscillator _glow1Breathe = new(0.9f, 0.1f, frequencyHz: 0.05f);
    private readonly SineOscillator _glow2Breathe = new(0.9f, 0.1f, frequencyHz: 0.07f);

    // Animated tints — smoothly transition between persona-state colors.
    private AnimatedColor _tint1 = new(Vector4.Zero, speed: 2f);
    private AnimatedColor _tint2 = new(Vector4.Zero, speed: 2f);

    private RadialGradientTexture? _gradient;

    public AmbientRenderer(PersonaStateProvider stateProvider)
    {
        _stateProvider = stateProvider;
    }

    public void Initialize(GL gl)
    {
        _gradient ??= RadialGradientTexture.Create(gl, size: 256);
    }

    public void Update(float dt)
    {
        _elapsed += dt;

        // Resolve per-state target tints (RGB = color, A = base alpha).
        var (target1, target2) = GetTargetTints(_stateProvider.State);
        _tint1.Target = target1;
        _tint2.Target = target2;
        _tint1.Update(dt);
        _tint2.Update(dt);

        _particles.Update(dt, _stateProvider.State, Vector2.Zero); // bounds set at render time
    }

    public void RenderBackground(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
    {
        // Refresh particle bounds without advancing time
        _particles.Update(0f, _stateProvider.State, size);

        if (_gradient is not null)
            RenderWarmthGlows(drawList, origin, size);

        RenderParticles(drawList, origin, size);
    }

    private void RenderWarmthGlows(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
    {
        var breathe1 = _glow1Breathe.Sample(_elapsed);
        var breathe2 = _glow2Breathe.Sample(_elapsed);

        var tint1 = _tint1.Current;
        tint1.W *= breathe1;

        var tint2 = _tint2.Current;
        tint2.W *= breathe2 * 0.8f;

        DrawGlowQuad(
            drawList,
            center: origin + new Vector2(-size.X * 0.1f, -size.Y * 0.2f),
            radius: size.X * 1.4f,
            tint: tint1
        );

        DrawGlowQuad(
            drawList,
            center: origin + new Vector2(size.X * 1.1f, size.Y * 1.2f),
            radius: size.X * 1.3f,
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

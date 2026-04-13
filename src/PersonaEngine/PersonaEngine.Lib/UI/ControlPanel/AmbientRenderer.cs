using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
/// Renders ambient background effects: drifting gradient glows and floating particles.
/// Call <see cref="Update"/> then <see cref="RenderBackground"/> each frame.
/// </summary>
public sealed class AmbientRenderer
{
    private readonly PersonaStateProvider _stateProvider;
    private readonly ParticleSystem _particles = new(12);
    private float _elapsed;

    // Two glow positions drift on independent sine paths (values are normalized 0..1 of bounds)
    private readonly SineOscillator _glow1X = new(0.35f, 0.15f, frequencyHz: 0.1f);
    private readonly SineOscillator _glow1Y = new(0.40f, 0.10f, frequencyHz: 0.08f);
    private readonly SineOscillator _glow2X = new(0.65f, 0.12f, frequencyHz: 0.12f);
    private readonly SineOscillator _glow2Y = new(0.55f, 0.10f, frequencyHz: 0.09f);

    public AmbientRenderer(PersonaStateProvider stateProvider)
    {
        _stateProvider = stateProvider;
    }

    public void Update(float dt)
    {
        _elapsed += dt;
        _particles.Update(dt, _stateProvider.State, Vector2.Zero); // bounds set at render time
    }

    public void RenderBackground(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
    {
        // Refresh particle bounds to actual render area without advancing time
        _particles.Update(0f, _stateProvider.State, size);

        RenderGradientGlows(drawList, origin, size);
        RenderParticles(drawList, origin, size);
    }

    private void RenderGradientGlows(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
    {
        var state = _stateProvider.State;

        var (color1, color2, baseAlpha, radiusScale) = state switch
        {
            PersonaUiState.Speaking => (Theme.AccentPrimary, Theme.AccentSecondary, 0.14f, 1.15f),
            PersonaUiState.Thinking => (Theme.AccentSecondary, Theme.AccentPrimary, 0.11f, 1.0f),
            _ => (Theme.AccentSecondary, Theme.AccentSecondary, 0.09f, 1.0f),
        };

        var center1 =
            origin
            + new Vector2(_glow1X.Sample(_elapsed) * size.X, _glow1Y.Sample(_elapsed) * size.Y);
        RenderRadialGlow(drawList, center1, size.X * 0.4f * radiusScale, color1, baseAlpha);

        var center2 =
            origin
            + new Vector2(_glow2X.Sample(_elapsed) * size.X, _glow2Y.Sample(_elapsed) * size.Y);
        RenderRadialGlow(drawList, center2, size.X * 0.35f * radiusScale, color2, baseAlpha * 0.8f);
    }

    private static void RenderRadialGlow(
        ImDrawListPtr drawList,
        Vector2 center,
        float radius,
        Vector4 color,
        float maxAlpha
    )
    {
        // Approximate radial gradient with many concentric circles at decreasing alpha.
        // More rings + quadratic falloff = smoother gradient that reads as ambient light
        // instead of visible concentric rings.
        const int rings = 16;
        for (var i = rings; i >= 1; i--)
        {
            var t = i / (float)rings;
            var r = radius * t;
            // Quadratic falloff: stronger at center, softer toward edges
            var falloff = (1f - t) * (1f - t);
            var alpha = maxAlpha * falloff;
            var col = ImGui.ColorConvertFloat4ToU32(color with { W = alpha });
            ImGui.AddCircleFilled(drawList, center, r, col, 32);
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

            // Feathered circle: outer ring at lower alpha, inner at higher
            ImGui.AddCircleFilled(drawList, screenPos, p.Radius * 2f, outerCol, 16);
            ImGui.AddCircleFilled(drawList, screenPos, p.Radius, col, 16);
        }
    }
}

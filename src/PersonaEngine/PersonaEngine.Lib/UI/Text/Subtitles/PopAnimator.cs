using System.Numerics;

using FontStashSharp;

namespace PersonaEngine.Lib.UI.Text.Subtitles;

/// <summary>
///     Implements a "pop" animation effect using an overshoot easing function.
/// </summary>
public class PopAnimator : IWordAnimator
{
    private readonly float _overshoot;

    public PopAnimator(float overshoot = 1.70158f) { _overshoot = overshoot; }

    public Vector2 CalculateScale(float progress)
    {
        progress = Math.Clamp(progress, 0.0f, 1.0f);

        var scale = 1.0f + (_overshoot + 1.0f) * MathF.Pow(progress - 1.0f, 3) + _overshoot * MathF.Pow(progress - 1.0f, 2);

        scale = Math.Max(0.0f, scale);

        return new Vector2(scale, scale);
    }

    public FSColor CalculateColor(FSColor startColor, FSColor endColor, float progress)
    {
        progress = Math.Clamp(progress, 0.0f, 1.0f);

        var r = (byte)(startColor.R + (endColor.R - startColor.R) * progress);
        var g = (byte)(startColor.G + (endColor.G - startColor.G) * progress);
        var b = (byte)(startColor.B + (endColor.B - startColor.B) * progress);
        var a = (byte)(startColor.A + (endColor.A - startColor.A) * progress);

        return new FSColor(r, g, b, a);
    }
}
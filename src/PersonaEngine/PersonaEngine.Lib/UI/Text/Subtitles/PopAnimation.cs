using System.Numerics;

using FontStashSharp;

namespace PersonaEngine.Lib.UI.Text.Subtitles;

public class PopAnimation : IAnimationStrategy
{
    private readonly float _overshoot;

    public PopAnimation(float overshoot = 1.70158f) { _overshoot = overshoot; }

    public Vector2 CalculateScale(float progress)
    {
        var scale = 1 + (_overshoot + 1) * MathF.Pow(progress - 1, 3) + _overshoot * MathF.Pow(progress - 1, 2);

        return new Vector2(scale, scale);
    }

    public FSColor CalculateColor(FSColor highlight, FSColor normal, float progress)
    {
        var r = (byte)(highlight.R + (normal.R - highlight.R) * progress);
        var g = (byte)(highlight.G + (normal.G - highlight.G) * progress);
        var b = (byte)(highlight.B + (normal.B - highlight.B) * progress);
        var a = (byte)(highlight.A + (normal.A - highlight.A) * progress);

        return new FSColor(r, g, b, a);
    }
}
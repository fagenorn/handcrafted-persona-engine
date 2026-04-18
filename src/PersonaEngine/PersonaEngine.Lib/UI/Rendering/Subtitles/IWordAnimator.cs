using System.Numerics;
using FontStashSharp;

namespace PersonaEngine.Lib.UI.Rendering.Subtitles;

/// <summary>
///     Interface for defining word animation strategies (scale, color).
/// </summary>
public interface IWordAnimator
{
    Vector2 CalculateScale(float progress);

    FSColor CalculateColor(FSColor startColor, FSColor endColor, float progress)
    {
        return PopAnimator.LerpColor(startColor, endColor, progress);
    }
}

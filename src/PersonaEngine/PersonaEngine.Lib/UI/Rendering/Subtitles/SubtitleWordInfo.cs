using System.Numerics;
using FontStashSharp;

namespace PersonaEngine.Lib.UI.Rendering.Subtitles;

/// <summary>
///     Holds the properties and animation state of a single word for rendering.
/// </summary>
public class SubtitleWordInfo
{
    public required string Text { get; init; }

    public required Vector2 Size { get; init; }

    public required float AbsoluteStartTime { get; set; }

    public required float Duration { get; set; }

    public Vector2 Position { get; set; }

    public float AnimationProgress { get; set; }

    public FSColor CurrentColor { get; set; }

    public Vector2 CurrentScale { get; set; }

    public bool IsActive(float currentTime)
    {
        return currentTime >= AbsoluteStartTime && currentTime < AbsoluteStartTime + Duration;
    }

    public bool IsComplete(float currentTime)
    {
        return currentTime >= AbsoluteStartTime + Duration;
    }

    public bool HasStarted(float currentTime)
    {
        return currentTime >= AbsoluteStartTime;
    }
}

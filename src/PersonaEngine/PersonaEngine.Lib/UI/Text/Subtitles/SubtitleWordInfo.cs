using System.Numerics;

using FontStashSharp;

namespace PersonaEngine.Lib.UI.Text.Subtitles;

/// <summary>
///     Holds the intrinsic properties and calculated state of a single word for rendering.
///     Designed as a struct to potentially reduce GC pressure when dealing with many words,
///     but be mindful of copying costs if passed around extensively by value.
/// </summary>
public struct SubtitleWordInfo
{
    public string Text;

    public Vector2 Size;

    public float AbsoluteStartTime;

    public float Duration;

    public Vector2 Position;

    public float AnimationProgress;

    public FSColor CurrentColor;

    public Vector2 CurrentScale;

    public bool IsActive(float currentTime) { return currentTime >= AbsoluteStartTime && currentTime < AbsoluteStartTime + Duration; }

    public bool IsComplete(float currentTime) { return currentTime >= AbsoluteStartTime + Duration; }

    public bool HasStarted(float currentTime) { return currentTime >= AbsoluteStartTime; }
}
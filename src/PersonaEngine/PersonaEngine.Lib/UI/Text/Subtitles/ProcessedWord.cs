using System.Numerics;

namespace PersonaEngine.Lib.UI.Text.Subtitles;

public record ProcessedWord
{
    public ProcessedWord(string text, float startTime, float duration, Vector2 size)
    {
        Text      = text;
        StartTime = startTime;
        Duration  = Math.Max(0.001f, duration);
        Size      = size;
        Position  = Vector2.Zero;
    }

    public string Text { get; }

    public Vector2 Position { get; set; }

    public Vector2 Size { get; }

    public float StartTime { get; }

    public float Duration { get; }

    public float AnimationProgress { get; set; } = 0f;

    public bool HasStarted(float currentTime) { return currentTime >= StartTime; }

    public bool IsComplete(float currentTime) { return currentTime >= StartTime + Duration; }

    public void UpdateAnimationProgress(float currentTime, float deltaTime)
    {
        if ( !HasStarted(currentTime) )
        {
            AnimationProgress = 0f;

            return;
        }

        if ( IsComplete(currentTime) )
        {
            AnimationProgress = 1f;

            return;
        }

        AnimationProgress = MathF.Min(AnimationProgress + deltaTime / Duration, 1f);
    }
}
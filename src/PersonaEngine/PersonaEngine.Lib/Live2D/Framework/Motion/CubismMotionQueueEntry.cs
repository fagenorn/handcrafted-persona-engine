namespace PersonaEngine.Lib.Live2D.Framework.Motion;

public class CubismMotionQueueEntry
{
    public CubismMotionQueueEntry()
    {
        Available = true;
        StartTime = -1.0f;
        EndTime = -1.0f;
    }

    /// <summary>
    ///     Motion
    /// </summary>
    public required ACubismMotion Motion { get; set; }

    /// <summary>
    ///     Activation flag
    /// </summary>
    public bool Available { get; set; }

    /// <summary>
    ///     Completion flag
    /// </summary>
    public bool Finished { get; set; }

    /// <summary>
    ///     Start flag (since 0.9.00)
    /// </summary>
    public bool Started { get; set; }

    /// <summary>
    ///     Motion playback start time [seconds]
    /// </summary>
    public float StartTime { get; set; }

    /// <summary>
    ///     Fade-in start time (only the first time for loops) [seconds]
    /// </summary>
    public float FadeInStartTime { get; set; }

    /// <summary>
    ///     Scheduled end time [seconds]
    /// </summary>
    public float EndTime { get; set; }

    /// <summary>
    ///     Time state [seconds]
    /// </summary>
    public float StateTime { get; private set; }

    /// <summary>
    ///     Weight state
    /// </summary>
    public float StateWeight { get; private set; }

    /// <summary>
    ///     Last time checked by the Motion side
    /// </summary>
    public float LastEventCheckSeconds { get; set; }

    public float FadeOutSeconds { get; private set; }

    public bool IsTriggeredFadeOut { get; private set; }

    /// <summary>
    ///     Set the start of fade-out.
    /// </summary>
    /// <param name="fadeOutSeconds">Time required for fade-out [seconds]</param>
    public void SetFadeout(float fadeOutSeconds)
    {
        FadeOutSeconds = fadeOutSeconds;
        IsTriggeredFadeOut = true;
    }

    /// <summary>
    ///     Start fade-out.
    /// </summary>
    /// <param name="fadeOutSeconds">Time required for fade-out [seconds]</param>
    /// <param name="userTimeSeconds">Accumulated delta time [seconds]</param>
    public void StartFadeout(float fadeOutSeconds, float userTimeSeconds)
    {
        var newEndTimeSeconds = userTimeSeconds + fadeOutSeconds;
        IsTriggeredFadeOut = true;

        if (EndTime < 0.0f || newEndTimeSeconds < EndTime)
        {
            EndTime = newEndTimeSeconds;
        }
    }

    /// <summary>
    ///     Set the motion state.
    /// </summary>
    /// <param name="timeSeconds">Current time [seconds]</param>
    /// <param name="weight">Motion weight</param>
    public void SetState(float timeSeconds, float weight)
    {
        StateTime = timeSeconds;
        StateWeight = weight;
    }
}

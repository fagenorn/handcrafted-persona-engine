namespace PersonaEngine.Lib.Live2D.Behaviour.Emotion;

/// <summary>
/// Represents an emotion with its timestamp in the audio
/// </summary>
public record EmotionTiming
{
    /// <summary>
    /// Timestamp in seconds when the emotion occurs
    /// </summary>
    public double Timestamp { get; set; }

    /// <summary>
    /// Emotion emoji
    /// </summary>
    public string Emotion { get; set; } = string.Empty;
}

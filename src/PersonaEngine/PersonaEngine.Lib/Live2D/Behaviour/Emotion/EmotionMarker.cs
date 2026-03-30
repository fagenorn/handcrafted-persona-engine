namespace PersonaEngine.Lib.Live2D.Behaviour.Emotion;

/// <summary>
///     Stores emotion data extracted from text with its position information
/// </summary>
public record EmotionMarker
{
    /// <summary>
    ///     Character position in the cleaned text
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    ///     Emotion emoji
    /// </summary>
    public string Emotion { get; set; } = string.Empty;
}

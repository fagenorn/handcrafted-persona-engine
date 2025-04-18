namespace PersonaEngine.Lib.Live2D.Behaviour.Emotion;

internal record EmotionMarkerInfo
{
    public required string MarkerId { get; init; }

    public required string Emotion { get; init; }
}
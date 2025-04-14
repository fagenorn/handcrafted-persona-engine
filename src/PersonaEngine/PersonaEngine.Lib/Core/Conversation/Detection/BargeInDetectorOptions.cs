namespace PersonaEngine.Lib.Core.Conversation.Detection;

// TODO: Move this to configuration namespace later
public class BargeInDetectorOptions
{
    public int MinLength { get; set; } = 5;

    public TimeSpan DetectionDuration { get; set; } = TimeSpan.FromMilliseconds(400);
}
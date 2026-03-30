namespace PersonaEngine.Lib.TTS.Profanity;

/// <summary>
///     Represents the severity level of profanity.
/// </summary>
public enum ProfanitySeverity
{
    Clean, // No profanity detected.

    Mild, // Some profanity detected.

    Moderate, // Moderate profanity detected.

    Severe, // High level of profanity detected.
}

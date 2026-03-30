namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Token with part-of-speech information
/// </summary>
public class PosToken
{
    /// <summary>
    ///     The text of the token
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    ///     Part of speech tag
    /// </summary>
    public string? PartOfSpeech { get; set; }

    /// <summary>
    ///     Whitespace after this token
    /// </summary>
    public bool IsWhitespace { get; set; }
}

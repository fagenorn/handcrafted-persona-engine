namespace PersonaEngine.Lib.TTS.Synthesis;

internal readonly record struct PreprocessedText(
    string ProcessedText,
    List<string> Tokens,
    Dictionary<int, object> Features
);

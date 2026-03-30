using System.Globalization;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.ASR.Transcriber;

public record SpeechTranscriptorOptions
{
    public bool LanguageAutoDetect { get; set; } = true;

    public bool RetrieveTokenDetails { get; set; }

    public CultureInfo Language { get; set; } = CultureInfo.GetCultureInfo("en-us");

    public string? Prompt { get; set; }

    public WhisperConfigTemplate? Template { get; set; }
}

namespace PersonaEngine.Lib.TTS.Synthesis;

public class PhonemeContext
{
    public bool UseBritishEnglish { get; set; }

    public bool? NextStartsWithVowel { get; set; }

    public bool HasToToken { get; set; }
}

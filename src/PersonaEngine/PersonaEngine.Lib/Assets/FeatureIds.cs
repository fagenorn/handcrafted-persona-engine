namespace PersonaEngine.Lib.Assets;

public static class FeatureIds
{
    public static readonly FeatureId LlmText = new("Llm.Text");
    public static readonly FeatureId LlmVision = new("Llm.Vision");
    public static readonly FeatureId AsrFastTier = new("Asr.WhisperTiny");
    public static readonly FeatureId AsrAccurate = new("Asr.WhisperTurbo");
    public static readonly FeatureId TtsKokoro = new("Tts.Kokoro");
    public static readonly FeatureId TtsQwen3 = new("Tts.Qwen3");
    public static readonly FeatureId VoiceCloning = new("Tts.Rvc");
    public static readonly FeatureId Audio2Face = new("Tts.Audio2Face");
    public static readonly FeatureId VisionCapture = new("Vision.ScreenCapture");
    public static readonly FeatureId ProfanityFilter = new("Tts.ProfanityBeep");
    public static readonly FeatureId Live2DAvatar = new("Live2D.Avatar");
    public static readonly FeatureId MusicSourceSeparation = new("Music.SourceSeparation");

    public static IEnumerable<FeatureId> All =>
        typeof(FeatureIds)
            .GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
            )
            .Where(f => f.FieldType == typeof(FeatureId))
            .Select(f => (FeatureId)f.GetValue(null)!);
}

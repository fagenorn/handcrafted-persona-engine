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

    /// <summary>
    ///     Manifest-only sentinel used to gate the Live2D avatar bundle download
    ///     (see <c>install-manifest.json</c>). Live2D rendering is always wired
    ///     in DI via <c>AddLive2D</c> because the avatar is a core UX surface;
    ///     this id exists so the bootstrapper can plan/install the avatar assets
    ///     and surface enable/disable toggles without coupling DI to asset state.
    /// </summary>
    public static readonly FeatureId Live2DAvatar = new("Live2D.Avatar");

    /// <summary>
    ///     Manifest-only sentinel used to gate music source-separation model
    ///     downloads (MDX bundle + Mel-band RoFormer). The separation services
    ///     are constructed on demand by their callers (not via DI), so there is
    ///     no conditional registration to wire — gating is enforced when the
    ///     models are loaded from disk.
    /// </summary>
    public static readonly FeatureId MusicSourceSeparation = new("Music.SourceSeparation");

    public static IEnumerable<FeatureId> All =>
        typeof(FeatureIds)
            .GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
            )
            .Where(f => f.FieldType == typeof(FeatureId))
            .Select(f => (FeatureId)f.GetValue(null)!);
}

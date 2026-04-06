namespace PersonaEngine.Lib.TTS.Synthesis.Engine;

/// <summary>
///     Resolves the currently active <see cref="ISentenceSynthesizer" /> based on configuration.
///     Watches <c>IOptionsMonitor&lt;TtsConfiguration&gt;</c> for hot-reload of the ActiveEngine setting.
/// </summary>
public interface ITtsEngineProvider
{
    /// <summary>
    ///     Returns the currently active sentence synthesizer.
    /// </summary>
    ISentenceSynthesizer Current { get; }

    /// <summary>
    ///     Returns all registered engine IDs and their capabilities,
    ///     so the UI can populate engine selection and show relevant controls.
    /// </summary>
    IReadOnlyList<TtsEngineInfo> AvailableEngines { get; }
}

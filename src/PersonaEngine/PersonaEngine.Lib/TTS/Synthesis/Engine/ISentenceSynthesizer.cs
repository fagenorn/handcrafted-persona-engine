namespace PersonaEngine.Lib.TTS.Synthesis.Engine;

/// <summary>
///     Converts text into speech audio. Each implementation encapsulates one TTS backend.
///     Use <see cref="CreateSession" /> to obtain a scoped, stateful session for a
///     conversation turn — sessions may carry state across sentences for continuity.
/// </summary>
public interface ISentenceSynthesizer : IAsyncDisposable
{
    /// <summary>
    ///     Unique identifier for this engine (e.g., "kokoro", "qwen3").
    ///     Used for configuration binding and runtime selection.
    /// </summary>
    string EngineId { get; }

    /// <summary>
    ///     Declares what this engine supports, so the UI can show/hide relevant controls.
    /// </summary>
    TtsEngineCapabilities Capabilities { get; }

    /// <summary>
    ///     Creates a new synthesis session for one conversation turn.
    ///     The caller must dispose the session when the turn ends.
    /// </summary>
    ISynthesisSession CreateSession();
}

/// <summary>
///     Declares what features a TTS engine supports. Used by the UI to show relevant controls.
/// </summary>
[Flags]
public enum TtsEngineCapabilities
{
    None = 0,

    /// <summary>Supports phoneme-level control (e.g., custom pronunciation).</summary>
    PhonemeControl = 1 << 0,

    /// <summary>Supports voice instruction prompting (e.g., "sexy and breathy").</summary>
    VoiceInstruction = 1 << 1,

    /// <summary>Supports adjustable speech speed.</summary>
    SpeedControl = 1 << 2,
}

/// <summary>
///     Metadata about a registered TTS engine for UI display and engine selection.
/// </summary>
public readonly record struct TtsEngineInfo(string EngineId, TtsEngineCapabilities Capabilities);

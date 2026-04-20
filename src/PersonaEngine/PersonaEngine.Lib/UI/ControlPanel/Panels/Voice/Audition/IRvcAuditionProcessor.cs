using PersonaEngine.Lib.TTS.RVC;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Audition;

/// <summary>
///     Abstraction over <see cref="RVCFilter" /> for the audition pipeline.
///     Exists so tests can substitute the heavy ONNX-backed filter with a no-op stub.
/// </summary>
public interface IRvcAuditionProcessor
{
    void Apply(AudioSegment segment, RvcOverride @override);
}

/// <summary>
///     Default implementation: delegates directly to <see cref="RVCFilter.Process(AudioSegment, RvcOverride?)" />.
/// </summary>
public sealed class RvcAuditionProcessor(RVCFilter filter) : IRvcAuditionProcessor
{
    public void Apply(AudioSegment segment, RvcOverride @override) =>
        filter.Process(segment, @override);
}

/// <summary>
///     Used when the VoiceCloning feature is disabled (e.g. TryItOut profile)
///     so <see cref="VoiceAuditionService" /> can still be constructed via DI.
///     <see cref="VoiceAuditionService" /> only invokes <see cref="Apply" /> when the
///     audition request opts into RVC, so reaching this method indicates a UI bug
///     that surfaced an RVC option without the feature being available — fail loudly.
/// </summary>
public sealed class NoOpRvcAuditionProcessor : IRvcAuditionProcessor
{
    public void Apply(AudioSegment segment, RvcOverride @override) =>
        throw new InvalidOperationException(
            "RVC audition was requested but the VoiceCloning feature is not enabled in the current install profile."
        );
}

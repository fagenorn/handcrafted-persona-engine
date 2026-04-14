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

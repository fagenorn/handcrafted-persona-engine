namespace PersonaEngine.Lib.TTS.RVC;

/// <summary>
///     Metadata for a loaded RVC voice model.
/// </summary>
/// <param name="ModelPath">Full path to the ONNX model file.</param>
/// <param name="OutputSampleRate">Sample rate the model produces (varies per voice, e.g. 32000, 40000, 48000).</param>
public record RvcVoiceInfo(string ModelPath, int OutputSampleRate);

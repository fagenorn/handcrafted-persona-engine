using Microsoft.ML.OnnxRuntime;

namespace PersonaEngine.Lib.ASR.VAD;

internal class SileroInferenceState(OrtIoBinding binding)
{
    public float[] State { get; set; } = new float[SileroConstants.StateSize];

    public float[] PendingState { get; set; } = new float[SileroConstants.StateSize];

    public float[] Output { get; set; } = new float[SileroConstants.OutputSize];

    public OrtIoBinding Binding { get; } = binding;
}

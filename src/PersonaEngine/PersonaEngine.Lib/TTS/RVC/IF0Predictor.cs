namespace PersonaEngine.Lib.TTS.RVC;

public interface IF0Predictor : IDisposable
{
    void ComputeF0(ReadOnlyMemory<float> wav, Memory<float> f0Output, int length);
}

namespace PersonaEngine.Lib.TTS.RVC;

public class ACFMethod : IF0Predictor
{
    private readonly int SampleRate;

    private int HopLength;

    public ACFMethod(int hopLength, int samplingRate)
    {
        HopLength = hopLength;
        SampleRate = samplingRate;
    }

    public void ComputeF0(ReadOnlyMemory<float> wav, Memory<float> f0Output, int length)
    {
        HopLength = (int)Math.Floor(wav.Length / (double)length);

        var wavSpan = wav.Span;
        var f0Span = f0Output.Span;
        var frameLength = HopLength;

        for (var i = 0; i < length; i++)
        {
            // Create a window for this frame without allocations
            var startIdx = i * frameLength;
            var endIdx = Math.Min(startIdx + (int)(frameLength * 1.5), wav.Length);
            var frameSize = endIdx - startIdx;

            f0Span[i] = ComputeF0ForFrame(wavSpan.Slice(startIdx, frameSize));
        }
    }

    public void Dispose() { }

    private float ComputeF0ForFrame(ReadOnlySpan<float> frame)
    {
        var n = frame.Length;
        Span<float> autocorrelation = stackalloc float[n]; // Use stack allocation to avoid heap allocations

        // Calculate autocorrelation function
        for (var lag = 0; lag < n; lag++)
        {
            float sum = 0;
            for (var i = 0; i < n - lag; i++)
            {
                sum += frame[i] * frame[i + lag];
            }

            autocorrelation[lag] = sum;
        }

        // Ignore zero-delay peak, find first non-zero delay peak
        var peakIndex = 1;
        var maxVal = autocorrelation[1];
        for (
            ;
            peakIndex < autocorrelation.Length && (maxVal = autocorrelation[peakIndex]) > 0;
            peakIndex++
        )
        {
            ;
        }

        for (var lag = peakIndex; lag < n; lag++)
        {
            if (autocorrelation[lag] > maxVal)
            {
                maxVal = autocorrelation[lag];
                peakIndex = lag;
            }
        }

        // Calculate fundamental frequency
        var f0 = SampleRate / (float)peakIndex;

        return f0;
    }
}

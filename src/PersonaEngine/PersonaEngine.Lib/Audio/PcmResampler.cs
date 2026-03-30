using System.Buffers.Binary;

namespace PersonaEngine.Lib.Audio;

public class PcmResampler
{
    // Constants for audio format
    private const int BYTES_PER_SAMPLE = 2;

    private const int MAX_FRAME_SIZE = 1920;

    private readonly float[] _filterCoefficients;

    private readonly int _filterDelay;

    // Filter configuration
    private readonly int _filterTaps;

    private readonly float[] _floatHistory;

    private readonly short[] _history;

    // Sample rate configuration

    // Pre-allocated working buffers
    private readonly short[] _inputSamples;

    private int _historyLength;

    // State tracking
    private float _position = 0.0f;

    public PcmResampler(int inputSampleRate = 48000, int outputSampleRate = 16000)
    {
        if (inputSampleRate <= 0 || outputSampleRate <= 0)
        {
            throw new ArgumentException("Sample rates must be positive values");
        }

        InputSampleRate = inputSampleRate;
        OutputSampleRate = outputSampleRate;
        ResampleRatio = (float)InputSampleRate / OutputSampleRate;

        _filterTaps = DetermineOptimalFilterTaps(ResampleRatio);
        _filterDelay = _filterTaps / 2;

        var cutoffFrequency =
            Math.Min(0.45f * OutputSampleRate, 0.9f * OutputSampleRate / 2) / InputSampleRate;
        _filterCoefficients = GenerateLowPassFilter(_filterTaps, cutoffFrequency);

        _history = new short[_filterTaps + 10];
        _floatHistory = new float[_filterTaps + 10];
        _historyLength = 0;

        _inputSamples = new short[MAX_FRAME_SIZE];
    }

    public float ResampleRatio { get; }

    public int InputSampleRate { get; }

    public int OutputSampleRate { get; }

    private int DetermineOptimalFilterTaps(float ratio)
    {
        if (Math.Abs(ratio - Math.Round(ratio)) < 0.01f)
        {
            return Math.Max(24, (int)(12 * ratio));
        }

        return Math.Max(36, (int)(18 * ratio));
    }

    private float[] GenerateLowPassFilter(int taps, float cutoff)
    {
        var coefficients = new float[taps];
        var center = taps / 2;

        var sum = 0.0;
        for (var i = 0; i < taps; i++)
        {
            if (i == center)
            {
                coefficients[i] = (float)(2.0 * Math.PI * cutoff);
            }
            else
            {
                var x = 2.0 * Math.PI * cutoff * (i - center);
                coefficients[i] = (float)(Math.Sin(x) / x);
            }

            var window =
                0.42
                - 0.5 * Math.Cos(2.0 * Math.PI * i / (taps - 1))
                + 0.08 * Math.Cos(4.0 * Math.PI * i / (taps - 1));

            coefficients[i] *= (float)window;

            sum += coefficients[i];
        }

        for (var i = 0; i < taps; i++)
        {
            coefficients[i] /= (float)sum;
        }

        return coefficients;
    }

    public int Process(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var inputSampleCount = input.Length / BYTES_PER_SAMPLE;
        ConvertToShorts(input, _inputSamples, inputSampleCount);

        var maxOutputSamples = (int)Math.Ceiling(inputSampleCount / ResampleRatio) + 2;
        if (output.Length < maxOutputSamples * BYTES_PER_SAMPLE)
        {
            throw new ArgumentException("Output buffer is too small for the resampled data");
        }

        var outputIndex = 0;

        while (_position < inputSampleCount)
        {
            float sum = 0;
            var baseIndex = (int)Math.Floor(_position);

            for (var tap = 0; tap < _filterTaps; tap++)
            {
                var sampleIndex = baseIndex - _filterDelay + tap;
                var sample = GetSampleWithHistory(sampleIndex, _inputSamples, inputSampleCount);
                sum += sample * _filterCoefficients[tap];
            }

            var outputValue = (short)
                Math.Clamp((int)Math.Round(sum), short.MinValue, short.MaxValue);
            if (outputIndex < maxOutputSamples)
            {
                BinaryPrimitives.WriteInt16LittleEndian(
                    output.Slice(outputIndex * BYTES_PER_SAMPLE, BYTES_PER_SAMPLE),
                    outputValue
                );

                outputIndex++;
            }

            _position += ResampleRatio;
        }

        UpdateHistory(_inputSamples, inputSampleCount);
        _position -= inputSampleCount;

        return outputIndex * BYTES_PER_SAMPLE;
    }

    public int Process(Stream input, Memory<byte> output)
    {
        var buffer = new byte[MAX_FRAME_SIZE * BYTES_PER_SAMPLE];
        var bytesRead = input.Read(buffer, 0, buffer.Length);

        return Process(buffer.AsSpan(0, bytesRead), output.Span);
    }

    public int ProcessInPlace(Span<byte> buffer)
    {
        if (ResampleRatio < 1.0f)
        {
            throw new InvalidOperationException(
                "In-place resampling only supports downsampling (input rate > output rate)"
            );
        }

        var inputSampleCount = buffer.Length / BYTES_PER_SAMPLE;

        // Make a copy of the input for processing
        Span<short> inputCopy = stackalloc short[inputSampleCount];
        for (var i = 0; i < inputSampleCount; i++)
        {
            inputCopy[i] = BinaryPrimitives.ReadInt16LittleEndian(
                buffer.Slice(i * BYTES_PER_SAMPLE, BYTES_PER_SAMPLE)
            );
        }

        var expectedOutputCount = (int)Math.Ceiling(inputSampleCount / ResampleRatio);

        // Calculate positions and work from last to first
        var outputIndex = expectedOutputCount - 1;
        var lastPosition =
            _position + (inputSampleCount - 1) - ResampleRatio * (expectedOutputCount - 1);

        while (lastPosition >= 0 && outputIndex >= 0)
        {
            float sum = 0;
            var baseIndex = (int)Math.Floor(lastPosition);

            for (var tap = 0; tap < _filterTaps; tap++)
            {
                var sampleIndex = baseIndex - _filterDelay + tap;
                short sample;

                if (sampleIndex >= 0 && sampleIndex < inputSampleCount)
                {
                    sample = inputCopy[sampleIndex];
                }
                else if (sampleIndex < 0 && -sampleIndex <= _historyLength)
                {
                    sample = _history[_historyLength + sampleIndex];
                }
                else
                {
                    sample = 0;
                }

                sum += sample * _filterCoefficients[tap];
            }

            var outputValue = (short)
                Math.Clamp((int)Math.Round(sum), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(
                buffer.Slice(outputIndex * BYTES_PER_SAMPLE, BYTES_PER_SAMPLE),
                outputValue
            );

            outputIndex--;
            lastPosition -= ResampleRatio;
        }

        // We need to keep the input history despite having processed in-place
        UpdateHistory(inputCopy.ToArray(), inputSampleCount);
        _position = _position + inputSampleCount - ResampleRatio * expectedOutputCount;

        return expectedOutputCount * BYTES_PER_SAMPLE;
    }

    public int ProcessFloat(ReadOnlySpan<float> input, Span<float> output)
    {
        var inputSampleCount = input.Length;

        var maxOutputSamples = (int)Math.Ceiling(inputSampleCount / ResampleRatio) + 2;
        if (output.Length < maxOutputSamples)
        {
            throw new ArgumentException("Output buffer is too small for the resampled data");
        }

        var outputIndex = 0;

        while (_position < inputSampleCount)
        {
            float sum = 0;
            var baseIndex = (int)Math.Floor(_position);

            for (var tap = 0; tap < _filterTaps; tap++)
            {
                var sampleIndex = baseIndex - _filterDelay + tap;
                var sample = GetFloatSampleWithHistory(sampleIndex, input);
                sum += sample * _filterCoefficients[tap];
            }

            if (outputIndex < maxOutputSamples)
            {
                output[outputIndex] = sum;
                outputIndex++;
            }

            _position += ResampleRatio;
        }

        UpdateFloatHistory(input);
        _position -= inputSampleCount;

        return outputIndex;
    }

    public int ProcessFloatInPlace(Span<float> buffer)
    {
        // For in-place, we must work backward to avoid overwriting unprocessed input
        if (ResampleRatio < 1.0f)
        {
            throw new InvalidOperationException(
                "In-place resampling only supports downsampling (input rate > output rate)"
            );
        }

        var inputSampleCount = buffer.Length;
        var expectedOutputCount = (int)Math.Ceiling(inputSampleCount / ResampleRatio);

        // First, store the full input for history and reference
        var inputCopy = new float[inputSampleCount];
        buffer.CopyTo(inputCopy);

        // Calculate sample positions and work from last to first
        var outputIndex = expectedOutputCount - 1;
        var lastPosition =
            _position + (inputSampleCount - 1) - ResampleRatio * (expectedOutputCount - 1);

        while (lastPosition >= 0 && outputIndex >= 0)
        {
            float sum = 0;
            var baseIndex = (int)Math.Floor(lastPosition);

            for (var tap = 0; tap < _filterTaps; tap++)
            {
                var sampleIndex = baseIndex - _filterDelay + tap;
                float sample;

                if (sampleIndex >= 0 && sampleIndex < inputSampleCount)
                {
                    sample = inputCopy[sampleIndex];
                }
                else if (sampleIndex < 0 && -sampleIndex <= _historyLength)
                {
                    sample = _floatHistory[_historyLength + sampleIndex];
                }
                else
                {
                    sample = 0f;
                }

                sum += sample * _filterCoefficients[tap];
            }

            buffer[outputIndex] = sum;
            outputIndex--;
            lastPosition -= ResampleRatio;
        }

        UpdateFloatHistory(inputCopy);
        _position = _position + inputSampleCount - ResampleRatio * expectedOutputCount;

        return expectedOutputCount;
    }

    private short GetSampleWithHistory(int index, short[] inputSamples, int inputSampleCount)
    {
        if (index >= 0 && index < inputSampleCount)
        {
            return inputSamples[index];
        }

        if (index < 0 && -index <= _historyLength)
        {
            return _history[_historyLength + index];
        }

        return 0;
    }

    private float GetFloatSampleWithHistory(int index, ReadOnlySpan<float> inputSamples)
    {
        if (index >= 0 && index < inputSamples.Length)
        {
            return inputSamples[index];
        }

        if (index < 0 && -index <= _historyLength)
        {
            return _floatHistory[_historyLength + index];
        }

        return 0f;
    }

    private void UpdateHistory(short[] currentFrame, int frameLength)
    {
        var samplesToKeep = Math.Min(frameLength, _history.Length);

        if (samplesToKeep > 0)
        {
            var unusedHistorySamples = Math.Min(_historyLength, _history.Length - samplesToKeep);
            if (unusedHistorySamples > 0)
            {
                Array.Copy(
                    _history,
                    _historyLength - unusedHistorySamples,
                    _history,
                    0,
                    unusedHistorySamples
                );
            }

            Array.Copy(
                currentFrame,
                frameLength - samplesToKeep,
                _history,
                unusedHistorySamples,
                samplesToKeep
            );
            _historyLength = unusedHistorySamples + samplesToKeep;
        }

        _historyLength = Math.Min(_historyLength, _history.Length);
    }

    private void UpdateFloatHistory(ReadOnlySpan<float> currentFrame)
    {
        var samplesToKeep = Math.Min(currentFrame.Length, _floatHistory.Length);

        if (samplesToKeep > 0)
        {
            var unusedHistorySamples = Math.Min(
                _historyLength,
                _floatHistory.Length - samplesToKeep
            );
            if (unusedHistorySamples > 0)
            {
                Array.Copy(
                    _floatHistory,
                    _historyLength - unusedHistorySamples,
                    _floatHistory,
                    0,
                    unusedHistorySamples
                );
            }

            for (var i = 0; i < samplesToKeep; i++)
            {
                _floatHistory[unusedHistorySamples + i] = currentFrame[
                    currentFrame.Length - samplesToKeep + i
                ];
            }

            _historyLength = unusedHistorySamples + samplesToKeep;
        }

        _historyLength = Math.Min(_historyLength, _floatHistory.Length);
    }

    private void ConvertToShorts(ReadOnlySpan<byte> input, short[] output, int sampleCount)
    {
        for (var i = 0; i < sampleCount; i++)
        {
            output[i] = BinaryPrimitives.ReadInt16LittleEndian(
                input.Slice(i * BYTES_PER_SAMPLE, BYTES_PER_SAMPLE)
            );
        }
    }

    public void Reset()
    {
        _position = 0;
        _historyLength = 0;
        Array.Clear(_history, 0, _history.Length);
        Array.Clear(_floatHistory, 0, _floatHistory.Length);
    }
}

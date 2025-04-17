using System.Buffers;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PersonaEngine.Lib.TTS.RVC;

public class CrepeOnnx : IF0Predictor
{
    private const int SAMPLE_RATE = 16000;

    private const int WINDOW_SIZE = 1024;

    private const int PITCH_BINS = 360;

    private const int HOP_LENGTH = SAMPLE_RATE / 100; // 10ms

    private const int BATCH_SIZE = 512;

    private readonly float[] _inputBatchBuffer;

    // Preallocated buffers for zero-allocation processing
    private readonly DenseTensor<float> _inputTensor;

    private readonly float[] _logPInitBuffer;

    // Additional preallocated buffers for Viterbi decoding
    private readonly float[,] _logProbBuffer;

    private readonly float[] _medianBuffer;

    private readonly int[,] _ptrBuffer;

    private readonly InferenceSession _session;

    private readonly int[] _stateBuffer;

    private readonly float[,] _transitionMatrix;

    private readonly float[,] _transOutBuffer;

    private readonly float[,] _valueBuffer;

    private readonly float[] _windowBuffer;

    public CrepeOnnx(string modelPath)
    {
        var options = new SessionOptions {
                                             EnableMemoryPattern    = true,
                                             ExecutionMode          = ExecutionMode.ORT_PARALLEL,
                                             InterOpNumThreads      = Environment.ProcessorCount,
                                             IntraOpNumThreads      = Environment.ProcessorCount,
                                             GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                                             LogSeverityLevel       = OrtLoggingLevel.ORT_LOGGING_LEVEL_FATAL
                                         };

        options.AppendExecutionProvider_CPU();

        _session = new InferenceSession(modelPath, options);

        // Preallocate buffers
        _inputBatchBuffer = new float[BATCH_SIZE * WINDOW_SIZE];
        _inputTensor      = new DenseTensor<float>(new[] { BATCH_SIZE, WINDOW_SIZE });
        _windowBuffer     = new float[WINDOW_SIZE];
        _medianBuffer     = new float[5]; // For median filtering with window size 5

        // Preallocate Viterbi algorithm buffers
        _logProbBuffer    = new float[BATCH_SIZE, PITCH_BINS];
        _valueBuffer      = new float[BATCH_SIZE, PITCH_BINS];
        _ptrBuffer        = new int[BATCH_SIZE, PITCH_BINS];
        _logPInitBuffer   = new float[PITCH_BINS];
        _transitionMatrix = CreateTransitionMatrix();
        _transOutBuffer   = new float[PITCH_BINS, PITCH_BINS];
        _stateBuffer      = new int[BATCH_SIZE];

        // Initialize _logPInitBuffer with equal probabilities
        for ( var i = 0; i < PITCH_BINS; i++ )
        {
            _logPInitBuffer[i] = (float)Math.Log(1.0f / PITCH_BINS + float.Epsilon);
        }
    }

    public void Dispose() { _session?.Dispose(); }

    public void ComputeF0(ReadOnlyMemory<float> wav, Memory<float> f0Output, int length)
    {
        var wavSpan    = wav.Span;
        var outputSpan = f0Output.Span;

        if ( length > outputSpan.Length )
        {
            throw new ArgumentException("Output buffer is too small", nameof(f0Output));
        }

        // Preallocate periodicity data buffer
        var pdBuffer = ArrayPool<float>.Shared.Rent(length);
        try
        {
            var pdSpan = new Span<float>(pdBuffer, 0, length);

            // Process the audio to extract F0
            Crepe(wavSpan, outputSpan.Slice(0, length), pdSpan);

            // Apply post-processing
            for ( var i = 0; i < length; i++ )
            {
                if ( pdSpan[i] < 0.1 )
                {
                    outputSpan[i] = 0;
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(pdBuffer);
        }
    }

    private void Crepe(ReadOnlySpan<float> x, Span<float> f0, Span<float> pd)
    {
        var total_frames = 1 + x.Length / HOP_LENGTH;

        for ( var b = 0; b < total_frames; b += BATCH_SIZE )
        {
            var currentBatchSize = Math.Min(BATCH_SIZE, total_frames - b);

            // Fill the batch input buffer
            FillBatch(x, b, currentBatchSize);

            // Run inference on the batch
            var probabilities = Run(currentBatchSize);

            // Decode the probabilities into F0 values
            Decode(probabilities, f0, pd, currentBatchSize, b);
        }

        // Apply post-processing
        MedianFilter(pd, 3);
        MeanFilter(f0, 3);
    }

    private void FillBatch(ReadOnlySpan<float> x, int batchOffset, int batchSize)
    {
        for ( var i = 0; i < batchSize; i++ )
        {
            var frameIndex  = batchOffset + i;
            var inputOffset = i * WINDOW_SIZE;
            FillFrame(x, _inputBatchBuffer.AsSpan(inputOffset, WINDOW_SIZE), frameIndex);
        }
    }

    private void FillFrame(ReadOnlySpan<float> x, Span<float> frame, int frameIndex)
    {
        var pad   = WINDOW_SIZE / 2;
        var start = frameIndex * HOP_LENGTH - pad;

        for ( var j = 0; j < WINDOW_SIZE; j++ )
        {
            var   k = start + j;
            float v = 0;

            if ( k < 0 )
            {
                // Reflection
                k = -k;
            }

            if ( k >= x.Length )
            {
                // Reflection
                k = x.Length - 1 - (k - x.Length);
            }

            if ( k >= 0 && k < x.Length )
            {
                v = x[k];
            }

            frame[j] = v;
        }

        // Normalize the frame
        Normalize(frame);
    }

    private void Normalize(Span<float> input)
    {
        // Mean center and scale
        float sum = 0;
        for ( var j = 0; j < input.Length; j++ )
        {
            sum += input[j];
        }

        var   mean     = sum / input.Length;
        float stdValue = 0;

        for ( var j = 0; j < input.Length; j++ )
        {
            input[j] =  input[j] - mean;
            stdValue += input[j] * input[j];
        }

        stdValue = stdValue / input.Length;
        stdValue = (float)Math.Sqrt(stdValue);

        if ( stdValue < 1e-10 )
        {
            stdValue = 1e-10f;
        }

        for ( var j = 0; j < input.Length; j++ )
        {
            input[j] = input[j] / stdValue;
        }
    }

    private float[] Run(int batchSize)
    {
        // Copy the batch data to the input tensor
        for ( var i = 0; i < batchSize; i++ )
        {
            for ( var j = 0; j < WINDOW_SIZE; j++ )
            {
                _inputTensor[i, j] = _inputBatchBuffer[i * WINDOW_SIZE + j];
            }
        }

        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input", _inputTensor) };

        using var results = _session.Run(inputs);

        return results[0].AsTensor<float>().ToArray();
    }

    private void Decode(float[] probabilities, Span<float> f0, Span<float> pd, int outputSize, int offset)
    {
        // Remove frequencies outside of allowable range
        const int minidx = 39;  // 50hz
        const int maxidx = 308; // 2006hz

        for ( var t = 0; t < outputSize; t++ )
        {
            for ( var i = 0; i < PITCH_BINS; i++ )
            {
                if ( i < minidx || i >= maxidx )
                {
                    probabilities[t * PITCH_BINS + i] = float.NegativeInfinity;
                }
            }
        }

        // Use Viterbi algorithm to decode the probabilities
        DecodeViterbi(probabilities, f0, pd, outputSize, offset);
    }

    private void DecodeViterbi(float[] probabilities, Span<float> f0, Span<float> pd, int nSteps, int offset)
    {
        // Transfer probabilities to log_prob buffer and apply softmax
        for ( var i = 0; i < nSteps * PITCH_BINS; i++ )
        {
            _logProbBuffer[i / PITCH_BINS, i % PITCH_BINS] = probabilities[i];
        }

        Softmax(_logProbBuffer, nSteps);

        // Apply log to probabilities
        for ( var y = 0; y < nSteps; y++ )
        {
            for ( var x = 0; x < PITCH_BINS; x++ )
            {
                _logProbBuffer[y, x] = (float)Math.Log(_logProbBuffer[y, x] + float.Epsilon);
            }
        }

        // Initialize first step values
        for ( var i = 0; i < PITCH_BINS; i++ )
        {
            _valueBuffer[0, i] = _logProbBuffer[0, i] + _logPInitBuffer[i];
        }

        // Viterbi algorithm
        for ( var t = 1; t < nSteps; t++ )
        {
            // Calculate transition outputs
            for ( var y = 0; y < PITCH_BINS; y++ )
            {
                for ( var x = 0; x < PITCH_BINS; x++ )
                {
                    _transOutBuffer[y, x] = _valueBuffer[t - 1, x] + _transitionMatrix[x, y]; // Transposed matrix
                }
            }

            for ( var j = 0; j < PITCH_BINS; j++ )
            {
                // Find argmax
                var maxI    = 0;
                var maxProb = float.NegativeInfinity;
                for ( var k = 0; k < PITCH_BINS; k++ )
                {
                    if ( maxProb < _transOutBuffer[j, k] )
                    {
                        maxProb = _transOutBuffer[j, k];
                        maxI    = k;
                    }
                }

                _ptrBuffer[t, j]   = maxI;
                _valueBuffer[t, j] = _logProbBuffer[t, j] + _transOutBuffer[j, _ptrBuffer[t, j]];
            }
        }

        // Find the most likely final state
        var maxI2    = 0;
        var maxProb2 = float.NegativeInfinity;
        for ( var k = 0; k < PITCH_BINS; k++ )
        {
            if ( maxProb2 < _valueBuffer[nSteps - 1, k] )
            {
                maxProb2 = _valueBuffer[nSteps - 1, k];
                maxI2    = k;
            }
        }

        // Backward pass to find optimal path
        _stateBuffer[nSteps - 1] = maxI2;
        for ( var t = nSteps - 2; t >= 0; t-- )
        {
            _stateBuffer[t] = _ptrBuffer[t + 1, _stateBuffer[t + 1]];
        }

        // Convert to f0 values
        for ( var t = 0; t < nSteps; t++ )
        {
            var bins        = _stateBuffer[t];
            var periodicity = Periodicity(probabilities, t, bins);
            var frequency   = ConvertToFrequency(bins);

            if ( offset + t < f0.Length )
            {
                f0[offset + t] = frequency;
                pd[offset + t] = periodicity;
            }
        }
    }

    private void Softmax(float[,] data, int nSteps)
    {
        for ( var t = 0; t < nSteps; t++ )
        {
            float sum = 0;
            for ( var i = 0; i < PITCH_BINS; i++ )
            {
                sum += (float)Math.Exp(data[t, i]);
            }

            for ( var i = 0; i < PITCH_BINS; i++ )
            {
                data[t, i] = (float)Math.Exp(data[t, i]) / sum;
            }
        }
    }

    private float[,] CreateTransitionMatrix()
    {
        var transition = new float[PITCH_BINS, PITCH_BINS];
        for ( var y = 0; y < PITCH_BINS; y++ )
        {
            float sum = 0;
            for ( var x = 0; x < PITCH_BINS; x++ )
            {
                var v = 12 - Math.Abs(x - y);
                if ( v < 0 )
                {
                    v = 0;
                }

                transition[y, x] =  v;
                sum              += v;
            }

            for ( var x = 0; x < PITCH_BINS; x++ )
            {
                transition[y, x] = transition[y, x] / sum;

                // Pre-apply log to the transition matrix for efficiency
                transition[y, x] = (float)Math.Log(transition[y, x] + float.Epsilon);
            }
        }

        return transition;
    }

    private float Periodicity(float[] probabilities, int t, int bins) { return probabilities[t * PITCH_BINS + bins]; }

    private float ConvertToFrequency(int bin)
    {
        float CENTS_PER_BIN = 20;
        var   cents         = CENTS_PER_BIN * bin + 1997.3794084376191f;
        var   frequency     = 10 * (float)Math.Pow(2, cents / 1200);

        return frequency;
    }

    private void MedianFilter(Span<float> data, int windowSize)
    {
        if ( windowSize > _medianBuffer.Length || windowSize % 2 == 0 )
        {
            throw new ArgumentException("Window size must be odd and <= buffer size", nameof(windowSize));
        }

        // Create a copy of the original data
        var original = ArrayPool<float>.Shared.Rent(data.Length);
        try
        {
            data.CopyTo(original);

            for ( var i = 0; i < data.Length; i++ )
            {
                // Fill the window buffer
                for ( var j = 0; j < windowSize; j++ )
                {
                    var k = i + j - windowSize / 2;

                    // Handle boundary conditions
                    if ( k < 0 )
                    {
                        k = 0;
                    }

                    if ( k >= data.Length )
                    {
                        k = data.Length - 1;
                    }

                    _medianBuffer[j] = original[k];
                }

                // Sort the window
                Array.Sort(_medianBuffer, 0, windowSize);

                // Set the median value
                data[i] = _medianBuffer[windowSize / 2];
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(original);
        }
    }

    private void MeanFilter(Span<float> data, int windowSize)
    {
        // Create a copy of the original data
        var original = ArrayPool<float>.Shared.Rent(data.Length);
        try
        {
            data.CopyTo(original);

            for ( var i = 0; i < data.Length; i++ )
            {
                float sum = 0;

                for ( var j = 0; j < windowSize; j++ )
                {
                    var k = i + j - windowSize / 2;

                    // Handle boundary conditions
                    if ( k < 0 )
                    {
                        k = 0;
                    }

                    if ( k >= data.Length )
                    {
                        k = data.Length - 1;
                    }

                    sum += original[k];
                }

                // Set the mean value
                data[i] = sum / windowSize;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(original);
        }
    }
}
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PersonaEngine.Lib.TTS.RVC;

public class CrepeOnnxSimd : IF0Predictor, IDisposable
{
    private const int SAMPLE_RATE = 16000;

    private const int WINDOW_SIZE = 1024;

    private const int PITCH_BINS = 360;

    private const int HOP_LENGTH = SAMPLE_RATE / 100; // 10ms

    private const int BATCH_SIZE = 512;

    // Preallocated buffers
    private readonly float[] _inputBatchBuffer;

    private readonly DenseTensor<float> _inputTensor;

    private readonly float[] _logPInitBuffer;

    // Flattened arrays for better cache locality and SIMD operations
    private readonly float[] _logProbBuffer;

    private readonly float[] _medianBuffer;

    private readonly int[] _ptrBuffer;

    private readonly InferenceSession _session;

    private readonly int[] _stateBuffer;

    private readonly float[] _tempBuffer; // Used for temporary calculations

    private readonly float[] _transitionMatrix;

    private readonly float[] _transOutBuffer;

    private readonly float[] _valueBuffer;

    // SIMD vector size based on hardware
    private readonly int _vectorSize;

    public CrepeOnnxSimd(string modelPath)
    {
        // Determine vector size based on hardware capabilities
        _vectorSize = Vector<float>.Count;

        var options = new SessionOptions {
                                             EnableMemoryPattern    = true,
                                             ExecutionMode          = ExecutionMode.ORT_PARALLEL,
                                             InterOpNumThreads      = Environment.ProcessorCount,
                                             IntraOpNumThreads      = Environment.ProcessorCount,
                                             GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                                             LogSeverityLevel       = OrtLoggingLevel.ORT_LOGGING_LEVEL_FATAL
                                         };

        // Use hardware specific optimizations if available
        options.AppendExecutionProvider_CPU();

        _session = new InferenceSession(modelPath, options);

        // Initialize preallocated buffers
        _inputBatchBuffer = new float[BATCH_SIZE * WINDOW_SIZE];
        _inputTensor      = new DenseTensor<float>(new[] { BATCH_SIZE, WINDOW_SIZE });
        _medianBuffer     = new float[5]; // For median filtering with window size 5

        // Initialize flattened buffers for Viterbi algorithm
        _logProbBuffer    = new float[BATCH_SIZE * PITCH_BINS];
        _valueBuffer      = new float[BATCH_SIZE * PITCH_BINS];
        _ptrBuffer        = new int[BATCH_SIZE * PITCH_BINS];
        _logPInitBuffer   = new float[PITCH_BINS];
        _transitionMatrix = new float[PITCH_BINS * PITCH_BINS];
        _transOutBuffer   = new float[PITCH_BINS * PITCH_BINS];
        _stateBuffer      = new int[BATCH_SIZE];
        _tempBuffer       = new float[Math.Max(BATCH_SIZE, PITCH_BINS) * _vectorSize];

        // Initialize logPInitBuffer with equal probabilities
        var logInitProb = (float)Math.Log(1.0f / PITCH_BINS + float.Epsilon);
        FillArray(_logPInitBuffer, logInitProb);

        // Initialize transition matrix
        InitializeTransitionMatrix(_transitionMatrix);
    }

    public void Dispose() { _session?.Dispose(); }

    public void ComputeF0(ReadOnlyMemory<float> wav, Memory<float> f0Output, int length)
    {
        if ( length > f0Output.Length )
        {
            throw new ArgumentException("Output buffer is too small", nameof(f0Output));
        }

        // Rent buffer from pool for periodicity data
        var pdBuffer = ArrayPool<float>.Shared.Rent(length);
        try
        {
            var pdSpan  = pdBuffer.AsSpan(0, length);
            var wavSpan = wav.Span;
            var f0Span  = f0Output.Span.Slice(0, length);

            // Process audio to extract F0
            Crepe(wavSpan, f0Span, pdSpan);

            // Apply post-processing with SIMD
            ApplyPeriodicityThreshold(f0Span, pdSpan, length);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(pdBuffer);
        }
    }

    private void Crepe(ReadOnlySpan<float> x, Span<float> f0, Span<float> pd)
    {
        var totalFrames = 1 + x.Length / HOP_LENGTH;

        for ( var b = 0; b < totalFrames; b += BATCH_SIZE )
        {
            var currentBatchSize = Math.Min(BATCH_SIZE, totalFrames - b);

            // Fill the batch input buffer with SIMD
            FillBatch(x, b, currentBatchSize);

            // Run inference on the batch
            var probabilities = Run(currentBatchSize);

            // Decode the probabilities into F0 values
            Decode(probabilities, f0, pd, currentBatchSize, b);
        }

        // Apply post-processing with SIMD
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
                // Reflection padding
                k = -k;
            }

            if ( k >= x.Length )
            {
                // Reflection padding
                k = x.Length - 1 - (k - x.Length);
            }

            if ( k >= 0 && k < x.Length )
            {
                v = x[k];
            }

            frame[j] = v;
        }

        // Normalize the frame using SIMD
        NormalizeAvx(frame);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NormalizeSimd(Span<float> input)
    {
        // 1. Calculate mean using SIMD
        float sum         = 0;
        var   vectorCount = input.Length / _vectorSize;

        for ( var i = 0; i < vectorCount; i++ )
        {
            var v = new Vector<float>(input.Slice(i * _vectorSize, _vectorSize));
            sum += Vector.Sum(v);
        }

        // Handle remainder
        for ( var i = vectorCount * _vectorSize; i < input.Length; i++ )
        {
            sum += input[i];
        }

        var mean = sum / input.Length;

        // 2. Subtract mean and calculate variance using SIMD
        float variance   = 0;
        var   meanVector = new Vector<float>(mean);

        for ( var i = 0; i < vectorCount; i++ )
        {
            var slice    = input.Slice(i * _vectorSize, _vectorSize);
            var v        = new Vector<float>(slice);
            var centered = v - meanVector;
            centered.CopyTo(slice);
            variance += Vector.Sum(centered * centered);
        }

        // Handle remainder
        for ( var i = vectorCount * _vectorSize; i < input.Length; i++ )
        {
            input[i] -= mean;
            variance += input[i] * input[i];
        }

        // 3. Calculate stddev and normalize
        var stddev = MathF.Sqrt(variance / input.Length);
        stddev = Math.Max(stddev, 1e-10f);

        var stddevVector = new Vector<float>(stddev);

        for ( var i = 0; i < vectorCount; i++ )
        {
            var slice      = input.Slice(i * _vectorSize, _vectorSize);
            var v          = new Vector<float>(slice);
            var normalized = v / stddevVector;
            normalized.CopyTo(slice);
        }

        // Handle remainder
        for ( var i = vectorCount * _vectorSize; i < input.Length; i++ )
        {
            input[i] /= stddev;
        }
    }

    // Hardware-specific optimization for AVX2-capable systems
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NormalizeAvx(Span<float> input)
    {
        if ( !Avx.IsSupported || input.Length < 8 )
        {
            NormalizeSimd(input);

            return;
        }

        unsafe
        {
            fixed (float* pInput = input)
            {
                // Calculate sum using AVX
                var sumVec         = Vector256<float>.Zero;
                var avxVectorCount = input.Length / 8;

                for ( var i = 0; i < avxVectorCount; i++ )
                {
                    var v = Avx.LoadVector256(pInput + i * 8);
                    sumVec = Avx.Add(sumVec, v);
                }

                // Horizontal sum
                var sumArray = stackalloc float[8];
                Avx.Store(sumArray, sumVec);

                float sum = 0;
                for ( var i = 0; i < 8; i++ )
                {
                    sum += sumArray[i];
                }

                // Handle remainder
                for ( var i = avxVectorCount * 8; i < input.Length; i++ )
                {
                    sum += pInput[i];
                }

                var mean    = sum / input.Length;
                var meanVec = Vector256.Create(mean);

                // Subtract mean and compute variance
                var varianceVec = Vector256<float>.Zero;

                for ( var i = 0; i < avxVectorCount; i++ )
                {
                    var v        = Avx.LoadVector256(pInput + i * 8);
                    var centered = Avx.Subtract(v, meanVec);
                    Avx.Store(pInput + i * 8, centered);
                    varianceVec = Avx.Add(varianceVec, Avx.Multiply(centered, centered));
                }

                // Get variance
                var varArray = stackalloc float[8];
                Avx.Store(varArray, varianceVec);

                float variance = 0;
                for ( var i = 0; i < 8; i++ )
                {
                    variance += varArray[i];
                }

                // Handle remainder
                for ( var i = avxVectorCount * 8; i < input.Length; i++ )
                {
                    pInput[i] -= mean;
                    variance  += pInput[i] * pInput[i];
                }

                var stddev = MathF.Sqrt(variance / input.Length);
                stddev = Math.Max(stddev, 1e-10f);

                var stddevVec = Vector256.Create(stddev);

                // Normalize
                for ( var i = 0; i < avxVectorCount; i++ )
                {
                    var v          = Avx.LoadVector256(pInput + i * 8);
                    var normalized = Avx.Divide(v, stddevVec);
                    Avx.Store(pInput + i * 8, normalized);
                }

                // Handle remainder
                for ( var i = avxVectorCount * 8; i < input.Length; i++ )
                {
                    pInput[i] /= stddev;
                }
            }
        }
    }

    private float[] Run(int batchSize)
    {
        // Copy the batch data to the input tensor using SIMD where possible
        for ( var i = 0; i < batchSize; i++ )
        {
            var inputOffset = i * WINDOW_SIZE;
            var vectorCount = WINDOW_SIZE / _vectorSize;

            for ( var v = 0; v < vectorCount; v++ )
            {
                var vectorOffset = v * _vectorSize;
                var source       = new Vector<float>(_inputBatchBuffer.AsSpan(inputOffset + vectorOffset, _vectorSize));

                for ( var j = 0; j < _vectorSize; j++ )
                {
                    _inputTensor[i, vectorOffset + j] = source[j];
                }
            }

            // Handle remainder
            for ( var j = vectorCount * _vectorSize; j < WINDOW_SIZE; j++ )
            {
                _inputTensor[i, j] = _inputBatchBuffer[inputOffset + j];
            }
        }

        var       inputs  = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input", _inputTensor) };
        using var results = _session.Run(inputs);

        return results[0].AsTensor<float>().ToArray();
    }

    private void Decode(float[] probabilities, Span<float> f0, Span<float> pd, int outputSize, int offset)
    {
        // Apply frequency range limitation using SIMD where possible
        const int minidx = 39;  // 50hz
        const int maxidx = 308; // 2006hz

        ApplyFrequencyRangeLimit(probabilities, outputSize, minidx, maxidx);

        // Make a safe copy of the spans since we can't capture them in parallel operations
        var f0Array = new float[f0.Length];
        var pdArray = new float[pd.Length];
        f0.CopyTo(f0Array);
        pd.CopyTo(pdArray);

        // Use Viterbi algorithm to decode the probabilities
        DecodeViterbi(probabilities, f0Array, pdArray, outputSize, offset);

        // Copy results back
        for ( var i = 0; i < outputSize; i++ )
        {
            if ( offset + i < f0.Length )
            {
                f0[offset + i] = f0Array[offset + i];
                pd[offset + i] = pdArray[offset + i];
            }
        }
    }

    private void ApplyFrequencyRangeLimit(float[] probabilities, int outputSize, int minIdx, int maxIdx)
    {
        // Set values outside the frequency range to negative infinity
        Parallel.For(0, outputSize, t =>
                                    {
                                        var baseIdx = t * PITCH_BINS;

                                        // Handle first part (below minIdx)
                                        for ( var i = 0; i < minIdx; i++ )
                                        {
                                            probabilities[baseIdx + i] = float.NegativeInfinity;
                                        }

                                        // Handle second part (above maxIdx)
                                        for ( var i = maxIdx; i < PITCH_BINS; i++ )
                                        {
                                            probabilities[baseIdx + i] = float.NegativeInfinity;
                                        }
                                    });
    }

    private void DecodeViterbi(float[] probabilities, float[] f0, float[] pd, int nSteps, int offset)
    {
        // Transfer probabilities to logProbBuffer and apply softmax
        Buffer.BlockCopy(probabilities, 0, _logProbBuffer, 0, nSteps * PITCH_BINS * sizeof(float));

        // Apply softmax with SIMD
        SoftmaxSimd(_logProbBuffer, nSteps);

        // Apply log to probabilities
        ApplyLogToProbs(_logProbBuffer, nSteps);

        // Initialize first step values
        for ( var i = 0; i < PITCH_BINS; i++ )
        {
            _valueBuffer[i] = _logProbBuffer[i] + _logPInitBuffer[i];
        }

        // Viterbi algorithm (forward pass)
        for ( var t = 1; t < nSteps; t++ )
        {
            ViterbiForward(t, nSteps);
        }

        // Find the most likely final state
        var maxI = FindArgMax(_valueBuffer, (nSteps - 1) * PITCH_BINS, PITCH_BINS);

        // Backward pass to find optimal path
        _stateBuffer[nSteps - 1] = maxI;
        for ( var t = nSteps - 2; t >= 0; t-- )
        {
            _stateBuffer[t] = _ptrBuffer[(t + 1) * PITCH_BINS + _stateBuffer[t + 1]];
        }

        // Convert to f0 values and apply periodicity
        ConvertToF0(probabilities, f0, pd, nSteps, offset);
    }

    private void SoftmaxSimd(float[] data, int nSteps)
    {
        Parallel.For(0, nSteps, t =>
                                {
                                    var baseIdx = t * PITCH_BINS;

                                    // Find max for numerical stability
                                    var max = float.NegativeInfinity;
                                    for ( var i = 0; i < PITCH_BINS; i++ )
                                    {
                                        max = Math.Max(max, data[baseIdx + i]);
                                    }

                                    // Compute exp(x - max) and sum
                                    float sum = 0;
                                    for ( var i = 0; i < PITCH_BINS; i++ )
                                    {
                                        data[baseIdx + i] =  MathF.Exp(data[baseIdx + i] - max);
                                        sum               += data[baseIdx + i];
                                    }

                                    // Normalize
                                    var invSum    = 1.0f / sum;
                                    var vecCount  = PITCH_BINS / _vectorSize;
                                    var invSumVec = new Vector<float>(invSum);

                                    for ( var v = 0; v < vecCount; v++ )
                                    {
                                        var idx        = baseIdx + v * _vectorSize;
                                        var values     = new Vector<float>(data.AsSpan(idx, _vectorSize));
                                        var normalized = values * invSumVec;

                                        for ( var j = 0; j < _vectorSize; j++ )
                                        {
                                            data[idx + j] = normalized[j];
                                        }
                                    }

                                    // Handle remainder
                                    for ( var i = baseIdx + vecCount * _vectorSize; i < baseIdx + PITCH_BINS; i++ )
                                    {
                                        data[i] *= invSum;
                                    }
                                });
    }

    private void ApplyLogToProbs(float[] data, int nSteps)
    {
        var totalSize = nSteps * PITCH_BINS;
        var vecCount  = totalSize / _vectorSize;
        var dataSpan  = data.AsSpan(0, totalSize);

        for ( var v = 0; v < vecCount; v++ )
        {
            var slice     = dataSpan.Slice(v * _vectorSize, _vectorSize);
            var values    = new Vector<float>(slice);
            var logValues = Vector.Log(values + new Vector<float>(float.Epsilon));
            logValues.CopyTo(slice);
        }

        // Handle remainder
        for ( var i = vecCount * _vectorSize; i < totalSize; i++ )
        {
            data[i] = MathF.Log(data[i] + float.Epsilon);
        }
    }

    private void ViterbiForward(int t, int nSteps)
    {
        var baseIdxCurrent = t * PITCH_BINS;
        var baseIdxPrev    = (t - 1) * PITCH_BINS;

        // Fixed number of threads to avoid thread contention
        var threadCount = Math.Min(Environment.ProcessorCount, PITCH_BINS);

        Parallel.For(0, threadCount, threadIdx =>
                                     {
                                         // Each thread processes a chunk of states
                                         var statesPerThread = (PITCH_BINS + threadCount - 1) / threadCount;
                                         var startState      = threadIdx * statesPerThread;
                                         var endState        = Math.Min(startState + statesPerThread, PITCH_BINS);

                                         for ( var j = startState; j < endState; j++ )
                                         {
                                             var maxI   = 0;
                                             var maxVal = float.NegativeInfinity;

                                             // Find max transition - this could be further vectorized for specific hardware
                                             for ( var k = 0; k < PITCH_BINS; k++ )
                                             {
                                                 var transVal = _valueBuffer[baseIdxPrev + k] + _transitionMatrix[j * PITCH_BINS + k];
                                                 if ( transVal > maxVal )
                                                 {
                                                     maxVal = transVal;
                                                     maxI   = k;
                                                 }
                                             }

                                             _ptrBuffer[baseIdxCurrent + j]   = maxI;
                                             _valueBuffer[baseIdxCurrent + j] = _logProbBuffer[baseIdxCurrent + j] + maxVal;
                                         }
                                     });
    }

    private int FindArgMax(float[] data, int offset, int length)
    {
        var maxIdx = 0;
        var maxVal = float.NegativeInfinity;

        for ( var i = 0; i < length; i++ )
        {
            if ( data[offset + i] > maxVal )
            {
                maxVal = data[offset + i];
                maxIdx = i;
            }
        }

        return maxIdx;
    }

    private void ConvertToF0(float[] probabilities, float[] f0, float[] pd, int nSteps, int offset)
    {
        for ( var t = 0; t < nSteps; t++ )
        {
            if ( offset + t >= f0.Length )
            {
                break;
            }

            var bin         = _stateBuffer[t];
            var periodicity = probabilities[t * PITCH_BINS + bin];
            var frequency   = ConvertBinToFrequency(bin);

            f0[offset + t] = frequency;
            pd[offset + t] = periodicity;
        }
    }

    private void ApplyPeriodicityThreshold(Span<float> f0, Span<float> pd, int length)
    {
        const float threshold = 0.1f;
        var         vecCount  = length / _vectorSize;

        // Use SIMD for bulk processing
        for ( var v = 0; v < vecCount; v++ )
        {
            var offset  = v * _vectorSize;
            var pdSlice = pd.Slice(offset, _vectorSize);
            var f0Slice = f0.Slice(offset, _vectorSize);

            var pdVec        = new Vector<float>(pdSlice);
            var thresholdVec = new Vector<float>(threshold);
            var zeroVec      = new Vector<float>(0.0f);
            var f0Vec        = new Vector<float>(f0Slice);

            // Where pd < threshold, set f0 to 0
            var mask   = Vector.LessThan(pdVec, thresholdVec);
            var result = Vector.ConditionalSelect(mask, zeroVec, f0Vec);

            result.CopyTo(f0Slice);
        }

        // Handle remainder with scalar code
        for ( var i = vecCount * _vectorSize; i < length; i++ )
        {
            if ( pd[i] < threshold )
            {
                f0[i] = 0;
            }
        }
    }

    private void MedianFilter(Span<float> data, int windowSize)
    {
        if ( windowSize > _medianBuffer.Length || windowSize % 2 == 0 )
        {
            throw new ArgumentException("Window size must be odd and <= buffer size", nameof(windowSize));
        }

        var original = ArrayPool<float>.Shared.Rent(data.Length);
        var result   = ArrayPool<float>.Shared.Rent(data.Length);
        try
        {
            data.CopyTo(original.AsSpan(0, data.Length));
            var radius = windowSize / 2;
            var length = data.Length;

            // Use thread-local window buffers for parallel processing
            Parallel.For(0, length, i =>
                                    {
                                        // Allocate a local median buffer for each thread
                                        var localMedianBuffer = new float[windowSize];

                                        // Get window values
                                        for ( var j = 0; j < windowSize; j++ )
                                        {
                                            var k = i + j - radius;
                                            k                    = Math.Clamp(k, 0, length - 1);
                                            localMedianBuffer[j] = original[k];
                                        }

                                        // Simple sort for small window
                                        Array.Sort(localMedianBuffer, 0, windowSize);
                                        result[i] = localMedianBuffer[radius];
                                    });

            // Copy results back to the span
            new Span<float>(result, 0, data.Length).CopyTo(data);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(original);
            ArrayPool<float>.Shared.Return(result);
        }
    }

    private void MeanFilter(Span<float> data, int windowSize)
    {
        var original = ArrayPool<float>.Shared.Rent(data.Length);
        var result   = ArrayPool<float>.Shared.Rent(data.Length);
        try
        {
            // Copy to array for processing
            data.CopyTo(original.AsSpan(0, data.Length));
            var radius = windowSize / 2;
            var length = data.Length;

            // Use arrays instead of spans for parallel processing
            Parallel.For(0, length, i =>
                                    {
                                        float sum = 0;
                                        for ( var j = -radius; j <= radius; j++ )
                                        {
                                            var k = Math.Clamp(i + j, 0, length - 1);
                                            sum += original[k];
                                        }

                                        result[i] = sum / windowSize;
                                    });

            // Copy back to span
            new Span<float>(result, 0, data.Length).CopyTo(data);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(original);
            ArrayPool<float>.Shared.Return(result);
        }
    }

    private void InitializeTransitionMatrix(float[] transitionMatrix)
    {
        Parallel.For(0, PITCH_BINS, y =>
                                    {
                                        float sum = 0;
                                        for ( var x = 0; x < PITCH_BINS; x++ )
                                        {
                                            float v = 12 - Math.Abs(x - y);
                                            v                                    =  Math.Max(v, 0);
                                            transitionMatrix[y * PITCH_BINS + x] =  v;
                                            sum                                  += v;
                                        }

                                        // Normalize and pre-apply log
                                        var invSum      = 1.0f / sum;
                                        var vectorCount = PITCH_BINS / _vectorSize;
                                        var invSumVec   = new Vector<float>(invSum);
                                        var epsilonVec  = new Vector<float>(float.Epsilon);

                                        for ( var v = 0; v < vectorCount; v++ )
                                        {
                                            var idx        = y * PITCH_BINS + v * _vectorSize;
                                            var values     = new Vector<float>(transitionMatrix.AsSpan(idx, _vectorSize));
                                            var normalized = values * invSumVec;
                                            var logValues  = Vector.Log(normalized + epsilonVec);

                                            for ( var j = 0; j < _vectorSize; j++ )
                                            {
                                                transitionMatrix[idx + j] = logValues[j];
                                            }
                                        }

                                        // Handle remainder
                                        for ( var x = vectorCount * _vectorSize; x < PITCH_BINS; x++ )
                                        {
                                            var idx = y * PITCH_BINS + x;
                                            transitionMatrix[idx] = transitionMatrix[idx] * invSum;
                                            transitionMatrix[idx] = MathF.Log(transitionMatrix[idx] + float.Epsilon);
                                        }
                                    });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float ConvertBinToFrequency(int bin)
    {
        const float CENTS_PER_BIN = 20;
        var         cents         = CENTS_PER_BIN * bin + 1997.3794084376191f;

        return 10 * MathF.Pow(2, cents / 1200);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FillArray(Span<float> array, float value)
    {
        var vectorCount = array.Length / _vectorSize;
        var valueVec    = new Vector<float>(value);

        for ( var v = 0; v < vectorCount; v++ )
        {
            valueVec.CopyTo(array.Slice(v * _vectorSize, _vectorSize));
        }

        // Handle remainder
        for ( var i = vectorCount * _vectorSize; i < array.Length; i++ )
        {
            array[i] = value;
        }
    }
}
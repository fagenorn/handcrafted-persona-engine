using System.Buffers;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PersonaEngine.Lib.TTS.RVC;

public class OnnxRVC : IDisposable
{
    private readonly ArrayPool<float> _arrayPool;

    private readonly int _hopSize;

    private readonly InferenceSession _model;

    private readonly ArrayPool<short> _shortArrayPool;

    // Preallocated buffers and tensors
    private readonly DenseTensor<long> _speakerIdTensor;

    private readonly ContentVec _vecModel;

    public OnnxRVC(string modelPath, int hopsize, string vecPath)
    {
        var options = new SessionOptions {
                                             EnableMemoryPattern    = true,
                                             ExecutionMode          = ExecutionMode.ORT_PARALLEL,
                                             InterOpNumThreads      = Environment.ProcessorCount,
                                             IntraOpNumThreads      = Environment.ProcessorCount,
                                             GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                                             LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
                                         };

        options.AppendExecutionProvider_CUDA();
        
        _model    = new InferenceSession(modelPath, options);
        _hopSize  = hopsize;
        _vecModel = new ContentVec(vecPath);

        // Preallocate the speaker ID tensor
        _speakerIdTensor = new DenseTensor<long>(new[] { 1 });

        // Create array pools for temporary buffers
        _arrayPool      = ArrayPool<float>.Shared;
        _shortArrayPool = ArrayPool<short>.Shared;
    }

    public void Dispose()
    {
        _model?.Dispose();
        _vecModel?.Dispose();
    }

    public int ProcessAudio(ReadOnlyMemory<float> inputAudio,  Memory<float> outputAudio,
                            IF0Predictor          f0Predictor, int           speakerId, int f0UpKey)
    {
        // Early exit if input is empty
        if ( inputAudio.Length == 0 )
        {
            return 0;
        }

        // Check if output buffer is large enough
        if ( outputAudio.Length < inputAudio.Length )
        {
            throw new ArgumentException("Output buffer is too small", nameof(outputAudio));
        }

        // Set the speaker ID
        _speakerIdTensor[0] = speakerId;

        // Process the audio
        return ProcessInPlace(inputAudio, outputAudio, f0Predictor, _speakerIdTensor, f0UpKey);
    }

    private int ProcessInPlace(ReadOnlyMemory<float> input, Memory<float> output,
                               IF0Predictor          f0Predictor,
                               DenseTensor<long>     speakerIdTensor, int f0UpKey)
    {
        const int f0Min    = 50;
        const int f0Max    = 1100;
        var       f0MelMin = 1127 * Math.Log(1 + f0Min / 700.0);
        var       f0MelMax = 1127 * Math.Log(1 + f0Max / 700.0);

        if ( input.Length / 16000.0 > 30.0 )
        {
            throw new Exception("Audio segment is too long (>30s)");
        }

        // Calculate original scale for normalization (matching original implementation)
        var minValue  = float.MaxValue;
        var maxValue  = float.MinValue;
        var inputSpan = input.Span;
        for ( var i = 0; i < input.Length; i++ )
        {
            minValue = Math.Min(minValue, inputSpan[i]);
            maxValue = Math.Max(maxValue, inputSpan[i]);
        }

        var originalScale = maxValue - minValue;

        // Get the hubert features
        var hubert = _vecModel.Forward(input);

        // Repeat and transpose the features
        var hubertRepeated = RVCUtils.RepeatTensor(hubert, 2);
        hubertRepeated = RVCUtils.Transpose(hubertRepeated, 0, 2, 1);

        hubert = null; // Allow for garbage collection

        var hubertLength       = hubertRepeated.Dimensions[1];
        var hubertLengthTensor = new DenseTensor<long>(new[] { 1 }) { [0] = hubertLength };

        // Allocate buffers for F0 calculations
        var f0Buffer = _arrayPool.Rent(hubertLength);
        var f0Memory = new Memory<float>(f0Buffer, 0, hubertLength);

        try
        {
            // Calculate F0 directly into buffer
            f0Predictor.ComputeF0(input, f0Memory, hubertLength);

            // Create pitch tensors
            var pitchBuffer  = _arrayPool.Rent(hubertLength);
            var pitchTensor  = new DenseTensor<long>(new[] { 1, hubertLength });
            var pitchfTensor = new DenseTensor<float>(new[] { 1, hubertLength });

            try
            {
                // Apply pitch shift and convert to mel scale
                for ( var i = 0; i < hubertLength; i++ )
                {
                    // Apply pitch shift
                    var shiftedF0 = f0Buffer[i] * (float)Math.Pow(2, f0UpKey / 12.0);
                    pitchfTensor[0, i] = shiftedF0;

                    // Convert to mel scale for pitch
                    var f0Mel = 1127 * Math.Log(1 + shiftedF0 / 700.0);
                    if ( f0Mel > 0 )
                    {
                        f0Mel = (f0Mel - f0MelMin) * 254 / (f0MelMax - f0MelMin) + 1;
                        f0Mel = Math.Round(f0Mel);
                    }

                    if ( f0Mel <= 1 )
                    {
                        f0Mel = 1;
                    }

                    if ( f0Mel > 255 )
                    {
                        f0Mel = 255;
                    }

                    pitchTensor[0, i] = (long)f0Mel;
                }

                // Generate random noise tensor
                var rndTensor = new DenseTensor<float>(new[] { 1, 192, hubertLength });
                var random    = new Random();
                for ( var i = 0; i < 192 * hubertLength; i++ )
                {
                    rndTensor[0, i / hubertLength, i % hubertLength] = (float)random.NextDouble();
                }

                // Run the model
                var outWav = Forward(hubertRepeated, hubertLengthTensor, pitchTensor,
                                     pitchfTensor, speakerIdTensor, rndTensor);

                // Apply padding to match original implementation
                // (adding padding at the end only, like in original Pad method)
                var paddedSize   = outWav.Length + 2 * _hopSize;
                var paddedOutput = _shortArrayPool.Rent(paddedSize);
                try
                {
                    // Copy original output to the beginning of padded output
                    for ( var i = 0; i < outWav.Length; i++ )
                    {
                        paddedOutput[i] = outWav[i];
                    }
                    // Rest of array is already zeroed when rented from pool

                    // Find min and max values for normalization
                    var minOutValue = short.MaxValue;
                    var maxOutValue = short.MinValue;
                    for ( var i = 0; i < outWav.Length; i++ )
                    {
                        minOutValue = Math.Min(minOutValue, outWav[i]);
                        maxOutValue = Math.Max(maxOutValue, outWav[i]);
                    }

                    // Copy the output to the buffer with normalization matching original
                    var outputSpan = output.Span;
                    if ( outputSpan.Length < paddedSize )
                    {
                        throw new InvalidOperationException($"Output buffer too small. Needed {paddedSize}, but only had {outputSpan.Length}");
                    }

                    var maxLen = Math.Min(paddedSize, outputSpan.Length);

                    // Apply normalization that matches the original implementation
                    float range = maxOutValue - minOutValue;
                    if ( range > 0 )
                    {
                        for ( var i = 0; i < maxLen; i++ )
                        {
                            outputSpan[i] = paddedOutput[i] * originalScale / range;
                        }
                    }
                    else
                    {
                        // Handle edge case where all values are the same
                        for ( var i = 0; i < maxLen; i++ )
                        {
                            outputSpan[i] = 0;
                        }
                    }

                    return outWav.Length;
                }
                finally
                {
                    _shortArrayPool.Return(paddedOutput);
                }
            }
            finally
            {
                _arrayPool.Return(pitchBuffer);
            }
        }
        finally
        {
            _arrayPool.Return(f0Buffer);
        }
    }

    private short[] Forward(DenseTensor<float> hubert,    DenseTensor<long>  hubertLength,
                            DenseTensor<long>  pitch,     DenseTensor<float> pitchf,
                            DenseTensor<long>  speakerId, DenseTensor<float> noise)
    {
        var inputs = new List<NamedOnnxValue> {
                                                  NamedOnnxValue.CreateFromTensor(_model.InputMetadata.Keys.ElementAt(0), hubert),
                                                  NamedOnnxValue.CreateFromTensor(_model.InputMetadata.Keys.ElementAt(1), hubertLength),
                                                  NamedOnnxValue.CreateFromTensor(_model.InputMetadata.Keys.ElementAt(2), pitch),
                                                  NamedOnnxValue.CreateFromTensor(_model.InputMetadata.Keys.ElementAt(3), pitchf),
                                                  NamedOnnxValue.CreateFromTensor(_model.InputMetadata.Keys.ElementAt(4), speakerId),
                                                  NamedOnnxValue.CreateFromTensor(_model.InputMetadata.Keys.ElementAt(5), noise)
                                              };

        var results = _model.Run(inputs);
        var output  = results.First().AsTensor<float>();

        return output.Select(x => (short)(x * 32767)).ToArray();
    }
}
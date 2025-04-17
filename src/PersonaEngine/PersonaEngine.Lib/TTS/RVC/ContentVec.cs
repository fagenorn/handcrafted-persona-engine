using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PersonaEngine.Lib.TTS.RVC;

public class ContentVec : IDisposable
{
    private readonly InferenceSession _model;

    public ContentVec(string modelPath)
    {
        var options = new SessionOptions {
                                             EnableMemoryPattern    = true,
                                             ExecutionMode          = ExecutionMode.ORT_PARALLEL,
                                             InterOpNumThreads      = Environment.ProcessorCount,
                                             IntraOpNumThreads      = Environment.ProcessorCount,
                                             GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                                             LogSeverityLevel       = OrtLoggingLevel.ORT_LOGGING_LEVEL_FATAL,
                                         };

        options.AppendExecutionProvider_CPU();

        _model = new InferenceSession(modelPath, options);
    }

    public void Dispose() { _model?.Dispose(); }

    public DenseTensor<float> Forward(ReadOnlyMemory<float> wav)
    {
        // Create input tensor without unnecessary copying
        var tensor  = new DenseTensor<float>(new[] { 1, 1, wav.Length });
        var wavSpan = wav.Span;

        if ( wav.Length == 2 )
        {
            // Handle division by 2 for each element
            tensor[0, 0, 0] = wavSpan[0] / 2;
            tensor[0, 0, 1] = wavSpan[1] / 2;
        }
        else
        {
            // Process normally for all other lengths
            for ( var i = 0; i < wav.Length; i++ )
            {
                tensor[0, 0, i] = wavSpan[i];
            }
        }

        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_model.InputMetadata.Keys.First(), tensor) };

        using var results = _model.Run(inputs);
        var       output  = results[0].AsTensor<float>();

        // Process the output tensor
        return RVCUtils.Transpose(output, 0, 2, 1);
    }
}
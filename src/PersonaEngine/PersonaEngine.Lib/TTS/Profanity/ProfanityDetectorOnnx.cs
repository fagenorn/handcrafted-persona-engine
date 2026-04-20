using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using PersonaEngine.Lib.Utils.Onnx;

namespace PersonaEngine.Lib.TTS.Profanity;

public class ProfanityDetectorOnnx : IDisposable
{
    private readonly InferenceSession _session;

    private readonly Tokenizer _tokenizer;

    public ProfanityDetectorOnnx(string? modelPath = null, string? vocabPath = null)
    {
        if (modelPath == null)
        {
            modelPath = ModelUtils.GetModelPath(ModelType.TinyToxic);
        }

        if (vocabPath == null)
        {
            vocabPath = ModelUtils.GetModelPath(ModelType.TinyToxicVocab);
        }

        _session = OnnxSessionFactory.Create(
            modelPath,
            ExecutionProvider.Cpu,
            logLevel: OrtLoggingLevel.ORT_LOGGING_LEVEL_FATAL
        );

        using Stream vocabStream = File.OpenRead(vocabPath);
        _tokenizer = BertTokenizer.Create(vocabStream);
    }

    public void Dispose()
    {
        _session?.Dispose();
    }

    private IEnumerable<long> Tokenize(string sentence)
    {
        return _tokenizer.EncodeToIds(sentence).Select(x => (long)x);
    }

    public float Run(string sentence)
    {
        var inputIds = Tokenize(sentence).ToArray();
        var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, inputIds.Length });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
        };

        using var results = _session.Run(inputs);

        return results[0].AsTensor<float>().ToArray().Single();
    }
}

namespace PersonaEngine.Lib.IO;

/// <summary>
///     Well-known model resource identifiers, organized by subsystem.
/// </summary>
public static class ModelType
{
    public static class Kokoro
    {
        public static readonly ModelId Synthesis = new("kokoro/model_slim.onnx");
        public static readonly ModelId Voices = new("kokoro/voices");
        public static readonly ModelId PhonemeMappings = new("kokoro/phoneme_to_id.txt");
    }

    public static class Qwen3
    {
        public static readonly ModelId Talker = new("qwen3-tts/qwen3_tts_talker.q5_k.gguf");

        public static readonly ModelId Predictor = new("qwen3-tts/qwen3_tts_predictor.q8_0.gguf");

        public static readonly ModelId Decoder = new("qwen3-tts/qwen3_tts_decoder.fp16.onnx");
        public static readonly ModelId Embeddings = new("qwen3-tts/embeddings");
        public static readonly ModelId Config = new("qwen3-tts/model_profile.json");
        public static readonly ModelId Tokenizer = new("qwen3-tts/tokenizer.json");
        public static readonly ModelId Speakers = new("qwen3-tts/speakers");
    }

    public static class Rvc
    {
        public static readonly ModelId Voices = new("rvc/voices");
        public static readonly ModelId Hubert = new("rvc/vec-768-layer-12.onnx");
        public static readonly ModelId CrepeTiny = new("rvc/crepe_tiny.onnx");
        public static readonly ModelId Rmvpe = new("rvc/rmvpe.onnx");
    }

    public static class Ctc
    {
        public static readonly ModelId Model = new("wav2vec2/onnx/model.onnx");
        public static readonly ModelId Vocab = new("wav2vec2/vocab.json");
    }

    public static class OpenNlp
    {
        public static readonly ModelId Directory = new("opennlp");
    }

    public static class Mdx
    {
        public static readonly ModelId ModelData = new("mdx/model_data.json");
        public static readonly ModelId VocalModel = new("mdx/UVR-MDX-NET-Voc_FT.onnx");
        public static readonly ModelId VocalMainModel = new("mdx/UVR_MDXNET_KARA_2.onnx");
        public static readonly ModelId DeReverbModel = new("mdx/Reverb_HQ_By_FoxJoy.onnx");
    }

    public static class MelBandRoformer
    {
        public static readonly ModelId Model = new(
            "mel_band_roformer/melbandroformer_optimized.onnx"
        );
    }
}

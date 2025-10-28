using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace PersonaEngine.Lib.IO;

/// <summary>
///     Types of models
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public enum ModelType
{
    [Description("kokoro/model_slim.onnx")]
    KokoroSynthesis,

    [Description("kokoro/voices")]
    KokoroVoices,

    [Description("kokoro/phoneme_to_id.txt")]
    KokoroPhonemeMappings,

    [Description("opennlp")]
    OpenNLPDir,

    [Description("rvc/voices")]
    RVCVoices,

    [Description("rvc/vec-768-layer-12.onnx")]
    RVCHubert,

    [Description("rvc/crepe_tiny.onnx")]
    RVCCrepeTiny,

    [Description("mdx/model_data.json")]
    MDXModelData,

    [Description("mdx/UVR-MDX-NET-Voc_FT.onnx")]
    MDXVocalModel,

    [Description("mdx/UVR_MDXNET_KARA_2.onnx")]
    MDXVocalMainModel,

    [Description("mdx/Reverb_HQ_By_FoxJoy.onnx")]
    MDXVocalDeReverbModel,

    [Description("mel_band_roformer/melbandroformer_optimized.onnx")]
    MelBandRoformerModel,
}

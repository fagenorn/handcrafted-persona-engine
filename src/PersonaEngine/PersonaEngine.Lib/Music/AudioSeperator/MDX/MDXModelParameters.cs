using System.Text.Json.Serialization;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PersonaEngine.Lib.Music.MDX;

public class ModelParamsData
{
    [JsonPropertyName("mdx_dim_f_set")]
    public int DimF { get; set; }

    [JsonPropertyName("mdx_dim_t_set")]
    public int DimTLog2 { get; set; } // "mdx_dim_t_set": 7 -> 2**7

    [JsonPropertyName("mdx_n_fft_scale_set")]
    public int NFftScale { get; set; } // "mdx_n_fft_scale_set": 6144

    [JsonPropertyName("primary_stem")]
    public string PrimaryStem { get; set; }

    [JsonPropertyName("compensate")]
    public float Compensation { get; set; }
}

public class MDXModelParameters
{
    public int DimF { get; }
    public int DimT { get; }
    public int NFft { get; }
    public int Hop { get; }
    public string StemName { get; }
    public float Compensation { get; }

    public int NBins { get; }
    public int ChunkSize { get; }
    public int GenSize { get; }
    public int Trim { get; }

    // These would hold the Hann window and frequency padding
    public float[] Window { get; }
    public DenseTensor<float> FreqPad { get; }

    private const int DimC = 4;

    public MDXModelParameters(int device, ModelParamsData data, int hop = 1024)
    {
        DimF = data.DimF;
        DimT = (int)Math.Pow(2, data.DimTLog2);
        NFft = data.NFftScale;
        Hop = hop;
        StemName = data.PrimaryStem;
        Compensation = data.Compensation;

        NBins = NFft / 2 + 1;
        ChunkSize = Hop * (DimT - 1);
        Trim = NFft / 2;
        GenSize = ChunkSize - 2 * Trim;

        // 1. Create Hann Window
        // This logic needs to be ported from torch.hann_window
        Window = new float[NFft];
        for (int i = 0; i < NFft; i++)
        {
            // This is the formula for a periodic Hann window
            Window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / NFft));
        }

        // 2. Create FreqPad
        // This is a zero-tensor used in iSTFT
        // Shape: [1, 4, n_bins - dim_f, dim_t]
        int padBins = NBins - DimF;
        if (padBins > 0)
        {
            FreqPad = new DenseTensor<float>(new[] { 1, DimC, padBins, DimT });
            // It's initialized to zeros by default, so no need to fill it.
        }
    }
}

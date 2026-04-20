namespace PersonaEngine.Lib.TTS.Synthesis.Kokoro;

public class KokoroVoiceEmbedding(string id, Memory<float> rawEmbedding)
{
    private const int StyleDim = 256;

    public string Id { get; } = id;

    public Memory<float> GetEmbedding(ReadOnlySpan<int> inputDimensions)
    {
        var numTokens = GetNumTokens(inputDimensions);
        var offset = numTokens * StyleDim;

        return offset + StyleDim > rawEmbedding.Length
            ? new float[StyleDim]
            : rawEmbedding.Slice(offset, StyleDim);
    }

    private int GetNumTokens(ReadOnlySpan<int> inputDimensions)
    {
        var lastDim = inputDimensions[^1];

        return Math.Min(Math.Max(lastDim - 2, 0), 509);
    }
}

namespace PersonaEngine.Lib.TTS.Synthesis;

public class VoiceData
{
    private const int StyleDim = 256;

    private readonly Memory<float> _rawEmbedding;

    public VoiceData(string id, Memory<float> rawEmbedding)
    {
        Id = id;
        _rawEmbedding = rawEmbedding;
    }

    public string Id { get; }

    public Memory<float> GetEmbedding(ReadOnlySpan<int> inputDimensions)
    {
        var numTokens = GetNumTokens(inputDimensions);
        var offset = numTokens * StyleDim;

        return offset + StyleDim > _rawEmbedding.Length
            ? new float[StyleDim]
            : _rawEmbedding.Slice(offset, StyleDim);
    }

    private int GetNumTokens(ReadOnlySpan<int> inputDimensions)
    {
        var lastDim = inputDimensions[^1];

        return Math.Min(Math.Max(lastDim - 2, 0), 509);
    }
}

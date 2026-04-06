namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Trims leading and trailing silence from audio sample buffers.
/// </summary>
internal static class AudioSilenceTrimmer
{
    public static Memory<float> Trim(
        Memory<float> audioData,
        float threshold = 0.01f,
        int paddingSamples = 512
    )
    {
        if (audioData.IsEmpty || audioData.Length <= paddingSamples * 2)
        {
            return audioData;
        }

        var span = audioData.Span;

        var startIndex = FindFirstAboveThreshold(span, threshold);
        if (startIndex == -1)
        {
            return audioData;
        }

        var endIndex = FindLastAboveThreshold(span, threshold);
        if (endIndex == -1)
        {
            endIndex = span.Length - 1;
        }

        startIndex = Math.Max(0, startIndex - paddingSamples);
        endIndex = Math.Min(span.Length - 1, endIndex + paddingSamples);

        if (startIndex >= endIndex)
        {
            return audioData;
        }

        var length = endIndex - startIndex + 1;

        return length == audioData.Length ? audioData : audioData.Slice(startIndex, length);
    }

    private static int FindFirstAboveThreshold(ReadOnlySpan<float> span, float threshold)
    {
        for (var i = 0; i < span.Length; i++)
        {
            if (MathF.Abs(span[i]) > threshold)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindLastAboveThreshold(ReadOnlySpan<float> span, float threshold)
    {
        for (var i = span.Length - 1; i >= 0; i--)
        {
            if (MathF.Abs(span[i]) > threshold)
            {
                return i;
            }
        }

        return -1;
    }
}

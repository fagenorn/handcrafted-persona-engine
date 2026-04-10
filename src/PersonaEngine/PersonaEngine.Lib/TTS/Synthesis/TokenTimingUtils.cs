namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Shared utility for proportional token timing distribution.
///     Used by TTS engines that receive a word-level (start, end) timing boundary
///     and need to distribute it across multiple phonemizer tokens.
/// </summary>
internal static class TokenTimingUtils
{
    /// <summary>
    ///     Distributes a time interval proportionally across a contiguous range of tokens
    ///     in an array, using <paramref name="weightSelector" /> to determine each token's
    ///     share. A <paramref name="sliceOffset" /> is subtracted from every timestamp so
    ///     that times are relative to the start of the emitted audio slice.
    /// </summary>
    /// <param name="tokens">The full token array.</param>
    /// <param name="start">Index of the first token in the range.</param>
    /// <param name="count">Number of tokens in the range.</param>
    /// <param name="startTime">Absolute start time of the interval (seconds).</param>
    /// <param name="endTime">Absolute end time of the interval (seconds).</param>
    /// <param name="sliceOffset">Seconds to subtract from each computed timestamp.</param>
    /// <param name="weightSelector">Returns the weight for a given token (must be ≥ 1 after clamping).</param>
    public static void DistributeTimings(
        Token[] tokens,
        int start,
        int count,
        double startTime,
        double endTime,
        double sliceOffset,
        Func<Token, int> weightSelector
    )
    {
        if (count <= 0)
        {
            return;
        }

        if (count == 1)
        {
            tokens[start].StartTs = startTime - sliceOffset;
            tokens[start].EndTs = endTime - sliceOffset;

            return;
        }

        var totalWeight = 0;
        for (var i = start; i < start + count; i++)
        {
            totalWeight += Math.Max(1, weightSelector(tokens[i]));
        }

        var duration = endTime - startTime;
        var cursor = startTime;

        for (var i = start; i < start + count; i++)
        {
            var weight = Math.Max(1, weightSelector(tokens[i]));
            var tokenDuration = duration * weight / totalWeight;
            tokens[i].StartTs = cursor - sliceOffset;
            tokens[i].EndTs = cursor + tokenDuration - sliceOffset;
            cursor += tokenDuration;
        }
    }
}

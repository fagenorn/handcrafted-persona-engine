namespace PersonaEngine.Lib.TTS.Synthesis.Kokoro;

/// <summary>
///     Converts phoneme character sequences to model token IDs with BOS/EOS framing.
/// </summary>
internal static class KokoroTokenConverter
{
    private const long BosTokenId = 0;

    private const long EosTokenId = 0;

    /// <summary>
    ///     Writes BOS + mapped phoneme tokens + EOS into <paramref name="buffer"/>.
    ///     Unrecognized phonemes are skipped (compacted out).
    /// </summary>
    /// <returns>Total number of tokens written (including BOS and EOS).</returns>
    public static int Convert(string phonemes, Dictionary<char, long> phonemeMap, long[] buffer)
    {
        var writeIndex = 0;
        buffer[writeIndex++] = BosTokenId;

        for (var i = 0; i < phonemes.Length; i++)
        {
            if (phonemeMap.TryGetValue(phonemes[i], out var id))
            {
                buffer[writeIndex++] = id;
            }
        }

        buffer[writeIndex++] = EosTokenId;

        return writeIndex;
    }
}

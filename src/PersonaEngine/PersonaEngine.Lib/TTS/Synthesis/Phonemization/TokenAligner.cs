namespace PersonaEngine.Lib.TTS.Synthesis;

internal static class TokenAligner
{
    public static void ApplyFeatures(
        List<Token> tokens,
        List<string> textTokens,
        Dictionary<int, object> features
    )
    {
        if (features.Count == 0)
        {
            return;
        }

        var alignment = CreateBidirectionalAlignment(
            textTokens,
            tokens.Select(t => t.Text).ToList()
        );

        foreach (var kvp in features)
        {
            var sourceIndex = kvp.Key;
            var value = kvp.Value;

            var matchedIndices = new List<int>();
            for (var i = 0; i < alignment.Length; i++)
            {
                if (alignment[i] == sourceIndex)
                {
                    matchedIndices.Add(i);
                }
            }

            for (var matchCount = 0; matchCount < matchedIndices.Count; matchCount++)
            {
                var tokenIndex = matchedIndices[matchCount];

                if (tokenIndex >= tokens.Count)
                {
                    continue;
                }

                if (value is int intValue)
                {
                    tokens[tokenIndex].Stress = intValue;
                }
                else if (value is double doubleValue)
                {
                    tokens[tokenIndex].Stress = doubleValue;
                }
                else if (value is string strValue)
                {
                    if (strValue.StartsWith("/"))
                    {
                        tokens[tokenIndex].IsHead = matchCount == 0;
                        tokens[tokenIndex].Phonemes =
                            matchCount == 0 ? strValue.Substring(1) : string.Empty;
                        tokens[tokenIndex].Rating = 5;
                    }
                    else if (strValue.StartsWith("#"))
                    {
                        tokens[tokenIndex].NumFlags = strValue.Substring(1);
                    }
                }
            }
        }
    }

    private static int[] CreateBidirectionalAlignment(List<string> source, List<string> target)
    {
        var alignment = new int[target.Count];

        for (var i = 0; i < alignment.Length; i++)
        {
            alignment[i] = -1;
        }

        var targetIndex = 0;
        var sourcePos = 0;

        for (var sourceIndex = 0; sourceIndex < source.Count; sourceIndex++)
        {
            var sourceToken = source[sourceIndex].ToLowerInvariant();
            sourcePos += sourceToken.Length;

            var targetPos = 0;
            for (var i = 0; i < targetIndex; i++)
            {
                targetPos += target[i].ToLowerInvariant().Length;
            }

            while (targetIndex < target.Count && targetPos < sourcePos)
            {
                var targetToken = target[targetIndex].ToLowerInvariant();
                alignment[targetIndex] = sourceIndex;

                targetPos += targetToken.Length;
                targetIndex++;
            }
        }

        return alignment;
    }
}

namespace PersonaEngine.Lib.Utils.Text;

public static class StringDistanceExtensions
{
    /// <summary>
    ///     Computes Levenshtein edit distance using single-row DP (O(m) space).
    ///     Uses stackalloc for short strings to avoid heap allocation.
    /// </summary>
    public static int LevenshteinDistance(string s, string t)
    {
        var n = s.Length;
        var m = t.Length;

        if (n == 0)
        {
            return m;
        }

        if (m == 0)
        {
            return n;
        }

        const int stackAllocThreshold = 128;
        Span<int> prev = m + 1 <= stackAllocThreshold ? stackalloc int[m + 1] : new int[m + 1];
        Span<int> curr = m + 1 <= stackAllocThreshold ? stackalloc int[m + 1] : new int[m + 1];

        for (var j = 0; j <= m; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            var tmp = prev;
            prev = curr;
            curr = tmp;
        }

        return prev[m];
    }

    /// <summary>
    ///     Returns normalized similarity in [0, 1] where 1 = identical.
    /// </summary>
    public static float NormalizedSimilarity(string a, string b)
    {
        if (a == b)
        {
            return 1.0f;
        }

        var maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0)
        {
            return 1.0f;
        }

        return 1.0f - (float)LevenshteinDistance(a, b) / maxLen;
    }

    /// <summary>
    ///     Returns a new string containing only Unicode letter characters.
    /// </summary>
    public static string KeepOnlyLetters(this string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var allLetters = true;
        foreach (var c in text)
        {
            if (!char.IsLetter(c))
            {
                allLetters = false;
                break;
            }
        }

        if (allLetters)
        {
            return text;
        }

        return string.Create(
                text.Length,
                text,
                static (span, src) =>
                {
                    var pos = 0;
                    foreach (var c in src)
                    {
                        if (char.IsLetter(c))
                        {
                            span[pos++] = c;
                        }
                    }

                    span[pos..].Fill('\0');
                }
            )
            .TrimEnd('\0');
    }
}

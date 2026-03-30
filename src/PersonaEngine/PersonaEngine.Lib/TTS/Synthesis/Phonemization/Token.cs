using System.Runtime.CompilerServices;
using System.Text;

namespace PersonaEngine.Lib.TTS.Synthesis;

public record Token
{
    public string Text { get; set; } = string.Empty;

    public string Tag { get; set; } = string.Empty;

    public string Whitespace { get; set; } = string.Empty;

    public bool IsHead { get; set; } = true;

    public string? Alias { get; set; }

    public string? Phonemes { get; set; }

    public double? Stress { get; set; }

    public string? Currency { get; set; }

    public string NumFlags { get; set; } = string.Empty;

    public bool Prespace { get; set; }

    public int? Rating { get; set; }

    public double? StartTs { get; set; }

    public double? EndTs { get; set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Token MergeTokens(List<Token> tokens, string? unk = null)
    {
        if (tokens.Count == 0)
        {
            throw new ArgumentException("Cannot merge empty token list", nameof(tokens));
        }

        if (tokens.Count == 1)
        {
            return tokens[0];
        }

        var stressValues = new HashSet<double?>();
        var currencyValues = new HashSet<string?>();
        var ratingValues = new HashSet<int?>();
        string? phonemes;

        foreach (var t in tokens)
        {
            if (t.Stress.HasValue)
            {
                stressValues.Add(t.Stress);
            }

            if (t.Currency != null)
            {
                currencyValues.Add(t.Currency);
            }

            ratingValues.Add(t.Rating);
        }

        if (unk == null)
        {
            phonemes = null;
        }
        else
        {
            var sb = new StringBuilder();
            for (var i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (
                    t.Prespace
                    && sb.Length > 0
                    && sb[sb.Length - 1] != ' '
                    && !string.IsNullOrEmpty(t.Phonemes)
                )
                {
                    sb.Append(' ');
                }

                sb.Append(t.Phonemes == null ? unk : t.Phonemes);
            }

            phonemes = sb.ToString();
        }

        // Get tag from token with highest "weight"
        var tag = tokens[0].Tag;
        var maxWeight = GetTagWeight(tokens[0]);

        for (var i = 1; i < tokens.Count; i++)
        {
            var weight = GetTagWeight(tokens[i]);
            if (weight > maxWeight)
            {
                maxWeight = weight;
                tag = tokens[i].Tag;
            }
        }

        // Concatenate text and whitespace
        var textBuilder = new StringBuilder();
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            textBuilder.Append(tokens[i].Text);
            textBuilder.Append(tokens[i].Whitespace);
        }

        textBuilder.Append(tokens[tokens.Count - 1].Text);

        // Build num flags
        var numFlagsBuilder = new StringBuilder();
        var flagsSet = new HashSet<char>();

        foreach (var t in tokens)
        {
            foreach (var c in t.NumFlags)
            {
                flagsSet.Add(c);
            }
        }

        foreach (var c in flagsSet.OrderBy(c => c))
        {
            numFlagsBuilder.Append(c);
        }

        return new Token
        {
            Text = textBuilder.ToString(),
            Tag = tag,
            Whitespace = tokens[tokens.Count - 1].Whitespace,
            IsHead = tokens[0].IsHead,
            Alias = null,
            Phonemes = phonemes,
            Stress = stressValues.Count == 1 ? stressValues.First() : null,
            Currency = currencyValues.Any()
                ? currencyValues.OrderByDescending(c => c).First()
                : null,
            NumFlags = numFlagsBuilder.ToString(),
            Prespace = tokens[0].Prespace,
            Rating = ratingValues.Contains(null) ? null : ratingValues.Min(),
            StartTs = tokens[0].StartTs,
            EndTs = tokens[tokens.Count - 1].EndTs,
        };
    }

    private static int GetTagWeight(Token token)
    {
        var weight = 0;
        foreach (var c in token.Text)
        {
            weight += char.IsLower(c) ? 1 : 2;
        }

        return weight;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsTo()
    {
        return Text == "to" || Text == "To" || (Text == "TO" && (Tag == "TO" || Tag == "IN"));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int StressWeight()
    {
        if (string.IsNullOrEmpty(Phonemes))
        {
            return 0;
        }

        var weight = 0;
        foreach (var c in Phonemes)
        {
            if (PhonemizerConstants.Diphthongs.Contains(c))
            {
                weight += 2;
            }
            else
            {
                weight += 1;
            }
        }

        return weight;
    }
}

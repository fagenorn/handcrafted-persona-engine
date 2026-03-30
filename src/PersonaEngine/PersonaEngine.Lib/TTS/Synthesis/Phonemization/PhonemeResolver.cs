using System.Text;

namespace PersonaEngine.Lib.TTS.Synthesis;

internal class PhonemeResolver
{
    private readonly IFallbackPhonemizer? _fallback;

    private readonly ILexicon _lexicon;

    private readonly string _unk;

    public PhonemeResolver(ILexicon lexicon, IFallbackPhonemizer? fallback, string unk)
    {
        _lexicon = lexicon;
        _fallback = fallback;
        _unk = unk;
    }

    public async Task ProcessTokensAsync(
        List<RetokenizedItem> tokens,
        TokenContext ctx,
        CancellationToken cancellationToken
    )
    {
        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (tokens[i].IsSingleToken)
            {
                var token = tokens[i].SingleToken;
                if (token.Phonemes == null)
                {
                    (token.Phonemes, token.Rating) = await GetPhonemesAsync(
                        token,
                        ctx,
                        cancellationToken
                    );
                }

                ctx = TokenContext.UpdateContext(ctx, token.Phonemes, token);
            }
            else if (tokens[i].IsTokenGroup)
            {
                await ProcessSubtokensAsync(tokens[i].TokenGroup, ctx, cancellationToken);
            }
        }
    }

    private async Task ProcessSubtokensAsync(
        List<Token> tokens,
        TokenContext ctx,
        CancellationToken cancellationToken
    )
    {
        int left = 0,
            right = tokens.Count;

        var shouldFallback = false;

        while (left < right)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (
                tokens.Skip(left).Take(right - left).Any(t => t.Alias != null || t.Phonemes != null)
            )
            {
                left++;

                continue;
            }

            var mergedToken = Token.MergeTokens(
                tokens.Skip(left).Take(right - left).ToList(),
                _unk
            );
            var (phonemes, rating) = await GetPhonemesAsync(mergedToken, ctx, cancellationToken);

            if (phonemes != null)
            {
                AssignPhonemesToGroup(tokens, left, right, phonemes, rating);

                ctx = TokenContext.UpdateContext(ctx, phonemes, mergedToken);
                right = left;
                left = 0;
            }
            else if (left + 1 < right)
            {
                left++;
            }
            else
            {
                right--;
                var t = tokens[right];

                if (t.Phonemes == null)
                {
                    if (IsJunkText(t.Text))
                    {
                        t.Phonemes = string.Empty;
                        t.Rating = 3;
                    }
                    else if (_fallback != null)
                    {
                        shouldFallback = true;

                        break;
                    }
                }

                left = 0;
            }
        }

        if (shouldFallback && _fallback != null)
        {
            var mergedToken = Token.MergeTokens(tokens, _unk);
            var (phonemes, rating) = await _fallback.GetPhonemesAsync(
                mergedToken.Text,
                cancellationToken
            );

            if (phonemes != null)
            {
                AssignPhonemesToGroup(tokens, 0, tokens.Count, phonemes, rating);
            }
        }

        ResolveTokens(tokens);
    }

    private async Task<(string? Phonemes, int? Rating)> GetPhonemesAsync(
        Token token,
        TokenContext ctx,
        CancellationToken cancellationToken
    )
    {
        var (phonemes, rating) = _lexicon.ProcessToken(token, ctx);

        if (phonemes == null && _fallback != null)
        {
            return await _fallback.GetPhonemesAsync(token.Alias ?? token.Text, cancellationToken);
        }

        return (phonemes, rating);
    }

    private static void AssignPhonemesToGroup(
        List<Token> tokens,
        int start,
        int end,
        string phonemes,
        int? rating
    )
    {
        tokens[start].Phonemes = phonemes;
        tokens[start].Rating = rating;

        for (var i = start + 1; i < end; i++)
        {
            tokens[i].Phonemes = string.Empty;
            tokens[i].Rating = rating;
        }
    }

    private static bool IsJunkText(string text)
    {
        return text.All(c => PhonemizerConstants.SubtokenJunks.Contains(c));
    }

    private void ResolveTokens(List<Token> tokens)
    {
        var text =
            string.Concat(tokens.Take(tokens.Count - 1).Select(t => t.Text + t.Whitespace))
            + tokens[tokens.Count - 1].Text;

        var prespace =
            text.Contains(' ')
            || text.Contains('/')
            || text.Where(c => !PhonemizerConstants.SubtokenJunks.Contains(c))
                .Select(c =>
                    char.IsLetter(c) ? 0
                    : char.IsDigit(c) ? 1
                    : 2
                )
                .Distinct()
                .Count() > 1;

        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];

            if (t.Phonemes == null)
            {
                if (
                    i == tokens.Count - 1
                    && t.Text.Length > 0
                    && PhonemizerConstants.NonQuotePuncts.Contains(t.Text[0])
                )
                {
                    t.Phonemes = t.Text;
                    t.Rating = 3;
                }
                else if (IsJunkText(t.Text))
                {
                    t.Phonemes = string.Empty;
                    t.Rating = 3;
                }
            }
            else if (i > 0)
            {
                t.Prespace = prespace;
            }
        }

        if (prespace)
        {
            return;
        }

        var indices = new List<(bool HasPrimaryStress, int Weight, int Index)>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!string.IsNullOrEmpty(tokens[i].Phonemes))
            {
                var hasPrimary = tokens[i].Phonemes.Contains(PhonemizerConstants.PrimaryStress);
                indices.Add((hasPrimary, tokens[i].StressWeight(), i));
            }
        }

        if (indices.Count == 2 && tokens[indices[0].Index].Text.Length == 1)
        {
            var i = indices[1].Index;
            tokens[i].Phonemes = ApplyStress(tokens[i].Phonemes!, -0.5);

            return;
        }

        if (indices.Count < 2 || indices.Count(x => x.HasPrimaryStress) <= (indices.Count + 1) / 2)
        {
            return;
        }

        indices.Sort();
        foreach (var (_, _, i) in indices.Take(indices.Count / 2))
        {
            tokens[i].Phonemes = ApplyStress(tokens[i].Phonemes!, -0.5);
        }
    }

    private string ApplyStress(string phonemes, double stress)
    {
        if (stress < -1)
        {
            return phonemes
                .Replace(PhonemizerConstants.PrimaryStress.ToString(), string.Empty)
                .Replace(PhonemizerConstants.SecondaryStress.ToString(), string.Empty);
        }

        if (
            stress == -1
            || (
                (stress == 0 || stress == -0.5)
                && phonemes.Contains(PhonemizerConstants.PrimaryStress)
            )
        )
        {
            return phonemes
                .Replace(PhonemizerConstants.SecondaryStress.ToString(), string.Empty)
                .Replace(
                    PhonemizerConstants.PrimaryStress.ToString(),
                    PhonemizerConstants.SecondaryStress.ToString()
                );
        }

        if (
            (stress == 0 || stress == 0.5 || stress == 1)
            && !phonemes.Contains(PhonemizerConstants.PrimaryStress)
            && !phonemes.Contains(PhonemizerConstants.SecondaryStress)
        )
        {
            if (!phonemes.Any(c => PhonemizerConstants.Vowels.Contains(c)))
            {
                return phonemes;
            }

            return RestressPhonemes(PhonemizerConstants.SecondaryStress + phonemes);
        }

        if (
            stress >= 1
            && !phonemes.Contains(PhonemizerConstants.PrimaryStress)
            && phonemes.Contains(PhonemizerConstants.SecondaryStress)
        )
        {
            return phonemes.Replace(
                PhonemizerConstants.SecondaryStress.ToString(),
                PhonemizerConstants.PrimaryStress.ToString()
            );
        }

        if (
            stress > 1
            && !phonemes.Contains(PhonemizerConstants.PrimaryStress)
            && !phonemes.Contains(PhonemizerConstants.SecondaryStress)
        )
        {
            if (!phonemes.Any(c => PhonemizerConstants.Vowels.Contains(c)))
            {
                return phonemes;
            }

            return RestressPhonemes(PhonemizerConstants.PrimaryStress + phonemes);
        }

        return phonemes;
    }

    private string RestressPhonemes(string phonemes)
    {
        var chars = phonemes.ToCharArray();
        var charPositions = new List<(int Position, char Char)>();

        for (var i = 0; i < chars.Length; i++)
        {
            charPositions.Add((i, chars[i]));
        }

        var stressPositions = new Dictionary<int, int>();
        for (var i = 0; i < charPositions.Count; i++)
        {
            if (PhonemizerConstants.Stresses.Contains(charPositions[i].Char))
            {
                var vowelPos = -1;
                for (var j = i + 1; j < charPositions.Count; j++)
                {
                    if (PhonemizerConstants.Vowels.Contains(charPositions[j].Char))
                    {
                        vowelPos = j;

                        break;
                    }
                }

                if (vowelPos != -1)
                {
                    stressPositions[charPositions[i].Position] = charPositions[vowelPos].Position;
                    charPositions[i] = ((int)(vowelPos - 0.5), charPositions[i].Char);
                }
            }
        }

        charPositions.Sort((a, b) => a.Position.CompareTo(b.Position));

        return new string(charPositions.Select(cp => cp.Char).ToArray());
    }
}

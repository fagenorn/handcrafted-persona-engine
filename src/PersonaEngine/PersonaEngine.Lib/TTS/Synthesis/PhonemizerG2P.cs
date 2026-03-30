using System.Text;
using System.Text.RegularExpressions;

namespace PersonaEngine.Lib.TTS.Synthesis;

public class PhonemizerG2P : IPhonemizer
{
    private readonly IFallbackPhonemizer? _fallback;

    private readonly ILexicon _lexicon;

    private readonly Regex _linkRegex;

    private readonly IPosTagger _posTagger;

    private readonly Regex _subtokenRegex;

    private readonly string _unk;

    private bool _disposed;

    public PhonemizerG2P(
        IPosTagger posTagger,
        ILexicon lexicon,
        IFallbackPhonemizer? fallback = null,
        string unk = "❓"
    )
    {
        _posTagger = posTagger ?? throw new ArgumentNullException(nameof(posTagger));
        _fallback = fallback;
        _unk = unk;
        _lexicon = lexicon;

        // Initialize regular expressions once for performance
        _linkRegex = new Regex(@"\[([^\]]+)\]\(([^\)]*)\)", RegexOptions.Compiled);
        _subtokenRegex = new Regex(
            @"^[''']+|\p{Lu}(?=\p{Lu}\p{Ll})|(?:^-)?(?:\d?[,.]?\d)+|[-_]+|[''']{2,}|\p{L}*?(?:[''']\p{L})*?\p{Ll}(?=\p{Lu})|\p{L}+(?:[''']\p{L})*|[^-_\p{L}'''\d]|[''']+$",
            RegexOptions.Compiled
        );

        _disposed = false;
    }

    public async Task<PhonemeResult> ToPhonemesAsync(
        string text,
        CancellationToken cancellationToken = default
    )
    {
        // 1. Preprocess text
        var (processedText, textTokens, features) = Preprocess(text);

        // 2. Tag text with POS tagger
        var posTokens = await _posTagger.TagAsync(processedText, cancellationToken);

        // 3. Convert to internal token representation
        var tokens = ConvertToTokens(posTokens);

        // 4. Apply features from preprocessing
        ApplyFeatures(tokens, textTokens, features);

        // 5. Fold left (merge non-head tokens with previous token)
        tokens = FoldLeft(tokens);

        // 6. Retokenize (split complex tokens and handle special cases)
        var retokenizedTokens = Retokenize(tokens);

        // 7. Process phonemes using lexicon and fallback
        var ctx = new TokenContext();
        await ProcessTokensAsync(retokenizedTokens, ctx, cancellationToken);

        // 8. Merge retokenized tokens
        var mergedTokens = MergeRetokenizedTokens(retokenizedTokens);

        // 9. Generate final phoneme string
        var phonemes = string.Concat(mergedTokens.Select(t => (t.Phonemes ?? _unk) + t.Whitespace));

        return new PhonemeResult(phonemes, mergedTokens);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private List<Token> ConvertToTokens(IReadOnlyList<PosToken> posTokens)
    {
        var tokens = new List<Token>(posTokens.Count);

        foreach (var pt in posTokens)
        {
            tokens.Add(
                new Token
                {
                    Text = pt.Text,
                    Tag = pt.PartOfSpeech ?? string.Empty,
                    Whitespace = pt.IsWhitespace ? " " : string.Empty,
                }
            );
        }

        return tokens;
    }

    private (
        string ProcessedText,
        List<string> Tokens,
        Dictionary<int, object> Features
    ) Preprocess(string text)
    {
        text = text.TrimStart();
        var result = new StringBuilder(text.Length);
        var tokens = new List<string>();
        var features = new Dictionary<int, object>();

        var lastEnd = 0;

        foreach (Match m in _linkRegex.Matches(text))
        {
            if (m.Index > lastEnd)
            {
                var segment = text.Substring(lastEnd, m.Index - lastEnd);
                result.Append(segment);
                tokens.AddRange(segment.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }

            var linkText = m.Groups[1].Value;
            var featureText = m.Groups[2].Value;

            object? featureValue = null;
            if (featureText.Length >= 1)
            {
                if (
                    (featureText[0] == '-' || featureText[0] == '+')
                    && int.TryParse(featureText, out var intVal)
                )
                {
                    featureValue = intVal;
                }
                else if (int.TryParse(featureText, out intVal))
                {
                    featureValue = intVal;
                }
                else if (featureText == "0.5" || featureText == "+0.5")
                {
                    featureValue = 0.5;
                }
                else if (featureText == "-0.5")
                {
                    featureValue = -0.5;
                }
                else if (featureText.Length > 1)
                {
                    var firstChar = featureText[0];
                    var lastChar = featureText[featureText.Length - 1];

                    if (firstChar == '/' && lastChar == '/')
                    {
                        featureValue = firstChar + featureText.Substring(1, featureText.Length - 2);
                    }
                    else if (firstChar == '#' && lastChar == '#')
                    {
                        featureValue = firstChar + featureText.Substring(1, featureText.Length - 2);
                    }
                }
            }

            if (featureValue != null)
            {
                features[tokens.Count] = featureValue;
            }

            result.Append(linkText);
            tokens.Add(linkText);
            lastEnd = m.Index + m.Length;
        }

        if (lastEnd < text.Length)
        {
            var segment = text.Substring(lastEnd);
            result.Append(segment);
            tokens.AddRange(segment.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        return (result.ToString(), tokens, features);
    }

    private void ApplyFeatures(
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
                        // The "i == 0" in Python refers to the first match of this feature
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

    private int[] CreateBidirectionalAlignment(List<string> source, List<string> target)
    {
        // This is a simplified version - ideally it would match spaCy's algorithm more closely
        var alignment = new int[target.Count];

        // Create a combined string from each list to verify they match overall
        var sourceText = string.Join("", source).ToLowerInvariant();
        var targetText = string.Join("", target).ToLowerInvariant();

        // Initialize all alignments to no match
        for (var i = 0; i < alignment.Length; i++)
        {
            alignment[i] = -1;
        }

        var targetIndex = 0;
        var sourcePos = 0;

        // Track position in the joined strings to handle multi-token to single token mappings
        for (var sourceIndex = 0; sourceIndex < source.Count; sourceIndex++)
        {
            var sourceToken = source[sourceIndex].ToLowerInvariant();
            sourcePos += sourceToken.Length;

            var targetPos = 0;
            for (var i = 0; i < targetIndex; i++)
            {
                targetPos += target[i].ToLowerInvariant().Length;
            }

            // Map all target tokens that overlap with this source token
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

    private List<Token> FoldLeft(List<Token> tokens)
    {
        if (tokens.Count <= 1)
        {
            return tokens;
        }

        var result = new List<Token>(tokens.Count);
        result.Add(tokens[0]);

        for (var i = 1; i < tokens.Count; i++)
        {
            if (!tokens[i].IsHead)
            {
                var merged = Token.MergeTokens([result[^1], tokens[i]], _unk);

                result[^1] = merged;
            }
            else
            {
                result.Add(tokens[i]);
            }
        }

        return result;
    }

    private List<object> Retokenize(List<Token> tokens)
    {
        var result = new List<object>(tokens.Count * 2); // Estimate capacity
        string? currentCurrency = null;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            List<Token> subtokens;

            // Split token if needed
            if (token.Alias == null && token.Phonemes == null)
            {
                var subTokenTexts = Subtokenize(token.Text);
                subtokens = new List<Token>(subTokenTexts.Count);

                for (var j = 0; j < subTokenTexts.Count; j++)
                {
                    subtokens.Add(
                        new Token
                        {
                            Text = subTokenTexts[j],
                            Tag = token.Tag,
                            Whitespace =
                                j == subTokenTexts.Count - 1 ? token.Whitespace : string.Empty,
                            IsHead = token.IsHead && j == 0,
                            Stress = token.Stress,
                            NumFlags = token.NumFlags,
                        }
                    );
                }
            }
            else
            {
                subtokens = new List<Token> { token };
            }

            // Process each subtoken
            for (var j = 0; j < subtokens.Count; j++)
            {
                var t = subtokens[j];

                if (t.Alias != null || t.Phonemes != null)
                {
                    // Skip special handling for already processed tokens
                }
                else if (t.Tag == "$" && PhonemizerConstants.Currencies.ContainsKey(t.Text))
                {
                    currentCurrency = t.Text;
                    t.Phonemes = string.Empty;
                    t.Rating = 4;
                }
                else if (t is { Tag: ":", Text: "-" or "–" })
                {
                    t.Phonemes = "—";
                    t.Rating = 3;
                }
                else if (PhonemizerConstants.PunctTags.Contains(t.Tag))
                {
                    if (PhonemizerConstants.PunctTagPhonemes.TryGetValue(t.Tag, out var phoneme))
                    {
                        t.Phonemes = phoneme;
                    }
                    else
                    {
                        var sb = new StringBuilder();
                        foreach (var c in t.Text)
                        {
                            if (PhonemizerConstants.Puncts.Contains(c))
                            {
                                sb.Append(c);
                            }
                        }

                        t.Phonemes = sb.ToString();
                    }

                    t.Rating = 4;
                }
                else if (currentCurrency != null)
                {
                    if (t.Tag != "CD")
                    {
                        currentCurrency = null;
                    }
                    else if (
                        j + 1 == subtokens.Count
                        && (i + 1 == tokens.Count || tokens[i + 1].Tag != "CD")
                    )
                    {
                        t.Currency = currentCurrency;
                    }
                }
                else if (
                    0 < j
                    && j < subtokens.Count - 1
                    && t.Text == "2"
                    && char.IsLetter(subtokens[j - 1].Text[subtokens[j - 1].Text.Length - 1])
                    && char.IsLetter(subtokens[j + 1].Text[0])
                )
                {
                    t.Alias = "to";
                }

                // Add to result
                if (t.Alias != null || t.Phonemes != null)
                {
                    result.Add(t);
                }
                else if (
                    result.Count > 0
                    && result[result.Count - 1] is List<Token> lastList
                    && lastList.Count > 0
                    && string.IsNullOrEmpty(lastList[lastList.Count - 1].Whitespace)
                )
                {
                    t.IsHead = false;
                    ((List<Token>)result[^1]).Add(t);
                }
                else
                {
                    result.Add(string.IsNullOrEmpty(t.Whitespace) ? new List<Token> { t } : t);
                }
            }
        }

        // Simplify lists with single elements
        for (var i = 0; i < result.Count; i++)
        {
            if (result[i] is List<Token> list && list.Count == 1)
            {
                result[i] = list[0];
            }
        }

        return result;
    }

    private List<string> Subtokenize(string word)
    {
        var matches = _subtokenRegex.Matches(word);
        if (matches.Count == 0)
        {
            return new List<string> { word };
        }

        var result = new List<string>(matches.Count);
        foreach (Match match in matches)
        {
            result.Add(match.Value);
        }

        return result;
    }

    private async Task ProcessTokensAsync(
        List<object> tokens,
        TokenContext ctx,
        CancellationToken cancellationToken
    )
    {
        // Process tokens in reverse order
        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (tokens[i] is Token token)
            {
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
            else if (tokens[i] is List<Token> subtokens)
            {
                await ProcessSubtokensAsync(subtokens, ctx, cancellationToken);
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
                tokens[left].Phonemes = phonemes;
                tokens[left].Rating = rating;

                for (var i = left + 1; i < right; i++)
                {
                    tokens[i].Phonemes = string.Empty;
                    tokens[i].Rating = rating;
                }

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
                    if (t.Text.All(c => PhonemizerConstants.SubtokenJunks.Contains(c)))
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
                tokens[0].Phonemes = phonemes;
                tokens[0].Rating = rating;

                for (var j = 1; j < tokens.Count; j++)
                {
                    tokens[j].Phonemes = string.Empty;
                    tokens[j].Rating = rating;
                }
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
        // Try to get from lexicon
        var (phonemes, rating) = _lexicon.ProcessToken(token, ctx);

        // If not found, try fallback
        if (phonemes == null && _fallback != null)
        {
            return await _fallback.GetPhonemesAsync(token.Alias ?? token.Text, cancellationToken);
        }

        return (phonemes, rating);
    }

    private void ResolveTokens(List<Token> tokens)
    {
        // Calculate if there should be space between phonemes
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

        // Handle specific cases
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
                else if (t.Text.All(c => PhonemizerConstants.SubtokenJunks.Contains(c)))
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

        // Adjust stress patterns
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
                // Find the next vowel
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

    private List<Token> MergeRetokenizedTokens(List<object> retokenizedTokens)
    {
        var result = new List<Token>();

        foreach (var item in retokenizedTokens)
        {
            if (item is Token token)
            {
                result.Add(token);
            }
            else if (item is List<Token> tokens)
            {
                result.Add(Token.MergeTokens(tokens, _unk));
            }
        }

        return result;
    }
}

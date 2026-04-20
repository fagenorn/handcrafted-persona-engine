namespace PersonaEngine.Lib.TTS.Synthesis;

internal class TokenRestructurer
{
    private readonly TextPreprocessor _preprocessor;

    private readonly string _unk;

    public TokenRestructurer(TextPreprocessor preprocessor, string unk)
    {
        _preprocessor = preprocessor;
        _unk = unk;
    }

    public List<Token> ConvertToTokens(IReadOnlyList<PosToken> posTokens)
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

    public List<Token> FoldLeft(List<Token> tokens)
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

    public List<RetokenizedItem> Retokenize(List<Token> tokens)
    {
        var result = new List<RetokenizedItem>(tokens.Count * 2);
        string? currentCurrency = null;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            List<Token> subtokens;

            if (token.Alias == null && token.Phonemes == null)
            {
                var subTokenTexts = _preprocessor.Subtokenize(token.Text);
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
                        var sb = new System.Text.StringBuilder();
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

                if (t.Alias != null || t.Phonemes != null)
                {
                    result.Add(RetokenizedItem.FromToken(t));
                }
                else if (
                    result.Count > 0
                    && result[^1].IsTokenGroup
                    && result[^1].TokenGroup.Count > 0
                    && string.IsNullOrEmpty(result[^1].TokenGroup[^1].Whitespace)
                )
                {
                    t.IsHead = false;
                    result[^1].TokenGroup.Add(t);
                }
                else
                {
                    result.Add(
                        string.IsNullOrEmpty(t.Whitespace)
                            ? RetokenizedItem.FromGroup(new List<Token> { t })
                            : RetokenizedItem.FromToken(t)
                    );
                }
            }
        }

        for (var i = 0; i < result.Count; i++)
        {
            if (result[i].IsTokenGroup && result[i].TokenGroup.Count == 1)
            {
                result[i] = RetokenizedItem.FromToken(result[i].TokenGroup[0]);
            }
        }

        return result;
    }

    public List<Token> MergeRetokenizedTokens(List<RetokenizedItem> retokenizedTokens)
    {
        var result = new List<Token>();

        foreach (var item in retokenizedTokens)
        {
            if (item.IsSingleToken)
            {
                result.Add(item.SingleToken);
            }
            else if (item.IsTokenGroup)
            {
                result.Add(Token.MergeTokens(item.TokenGroup, _unk));
            }
        }

        return result;
    }
}

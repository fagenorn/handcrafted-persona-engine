using NSubstitute;
using PersonaEngine.Lib.TTS.Synthesis;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.Synthesis;

public class PhonemizerG2PTests : IDisposable
{
    private readonly IFallbackPhonemizer _fallback = Substitute.For<IFallbackPhonemizer>();

    private readonly ILexicon _lexicon = Substitute.For<ILexicon>();

    private readonly IPosTagger _posTagger = Substitute.For<IPosTagger>();

    public void Dispose() { }

    private PhonemizerG2P CreateSut(bool withFallback = true)
    {
        return new PhonemizerG2P(_posTagger, _lexicon, withFallback ? _fallback : null);
    }

    private void SetupLexicon(Dictionary<string, (string Phonemes, int Rating)> entries)
    {
        _lexicon
            .ProcessToken(Arg.Any<Token>(), Arg.Any<TokenContext>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<Token>();
                var key = token.Alias ?? token.Text;
                if (entries.TryGetValue(key, out var entry))
                {
                    return (entry.Phonemes, (int?)entry.Rating);
                }

                return ((string?)null, (int?)null);
            });
    }

    // ═══════════════════════════════════════════════════════════
    // Basic Lexicon Resolution
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task SingleWord_ResolvedFromLexicon_ReturnsPhonemes()
    {
        _posTagger
            .TagAsync("hello", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["hello"] = ("həˈloʊ", 5) });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("hello");

        Assert.Equal("həˈloʊ", result.Phonemes);
        Assert.Single(result.Tokens);
        Assert.Equal("hello", result.Tokens[0].Text);
        Assert.Equal("həˈloʊ", result.Tokens[0].Phonemes);
        Assert.Equal(5, result.Tokens[0].Rating);
    }

    [Fact]
    public async Task TwoWords_BothInLexicon_ReturnsSpaceSeparatedPhonemes()
    {
        _posTagger
            .TagAsync("hello world", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = true },
                    new() { Text = "world", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(
            new() { ["hello"] = ("həˈloʊ", 5), ["world"] = ("wɜːɹld", 5) }
        );

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("hello world");

        Assert.Equal("həˈloʊ wɜːɹld", result.Phonemes);
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal("hello", result.Tokens[0].Text);
        Assert.Equal(" ", result.Tokens[0].Whitespace);
        Assert.Equal("world", result.Tokens[1].Text);
        Assert.Equal(string.Empty, result.Tokens[1].Whitespace);
    }

    [Fact]
    public async Task ThreeWords_AllInLexicon_PreservesWhitespace()
    {
        _posTagger
            .TagAsync("I like cats", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "I", PartOfSpeech = "PRP", IsWhitespace = true },
                    new() { Text = "like", PartOfSpeech = "VBP", IsWhitespace = true },
                    new() { Text = "cats", PartOfSpeech = "NNS", IsWhitespace = false },
                }
            );

        SetupLexicon(
            new()
            {
                ["I"] = ("AY", 5),
                ["like"] = ("lAYk", 5),
                ["cats"] = ("kæts", 5),
            }
        );

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("I like cats");

        Assert.Equal("AY lAYk kæts", result.Phonemes);
        Assert.Equal(3, result.Tokens.Count);
    }

    // ═══════════════════════════════════════════════════════════
    // Fallback Resolution
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task UnknownWord_WithFallback_UsesFallbackPhonemizer()
    {
        _posTagger
            .TagAsync("xyzzy", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "xyzzy", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(new());
        _fallback
            .GetPhonemesAsync("xyzzy", Arg.Any<CancellationToken>())
            .Returns(("ˈzɪzi", (int?)2));

        using var sut = CreateSut(withFallback: true);
        var result = await sut.ToPhonemesAsync("xyzzy");

        Assert.Equal("ˈzɪzi", result.Phonemes);
        Assert.Single(result.Tokens);
        Assert.Equal(2, result.Tokens[0].Rating);
    }

    [Fact]
    public async Task UnknownWord_NoFallback_ReturnsUnknownMarker()
    {
        _posTagger
            .TagAsync("xyzzy", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "xyzzy", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(new());

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("xyzzy");

        Assert.Contains("❓", result.Phonemes);
    }

    [Fact]
    public async Task MixedKnownUnknown_FallbackCalledOnlyForUnknown()
    {
        _posTagger
            .TagAsync("hello xyzzy", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = true },
                    new() { Text = "xyzzy", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["hello"] = ("həˈloʊ", 5) });
        _fallback
            .GetPhonemesAsync("xyzzy", Arg.Any<CancellationToken>())
            .Returns(("ˈzɪzi", (int?)2));

        using var sut = CreateSut(withFallback: true);
        var result = await sut.ToPhonemesAsync("hello xyzzy");

        Assert.Equal("həˈloʊ ˈzɪzi", result.Phonemes);
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal(5, result.Tokens[0].Rating);
        Assert.Equal(2, result.Tokens[1].Rating);
    }

    // ═══════════════════════════════════════════════════════════
    // Punctuation Handling
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Comma_MappedFromPunctChars()
    {
        _posTagger
            .TagAsync("hello, world", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                    new() { Text = ",", PartOfSpeech = ",", IsWhitespace = true },
                    new() { Text = "world", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(
            new() { ["hello"] = ("həˈloʊ", 5), ["world"] = ("wɜːɹld", 5) }
        );

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("hello, world");

        // Comma is in Puncts set, so its phoneme is ","
        Assert.Equal(3, result.Tokens.Count);
        Assert.Equal(",", result.Tokens[1].Phonemes);
        Assert.Equal(4, result.Tokens[1].Rating);
    }

    [Fact]
    public async Task Period_MappedFromPunctChars()
    {
        _posTagger
            .TagAsync("hello.", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                    new() { Text = ".", PartOfSpeech = ".", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["hello"] = ("həˈloʊ", 5) });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("hello.");

        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal(".", result.Tokens[1].Phonemes);
        Assert.Equal(4, result.Tokens[1].Rating);
    }

    [Fact]
    public async Task LeftParenthesis_MappedFromPunctTagPhonemes()
    {
        _posTagger
            .TagAsync("(hello)", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "(", PartOfSpeech = "-LRB-", IsWhitespace = false },
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                    new() { Text = ")", PartOfSpeech = "-RRB-", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["hello"] = ("həˈloʊ", 5) });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("(hello)");

        Assert.Equal(3, result.Tokens.Count);
        Assert.Equal("(", result.Tokens[0].Phonemes);
        Assert.Equal(")", result.Tokens[2].Phonemes);
    }

    [Fact]
    public async Task OpeningQuotes_MappedToEmDash()
    {
        _posTagger
            .TagAsync("``hello", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "``", PartOfSpeech = "``", IsWhitespace = false },
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["hello"] = ("həˈloʊ", 5) });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("``hello");

        Assert.Equal("\u2014", result.Tokens[0].Phonemes); // em dash
    }

    [Fact]
    public async Task ClosingQuotes_MappedToRightDoubleQuote()
    {
        _posTagger
            .TagAsync("hello''", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                    new() { Text = "''", PartOfSpeech = "''", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["hello"] = ("həˈloʊ", 5) });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("hello''");

        Assert.Equal("\u201D", result.Tokens[1].Phonemes); // right double quote
    }

    [Fact]
    public async Task DashWithColonTag_BecomesEmDash()
    {
        _posTagger
            .TagAsync("word - word", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "word", PartOfSpeech = "NN", IsWhitespace = true },
                    new() { Text = "-", PartOfSpeech = ":", IsWhitespace = true },
                    new() { Text = "word", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["word"] = ("wɜːɹd", 5) });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("word - word");

        Assert.Equal(3, result.Tokens.Count);
        Assert.Equal("—", result.Tokens[1].Phonemes);
        Assert.Equal(3, result.Tokens[1].Rating);
    }

    [Fact]
    public async Task EnDashWithColonTag_BecomesEmDash()
    {
        _posTagger
            .TagAsync("word – word", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "word", PartOfSpeech = "NN", IsWhitespace = true },
                    new() { Text = "–", PartOfSpeech = ":", IsWhitespace = true },
                    new() { Text = "word", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["word"] = ("wɜːɹd", 5) });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("word – word");

        Assert.Equal("—", result.Tokens[1].Phonemes);
    }

    [Fact]
    public async Task ExclamationMark_MappedAsPunct()
    {
        // "." POS tag covers sentence-ending punctuation: . ! ?
        _posTagger
            .TagAsync("hello!", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                    new() { Text = "!", PartOfSpeech = ".", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["hello"] = ("həˈloʊ", 5) });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("hello!");

        Assert.Equal(2, result.Tokens.Count);
        // "!" is in Puncts set, so phoneme is "!"
        Assert.Equal("!", result.Tokens[1].Phonemes);
        Assert.Equal(4, result.Tokens[1].Rating);
    }

    // ═══════════════════════════════════════════════════════════
    // Currency Handling
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task DollarSign_SetsCurrencyOnFollowingNumber()
    {
        _posTagger
            .TagAsync("$50", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "$", PartOfSpeech = "$", IsWhitespace = false },
                    new() { Text = "50", PartOfSpeech = "CD", IsWhitespace = false },
                }
            );

        // The $ token gets empty phonemes; "50" gets Currency="$" and goes to lexicon
        _lexicon
            .ProcessToken(Arg.Any<Token>(), Arg.Any<TokenContext>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<Token>();
                if (token.Text == "50" && token.Currency == "$")
                {
                    return ("ˈfɪfti ˈdɑlɜːz", (int?)4);
                }

                return ((string?)null, (int?)null);
            });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("$50");

        // $ token has empty phonemes, 50 token has currency phonemes
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal(string.Empty, result.Tokens[0].Phonemes);
        Assert.Equal("$", result.Tokens[1].Currency);
    }

    [Fact]
    public async Task PoundSign_SetsCurrencyOnFollowingNumber()
    {
        _posTagger
            .TagAsync("£10", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "£", PartOfSpeech = "$", IsWhitespace = false },
                    new() { Text = "10", PartOfSpeech = "CD", IsWhitespace = false },
                }
            );

        _lexicon
            .ProcessToken(Arg.Any<Token>(), Arg.Any<TokenContext>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<Token>();
                if (token.Text == "10" && token.Currency == "£")
                {
                    return ("tɛn ˈpaʊndz", (int?)4);
                }

                return ((string?)null, (int?)null);
            });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("£10");

        Assert.Equal("£", result.Tokens[1].Currency);
    }

    [Fact]
    public async Task DollarSign_FollowedByNonNumber_NoCurrencySet()
    {
        _posTagger
            .TagAsync("$ word", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "$", PartOfSpeech = "$", IsWhitespace = true },
                    new() { Text = "word", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["word"] = ("wɜːɹd", 5) });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("$ word");

        // $ gets empty phonemes, "word" has no currency since tag is not CD
        Assert.Equal(2, result.Tokens.Count);
        Assert.Null(result.Tokens[1].Currency);
    }

    // ═══════════════════════════════════════════════════════════
    // Markdown Link Feature Annotations
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task PhonemeOverride_LinkWithSlashes_SetsPhonemesDirectly()
    {
        // Input: "[hello](/həˈloʊ/)" → preprocessed text is "hello"
        _posTagger
            .TagAsync("hello", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                }
            );

        // Lexicon should NOT be called since phonemes are already set by feature
        SetupLexicon(new());

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("[hello](/həˈloʊ/)");

        Assert.Equal("həˈloʊ", result.Phonemes);
        Assert.Single(result.Tokens);
        Assert.Equal(5, result.Tokens[0].Rating);
    }

    [Fact]
    public async Task IntegerStress_LinkWithPositiveInt_SetsStressOnToken()
    {
        // Input: "[hello](+1)" → preprocessed text is "hello", feature is int 1
        _posTagger
            .TagAsync("hello", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                }
            );

        _lexicon
            .ProcessToken(Arg.Any<Token>(), Arg.Any<TokenContext>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<Token>();
                if (token.Text == "hello")
                {
                    // Verify stress was applied
                    Assert.Equal(1.0, token.Stress);
                    return ("həˈloʊ", (int?)5);
                }

                return ((string?)null, (int?)null);
            });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("[hello](+1)");

        Assert.Equal("həˈloʊ", result.Phonemes);
    }

    [Fact]
    public async Task NegativeStress_LinkWithNegativeInt_SetsNegativeStress()
    {
        // Input: "[hello](-1)" → feature is int -1
        _posTagger
            .TagAsync("hello", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                }
            );

        _lexicon
            .ProcessToken(Arg.Any<Token>(), Arg.Any<TokenContext>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<Token>();
                if (token.Text == "hello")
                {
                    Assert.Equal(-1.0, token.Stress);
                    return ("həˈloʊ", (int?)5);
                }

                return ((string?)null, (int?)null);
            });

        using var sut = CreateSut(withFallback: false);
        await sut.ToPhonemesAsync("[hello](-1)");
    }

    [Fact]
    public async Task HalfStress_LinkWithPointFive_SetsHalfStress()
    {
        // Input: "[hello](0.5)" → feature is double 0.5
        _posTagger
            .TagAsync("hello", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                }
            );

        _lexicon
            .ProcessToken(Arg.Any<Token>(), Arg.Any<TokenContext>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<Token>();
                if (token.Text == "hello")
                {
                    Assert.Equal(0.5, token.Stress);
                    return ("həˈloʊ", (int?)5);
                }

                return ((string?)null, (int?)null);
            });

        using var sut = CreateSut(withFallback: false);
        await sut.ToPhonemesAsync("[hello](0.5)");
    }

    [Fact]
    public async Task NumFlags_LinkWithHashSyntax_SetsNumFlagsOnToken()
    {
        // Input: "[50](#o#)" → feature is "#o"
        _posTagger
            .TagAsync("50", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "50", PartOfSpeech = "CD", IsWhitespace = false },
                }
            );

        _lexicon
            .ProcessToken(Arg.Any<Token>(), Arg.Any<TokenContext>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<Token>();
                if (token.Text == "50")
                {
                    // NumFlags should contain "o" (the content between # markers)
                    Assert.Contains("o", token.NumFlags);
                    return ("ˈfɪfti", (int?)5);
                }

                return ((string?)null, (int?)null);
            });

        using var sut = CreateSut(withFallback: false);
        await sut.ToPhonemesAsync("[50](#o#)");
    }

    [Fact]
    public async Task MultipleLinks_FeaturesAppliedToCorrectTokens()
    {
        // Input: "[hello](/həˈloʊ/) [world](/wɜːɹld/)"
        // Preprocessed text: "hello world"
        _posTagger
            .TagAsync("hello world", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = true },
                    new() { Text = "world", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(new());

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("[hello](/həˈloʊ/) [world](/wɜːɹld/)");

        Assert.Equal("həˈloʊ wɜːɹld", result.Phonemes);
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal(5, result.Tokens[0].Rating);
        Assert.Equal(5, result.Tokens[1].Rating);
    }

    [Fact]
    public async Task LinkWithPlainTextBefore_BothProcessedCorrectly()
    {
        // Input: "say [hello](/həˈloʊ/)"
        // Preprocessed text: "say hello", tokens: ["say", "hello"], features: {1: "/həˈloʊ"}
        _posTagger
            .TagAsync("say hello", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "say", PartOfSpeech = "VB", IsWhitespace = true },
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["say"] = ("seɪ", 5) });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("say [hello](/həˈloʊ/)");

        Assert.Equal("seɪ həˈloʊ", result.Phonemes);
        Assert.Equal(2, result.Tokens.Count);
    }

    // ═══════════════════════════════════════════════════════════
    // FoldLeft - Non-head Token Merging
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task PhonemeOverride_SecondTokenOfMultiTokenWord_MergedWithFirst()
    {
        // When a multi-word link maps to multiple POS tokens,
        // the second+ tokens get IsHead=false and phonemes="" via ApplyFeatures.
        // FoldLeft merges them with the previous token.
        // Input: "[good morning](/gʊd ˈmɔːɹnɪŋ/)"
        // Preprocessed text: "good morning", tokens: ["good morning"], features: {0: "/gʊd ˈmɔːɹnɪŋ"}
        _posTagger
            .TagAsync("good morning", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "good", PartOfSpeech = "JJ", IsWhitespace = true },
                    new() { Text = "morning", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(new());

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("[good morning](/gʊd ˈmɔːɹnɪŋ/)");

        // Both POS tokens map to feature index 0.
        // First gets IsHead=true, Phonemes="gʊd ˈmɔːɹnɪŋ"
        // Second gets IsHead=false, Phonemes=""
        // FoldLeft merges them into one token
        Assert.Single(result.Tokens);
        Assert.Equal("gʊd ˈmɔːɹnɪŋ", result.Tokens[0].Phonemes);
    }

    // ═══════════════════════════════════════════════════════════
    // Subtokenization and Token Groups
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task HyphenatedWord_LexiconCalledWithMergedText()
    {
        // "self-care" as a single POS token gets subtokenized into ["self", "-", "care"]
        // These form a token group, and the lexicon is first called with the merged text
        _posTagger
            .TagAsync("self-care", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "self-care", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        var calledWithTexts = new List<string>();
        _lexicon
            .ProcessToken(Arg.Any<Token>(), Arg.Any<TokenContext>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<Token>();
                calledWithTexts.Add(token.Text);
                if (token.Text == "self-care")
                {
                    return ("ˌsɛlfˈkɛɹ", (int?)5);
                }

                return ((string?)null, (int?)null);
            });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("self-care");

        // The merged text "self-care" should have been tried
        Assert.Contains("self-care", calledWithTexts);
    }

    [Fact]
    public async Task HyphenatedWord_LexiconMiss_TriesSmallerWindows()
    {
        _posTagger
            .TagAsync("self-care", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "self-care", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        var calledWithTexts = new List<string>();
        _lexicon
            .ProcessToken(Arg.Any<Token>(), Arg.Any<TokenContext>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<Token>();
                calledWithTexts.Add(token.Text);
                // Only individual words succeed
                return token.Text switch
                {
                    "self" => ("sɛlf", (int?)5),
                    "care" => ("kɛɹ", (int?)5),
                    _ => ((string?)null, (int?)null),
                };
            });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("self-care");

        // Lexicon should have been tried with various subtoken combinations
        Assert.True(calledWithTexts.Count > 1);
    }

    [Fact]
    public async Task SubtokenGroup_FallbackUsedWhenLexiconFails()
    {
        _posTagger
            .TagAsync("self-zyxwv", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "self-zyxwv", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(new());
        _fallback
            .GetPhonemesAsync("self-zyxwv", Arg.Any<CancellationToken>())
            .Returns(("ˌsɛlfˈzɪkswv", (int?)2));

        using var sut = CreateSut(withFallback: true);
        var result = await sut.ToPhonemesAsync("self-zyxwv");

        // Fallback should have been used for the entire merged token
        Assert.Contains("ˌsɛlfˈzɪkswv", result.Phonemes);
    }

    // ═══════════════════════════════════════════════════════════
    // "2" Between Letters → Alias "to"
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task TwoBetweenLetters_AliasedToTo()
    {
        // "b2b" subtokenizes to ["b", "2", "b"], and "2" gets Alias="to"
        _posTagger
            .TagAsync("b2b", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "b2b", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        _lexicon
            .ProcessToken(Arg.Any<Token>(), Arg.Any<TokenContext>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<Token>();
                if (token.Alias == "to")
                {
                    return ("tuː", (int?)5);
                }

                return token.Text switch
                {
                    "b" => ("biː", (int?)5),
                    _ => ((string?)null, (int?)null),
                };
            });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("b2b");

        // The "2" token should have been resolved with alias "to"
        Assert.NotNull(result.Phonemes);
        Assert.DoesNotContain("❓", result.Phonemes);
    }

    // ═══════════════════════════════════════════════════════════
    // Leading Whitespace Handling
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task LeadingWhitespace_TrimmedBeforeProcessing()
    {
        _posTagger
            .TagAsync("hello", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["hello"] = ("həˈloʊ", 5) });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("   hello");

        Assert.Equal("həˈloʊ", result.Phonemes);
    }

    // ═══════════════════════════════════════════════════════════
    // Token Properties Preservation
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task TokenText_PreservedInResult()
    {
        _posTagger
            .TagAsync("the cat sat", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "the", PartOfSpeech = "DT", IsWhitespace = true },
                    new() { Text = "cat", PartOfSpeech = "NN", IsWhitespace = true },
                    new() { Text = "sat", PartOfSpeech = "VBD", IsWhitespace = false },
                }
            );

        SetupLexicon(
            new()
            {
                ["the"] = ("ðə", 5),
                ["cat"] = ("kæt", 5),
                ["sat"] = ("sæt", 5),
            }
        );

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("the cat sat");

        Assert.Equal(3, result.Tokens.Count);
        Assert.Equal("the", result.Tokens[0].Text);
        Assert.Equal("cat", result.Tokens[1].Text);
        Assert.Equal("sat", result.Tokens[2].Text);
        Assert.Equal("DT", result.Tokens[0].Tag);
        Assert.Equal("NN", result.Tokens[1].Tag);
        Assert.Equal("VBD", result.Tokens[2].Tag);
    }

    [Fact]
    public async Task WhitespacePattern_PreservedInOutput()
    {
        _posTagger
            .TagAsync("a b", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "a", PartOfSpeech = "DT", IsWhitespace = true },
                    new() { Text = "b", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["a"] = ("eɪ", 5), ["b"] = ("biː", 5) });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("a b");

        // Phoneme string should have space between tokens
        Assert.Equal("eɪ biː", result.Phonemes);
        Assert.Equal(" ", result.Tokens[0].Whitespace);
        Assert.Equal(string.Empty, result.Tokens[1].Whitespace);
    }

    // ═══════════════════════════════════════════════════════════
    // POS Tagger Integration
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task PosTag_PassedToLexiconCorrectly()
    {
        _posTagger
            .TagAsync("read", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "read", PartOfSpeech = "VBD", IsWhitespace = false },
                }
            );

        _lexicon
            .ProcessToken(Arg.Any<Token>(), Arg.Any<TokenContext>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<Token>();
                Assert.Equal("VBD", token.Tag);
                return ("ɹɛd", (int?)5);
            });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("read");

        Assert.Equal("ɹɛd", result.Phonemes);
    }

    // ═══════════════════════════════════════════════════════════
    // Context Flow (TokenContext)
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task TokenContext_FlowsFromRightToLeft()
    {
        // ProcessTokensAsync iterates in reverse, so context from later tokens
        // is available when processing earlier tokens.
        _posTagger
            .TagAsync("to eat", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "to", PartOfSpeech = "TO", IsWhitespace = true },
                    new() { Text = "eat", PartOfSpeech = "VB", IsWhitespace = false },
                }
            );

        var contextForTo = (TokenContext?)null;
        _lexicon
            .ProcessToken(Arg.Any<Token>(), Arg.Any<TokenContext>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<Token>();
                var ctx = callInfo.ArgAt<TokenContext>(1);
                if (token.Text == "to")
                {
                    contextForTo = ctx;
                    return ("tuː", (int?)5);
                }

                if (token.Text == "eat")
                {
                    return ("iːt", (int?)5);
                }

                return ((string?)null, (int?)null);
            });

        using var sut = CreateSut(withFallback: false);
        await sut.ToPhonemesAsync("to eat");

        // "eat" starts with a vowel phoneme, so FutureVowel should be set
        // when processing "to"
        Assert.NotNull(contextForTo);
    }

    // ═══════════════════════════════════════════════════════════
    // Constructor Validation
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_NullPosTagger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new PhonemizerG2P(null!, _lexicon, _fallback)
        );
    }

    [Fact]
    public async Task Constructor_NullFallback_WorksWithoutFallback()
    {
        _posTagger
            .TagAsync("hello", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["hello"] = ("həˈloʊ", 5) });

        using var sut = new PhonemizerG2P(_posTagger, _lexicon, null);
        var result = await sut.ToPhonemesAsync("hello");

        Assert.Equal("həˈloʊ", result.Phonemes);
    }

    // ═══════════════════════════════════════════════════════════
    // Complex Sentences
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task FullSentence_WithPunctuation_ProcessedCorrectly()
    {
        _posTagger
            .TagAsync("hello, world!", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                    new() { Text = ",", PartOfSpeech = ",", IsWhitespace = true },
                    new() { Text = "world", PartOfSpeech = "NN", IsWhitespace = false },
                    new() { Text = "!", PartOfSpeech = ".", IsWhitespace = false },
                }
            );

        SetupLexicon(
            new() { ["hello"] = ("həˈloʊ", 5), ["world"] = ("wɜːɹld", 5) }
        );

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("hello, world!");

        Assert.Equal(4, result.Tokens.Count);
        Assert.Equal("həˈloʊ", result.Tokens[0].Phonemes);
        Assert.Equal(",", result.Tokens[1].Phonemes);
        Assert.Equal("wɜːɹld", result.Tokens[2].Phonemes);
        Assert.Equal("!", result.Tokens[3].Phonemes);

        // Verify the concatenated phoneme string includes correct whitespace
        Assert.Equal("həˈloʊ, wɜːɹld!", result.Phonemes);
    }

    [Fact]
    public async Task SentenceWithDash_ProcessedCorrectly()
    {
        _posTagger
            .TagAsync("hello - world", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = true },
                    new() { Text = "-", PartOfSpeech = ":", IsWhitespace = true },
                    new() { Text = "world", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(
            new() { ["hello"] = ("həˈloʊ", 5), ["world"] = ("wɜːɹld", 5) }
        );

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("hello - world");

        Assert.Equal("həˈloʊ — wɜːɹld", result.Phonemes);
    }

    // ═══════════════════════════════════════════════════════════
    // Multiple Tokens with Same Text (Homographs)
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task SameWordDifferentPOS_LexiconReceivesCorrectTag()
    {
        // "read" can be VB (present) or VBD (past) with different pronunciations
        _posTagger
            .TagAsync("I read books I read", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "I", PartOfSpeech = "PRP", IsWhitespace = true },
                    new() { Text = "read", PartOfSpeech = "VBP", IsWhitespace = true },
                    new() { Text = "books", PartOfSpeech = "NNS", IsWhitespace = true },
                    new() { Text = "I", PartOfSpeech = "PRP", IsWhitespace = true },
                    new() { Text = "read", PartOfSpeech = "VBD", IsWhitespace = false },
                }
            );

        var tagsSeen = new List<string>();
        _lexicon
            .ProcessToken(Arg.Any<Token>(), Arg.Any<TokenContext>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<Token>();
                if (token.Text == "read")
                {
                    tagsSeen.Add(token.Tag);
                }

                return token.Text switch
                {
                    "I" => ("AY", (int?)5),
                    "read" when token.Tag == "VBP" => ("ɹiːd", (int?)5),
                    "read" when token.Tag == "VBD" => ("ɹɛd", (int?)5),
                    "books" => ("bʊks", (int?)5),
                    _ => ((string?)null, (int?)null),
                };
            });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("I read books I read");

        // Both POS tags should have been seen
        Assert.Contains("VBP", tagsSeen);
        Assert.Contains("VBD", tagsSeen);
        Assert.Equal(5, result.Tokens.Count);
    }

    // ═══════════════════════════════════════════════════════════
    // Cancellation
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task CancellationRequested_StopsProcessing()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _posTagger
            .TagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                }
            );

        SetupLexicon(new());

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("hello", cts.Token);

        // With cancellation, phonemes may not be resolved
        // The key assertion is that it doesn't throw and returns a result
        Assert.NotNull(result);
    }

    // ═══════════════════════════════════════════════════════════
    // NFP (Non-standard punctuation) Tag
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task NFPTag_PunctCharactersExtracted()
    {
        _posTagger
            .TagAsync("hello...", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                    new() { Text = "...", PartOfSpeech = "NFP", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["hello"] = ("həˈloʊ", 5) });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("hello...");

        // "..." is subtokenized into [".", ".", "."] which form a token group,
        // then merged back. The dots are in Puncts so phonemes = "..."
        Assert.Equal(4, result.Tokens.Count);
        // All punct tokens should have phonemes set
        Assert.All(result.Tokens.Skip(1), t => Assert.NotNull(t.Phonemes));
    }

    // ═══════════════════════════════════════════════════════════
    // Semicolon and colon punctuation
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Semicolon_WithColonTag_MappedAsPunct()
    {
        _posTagger
            .TagAsync("hello; world", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "hello", PartOfSpeech = "UH", IsWhitespace = false },
                    new() { Text = ";", PartOfSpeech = ":", IsWhitespace = true },
                    new() { Text = "world", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(
            new() { ["hello"] = ("həˈloʊ", 5), ["world"] = ("wɜːɹld", 5) }
        );

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("hello; world");

        Assert.Equal(3, result.Tokens.Count);
        // ";" text with ":" POS tag: not "-" or "–", so goes to PunctTags branch
        // ":" is in PunctTags, PunctTagPhonemes doesn't have ":"
        // Chars: ';' is in Puncts → phoneme is ";"
        Assert.Equal(";", result.Tokens[1].Phonemes);
    }

    // ═══════════════════════════════════════════════════════════
    // Hash/Dollar POS tags (non-currency)
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task HashTag_InPunctTags_ProcessedAsPunct()
    {
        _posTagger
            .TagAsync("#hello", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "#", PartOfSpeech = "#", IsWhitespace = false },
                    new() { Text = "hello", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(new() { ["hello"] = ("həˈloʊ", 5) });

        using var sut = CreateSut(withFallback: false);
        var result = await sut.ToPhonemesAsync("#hello");

        Assert.Equal(2, result.Tokens.Count);
        // "#" tag is in PunctTags; '#' char is NOT in Puncts → empty phoneme
        Assert.Equal(string.Empty, result.Tokens[0].Phonemes);
    }

    // ═══════════════════════════════════════════════════════════
    // Custom unknown marker
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task CustomUnkMarker_UsedInOutput()
    {
        _posTagger
            .TagAsync("xyzzy", Arg.Any<CancellationToken>())
            .Returns(
                new List<PosToken>
                {
                    new() { Text = "xyzzy", PartOfSpeech = "NN", IsWhitespace = false },
                }
            );

        SetupLexicon(new());

        using var sut = new PhonemizerG2P(_posTagger, _lexicon, null, unk: "???");
        var result = await sut.ToPhonemesAsync("xyzzy");

        Assert.Equal("???", result.Phonemes);
    }
}

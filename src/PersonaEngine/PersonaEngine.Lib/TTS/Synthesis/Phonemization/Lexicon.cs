using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.TTS.Synthesis;

public partial class Lexicon : ILexicon, IDisposable
{
    private static readonly Regex _suffixRegex = SuffixRegex();

    private static readonly Regex _doubleConsonantIngRegex = DoubleConsonantIngRegex();

    private static readonly Regex _vsRegex = VersusRegex();

    private readonly (double Min, double Max) _capStresses = (0.5, 2.0);

    private readonly Lock _dictionaryLock = new();

    private readonly LruCache<string, (string? Phonemes, int? Rating)> _stemCache;

    private readonly IOptionsMonitor<TtsConfiguration> _ttsConfig;

    private readonly IOptionsMonitor<KokoroVoiceOptions> _voiceOptions;

    private readonly IDisposable? _voiceOptionsChangeToken;

    private readonly LruCache<string, (string? Phonemes, int? Rating)> _wordCache;

    private volatile bool _british;

    private volatile IReadOnlyDictionary<string, PhonemeEntry>? _golds;

    private volatile IReadOnlyDictionary<string, PhonemeEntry>? _silvers;

    private volatile HashSet<char> _vocab;

    public Lexicon(
        IOptionsMonitor<TtsConfiguration> ttsConfig,
        IOptionsMonitor<KokoroVoiceOptions> voiceOptions
    )
    {
        _ttsConfig = ttsConfig;
        _voiceOptions = voiceOptions;

        // Initialize caches - size based on typical working set
        _wordCache = new LruCache<string, (string?, int?)>(2048, StringComparer.Ordinal);
        _stemCache = new LruCache<string, (string?, int?)>(512, StringComparer.Ordinal);

        // Initial load of dictionaries
        LoadDictionaries();

        // Register for configuration changes
        _voiceOptionsChangeToken = _voiceOptions.OnChange(options =>
        {
            var currentBritish = options.UseBritishEnglish;
            if (_british != currentBritish)
            {
                LoadDictionaries();
            }
        });
    }

    public void Dispose()
    {
        _voiceOptionsChangeToken?.Dispose();
    }

    public (string? Phonemes, int? Rating) ProcessToken(Token token, TokenContext ctx)
    {
        // Ensure dictionaries are loaded
        if (_golds == null || _silvers == null)
        {
            LoadDictionaries();
        }

        // Normalize text: replace special quotes with straight quotes and normalize Unicode
        var word = (token.Text ?? token.Alias ?? "")
            .Replace('\u2018', '\'')
            .Replace('\u2019', '\'');
        word = word.Normalize(NormalizationForm.FormKC);

        // Calculate stress based on capitalization
        var stress = token.Stress;
        if (stress == null && word != word.ToLower())
        {
            stress = word == word.ToUpper() ? _capStresses.Max : _capStresses.Min;
        }

        // Call GetWord directly - it already handles all the necessary phoneme lookups,
        // stemming, and special cases
        return GetWord(word, token.Tag, stress, ctx, token.IsHead, token.Currency, token.NumFlags);
    }

    private void LoadDictionaries()
    {
        lock (_dictionaryLock)
        {
            var voiceOptions = _voiceOptions.CurrentValue;
            var ttsConfig = _ttsConfig.CurrentValue;
            var british = voiceOptions.UseBritishEnglish;

            // Double-check after acquiring lock to avoid unnecessary reloads
            if (_golds != null && _british == british)
            {
                return;
            }

            // Update settings
            _british = british;
            _vocab = british ? PhonemizerConstants.GbVocab : PhonemizerConstants.UsVocab;

            // Construct paths
            var lexiconPrefix = british ? "gb" : "us";
            var lexiconsRoot = Path.Combine(ttsConfig.ModelDirectory, "kokoro", "lexicons");
            var primaryPath = Path.Combine(lexiconsRoot, $"{lexiconPrefix}_gold.json");
            var secondaryPath = Path.Combine(lexiconsRoot, $"{lexiconPrefix}_silver.json");

            // Use JsonSerializer with custom options
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new PhonemeEntryConverter() },
            };

            // Load dictionaries
            _golds = LoadAndGrowDictionary(primaryPath, options);
            _silvers = LoadAndGrowDictionary(secondaryPath, options);

            // Clear caches when dictionaries change
            _wordCache.Clear();
            _stemCache.Clear();
        }
    }

    private IReadOnlyDictionary<string, PhonemeEntry> LoadAndGrowDictionary(
        string path,
        JsonSerializerOptions options
    )
    {
        using var fileStream = File.OpenRead(path);

        // Deserialize with custom converter
        var rawDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(fileStream);
        if (rawDict == null)
        {
            throw new InvalidOperationException($"Failed to deserialize lexicon from {path}");
        }

        // Convert to strongly typed entries
        var dict = new Dictionary<string, PhonemeEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, element) in rawDict)
        {
            if (key.Length >= 2)
            {
                dict[key] = PhonemeEntry.FromJsonElement(element);
            }
        }

        // Grow the dictionary with additional forms
        var extended = new Dictionary<string, PhonemeEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in dict)
        {
            if (key.Length < 2)
            {
                continue;
            }

            if (key == key.ToLower())
            {
                var capitalized = LexiconUtils.Capitalize(key);
                if (key != capitalized && !dict.ContainsKey(capitalized))
                {
                    extended[capitalized] = value;
                }
            }
            else if (key == LexiconUtils.Capitalize(key.ToLower()))
            {
                var lower = key.ToLower();
                if (!dict.ContainsKey(lower))
                {
                    extended[lower] = value;
                }
            }
        }

        // Combine original and extended entries
        foreach (var (key, value) in extended)
        {
            dict[key] = value;
        }

        // Return as frozen dictionary for performance
        return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (string? Phonemes, int? Rating) GetWord(
        string word,
        string tag,
        double? stress,
        TokenContext ctx,
        bool isHead,
        string? currency,
        string numFlags
    )
    {
        // Cache lookup - create a composite key for context-dependent lookups
        var cacheKey =
            $"{word}|{tag}|{stress}|{ctx.FutureVowel}|{ctx.FutureTo}|{currency}|{numFlags}";
        if (_wordCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        // 1. Check special cases first
        var specialCase = GetSpecialCase(word, tag, stress, ctx);
        if (specialCase.Phonemes != null)
        {
            var finalResult = (AppendCurrency(specialCase.Phonemes, currency), specialCase.Rating);
            _wordCache[cacheKey] = finalResult;

            return finalResult;
        }

        // 2. Check if word is in dictionaries
        if (IsKnown(word, tag))
        {
            var result = Lookup(word, tag, stress, ctx);
            if (result.Phonemes != null)
            {
                var finalResult = (
                    ApplyStress(AppendCurrency(result.Phonemes, currency), stress),
                    result.Rating
                );
                _wordCache[cacheKey] = finalResult;

                return finalResult;
            }
        }

        // 3. Check for apostrophe s at the end (possessives)
        if (EndsWith(word, "s'") && IsKnown(word[..^2] + "'s", tag))
        {
            var result = Lookup(word[..^2] + "'s", tag, stress, ctx);
            if (result.Phonemes != null)
            {
                var finalResult = (AppendCurrency(result.Phonemes, currency), result.Rating);
                _wordCache[cacheKey] = finalResult;

                return finalResult;
            }
        }

        // 4. Check for words ending with apostrophe
        if (EndsWith(word, "'") && IsKnown(word[..^1], tag))
        {
            var result = Lookup(word[..^1], tag, stress, ctx);
            if (result.Phonemes != null)
            {
                var finalResult = (AppendCurrency(result.Phonemes, currency), result.Rating);
                _wordCache[cacheKey] = finalResult;

                return finalResult;
            }
        }

        // 5. Try stemming for -s suffix
        var stemS = StemS(word, tag, stress, ctx);
        if (stemS.Item1 != null)
        {
            var finalResult = (AppendCurrency(stemS.Item1, currency), stemS.Item2);
            _wordCache[cacheKey] = finalResult;

            return finalResult;
        }

        // 6. Try stemming for -ed suffix
        var stemEd = StemEd(word, tag, stress, ctx);
        if (stemEd.Item1 != null)
        {
            var finalResult = (AppendCurrency(stemEd.Item1, currency), stemEd.Item2);
            _wordCache[cacheKey] = finalResult;

            return finalResult;
        }

        // 7. Try stemming for -ing suffix
        var stemIng = StemIng(word, tag, stress ?? 0.5, ctx);
        if (stemIng.Item1 != null)
        {
            var finalResult = (AppendCurrency(stemIng.Item1, currency), stemIng.Item2);
            _wordCache[cacheKey] = finalResult;

            return finalResult;
        }

        // 8. Handle numbers
        if (IsNumber(word, isHead))
        {
            var result = GetNumber(word, currency, isHead, numFlags);
            _wordCache[cacheKey] = result;

            return result;
        }

        // 9. Handle acronyms and capitalized words
        var isAllUpper =
            word.Length > 1 && word == word.ToUpper() && word.ToUpper() != word.ToLower();
        if (isAllUpper)
        {
            var (nnpPhonemes, nnpRating) = GetNNP(word);
            if (nnpPhonemes != null)
            {
                var finalResult = (AppendCurrency(nnpPhonemes, currency), nnpRating);
                _wordCache[cacheKey] = finalResult;

                return finalResult;
            }
        }

        // 10. Try lowercase version if word has some uppercase letters
        if (word != word.ToLower() && (word == word.ToUpper() || word[1..] == word[1..].ToLower()))
        {
            var lowercaseResult = Lookup(word.ToLower(), tag, stress, ctx);
            if (lowercaseResult.Phonemes != null)
            {
                var finalResult = (
                    AppendCurrency(lowercaseResult.Phonemes, currency),
                    lowercaseResult.Rating
                );
                _wordCache[cacheKey] = finalResult;

                return finalResult;
            }
        }

        // No match found
        _wordCache[cacheKey] = (null, null);

        return (null, null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (string? Phonemes, int? Rating) GetSpecialCase(
        string word,
        string tag,
        double? stress,
        TokenContext ctx
    )
    {
        // Handle special cases with optimized lookups
        var wordSpan = word.AsSpan();

        // Special symbols
        if (tag == "ADD" && PhonemizerConstants.AddSymbols.TryGetValue(word, out var addSymbol))
        {
            return Lookup(addSymbol, null, -0.5, ctx);
        }

        if (PhonemizerConstants.Symbols.TryGetValue(word, out var symbol))
        {
            return Lookup(symbol, null, null, ctx);
        }

        if (
            wordSpan.Contains('.')
            && IsAllLettersOrDots(wordSpan)
            && MaxSubstringLength(wordSpan, '.') < 3
        )
        {
            return GetNNP(word);
        }

        switch (word)
        {
            case "a":
            case "A" when tag == "DT":
                return ("ɐ", 4);
            case "I" when tag == "PRP":
                return ($"{PhonemizerConstants.SecondaryStress}I", 4);
            case "am"
            or "Am"
            or "AM" when tag.StartsWith("NN"):
                return GetNNP(word);
            case "am"
            or "Am"
            or "AM" when ctx.FutureVowel != null && word == "am" && stress is not > 0:
                return ("ɐm", 4);
            case "am"
            or "Am"
            or "AM"
                when _golds.TryGetValue("am", out var amEntry)
                    && amEntry is SimplePhonemeEntry simpleAm:
                return (simpleAm.Phoneme, 4);
            case "am"
            or "Am"
            or "AM":
                return ("ɐm", 4);
            case ("an" or "An" or "AN")
            and "AN" when tag.StartsWith("NN"):
                return GetNNP(word);
            case "an"
            or "An"
            or "AN":
                return ("ɐn", 4);
            case "by"
            or "By"
            or "BY" when LexiconUtils.GetParentTag(tag) == "ADV":
                return ("bˈI", 4);
            case "to":
            case "To":
            case "TO" when tag is "TO" or "IN":
                switch (ctx.FutureVowel)
                {
                    case null:
                    {
                        if (
                            _golds.TryGetValue("to", out var toEntry)
                            && toEntry is SimplePhonemeEntry simpleTo
                        )
                        {
                            return (simpleTo.Phoneme, 4);
                        }

                        break;
                    }
                    case false:
                        return ("tə", 4);
                    default:
                        return ("tʊ", 4);
                }

                break;
            case "the":
            case "The":
            case "THE" when tag == "DT":
                return (ctx.FutureVowel == true ? "ði" : "ðə", 4);
        }

        if (tag == "IN" && _vsRegex.IsMatch(word))
        {
            return Lookup("versus", null, null, ctx);
        }

        if (word is "used" or "Used" or "USED")
        {
            if (tag is "VBD" or "JJ" && ctx.FutureTo)
            {
                if (
                    _golds.TryGetValue("used", out var usedEntry)
                    && usedEntry is ContextualPhonemeEntry contextualUsed
                )
                {
                    return (contextualUsed.GetForm("VBD", null), 4);
                }
            }

            if (
                _golds.TryGetValue("used", out var usedDefaultEntry)
                && usedDefaultEntry is ContextualPhonemeEntry contextualDefault
            )
            {
                return (contextualDefault.GetForm("DEFAULT", null), 4);
            }
        }

        return (null, null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsAllLettersOrDots(ReadOnlySpan<char> word)
    {
        foreach (var c in word)
        {
            if (c != '.' && !char.IsLetter(c))
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int MaxSubstringLength(ReadOnlySpan<char> text, char separator)
    {
        var maxLength = 0;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == separator)
            {
                maxLength = Math.Max(maxLength, i - start);
                start = i + 1;
            }
        }

        // Check the last segment
        maxLength = Math.Max(maxLength, text.Length - start);

        return maxLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (string? Phonemes, int? Rating) Lookup(
        string word,
        string? tag,
        double? stress,
        TokenContext ctx
    )
    {
        var isNNP = false;
        if (word == word.ToUpper() && !_golds.ContainsKey(word))
        {
            word = word.ToLower();
            isNNP = tag == "NNP";
        }

        PhonemeEntry? entry = null;
        var rating = 4;

        if (_golds.TryGetValue(word, out entry))
        {
            // Found in gold dictionary
        }
        else if (!isNNP && _silvers.TryGetValue(word, out entry))
        {
            rating = 3;
        }

        if (entry == null)
        {
            if (isNNP)
            {
                return GetNNP(word);
            }

            return (null, null);
        }

        string? phonemes = null;

        switch (entry)
        {
            case SimplePhonemeEntry simple:
                phonemes = simple.Phoneme;

                break;

            case ContextualPhonemeEntry contextual:
                phonemes = contextual.GetForm(tag, ctx);

                break;
        }

        if (phonemes == null || (isNNP && !phonemes.Contains(PhonemizerConstants.PrimaryStress)))
        {
            return GetNNP(word);
        }

        return (ApplyStress(phonemes, stress), rating);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsKnown(string word, string tag)
    {
        if (
            _golds.ContainsKey(word)
            || PhonemizerConstants.Symbols.ContainsKey(word)
            || _silvers.ContainsKey(word)
        )
        {
            return true;
        }

        if (!word.All(c => char.IsLetter(c)) || !word.All(IsValidCharacter))
        {
            return false;
        }

        if (word.Length == 1)
        {
            return true;
        }

        if (word == word.ToUpper() && _golds.ContainsKey(word.ToLower()))
        {
            return true;
        }

        return word.Length > 1 && word[1..] == word[1..].ToUpper();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsValidCharacter(char c)
    {
        // Check if character is valid for lexicon
        return (c >= 'a' && c <= 'z')
            || (c >= 'A' && c <= 'Z')
            || c == '\''
            || c == '-'
            || c == '_';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string ApplyStress(string phonemes, double? stress)
    {
        if (phonemes == null)
        {
            return "";
        }

        if (stress == null)
        {
            return phonemes;
        }

        // Early return conditions
        if ((stress == 0 || stress == 1) && !ContainsStressMarkers(phonemes))
        {
            return phonemes;
        }

        // Remove all stress for very negative stress
        if (stress < -1)
        {
            return phonemes
                .Replace(PhonemizerConstants.PrimaryStress.ToString(), string.Empty)
                .Replace(PhonemizerConstants.SecondaryStress.ToString(), string.Empty);
        }

        // Lower stress level for -1 or 0
        if (
            stress == -1
            || (stress is 0 or -0.5 && phonemes.Contains(PhonemizerConstants.PrimaryStress))
        )
        {
            return phonemes
                .Replace(PhonemizerConstants.SecondaryStress.ToString(), string.Empty)
                .Replace(
                    PhonemizerConstants.PrimaryStress.ToString(),
                    PhonemizerConstants.SecondaryStress.ToString()
                );
        }

        // Add secondary stress for unstressed phonemes
        if (
            stress is 0 or 0.5 or 1
            && !phonemes.Contains(PhonemizerConstants.PrimaryStress)
            && !phonemes.Contains(PhonemizerConstants.SecondaryStress)
        )
        {
            if (!ContainsVowel(phonemes))
            {
                return phonemes;
            }

            return RestressPhonemes(PhonemizerConstants.SecondaryStress + phonemes);
        }

        // Upgrade secondary stress to primary
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

        // Add primary stress for high stress values
        if (
            stress > 1
            && !phonemes.Contains(PhonemizerConstants.PrimaryStress)
            && !phonemes.Contains(PhonemizerConstants.SecondaryStress)
        )
        {
            if (!ContainsVowel(phonemes))
            {
                return phonemes;
            }

            return RestressPhonemes(PhonemizerConstants.PrimaryStress + phonemes);
        }

        return phonemes;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ContainsStressMarkers(string phonemes)
    {
        return phonemes.Contains(PhonemizerConstants.PrimaryStress)
            || phonemes.Contains(PhonemizerConstants.SecondaryStress);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ContainsVowel(string phonemes)
    {
        return phonemes.Any(c => PhonemizerConstants.Vowels.Contains(c));
    }

    private string RestressPhonemes(string phonemes)
    {
        // Optimization: Avoid allocations if there are no stress markers
        if (!ContainsStressMarkers(phonemes))
        {
            return phonemes;
        }

        var chars = phonemes.ToCharArray();
        var charPositions = new List<(int Position, char Char)>(chars.Length);

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool EndsWith(string word, string suffix)
    {
        return word.Length > suffix.Length && word.EndsWith(suffix, StringComparison.Ordinal);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (string?, int?) GetNNP(string word)
    {
        // Early exit for extremely short or long words
        if (word.Length < 1 || word.Length > 20)
        {
            return (null, null);
        }

        // Cache lookups for acronyms
        var cacheKey = $"NNP_{word}";
        if (_wordCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        // Collect phonemes for each letter
        var phoneticLetters = new List<string>();

        foreach (var c in word)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            if (_golds.TryGetValue(c.ToString().ToUpper(), out var letterEntry))
            {
                if (letterEntry is SimplePhonemeEntry simpleEntry)
                {
                    phoneticLetters.Add(simpleEntry.Phoneme);
                }
                else
                {
                    _wordCache[cacheKey] = (null, null);

                    return (null, null);
                }
            }
            else
            {
                _wordCache[cacheKey] = (null, null);

                return (null, null);
            }
        }

        if (phoneticLetters.Count == 0)
        {
            _wordCache[cacheKey] = (null, null);

            return (null, null);
        }

        // Join and apply stress
        var phonemes = string.Join("", phoneticLetters);
        var result = ApplyStress(phonemes, 0);

        // Split by secondary stress and join with primary stress
        // This matches the Python implementation more closely
        var parts = result.Split(PhonemizerConstants.SecondaryStress);
        if (parts.Length > 1)
        {
            // Taking only the last split, as in Python's rsplit(SECONDARY_STRESS, 1)
            var lastIdx = result.LastIndexOf(PhonemizerConstants.SecondaryStress);
            var beginning = result.Substring(0, lastIdx);
            var end = result.Substring(lastIdx + 1);
            result = beginning + PhonemizerConstants.PrimaryStress + end;
        }

        _wordCache[cacheKey] = (result, 3);

        return (result, 3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string? S(string? stem)
    {
        // https://en.wiktionary.org/wiki/-s
        if (string.IsNullOrEmpty(stem))
        {
            return null;
        }

        // Cache result for frequently used stems
        var cacheKey = $"S_{stem}";
        if (_stemCache.TryGetValue(cacheKey, out var cached) && cached.Phonemes != null)
        {
            return cached.Phonemes;
        }

        string? result;
        if ("ptkfθ".Contains(stem[^1]))
        {
            result = stem + "s";
        }
        else if ("szʃʒʧʤ".Contains(stem[^1]))
        {
            result = stem + (_british ? "ɪ" : "ᵻ") + "z";
        }
        else
        {
            result = stem + "z";
        }

        _stemCache[cacheKey] = (result, null);

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (string?, int?) StemS(string word, string tag, double? stress, TokenContext ctx)
    {
        // Cache lookup
        var cacheKey = $"StemS_{word}_{tag}";
        if (_stemCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        string stem;

        if (
            word.Length > 2
            && EndsWith(word, "s")
            && !EndsWith(word, "ss")
            && IsKnown(word[..^1], tag)
        )
        {
            stem = word[..^1];
        }
        else if (
            (
                EndsWith(word, "'s")
                || (word.Length > 4 && EndsWith(word, "es") && !EndsWith(word, "ies"))
            ) && IsKnown(word[..^2], tag)
        )
        {
            stem = word[..^2];
        }
        else if (word.Length > 4 && EndsWith(word, "ies") && IsKnown(word[..^3] + "y", tag))
        {
            stem = word[..^3] + "y";
        }
        else
        {
            _stemCache[cacheKey] = (null, null);

            return (null, null);
        }

        var (stemPhonemes, rating) = Lookup(stem, tag, stress, ctx);
        if (stemPhonemes != null)
        {
            var result = (S(stemPhonemes), rating);
            _stemCache[cacheKey] = result;

            return result;
        }

        _stemCache[cacheKey] = (null, null);

        return (null, null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string? Ed(string? stem)
    {
        // https://en.wiktionary.org/wiki/-ed
        if (string.IsNullOrEmpty(stem))
        {
            return null;
        }

        // Cache result for frequently used stems
        var cacheKey = $"Ed_{stem}";
        if (_stemCache.TryGetValue(cacheKey, out var cached) && cached.Phonemes != null)
        {
            return cached.Phonemes;
        }

        string? result;
        if ("pkfθʃsʧ".Contains(stem[^1]))
        {
            result = stem + "t";
        }
        else if (stem[^1] == 'd')
        {
            result = stem + (_british ? "ɪ" : "ᵻ") + "d";
        }
        else if (stem[^1] != 't')
        {
            result = stem + "d";
        }
        else if (_british || stem.Length < 2)
        {
            result = stem + "ɪd";
        }
        else if (PhonemizerConstants.UsTaus.Contains(stem[^2]))
        {
            result = stem[..^1] + "ɾᵻd";
        }
        else
        {
            result = stem + "ᵻd";
        }

        _stemCache[cacheKey] = (result, null);

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (string?, int?) StemEd(string word, string tag, double? stress, TokenContext ctx)
    {
        // Cache lookup
        var cacheKey = $"StemEd_{word}_{tag}";
        if (_stemCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        string stem;

        if (EndsWith(word, "d") && !EndsWith(word, "dd") && IsKnown(word[..^1], tag))
        {
            stem = word[..^1];
        }
        else if (EndsWith(word, "ed") && !EndsWith(word, "eed") && IsKnown(word[..^2], tag))
        {
            stem = word[..^2];
        }
        else
        {
            _stemCache[cacheKey] = (null, null);

            return (null, null);
        }

        var (stemPhonemes, rating) = Lookup(stem, tag, stress, ctx);
        if (stemPhonemes != null)
        {
            var result = (Ed(stemPhonemes), rating);
            _stemCache[cacheKey] = result;

            return result;
        }

        _stemCache[cacheKey] = (null, null);

        return (null, null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string? Ing(string? stem)
    {
        if (string.IsNullOrEmpty(stem))
        {
            return null;
        }

        // Cache result for frequently used stems
        var cacheKey = $"Ing_{stem}";
        if (_stemCache.TryGetValue(cacheKey, out var cached) && cached.Phonemes != null)
        {
            return cached.Phonemes;
        }

        string? result;
        if (_british)
        {
            if (stem[^1] == 'ə' || stem[^1] == 'ː')
            {
                _stemCache[cacheKey] = (null, null);

                return null;
            }
        }
        else if (
            stem.Length > 1
            && stem[^1] == 't'
            && PhonemizerConstants.UsTaus.Contains(stem[^2])
        )
        {
            result = stem[..^1] + "ɾɪŋ";
            _stemCache[cacheKey] = (result, null);

            return result;
        }

        result = stem + "ɪŋ";
        _stemCache[cacheKey] = (result, null);

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (string?, int?) StemIng(string word, string tag, double stress, TokenContext ctx)
    {
        // Cache lookup
        var cacheKey = $"StemIng_{word}_{tag}";
        if (_stemCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        string stem;

        if (EndsWith(word, "ing") && IsKnown(word[..^3], tag))
        {
            stem = word[..^3];
        }
        else if (EndsWith(word, "ing") && IsKnown(word[..^3] + "e", tag))
        {
            stem = word[..^3] + "e";
        }
        else if (_doubleConsonantIngRegex.IsMatch(word) && IsKnown(word[..^4], tag))
        {
            stem = word[..^4];
        }
        else
        {
            _stemCache[cacheKey] = (null, null);

            return (null, null);
        }

        var (stemPhonemes, rating) = Lookup(stem, tag, stress, ctx);
        if (stemPhonemes != null)
        {
            var result = (Ing(stemPhonemes), rating);
            _stemCache[cacheKey] = result;

            return result;
        }

        _stemCache[cacheKey] = (null, null);

        return (null, null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsNumber(string word, bool isHead)
    {
        // Quick check for any digit
        if (!word.Any(char.IsDigit))
        {
            return false;
        }

        // Check for valid number format with possible suffixes
        var end = word.Length;

        // Check for common suffixes
        foreach (var suffix in PhonemizerConstants.Ordinals)
        {
            if (EndsWith(word, suffix))
            {
                end -= suffix.Length;

                break;
            }
        }

        if (EndsWith(word, "'s"))
        {
            end -= 2;
        }
        else if (EndsWith(word, "s"))
        {
            end -= 1;
        }
        else if (EndsWith(word, "ing"))
        {
            end -= 3;
        }
        else if (EndsWith(word, "'d") || EndsWith(word, "ed"))
        {
            end -= 2;
        }

        // Validate characters in the number portion
        for (var i = 0; i < end; i++)
        {
            var c = word[i];
            if (!(char.IsDigit(c) || c == ',' || c == '.' || (isHead && i == 0 && c == '-')))
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCurrency(string word)
    {
        if (!word.Contains('.'))
        {
            return true;
        }

        if (word.Count(c => c == '.') > 1)
        {
            return false;
        }

        var cents = word.Split('.')[1];

        return cents.Length < 3 || cents.All(c => c == '0');
    }

    private (string?, int?) GetNumber(string word, string? currency, bool isHead, string numFlags)
    {
        // Process suffix (like 'st', 'nd', 'rd', 'th')
        var suffixMatch = _suffixRegex.Match(word);
        var suffix = suffixMatch.Success ? suffixMatch.Value : null;

        if (suffix != null)
        {
            word = word[..^suffix.Length];
        }

        var result = new List<(string Phoneme, int Rating)>();

        // Handle negative numbers
        if (word.StartsWith('-'))
        {
            ExtendResult("minus");
            word = word[1..];
        }

        // Main number processing logic
        if (!isHead && !word.Contains('.'))
        {
            var num = word.Replace(",", "");

            // Handle leading zeros or longer numbers digit by digit
            if (num[0] == '0' || num.Length > 3)
            {
                foreach (var digit in num)
                {
                    if (!ExtendResult(digit.ToString(), false))
                    {
                        return (null, null);
                    }
                }
            }
            // Handle 3-digit numbers that don't end with "00"
            else if (num.Length == 3 && !num.EndsWith("00"))
            {
                // Handle first digit + "hundred"
                if (!ExtendResult(num[0].ToString()))
                {
                    return (null, null);
                }

                if (!ExtendResult("hundred"))
                {
                    return (null, null);
                }

                // Handle remaining digits
                if (num[1] == '0')
                {
                    // Special case: x0y
                    if (!ExtendResult("O", specialRating: -2))
                    {
                        return (null, null);
                    }

                    if (!ExtendResult(num[2].ToString(), false))
                    {
                        return (null, null);
                    }
                }
                else
                {
                    // Handle last two digits as a number
                    if (!ExtendNum(int.Parse(num.Substring(1, 2)), false))
                    {
                        return (null, null);
                    }
                }
            }
            else
            {
                // Standard number conversion
                if (!ExtendNum(int.Parse(num)))
                {
                    return (null, null);
                }
            }
        }
        else if (word.Count(c => c == '.') > 1 || !isHead)
        {
            // Handle multiple decimal points or non-head position
            var parts = word.Replace(",", "").Split('.');
            var isFirst = true;

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                if (part[0] == '0' || (part.Length != 2 && part.Any(n => n != '0')))
                {
                    // Handle digit by digit
                    foreach (var digit in part)
                    {
                        if (!ExtendResult(digit.ToString(), false))
                        {
                            return (null, null);
                        }
                    }
                }
                else
                {
                    // Standard number conversion
                    if (!ExtendNum(int.Parse(part), isFirst))
                    {
                        return (null, null);
                    }
                }

                isFirst = false;
            }
        }
        else if (
            currency != null
            && PhonemizerConstants.Currencies.TryGetValue(currency, out var currencyPair)
            && IsCurrency(word)
        )
        {
            // Parse the parts
            var parts = word.Replace(",", "")
                .Split('.')
                .Select(p => string.IsNullOrEmpty(p) ? 0 : int.Parse(p))
                .ToList();

            while (parts.Count < 2)
            {
                parts.Add(0);
            }

            // Filter out zero parts
            var nonZeroParts = parts
                .Take(2)
                .Select(
                    (value, index) =>
                        (Value: value, Unit: index == 0 ? currencyPair.Dollar : currencyPair.Cent)
                )
                .Where(p => p.Value != 0)
                .ToList();

            for (var i = 0; i < nonZeroParts.Count; i++)
            {
                var (value, unit) = nonZeroParts[i];

                if (i > 0 && !ExtendResult("and"))
                {
                    return (null, null);
                }

                // Convert number to words
                if (!ExtendNum(value, i == 0))
                {
                    return (null, null);
                }

                // Add currency unit
                if (Math.Abs(value) != 1 && unit != "pence")
                {
                    var (unitPhonemes, unitRating) = StemS(unit + "s", null, null, null);
                    if (unitPhonemes != null)
                    {
                        result.Add((unitPhonemes, unitRating.Value));
                    }
                    else
                    {
                        return (null, null);
                    }
                }
                else
                {
                    if (!ExtendResult(unit))
                    {
                        return (null, null);
                    }
                }
            }
        }
        else
        {
            // Standard number handling for most cases
            string wordsForm;

            if (int.TryParse(word.Replace(",", ""), out var number))
            {
                // Choose conversion type
                var to = "cardinal";
                if (suffix != null && PhonemizerConstants.Ordinals.Contains(suffix))
                {
                    to = "ordinal";
                }
                else if (result.Count == 0 && word.Length == 4)
                {
                    to = "year";
                }

                wordsForm = Num2Words.Convert(number, to);
            }
            else if (double.TryParse(word.Replace(",", ""), out var numberDouble))
            {
                if (word.StartsWith("."))
                {
                    // Handle ".xx" format
                    wordsForm =
                        "point "
                        + string.Join(
                            " ",
                            word[1..].Select(c => Num2Words.Convert(int.Parse(c.ToString())))
                        );
                }
                else
                {
                    wordsForm = Num2Words.Convert(numberDouble);
                }
            }
            else
            {
                return (null, null);
            }

            // Process the words form
            if (!ExtendNum(wordsForm, escape: true))
            {
                return (null, null);
            }
        }

        if (result.Count == 0)
        {
            return (null, null);
        }

        // Join the phonemes and handle suffix
        var combinedPhoneme = string.Join(" ", result.Select(r => r.Phoneme));
        var minRating = result.Min(r => r.Rating);

        // Apply suffix transformations
        return suffix switch
        {
            "s" or "'s" => (S(combinedPhoneme), minRating),
            "ed" or "'d" => (Ed(combinedPhoneme), minRating),
            "ing" => (Ing(combinedPhoneme), minRating),
            _ => (combinedPhoneme, minRating),
        };

        // Helper method to extend number words
        bool ExtendNum(object num, bool first = true, bool escape = false)
        {
            var wordsForm = escape ? num.ToString()! : Num2Words.Convert(Convert.ToInt32(num));
            var splits = wordsForm.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < splits.Length; i++)
            {
                var w = splits[i];

                if (w != "and" || numFlags.Contains('&'))
                {
                    if (
                        first
                        && i == 0
                        && splits.Length > 1
                        && w == "one"
                        && numFlags.Contains('a')
                    )
                    {
                        result.Add(("ə", 4));
                    }
                    else
                    {
                        double? specialRating = w == "point" ? -2.0 : null;
                        var (wordPhoneme, wordRating) = Lookup(w, null, specialRating, null);
                        if (wordPhoneme != null)
                        {
                            result.Add((wordPhoneme, wordRating.Value));
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
                else if (w == "and" && numFlags.Contains('n') && result.Count > 0)
                {
                    // Append "ən" to the previous word
                    var last = result[^1];
                    result[^1] = (last.Phoneme + "ən", last.Rating);
                }
            }

            return true;
        }

        // Helper method to add a single word result
        bool ExtendResult(string word, bool first = true, double? specialRating = null)
        {
            var (phoneme, rating) = Lookup(word, null, specialRating, null);
            if (phoneme != null)
            {
                result.Add((phoneme, rating.Value));

                return true;
            }

            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string AppendCurrency(string phonemes, string? currency)
    {
        if (string.IsNullOrEmpty(currency))
        {
            return phonemes;
        }

        if (!PhonemizerConstants.Currencies.TryGetValue(currency, out var currencyPair))
        {
            return phonemes;
        }

        var (stemPhonemes, _) = StemS(currencyPair.Dollar + "s", null, null, null);
        if (stemPhonemes != null)
        {
            return $"{phonemes} {stemPhonemes}";
        }

        return phonemes;
    }

    [GeneratedRegex(@"(?i)vs\.?$", RegexOptions.Compiled, "en-BE")]
    private static partial Regex VersusRegex();

    [GeneratedRegex(@"([bcdgklmnprstvxz])\1ing$|cking$", RegexOptions.Compiled)]
    private static partial Regex DoubleConsonantIngRegex();

    [GeneratedRegex(@"[a-z']+$", RegexOptions.Compiled)]
    private static partial Regex SuffixRegex();
}

public class LruCache<TKey, TValue>(int capacity, IEqualityComparer<TKey> comparer)
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<LruCacheItem>> _cacheMap = new(
        capacity,
        comparer
    );

    private readonly LinkedList<LruCacheItem> _lruList = new();

    public TValue this[TKey key]
    {
        get
        {
            if (_cacheMap.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);

                return node.Value.Value;
            }

            throw new KeyNotFoundException($"Key {key} not found in cache");
        }
        set
        {
            if (_cacheMap.TryGetValue(key, out var existingNode))
            {
                _lruList.Remove(existingNode);
                _lruList.AddFirst(existingNode);
                existingNode.Value.Value = value;

                return;
            }

            if (_cacheMap.Count >= capacity)
            {
                var last = _lruList.Last;
                if (last != null)
                {
                    _cacheMap.Remove(last.Value.Key);
                    _lruList.RemoveLast();
                }
            }

            var cacheItem = new LruCacheItem(key, value);
            var newNode = new LinkedListNode<LruCacheItem>(cacheItem);
            _lruList.AddFirst(newNode);
            _cacheMap.Add(key, newNode);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(TKey key, out TValue value)
    {
        if (_cacheMap.TryGetValue(key, out var node))
        {
            _lruList.Remove(node);
            _lruList.AddFirst(node);

            value = node.Value.Value;

            return true;
        }

        value = default!;

        return false;
    }

    public void Clear()
    {
        _cacheMap.Clear();
        _lruList.Clear();
    }

    private record LruCacheItem(TKey Key, TValue Value)
    {
        public TValue Value { get; set; } = Value;
    }
}

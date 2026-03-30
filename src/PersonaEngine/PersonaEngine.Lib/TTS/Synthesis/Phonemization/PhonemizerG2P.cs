namespace PersonaEngine.Lib.TTS.Synthesis;

public class PhonemizerG2P : IPhonemizer
{
    private readonly IPosTagger _posTagger;

    private readonly TextPreprocessor _preprocessor;

    private readonly PhonemeResolver _resolver;

    private readonly TokenRestructurer _restructurer;

    private readonly string _unk;

    public PhonemizerG2P(
        IPosTagger posTagger,
        ILexicon lexicon,
        IFallbackPhonemizer? fallback = null,
        string unk = "❓"
    )
    {
        _posTagger = posTagger ?? throw new ArgumentNullException(nameof(posTagger));
        _unk = unk;

        _preprocessor = new TextPreprocessor();
        _restructurer = new TokenRestructurer(_preprocessor, unk);
        _resolver = new PhonemeResolver(lexicon, fallback, unk);
    }

    public async Task<PhonemeResult> ToPhonemesAsync(
        string text,
        CancellationToken cancellationToken = default
    )
    {
        // 1. Preprocess text
        var preprocessed = _preprocessor.Preprocess(text);

        // 2. Tag text with POS tagger
        var posTokens = await _posTagger.TagAsync(preprocessed.ProcessedText, cancellationToken);

        // 3. Convert to internal token representation
        var tokens = _restructurer.ConvertToTokens(posTokens);

        // 4. Apply features from preprocessing
        TokenAligner.ApplyFeatures(tokens, preprocessed.Tokens, preprocessed.Features);

        // 5. Fold left (merge non-head tokens with previous token)
        tokens = _restructurer.FoldLeft(tokens);

        // 6. Retokenize (split complex tokens and handle special cases)
        var retokenizedTokens = _restructurer.Retokenize(tokens);

        // 7. Process phonemes using lexicon and fallback
        var ctx = new TokenContext();
        await _resolver.ProcessTokensAsync(retokenizedTokens, ctx, cancellationToken);

        // 8. Merge retokenized tokens
        var mergedTokens = _restructurer.MergeRetokenizedTokens(retokenizedTokens);

        // 9. Generate final phoneme string
        var phonemes = string.Concat(mergedTokens.Select(t => (t.Phonemes ?? _unk) + t.Whitespace));

        return new PhonemeResult(phonemes, mergedTokens);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

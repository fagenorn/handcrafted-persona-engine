namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
/// Type-safe discriminated union for retokenized items:
/// either a single resolved token or a group of subtokens requiring joint phoneme resolution.
/// </summary>
internal readonly struct RetokenizedItem
{
    private readonly Token? _single;

    private readonly List<Token>? _group;

    public bool IsSingleToken => _single != null;

    public bool IsTokenGroup => _group != null;

    public Token SingleToken =>
        _single
        ?? throw new InvalidOperationException("Item is a token group, not a single token.");

    public List<Token> TokenGroup =>
        _group ?? throw new InvalidOperationException("Item is a single token, not a token group.");

    private RetokenizedItem(Token? single, List<Token>? group)
    {
        _single = single;
        _group = group;
    }

    public static RetokenizedItem FromToken(Token token) => new(token, null);

    public static RetokenizedItem FromGroup(List<Token> group) => new(null, group);
}

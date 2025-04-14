namespace PersonaEngine.Lib.Core.Conversation.Policies;

/// <summary>
///     The default output formatting strategy, which performs no modifications.
/// </summary>
public class DefaultOutputFormattingStrategy : IOutputFormattingStrategy
{
    public string Format(string rawContent, IReadOnlyDictionary<string, object>? requestMetadata = null) { return rawContent; }
}
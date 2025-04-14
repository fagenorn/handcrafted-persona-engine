namespace PersonaEngine.Lib.Core.Conversation.Policies;

/// <summary>
///     Defines a strategy for formatting raw assistant response content before output.
/// </summary>
public interface IOutputFormattingStrategy
{
    /// <summary>
    ///     Formats the raw aggregated response content.
    /// </summary>
    /// <param name="rawContent">The raw aggregated response from the LLM.</param>
    /// <param name="requestMetadata">Optional: Metadata that might influence formatting (e.g., target platform hint).</param>
    /// <returns>The formatted string to be used for output.</returns>
    string Format(string rawContent, IReadOnlyDictionary<string, object>? requestMetadata = null);
}
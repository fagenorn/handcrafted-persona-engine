namespace PersonaEngine.Lib.Core.Conversation.Context;

/// <summary>
/// Manages the conversational context, including history and external information like visual context.
/// </summary>
public interface IContextManager
{
    /// <summary>
    /// Adds a completed interaction (user utterance or assistant response) to the history.
    /// </summary>
    /// <param name="interaction">The interaction to add.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    Task AddInteractionAsync(Interaction interaction);

    /// <summary>
    /// Retrieves the conversation history.
    /// </summary>
    /// <param name="sourceId">Optional: Filter history relevant to a specific source ID.</param>
    /// <param name="maxTurns">Optional: Limit the number of turns returned (most recent first).</param>
    /// <returns>An enumerable collection of interactions representing the history.</returns>
    Task<IEnumerable<Interaction>> GetConversationHistoryAsync(string? sourceId = null, int? maxTurns = null);

    /// <summary>
    /// Gets a snapshot of the current context suitable for providing to the LLM.
    /// </summary>
    /// <param name="sourceId">Optional: The source ID requesting the context (may influence history filtering).</param>
    /// <returns>A snapshot containing history, visual context, topics, etc.</returns>
    Task<LlmContextSnapshot> GetContextSnapshotAsync(string? sourceId = null);
}
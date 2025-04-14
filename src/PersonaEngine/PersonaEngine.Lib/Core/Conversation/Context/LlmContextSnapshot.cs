namespace PersonaEngine.Lib.Core.Conversation.Context;

/// <summary>
/// Represents a snapshot of the context to be provided to the LLM.
/// </summary>
/// <param name="History">Recent conversation history.</param>
/// <param name="ScreenCaption">Current screen caption, if available.</param>
/// <param name="Topics">Relevant topics for the conversation.</param>
/// <param name="SystemContext">General system prompt or context description.</param>
public record LlmContextSnapshot(
    IEnumerable<Interaction> History,
    string? ScreenCaption,
    IEnumerable<string> Topics,
    string SystemContext
);
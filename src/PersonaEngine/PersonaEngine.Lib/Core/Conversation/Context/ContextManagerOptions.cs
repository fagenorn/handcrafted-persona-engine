namespace PersonaEngine.Lib.Core.Conversation.Context;

public class ContextManagerOptions
{
    /// <summary>
    /// Default system context/prompt to provide to the LLM.
    /// </summary>
    public string SystemContext { get; set; } = "You are a helpful assistant.";

    /// <summary>
    /// Default conversation topics.
    /// </summary>
    public List<string> Topics { get; set; } = ["general conversation"];

    /// <summary>
    /// Maximum number of interaction turns to keep in the history.
    /// Set to 0 or negative for unlimited (within memory constraints).
    /// </summary>
    public int MaxHistoryTurns { get; set; } = 50;
}
namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;

public record ConversationContextOptions
{
    public string? SystemPromptFile { get; set; }

    public string? SystemPrompt { get; set; }

    /// <summary>
    ///     When true, uses <see cref="SystemPrompt" /> (inline). When false, uses
    ///     <see cref="SystemPromptFile" />. Defaults to false (file-based).
    /// </summary>
    public bool UseCustomPrompt { get; set; }

    public string? CurrentContext { get; set; }

    public List<string> Topics { get; set; } = new();
}

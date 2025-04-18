namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;

public record ConversationContextOptions
{
    public string? SystemPromptFile { get; set; }

    public string? SystemPrompt { get; set; }

    public string? CurrentContext { get; set; }

    public List<string> Topics { get; set; } = new();
}
using Microsoft.SemanticKernel;

namespace PersonaEngine.Lib.LLM;

public record ChatMessage(string User, string Content);

public record InjectionMetadata(IEnumerable<string> Topics, string Context, string VisualContext);

public record VisualChatMessage(string Query, ReadOnlyMemory<byte> ImageData);

public interface IChatEngine : IDisposable
{
    IChatHistoryManager HistoryManager { get; }

    IAsyncEnumerable<string> GetStreamingChatResponseAsync(
        ChatMessage              userInput,
        InjectionMetadata?       injectionMetadata = null,
        PromptExecutionSettings? executionSettings = null,
        CancellationToken        cancellationToken = default);

    IAsyncEnumerable<string> GetStreamingChatResponseWithHistoryAsync(
        ChatMessage              userInput,
        IChatHistoryManager      historyManager,
        InjectionMetadata?       injectionMetadata = null,
        PromptExecutionSettings? executionSettings = null,
        CancellationToken        cancellationToken = default);
}

public interface IVisualChatEngine : IDisposable
{
    IAsyncEnumerable<string> GetStreamingChatResponseAsync(
        VisualChatMessage        userInput,
        PromptExecutionSettings? executionSettings = null,
        CancellationToken        cancellationToken = default);
}
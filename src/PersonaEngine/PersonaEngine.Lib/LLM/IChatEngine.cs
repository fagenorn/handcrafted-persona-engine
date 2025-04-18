using System.Threading.Channels;

using Microsoft.SemanticKernel;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;

namespace PersonaEngine.Lib.LLM;

public record VisualChatMessage(string Query, ReadOnlyMemory<byte> ImageData);

public interface IChatEngine : IDisposable
{
    Task<CompletionReason> GetStreamingChatResponseAsync(
        IConversationContext                 context,
        ChannelWriter<IOutputEvent> outputWriter,
        Guid                        turnId,
        Guid                        sessionId,
        PromptExecutionSettings?    executionSettings = null,
        CancellationToken           cancellationToken = default);
}

public interface IVisualChatEngine : IDisposable
{
    IAsyncEnumerable<string> GetStreamingChatResponseAsync(
        VisualChatMessage        userInput,
        PromptExecutionSettings? executionSettings = null,
        CancellationToken        cancellationToken = default);
}
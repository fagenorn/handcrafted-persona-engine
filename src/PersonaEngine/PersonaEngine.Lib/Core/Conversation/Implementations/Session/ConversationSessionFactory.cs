using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Implementations.Context;
using PersonaEngine.Lib.Core.Conversation.Implementations.Metrics;
using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

public class ConversationSessionFactory(
    ILoggerFactory             loggerFactory,
    IChatEngine                chatEngine,
    ITtsEngine                 ttsEngine,
    IEnumerable<IInputAdapter> inputAdapters,
    IOutputAdapter             outputAdapter,
    ConversationMetrics        metrics)
    : IConversationSessionFactory
{
    public IConversationSession CreateSession(ConversationContext context, ConversationOptions? options = null, Guid? sessionId = null)
    {
        var logger = loggerFactory.CreateLogger<ConversationSession>();

        return new ConversationSession(
                                       logger,
                                       chatEngine,
                                       ttsEngine,
                                       inputAdapters,
                                       outputAdapter,
                                       metrics,
                                       sessionId ?? Guid.NewGuid(),
                                       options ?? new ConversationOptions(),
                                       context
                                      );
    }
}
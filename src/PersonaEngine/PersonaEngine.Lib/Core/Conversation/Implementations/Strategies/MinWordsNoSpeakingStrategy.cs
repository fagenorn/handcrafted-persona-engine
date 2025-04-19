using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Strategies;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Input;
using PersonaEngine.Lib.Utils;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Strategies;

public class MinWordsNoSpeakingStrategy : IBargeInStrategy
{
    public bool ShouldAllowBargeIn(BargeInContext context)
    {
        if ( context.CurrentState == ConversationState.Speaking )
        {
            return false;
        }

        switch ( context.InputEvent )
        {
            case SttSegmentRecognizing segmentRecognizing:
            {
                var wordCount = segmentRecognizing.PartialTranscript.GetWordCount();

                return wordCount >= context.ConversationOptions.BargeInMinWords;
            }
            case SttSegmentRecognized segmentRecognized:
            {
                var wordCount = segmentRecognized.FinalTranscript.GetWordCount();

                return wordCount >= context.ConversationOptions.BargeInMinWords;
            }
            default:
                return false;
        }
    }
}
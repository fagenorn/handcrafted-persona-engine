namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

public enum ConversationState
{
    Initial,
    
    Initializing,

    Idle,

    Listening,

    ActiveTurn,
    
    ProcessingInput,

    WaitingForLlm,

    StreamingResponse,
    
    Speaking,

    Paused,

    Interrupted,

    Error,

    Ended
}
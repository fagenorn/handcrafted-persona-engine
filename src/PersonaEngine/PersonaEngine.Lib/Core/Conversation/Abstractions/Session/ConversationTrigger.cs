namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

public enum ConversationTrigger
{
    InitializeRequested,
    
    InitializeComplete,
    
    StopRequested,
    
    PauseRequested,
    
    ResumeRequested,
    
    InputDetected,
    
    InputFinalized,
    
    LlmRequestSent,
    
    LlmStreamStarted,
    
    LlmStreamChunkReceived,
    
    LlmStreamEnded,
    
    TtsRequestSent,
    
    TtsStreamStarted,
    
    TtsStreamChunkReceived,

    TtsStreamEnded,
    
    AudioStreamStarted,
    
    AudioStreamEnded,
    
    ErrorOccurred,
}
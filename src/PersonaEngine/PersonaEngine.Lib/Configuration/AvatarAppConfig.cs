using PersonaEngine.Lib.Core.Conversation.Context;
using PersonaEngine.Lib.Core.Conversation.Detection;
using PersonaEngine.Lib.Core.Conversation.Transcription;

namespace PersonaEngine.Lib.Configuration;

public record AvatarAppConfig
{
    public WindowConfiguration Window { get; set; } = new();

    public LlmOptions Llm { get; set; } = new();

    public TtsConfiguration Tts { get; set; } = new();

    public AsrConfiguration Asr { get; set; } = new();

    public MicrophoneConfiguration Microphone { get; set; } = new();

    public SubtitleOptions Subtitle { get; set; } = new();

    public Live2DOptions Live2D { get; set; } = new();

    public SpoutConfiguration[] SpoutConfigs { get; set; } = [];

    public VisionConfig Vision { get; set; } = new();

    public RouletteWheelOptions RouletteWheel { get; set; } = new();
    
    public BargeInDetectorOptions BargeInDetector { get; set; } = new();
    
    public TranscriptionServiceOptions[]  InputAdapters { get; set; } = [];
    
    public ContextManagerOptions ContextManager { get; set; } = new();
}
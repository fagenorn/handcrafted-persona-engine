#pragma warning disable SKEXP0001

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.SemanticKernel;

using PersonaEngine.Lib.ASR.Transcriber;
using PersonaEngine.Lib.ASR.VAD;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.Core;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Input;
using PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Output;
using PersonaEngine.Lib.Core.Conversation.Implementations.Metrics;
using PersonaEngine.Lib.Core.Conversation.Implementations.Session;
using PersonaEngine.Lib.Live2D;
using PersonaEngine.Lib.Live2D.Behaviour;
using PersonaEngine.Lib.Live2D.Behaviour.Emotion;
using PersonaEngine.Lib.Live2D.Behaviour.LipSync;
using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.TTS.Audio;
using PersonaEngine.Lib.TTS.Profanity;
using PersonaEngine.Lib.TTS.RVC;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.UI;
using PersonaEngine.Lib.UI.Common;
using PersonaEngine.Lib.UI.GUI;
using PersonaEngine.Lib.UI.RouletteWheel;
using PersonaEngine.Lib.UI.Text.Subtitles;
using PersonaEngine.Lib.Vision;

namespace PersonaEngine.Lib;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApp(this IServiceCollection services, IConfiguration configuration, Action<IKernelBuilder>? configureKernel = null)
    {
        services.Configure<AvatarAppConfig>(configuration.GetSection("Config"));

        services.AddConversation(configuration, configureKernel);
        services.AddUI(configuration);
        services.AddLive2D(configuration);
        services.AddSystemAudioPlayer();

        services.AddSingleton<AvatarApp>();

        OrtEnv.Instance().EnvLogLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;

        return services;
    }

    public static IServiceCollection AddConversation(this IServiceCollection services, IConfiguration configuration, Action<IKernelBuilder>? configureKernel = null)
    {
        services.AddASRSystem(configuration);
        services.AddTTSSystem(configuration);
        services.AddRVC(configuration);
#pragma warning disable SKEXP0010
        services.AddLLM(configuration, configureKernel);
#pragma warning restore SKEXP0010
        services.AddChatEngineSystem(configuration);

        services.AddConversationPipeline(configuration);

        services.AddSingleton<ProfanityDetector>();

        return services;
    }

    public static IServiceCollection AddConversationPipeline(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ConversationMetrics>();

        services.AddSingleton<IInputAdapter, MicrophoneInputAdapter>();

        services.AddSingleton<IConversationSessionFactory, ConversationSessionFactory>();
        services.AddSingleton<IConversationOrchestrator, ConversationOrchestrator>();

        return services;
    }

    public static IServiceCollection AddASRSystem(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AsrConfiguration>(configuration.GetSection("Config:Asr"));
        services.Configure<MicrophoneConfiguration>(configuration.GetSection("Config:Microphone"));

        services.AddSingleton<IVadDetector>(sp =>
                                            {
                                                var asrOptions = sp.GetRequiredService<IOptions<AsrConfiguration>>().Value;

                                                var siletroOptions = new SileroVadOptions(ModelUtils.GetModelPath(ModelType.Silero)) { Threshold = asrOptions.VadThreshold, ThresholdGap = asrOptions.VadThresholdGap };

                                                var vadOptions = new VadDetectorOptions { MinSpeechDuration = TimeSpan.FromMilliseconds(asrOptions.VadMinSpeechDuration), MinSilenceDuration = TimeSpan.FromMilliseconds(asrOptions.VadMinSilenceDuration) };

                                                return new SileroVadDetector(vadOptions, siletroOptions);
                                            });

        services.AddSingleton<IRealtimeSpeechTranscriptor>(sp =>
                                                           {
                                                               var asrOptions = sp.GetRequiredService<IOptions<AsrConfiguration>>().Value;

                                                               var realtimeSpeechTranscriptorOptions = new RealtimeSpeechTranscriptorOptions {
                                                                                                                                                 AutodetectLanguageOnce        = false,                    // Flag to detect the language only once or for each segment
                                                                                                                                                 IncludeSpeechRecogizingEvents = true,                     // Flag to include speech recognizing events (RealtimeSegmentRecognizing)
                                                                                                                                                 RetrieveTokenDetails          = false,                    // Flag to retrieve token details
                                                                                                                                                 LanguageAutoDetect            = false,                    // Flag to auto-detect the language
                                                                                                                                                 Language                      = new CultureInfo("en-US"), // Language to use for transcription
                                                                                                                                                 Prompt                        = asrOptions.TtsPrompt,
                                                                                                                                                 Template                      = asrOptions.TtsMode
                                                                                                                                             };

                                                               var realTimeOptions = new RealtimeOptions();

                                                               return new RealtimeTranscriptor(
                                                                                               new WhisperSpeechTranscriptorFactory(ModelUtils.GetModelPath(ModelType.WhisperGgmlTurbov3)),
                                                                                               sp.GetRequiredService<IVadDetector>(),
                                                                                               new WhisperSpeechTranscriptorFactory(ModelUtils.GetModelPath(ModelType.WhisperGgmlTiny)),
                                                                                               realtimeSpeechTranscriptorOptions,
                                                                                               realTimeOptions,
                                                                                               sp.GetRequiredService<ILogger<RealtimeTranscriptor>>());
                                                           });

        services.AddSingleton<IMicrophone, MicrophoneInputNAudioSource>();
        services.AddSingleton<IAwaitableAudioSource>(sp => sp.GetRequiredService<IMicrophone>());

        return services;
    }

    public static IServiceCollection AddSystemAudioPlayer(this IServiceCollection services)
    {
        services.AddSingleton<IOutputAdapter, PortaudioOutputAdapter>();
        services.AddSingleton<IAudioProgressNotifier, AudioProgressNotifier>();

        return services;
    }

    public static IServiceCollection AddChatEngineSystem(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ChatEngineOptions>(options => { options.SystemPrompt = File.ReadAllText(PromptUtils.GetModelPath(Promptype.Personality)); });

        services.AddSingleton<IChatHistoryManagerFactory, ChatHistoryManagerFactory>();
        services.AddSingleton<IChatHistoryManager>(sp => sp.GetRequiredService<IChatHistoryManagerFactory>().Create());
        services.AddSingleton<IChatEngine, SemanticKernelChatEngine>();
        services.AddSingleton<IVisualChatEngine, VisualQASemanticKernelChatEngine>();
        services.AddSingleton<IVisualQAService, VisualQAService>();
        services.AddSingleton<WindowCaptureService>();

        return services;
    }

    [Experimental("SKEXP0010")]
    public static IServiceCollection AddLLM(this IServiceCollection services, IConfiguration configuration, Action<IKernelBuilder>? configureKernel = null)
    {
        services.Configure<LlmOptions>(configuration.GetSection("Config:Llm"));

        services.AddSingleton(sp =>
                              {
                                  var llmOptions    = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
                                  var kernelBuilder = Kernel.CreateBuilder();

                                  kernelBuilder.AddOpenAIChatCompletion(llmOptions.TextModel, new Uri(llmOptions.TextEndpoint), llmOptions.TextApiKey, serviceId: "text");
                                  kernelBuilder.AddOpenAIChatCompletion(llmOptions.VisionEndpoint, new Uri(llmOptions.VisionEndpoint), llmOptions.VisionApiKey, serviceId: "vision");

                                  configureKernel?.Invoke(kernelBuilder);

                                  return kernelBuilder.Build();
                              });

        services.AddSingleton<ITextFilter, NameTextFilter>();

        return services;
    }

    public static IServiceCollection AddTTSSystem(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        // Add configuration
        services.Configure<TtsConfiguration>(configuration.GetSection("Config:Tts"));
        services.Configure<KokoroVoiceOptions>(configuration.GetSection("Config:Tts:Voice"));

        // Add core TTS components
        services.AddSingleton<ITtsEngine, TtsEngine>();

        // Add text processing components
        services.AddSingleton<ITextProcessor, TextProcessor>();
        services.AddSingleton<ITextNormalizer, TextNormalizer>();
        services.AddSingleton<ISentenceSegmenter, SentenceSegmenter>();
        services.AddSingleton<IMlSentenceDetector>(provider =>
                                                   {
                                                       var logger        = provider.GetRequiredService<ILogger<OpenNlpSentenceDetector>>();
                                                       var modelProvider = provider.GetRequiredService<IModelProvider>();
                                                       var basePath      = modelProvider.GetModelAsync(TTS.Synthesis.ModelType.OpenNLPDir).GetAwaiter().GetResult().Path;
                                                       var modelPath     = Path.Combine(basePath, "EnglishSD.nbin");

                                                       return new OpenNlpSentenceDetector(modelPath, logger);
                                                   });

        // Add phoneme processing components
        services.AddSingleton<IPhonemizer>(provider =>
                                           {
                                               var posTagger = provider.GetRequiredService<IPosTagger>();
                                               var lexicon   = provider.GetRequiredService<ILexicon>();
                                               var fallback  = provider.GetRequiredService<IFallbackPhonemizer>();

                                               return new PhonemizerG2P(posTagger, lexicon, fallback);
                                           });

        services.AddSingleton<IPosTagger>(provider =>
                                          {
                                              var logger        = provider.GetRequiredService<ILogger<OpenNlpPosTagger>>();
                                              var modelProvider = provider.GetRequiredService<IModelProvider>();

                                              var basePath  = modelProvider.GetModelAsync(TTS.Synthesis.ModelType.OpenNLPDir).GetAwaiter().GetResult().Path;
                                              var modelPath = Path.Combine(basePath, "EnglishPOS.nbin");

                                              return new OpenNlpPosTagger(modelPath, logger);
                                          });

        services.AddSingleton<ILexicon, Lexicon>();
        services.AddSingleton<IFallbackPhonemizer, EspeakFallbackPhonemizer>();

        services.AddSingleton<IAudioSynthesizer, OnnxAudioSynthesizer>();
        services.AddSingleton<IModelProvider>(provider =>
                                              {
                                                  var config = provider.GetRequiredService<IOptions<TtsConfiguration>>().Value;
                                                  var logger = provider.GetRequiredService<ILogger<FileModelProvider>>();

                                                  return new FileModelProvider(config.ModelDirectory, logger);
                                              });

        services.AddSingleton<IKokoroVoiceProvider, KokoroVoiceProvider>();
        services.AddSingleton<ITtsCache, TtsMemoryCache>();
        services.AddSingleton<IAudioFilter, BlacklistAudioFilter>();

        return services;
    }

    public static IServiceCollection AddUI(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        services.Configure<SubtitleOptions>(configuration.GetSection("Config:Subtitle"));
        services.Configure<RouletteWheelOptions>(configuration.GetSection("Config:RouletteWheel"));

        services.AddSingleton<IRenderComponent, SubtitleRenderer>();
        services.AddSingleton<RouletteWheel>();
        services.AddSingleton<IRenderComponent>(x => x.GetRequiredService<RouletteWheel>());
        services.AddConfigEditor();

        services.AddSingleton<FontProvider>();
        services.AddSingleton<IStartupTask>(x => x.GetRequiredService<FontProvider>());

        return services;
    }

    public static IServiceCollection AddLive2D(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        services.Configure<Live2DOptions>(configuration.GetSection("Config:Live2D"));

        services.AddSingleton<IRenderComponent, Live2DManager>();
        services.AddSingleton<ILive2DAnimationService, VBridgerLipSyncService>();
        services.AddSingleton<ILive2DAnimationService, IdleBlinkingAnimationService>();
        services.AddEmotionProcessing(configuration);

        return services;
    }

    public static IServiceCollection AddConfigEditor(this IServiceCollection services)
    {
        services.AddSingleton<IUiConfigurationManager, UiConfigurationManager>();
        services.AddSingleton<IEditorStateManager, EditorStateManager>();
        services.AddSingleton<IConfigSectionRegistry, ConfigSectionRegistry>();
        services.AddSingleton<IUiThemeManager, UiThemeManager>();
        services.AddSingleton<INotificationService, NotificationService>();

        services.AddSingleton<IRenderComponent, ConfigEditorComponent>();

        services.AddSingleton<TtsConfigEditor>();
        services.AddSingleton<RouletteWheelEditor>();
        services.AddSingleton<ChatEditor>();
        services.AddSingleton<MicrophoneConfigEditor>();

        services.AddSingleton<IStartupTask, ConfigSectionRegistrationTask>();

        return services;
    }

    public static IServiceCollection AddRVC(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RVCFilterOptions>(configuration.GetSection("Config:Tts:Rvc"));

        services.AddSingleton<IRVCVoiceProvider, RVCVoiceProvider>();
        services.AddSingleton<IAudioFilter, RVCFilter>();

        return services;
    }

    public static IServiceCollection AddEmotionProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IEmotionService, EmotionService>();
        services.AddSingleton<ITextFilter, EmotionProcessor>();
        services.AddSingleton<IAudioFilter, EmotionAudioFilter>();
        services.AddSingleton<ILive2DAnimationService, EmotionAnimationService>();

        return services;
    }
}
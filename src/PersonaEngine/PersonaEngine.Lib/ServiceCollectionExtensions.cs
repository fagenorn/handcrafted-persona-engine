#pragma warning disable SKEXP0001

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.SemanticKernel;
using PersonaEngine.Lib.ASR.Transcriber;
using PersonaEngine.Lib.ASR.VAD;
using PersonaEngine.Lib.Assets;
using PersonaEngine.Lib.Assets.Manifest;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.Core;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Input;
using PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Output;
using PersonaEngine.Lib.Core.Conversation.Implementations.Metrics;
using PersonaEngine.Lib.Core.Conversation.Implementations.Session;
using PersonaEngine.Lib.Health;
using PersonaEngine.Lib.Health.Probes;
using PersonaEngine.Lib.IO;
using PersonaEngine.Lib.Live2D;
using PersonaEngine.Lib.Live2D.Behaviour;
using PersonaEngine.Lib.Live2D.Behaviour.Emotion;
using PersonaEngine.Lib.Live2D.Behaviour.LipSync;
using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.LLM.Connection;
using PersonaEngine.Lib.Logging;
using PersonaEngine.Lib.TTS.Audio;
using PersonaEngine.Lib.TTS.Profanity;
using PersonaEngine.Lib.TTS.RVC;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.TTS.Synthesis.Alignment;
using PersonaEngine.Lib.TTS.Synthesis.Audio;
using PersonaEngine.Lib.TTS.Synthesis.Engine;
using PersonaEngine.Lib.TTS.Synthesis.Kokoro;
using PersonaEngine.Lib.TTS.Synthesis.LipSync;
using PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;
using PersonaEngine.Lib.TTS.Synthesis.LipSync.VBridger;
using PersonaEngine.Lib.TTS.Synthesis.Qwen3;
using PersonaEngine.Lib.TTS.Synthesis.TextProcessing;
using PersonaEngine.Lib.UI;
using PersonaEngine.Lib.UI.Common;
using PersonaEngine.Lib.UI.ControlPanel;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Avatar;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Avatar.Sections;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Listening;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Listening.Sections;
using PersonaEngine.Lib.UI.ControlPanel.Panels.LlmConnection;
using PersonaEngine.Lib.UI.ControlPanel.Panels.LlmConnection.Sections;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Personality;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Personality.Sections;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Subtitles;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Subtitles.Sections;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Audition;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Models;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Sections;
using PersonaEngine.Lib.UI.ControlPanel.Services;
using PersonaEngine.Lib.UI.ControlPanel.Threading;
using PersonaEngine.Lib.UI.ControlPanel.Visuals;
using PersonaEngine.Lib.UI.Host;
using PersonaEngine.Lib.UI.Overlay;
using PersonaEngine.Lib.UI.Rendering.RouletteWheel;
using PersonaEngine.Lib.UI.Rendering.Subtitles;
using PersonaEngine.Lib.Vision;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using Dashboard = PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Dashboard;
using SentenceSegmenter = PersonaEngine.Lib.TTS.Synthesis.TextProcessing.SentenceSegmenter;

namespace PersonaEngine.Lib;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApp(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IKernelBuilder>? configureKernel = null
    )
    {
        services.Configure<AvatarAppConfig>(configuration.GetSection("Config"));

        // IAssetCatalog must be registered before AddConversation so subsystem gates
        // (RVC, Vision, Audio2Face) can probe IsFeatureEnabled during DI registration.
        services.AddSingleton<InstallManifest>(_ => ManifestLoader.LoadEmbedded());
        services.AddSingleton<IAssetCatalog>(sp =>
            AssetCatalogFactory.Build(sp.GetRequiredService<InstallManifest>())
        );

        services.AddConversation(configuration, configureKernel);
        services.AddUI(configuration);
        services.AddLive2D(configuration);
        services.AddSystemAudioPlayer();
        services.AddPolly(configuration);

        services.AddSingleton<AvatarApp>();

        OrtEnv.Instance().EnvLogLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;

        return services;
    }

    public static IServiceCollection AddConversation(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IKernelBuilder>? configureKernel = null
    )
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

    public static IServiceCollection AddConversationPipeline(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddSingleton<ConversationMetrics>();

        services.AddSingleton<IInputAdapter, MicrophoneInputAdapter>();

        services.AddSingleton<IConversationInputGate, ConversationInputGate>();
        services.AddSingleton<IMicMuteController, MicMuteController>();
        services.AddSingleton<IConversationSessionFactory, ConversationSessionFactory>();
        services.AddSingleton<IConversationOrchestrator, ConversationOrchestrator>();

        return services;
    }

    public static IServiceCollection AddASRSystem(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<AsrConfiguration>(configuration.GetSection("Config:Asr"));
        services.Configure<MicrophoneConfiguration>(configuration.GetSection("Config:Microphone"));

        services.AddSingleton<IVadDetector>(sp =>
        {
            var asrOptions = sp.GetRequiredService<IOptions<AsrConfiguration>>().Value;

            var siletroOptions = new SileroVadOptions(ModelUtils.GetModelPath(ModelType.Silero))
            {
                Threshold = asrOptions.VadThreshold,
                ThresholdGap = asrOptions.VadThresholdGap,
            };

            var vadOptions = new VadDetectorOptions
            {
                MinSpeechDuration = TimeSpan.FromMilliseconds(asrOptions.VadMinSpeechDuration),
                MinSilenceDuration = TimeSpan.FromMilliseconds(asrOptions.VadMinSilenceDuration),
            };

            return new SileroVadDetector(vadOptions, siletroOptions);
        });

        services.AddSingleton<IRealtimeSpeechTranscriptor>(sp =>
        {
            var asrOptions = sp.GetRequiredService<IOptions<AsrConfiguration>>().Value;

            var realtimeSpeechTranscriptorOptions = new RealtimeSpeechTranscriptorOptions
            {
                AutodetectLanguageOnce = false,
                IncludeSpeechRecogizingEvents = false,
                RetrieveTokenDetails = false,
                LanguageAutoDetect = false,
                Language = new CultureInfo("en-US"),
                Prompt = asrOptions.TtsPrompt,
                Template = asrOptions.TtsMode,
            };

            var realTimeOptions = new RealtimeOptions();

            return new RealtimeTranscriptor(
                new WhisperSpeechTranscriptorFactory(
                    ModelUtils.GetModelPath(ModelType.WhisperGgmlTurbov3)
                ),
                sp.GetRequiredService<IVadDetector>(),
                new WhisperSpeechTranscriptorFactory(
                    ModelUtils.GetModelPath(ModelType.WhisperGgmlTiny)
                ),
                realtimeSpeechTranscriptorOptions,
                realTimeOptions,
                sp.GetRequiredService<ILogger<RealtimeTranscriptor>>()
            );
        });

        services.AddSingleton<IMicrophone, MicrophoneInputNAudioSource>();
        services.AddSingleton<IAwaitableAudioSource>(sp => sp.GetRequiredService<IMicrophone>());

        services.AddSingleton<IMicrophoneAmplitudeProvider, MicrophoneAmplitudeProvider>();
        services.AddSingleton<IVadProbabilityProvider, VadProbabilityProvider>();

        return services;
    }

    public static IServiceCollection AddSystemAudioPlayer(this IServiceCollection services)
    {
        services.AddSingleton<IOutputAdapter, PortaudioOutputAdapter>();
        services.AddSingleton<IAudioProgressNotifier, AudioProgressNotifier>();
        services.AddSingleton<IAudioAmplitudeProvider, AudioAmplitudeProvider>();

        return services;
    }

    public static IServiceCollection AddChatEngineSystem(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<ConversationOptions>(configuration.GetSection("Config:Conversation"));
        services.Configure<ConversationContextOptions>(
            configuration.GetSection("Config:ConversationContext")
        );

        services.AddSingleton<IChatEngine, SemanticKernelChatEngine>();
        services.AddSingleton<IVisualChatEngine, VisualQASemanticKernelChatEngine>();
        services.AddSingleton<IVisualQAService, VisualQAService>();
        services.AddSingleton<WindowCaptureService>();

        return services;
    }

    [Experimental("SKEXP0010")]
    public static IServiceCollection AddLLM(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IKernelBuilder>? configureKernel = null
    )
    {
        services.Configure<LlmOptions>(configuration.GetSection("Config:Llm"));

        // Named client so IHttpClientFactory pools/rotates the SocketsHttpHandler
        // (default 2-minute lifetime) — avoids DNS pinning from a long-lived
        // HttpClient singleton while keeping the 5-second probe timeout.
        services.AddHttpClient(
            LlmConnectionProbe.HttpClientName,
            c => c.Timeout = TimeSpan.FromSeconds(5)
        );
        services.AddSingleton<ILlmConnectionProbe, LlmConnectionProbe>();
        services.AddSingleton<IKernelReloadCoordinator, KernelReloadCoordinator>();
        services.AddSingleton<ILlmKernelProvider>(sp => new LlmKernelProvider(
            sp.GetRequiredService<IOptionsMonitor<LlmOptions>>(),
            sp.GetRequiredService<IKernelReloadCoordinator>(),
            configureKernel,
            sp.GetRequiredService<ILogger<LlmKernelProvider>>()
        ));
        // Chat engines take a Lazy<ILlmKernelProvider> to break the singleton
        // resolution cycle: LlmKernelProvider → IKernelReloadCoordinator →
        // IConversationOrchestrator → IConversationSessionFactory → IChatEngine
        // → ILlmKernelProvider. The Lazy defers resolution to first use, which
        // happens after the DI graph is fully constructed.
        services.AddSingleton<Lazy<ILlmKernelProvider>>(sp => new Lazy<ILlmKernelProvider>(
            sp.GetRequiredService<ILlmKernelProvider>
        ));

        services.AddSingleton<ITextFilter, NameTextFilter>();

        return services;
    }

    public static IServiceCollection AddTTSSystem(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // Configuration
        services.Configure<TtsConfiguration>(configuration.GetSection("Config:Tts"));
        services.Configure<KokoroVoiceOptions>(configuration.GetSection("Config:Tts:Kokoro"));
        services.Configure<Qwen3TtsOptions>(configuration.GetSection("Config:Tts:Qwen3"));

        // Shared model provider
        services.AddSingleton<IModelProvider>(provider =>
        {
            var config = provider.GetRequiredService<IOptions<TtsConfiguration>>().Value;
            var logger = provider.GetRequiredService<ILogger<FileModelProvider>>();

            return new FileModelProvider(config.ModelDirectory, logger);
        });

        // Shared forced aligner (CTC) — engine-agnostic word-level timing
        services.AddSingleton<IForcedAligner>(provider =>
        {
            var modelProvider = provider.GetRequiredService<IModelProvider>();
            var logger = provider.GetRequiredService<ILogger<CtcForcedAligner>>();

            return new CtcForcedAligner(modelProvider, logger);
        });

        // Shared text processing
        services.AddSingleton<SentenceProcessor>();
        services.AddSingleton<ITextNormalizer, TextNormalizer>();
        services.AddSingleton<ISentenceSegmenter, SentenceSegmenter>();
        services.AddSingleton<IMlSentenceDetector>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<OpenNlpSentenceDetector>>();
            var modelProvider = provider.GetRequiredService<IModelProvider>();
            var basePath = modelProvider.GetModelPath(IO.ModelType.OpenNlp.Directory);
            var modelPath = Path.Combine(basePath, "EnglishSD.nbin");

            return new OpenNlpSentenceDetector(modelPath, logger);
        });

        // Shared phoneme infrastructure (used by Kokoro directly + orchestrator enrichment)
        services.AddSingleton<IPhonemizer>(provider =>
        {
            var posTagger = provider.GetRequiredService<IPosTagger>();
            var lexicon = provider.GetRequiredService<ILexicon>();
            var fallback = provider.GetRequiredService<IFallbackPhonemizer>();

            return new PhonemizerG2P(posTagger, lexicon, fallback);
        });

        services.AddSingleton<IPosTagger>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<OpenNlpPosTagger>>();
            var modelProvider = provider.GetRequiredService<IModelProvider>();
            var basePath = modelProvider.GetModelPath(IO.ModelType.OpenNlp.Directory);
            var modelPath = Path.Combine(basePath, "EnglishPOS.nbin");

            return new OpenNlpPosTagger(modelPath, logger);
        });

        services.AddSingleton<ILexicon, Lexicon>();
        services.AddSingleton<IFallbackPhonemizer, EspeakFallbackPhonemizer>();

        // Kokoro-specific
        services.AddSingleton<IKokoroVoiceProvider, KokoroVoiceProvider>();
        services.AddSingleton<IQwen3VoiceProvider, Qwen3VoiceProvider>();

        // Engine registrations
        services.AddSingleton<ISentenceSynthesizer, KokoroSentenceSynthesizer>();
        services.AddSingleton<ISentenceSynthesizer, Qwen3SentenceSynthesizer>();

        // Runtime engine switching
        services.AddSingleton<ITtsEngineProvider, TtsEngineProvider>();

        // Top-level orchestrator (replaces TtsEngine)
        services.AddSingleton<ITtsEngine, TtsOrchestrator>();

        services.AddSingleton<ITtsCache, TtsMemoryCache>();
        services.AddSingleton<IAudioFilter, BlacklistAudioFilter>();

        // Lip sync
        services.Configure<LipSyncOptions>(configuration.GetSection("Config:LipSync"));
        services.Configure<Audio2FaceOptions>(
            configuration.GetSection("Config:LipSync:Audio2Face")
        );
        services.AddSingleton<ILipSyncProcessor, VBridgerLipSyncProcessor>();
        services.AddSingleton<ILipSyncProcessor, Audio2FaceLipSyncProcessor>();
        services.AddSingleton<ILipSyncProcessorProvider, LipSyncProcessorProvider>();

        return services;
    }

    public static IServiceCollection AddUI(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<SubtitleOptions>(configuration.GetSection("Config:Subtitle"));
        services.Configure<RouletteWheelOptions>(configuration.GetSection("Config:RouletteWheel"));

        services.AddSingleton<IRenderComponent, SubtitleRenderer>();
        services.AddSingleton<RouletteWheel>();
        services.AddSingleton<IRenderComponent>(x => x.GetRequiredService<RouletteWheel>());
        services.AddSingleton<IUiThreadDispatcher, UiThreadDispatcher>();
        services.AddControlPanel();

        services.AddSingleton<FontProvider>();
        services.AddSingleton<IStartupTask>(x => x.GetRequiredService<FontProvider>());

        services.AddSingleton<WindowManager>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<AvatarAppConfig>>().Value.Window;
            return new WindowManager(
                new Silk.NET.Maths.Vector2D<int>(config.Width, config.Height),
                new Silk.NET.Maths.Vector2D<int>(config.MinWidth, config.MinHeight),
                config.Title
            );
        });

        services.AddSingleton<OverlayHost>();

        return services;
    }

    public static IServiceCollection AddLive2D(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<Live2DOptions>(configuration.GetSection("Config:Live2D"));

        services.AddSingleton<IRenderComponent, Live2DManager>();
        services.AddSingleton<ILive2DAnimationService, LipSyncAnimationService>();
        services.AddSingleton<ILive2DAnimationService, IdleBlinkingAnimationService>();
        services.AddEmotionProcessing(configuration);

        return services;
    }

    public static IServiceCollection AddControlPanel(this IServiceCollection services)
    {
        // Config writer — discovers section paths from AvatarAppConfig type hierarchy
        services.AddSingleton<IConfigWriter>(sp => new ConfigWriter(
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            logger: sp.GetRequiredService<ILogger<ConfigWriter>>()
        ));

        // Layout components
        services.AddSingleton<PresenceOrb>();
        services.AddSingleton<StatusBar>();
        services.AddSingleton<ControlBar>();
        services.AddSingleton<TitleBar>();

        // Persona state bridge for UI effects
        services.AddSingleton<PersonaStateProvider>();
        services.AddSingleton<AmbientRenderer>();
        services.AddSingleton<WindowFrameGlow>();
        services.AddSingleton<IUiSoundEmitter, NoOpUiSoundEmitter>();

        // Voice panel sections
        services.AddSingleton<VoiceModeSelector>();
        services.AddSingleton<VoiceCard>();
        services.AddSingleton<VoiceGallery>();
        services.AddSingleton<DeliverySection>();
        services.AddSingleton<CloneLayerSection>();
        services.AddSingleton<AdvancedSection>();

        // Voice metadata
        services.AddSingleton<VoiceMetadataCatalog>();

        // Voice audition pipeline
        services.AddSingleton<OneShotAudioPlayer>();
        services.AddSingleton<IOneShotPlayer>(sp => sp.GetRequiredService<OneShotAudioPlayer>());
        services.AddSingleton<IRvcAuditionProcessor, RvcAuditionProcessor>();
        services.AddSingleton<IVoiceAuditionService, VoiceAuditionService>();

        // Nav request bus — lets dashboard health cards + cross-panel hint
        // links drive the active sidebar section.
        services.AddSingleton<NavRequestBus>();
        services.AddSingleton<INavRequestBus>(sp => sp.GetRequiredService<NavRequestBus>());

        // Subsystem health probes — registration order defines dashboard card order.
        services.AddSingleton<MicrophoneHealthProbe>();
        services.AddSingleton<ISubsystemHealthProbe>(sp =>
            sp.GetRequiredService<MicrophoneHealthProbe>()
        );
        services.AddSingleton<LlmHealthProbe>();
        services.AddSingleton<ISubsystemHealthProbe>(sp => sp.GetRequiredService<LlmHealthProbe>());
        services.AddSingleton<TtsHealthProbe>();
        services.AddSingleton<ISubsystemHealthProbe>(sp => sp.GetRequiredService<TtsHealthProbe>());

        // Dashboard panel sections + services
        services.AddSingleton<SessionStatsCollector>();
        services.AddSingleton<PresenceStripSection>();
        services.AddSingleton<SystemHealthSection>();
        services.AddSingleton<TranscriptSection>();
        services.AddSingleton<ControlsSection>();
        services.AddSingleton<SessionStatsSection>();

        // Panels
        services.AddSingleton<Dashboard>();
        services.AddSingleton<VoicePanel>();
        // Personality panel sections
        services.AddSingleton<PromptSourceSection>();
        services.AddSingleton<CurrentVibeSection>();
        services.AddSingleton<TopicsSection>();
        services.AddSingleton<PersonalityPanel>();
        services.AddSingleton<MicrophoneDeviceSection>();
        services.AddSingleton<SpeechDetectionSection>();
        services.AddSingleton<RecognitionSection>();
        services.AddSingleton<InterruptionSection>();
        services.AddSingleton<LiveMeterWidget>();
        services.AddSingleton<ListeningPanel>();
        // Avatar panel sections
        services.AddSingleton<ModelSection>();
        services.AddSingleton<LipSyncSection>();
        services.AddSingleton<AvatarPanel>();
        // Subtitles panel sections
        services.AddSingleton<SubtitlePreviewRenderer>();
        services.AddSingleton<PreviewSection>();
        services.AddSingleton<TextStyleSection>();
        services.AddSingleton<ColorsSection>();
        services.AddSingleton<PlacementSection>();
        services.AddSingleton<CanvasSection>();
        services.AddSingleton<SubtitlesPanel>();
        services.AddSingleton<OverlayPanel>();
        services.AddSingleton<RouletteWheelPanel>();
        services.AddSingleton<ScreenAwareness>();
        services.AddSingleton<Streaming>();
        services.AddSingleton<TextLlmSection>();
        services.AddSingleton<VisionLlmSection>();
        services.AddSingleton<LlmConnectionPanel>();
        services.AddSingleton<Application>();

        // Shell — registered as IRenderComponent
        services.AddSingleton<IRenderComponent, ControlPanelComponent>();

        return services;
    }

    public static IServiceCollection AddRVC(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<RVCFilterOptions>(configuration.GetSection("Config:Tts:Rvc"));

        services.AddSingleton<IRVCVoiceProvider, RVCVoiceProvider>();
        services.AddSingleton<RVCFilter>();
        services.AddSingleton<IAudioFilter>(sp => sp.GetRequiredService<RVCFilter>());

        return services;
    }

    public static IServiceCollection AddEmotionProcessing(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddSingleton<IEmotionService, EmotionService>();
        services.AddSingleton<ITextFilter, EmotionProcessor>();
        services.AddSingleton<IAudioFilter, EmotionAudioFilter>();
        services.AddSingleton<ILive2DAnimationService, EmotionAnimationService>();

        return services;
    }

    public static IServiceCollection AddPolly(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddResiliencePipeline(
            "semantickernel-chat",
            pipelineBuilder =>
            {
                pipelineBuilder
                    .AddTimeout(TimeSpan.FromSeconds(60))
                    .AddRetry(
                        new RetryStrategyOptions
                        {
                            Name = "ChatServiceRetry",
                            ShouldHandle = new PredicateBuilder().Handle<Exception>(ex =>
                                ex is HttpRequestException
                                || ex is TimeoutRejectedException
                                || (
                                    ex is KernelException ke
                                    && ke.Message.Contains(
                                        "transient",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                )
                            ),
                            Delay = TimeSpan.FromSeconds(2),
                            BackoffType = DelayBackoffType.Exponential,
                            MaxDelay = TimeSpan.FromSeconds(30),
                            MaxRetryAttempts = 3,
                            UseJitter = true,
                            OnRetry = args =>
                            {
                                var sessionId = args.Context.Properties.TryGetValue(
                                    ResilienceKeys.SessionId,
                                    out var x
                                )
                                    ? x
                                    : Guid.Empty;
                                var logger = args.Context.Properties.TryGetValue(
                                    ResilienceKeys.Logger,
                                    out var y
                                )
                                    ? y
                                    : NullLogger.Instance;
                                logger.LogWarning(
                                    "Request failed/stopped for session {SessionId}. Retrying in {Timespan}. Attempt {RetryAttempt}...",
                                    sessionId,
                                    args.Duration,
                                    args.AttemptNumber
                                );

                                return ValueTask.CompletedTask;
                            },
                        }
                    );
            }
        );

        return services;
    }
}

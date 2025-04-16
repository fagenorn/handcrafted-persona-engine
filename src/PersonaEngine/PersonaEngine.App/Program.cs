using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PersonaEngine.Lib;
using PersonaEngine.Lib.ASR.Transcriber;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Input;
using PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Output;
using PersonaEngine.Lib.Core.Conversation.Implementations.Session;
using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.TTS.Synthesis;

using Serilog;
using Serilog.Events;

namespace PersonaEngine.App;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var builder = new ConfigurationBuilder()
                      .SetBasePath(Directory.GetCurrentDirectory())
                      .AddJsonFile("appsettings.json", false, true);

        IConfiguration config   = builder.Build();
        var            services = new ServiceCollection();

        services.AddLogging(loggingBuilder =>
                            {
                                CreateLogger();
                                loggingBuilder.AddSerilog();
                            });

        services.AddApp(config);

        var serviceProvider = services.BuildServiceProvider();

        //     public ConversationSession(ILogger logger, IConversationContext context, Guid sessionId, ConversationOptions options, IChatEngine chatEngine, ITtsEngine ttsEngine, IAggregatedStreamingAudioPlayer audioPlayer)

        var mic     = serviceProvider.GetRequiredService<IMicrophone>();
        var rt      = serviceProvider.GetRequiredService<IRealtimeSpeechTranscriptor>();
        var logger2 = serviceProvider.GetRequiredService<ILogger<MicrophoneInputAdapter>>();
        var ce      = serviceProvider.GetRequiredService<IChatEngine>();
        var te      = serviceProvider.GetRequiredService<ITtsEngine>();
        // var sp      = serviceProvider.GetRequiredService<IAggregatedStreamingAudioPlayer>();
        var logger  = serviceProvider.GetRequiredService<ILogger<ConversationSession>>();
        var logge3  = serviceProvider.GetRequiredService<ILogger<PortaudioOutputAdapter>>();
        var options = new ConversationOptions();
        var au      = new PortaudioOutputAdapter(logge3);

        var conversationSession = new ConversationSession(logger, Guid.NewGuid(), options, ce, te, [new MicrophoneInputAdapter(mic, rt, logger2)], au);
        await conversationSession.RunAsync(CancellationToken.None);

        Console.ReadLine();

        // var window = serviceProvider.GetRequiredService<AvatarApp>();
        // window.Run();

        await serviceProvider.DisposeAsync();
    }

    private static void CreateLogger()
    {
        Log.Logger = new LoggerConfiguration()
                     .MinimumLevel.Warning()
                     .MinimumLevel.Override("PersonaEngine.Lib.Core.CC", LogEventLevel.Information)
                     .Enrich.FromLogContext()
                     .Enrich.With<GuidToEmojiEnricher>()
                     .WriteTo.Console(
                                      outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                                     )
                     .CreateLogger();
    }
}
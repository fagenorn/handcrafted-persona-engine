using LLama.Native;
using Microsoft.ML.OnnxRuntime;
using Serilog;
using Serilog.Events;

namespace PersonaEngine.App;

/// <summary>
///     Centralised Serilog + native-runtime log wiring for the App host.
///     Separated from <see cref="Program" /> so startup ordering and log overrides live
///     in one place instead of being scattered through <c>Main</c>.
/// </summary>
internal static class LoggingConfiguration
{
    /// <summary>
    ///     Minimum levels per source context. Kept in one table so adding a new subsystem
    ///     override is a single-line change instead of a fluent chain edit.
    /// </summary>
    private static readonly (string SourceContext, LogEventLevel Level)[] SourceOverrides =
    [
        ("PersonaEngine.Lib.Core.Conversation", LogEventLevel.Information),
        ("PersonaEngine.Lib.TTS.Synthesis.Alignment", LogEventLevel.Information),
        ("PersonaEngine.Lib.TTS.Synthesis.LipSync", LogEventLevel.Information),
        ("PersonaEngine.Lib.TTS.Synthesis.Engine.SentenceProcessor", LogEventLevel.Information),
        ("Startup", LogEventLevel.Information),
        ("PersonaEngine.Lib.UI.Overlay", LogEventLevel.Information),
        ("llama.cpp", LogEventLevel.Error),
    ];

    /// <summary>
    ///     Initialises <see cref="Log.Logger" /> with console sink and per-subsystem level
    ///     overrides. Safe to call exactly once at process startup.
    /// </summary>
    public static void ConfigureSerilog()
    {
        var config = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .Enrich.FromLogContext()
            .Enrich.With<GuidToEmojiEnricher>()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            );

        foreach (var (source, level) in SourceOverrides)
        {
            config = config.MinimumLevel.Override(source, level);
        }

        Log.Logger = config.CreateLogger();
    }

    /// <summary>
    ///     Wires Serilog into the AppDomain-level unhandled-exception and process-exit
    ///     events so we get a final fatal log line even on crashes escaping the main loop.
    /// </summary>
    public static void InstallGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += static (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "Unhandled exception (terminating={Terminating})", e.IsTerminating);
            }
            else
            {
                Log.Fatal("Unhandled non-Exception object: {Object}", e.ExceptionObject);
            }

            Log.CloseAndFlush();
        };

        AppDomain.CurrentDomain.ProcessExit += static (_, _) => Log.CloseAndFlush();
    }

    /// <summary>
    ///     Silences ONNX Runtime warnings globally before any <c>InferenceSession</c> is created.
    ///     ORT's default verbosity duplicates diagnostics we already capture at the model-loader level.
    /// </summary>
    public static void SuppressOnnxRuntimeWarnings()
    {
        OrtEnv.Instance().EnvLogLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;
    }

    /// <summary>
    ///     Bridges LLamaSharp's native log callback into Serilog under the <c>llama.cpp</c>
    ///     source context. Must be invoked before any LLama inference to capture load-time diagnostics.
    /// </summary>
    public static void BridgeLlamaLogging()
    {
        var llamaLogger = Log.ForContext("SourceContext", "llama.cpp");
        NativeLibraryConfig.LLama.WithLogCallback(
            (level, message) =>
            {
                var msg = message.TrimEnd('\n', '\r');
                if (string.IsNullOrEmpty(msg))
                {
                    return;
                }

                switch (level)
                {
                    case LLamaLogLevel.Error:
                        llamaLogger.Error("{Message}", msg);
                        // ggml/llama call abort() shortly after emitting an error. Flush the
                        // managed Console buffer synchronously so the line survives the crash.
                        FlushConsoleBestEffort();
                        break;
                    case LLamaLogLevel.Warning:
                        llamaLogger.Warning("{Message}", msg);
                        break;
                    case LLamaLogLevel.Info:
                        llamaLogger.Information("{Message}", msg);
                        break;
                    case LLamaLogLevel.Debug:
                        llamaLogger.Debug("{Message}", msg);
                        break;
                }
            }
        );
    }

    private static void FlushConsoleBestEffort()
    {
        try
        {
            Console.Out.Flush();
            Console.Error.Flush();
        }
        catch
        {
            // Best-effort flush on a pre-abort path; swallow any I/O failures.
        }
    }
}

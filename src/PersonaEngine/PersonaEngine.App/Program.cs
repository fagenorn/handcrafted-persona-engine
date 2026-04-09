using System.Runtime.InteropServices;
using System.Text;
using LLama.Native;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.OnnxRuntime;
using PersonaEngine.Lib;
using PersonaEngine.Lib.Core;
using Serilog;
using Serilog.Events;

namespace PersonaEngine.App;

internal static class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

    private const uint LoadWithAlteredSearchPath = 0x00000008;

    private static async Task Main()
    {
        var nativeDir = Path.Combine(AppContext.BaseDirectory, "native");
        if (Directory.Exists(nativeDir))
        {
            SetDllDirectory(nativeDir);
        }

        Console.OutputEncoding = Encoding.UTF8;

        // LLamaSharp CUDA12 backend: ggml.dll imports ggml-cpu.dll which isn't in the
        // cuda12/ dir. Pre-load it from the avx2 dir, then load the CUDA12 chain.
        var llamaNativeDir = Path.Combine(
            AppContext.BaseDirectory,
            "runtimes",
            "win-x64",
            "native"
        );
        var llamaCudaDir = Path.Combine(llamaNativeDir, "cuda12");
        var llamaDll = Path.Combine(llamaCudaDir, "llama.dll");
        if (File.Exists(llamaDll))
        {
            // ggml-cpu.dll is in the avx2/ (or avx/) sibling directory
            var cpuDll = Path.Combine(llamaNativeDir, "avx2", "ggml-cpu.dll");
            if (!File.Exists(cpuDll))
            {
                cpuDll = Path.Combine(llamaNativeDir, "avx", "ggml-cpu.dll");
            }

            if (File.Exists(cpuDll))
            {
                LoadLibraryEx(cpuDll, IntPtr.Zero, LoadWithAlteredSearchPath);
            }

            if (LoadLibraryEx(llamaDll, IntPtr.Zero, LoadWithAlteredSearchPath) == IntPtr.Zero)
            {
                throw new DllNotFoundException(
                    $"Failed to load {llamaDll}. Check that all native dependencies are present."
                );
            }

            NativeLibraryConfig.LLama.WithLibrary(llamaDll);
        }

        var llamaLogger = Log.ForContext("SourceContext", "llama.cpp");
        NativeLibraryConfig.LLama.WithLogCallback(
            (level, message) =>
            {
                var msg = message.TrimEnd('\n', '\r');
                if (string.IsNullOrEmpty(msg))
                    return;

                switch (level)
                {
                    case LLamaLogLevel.Error:
                        llamaLogger.Error("{Message}", msg);
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

        CreateLogger();

        // Suppress ONNX Runtime warnings globally before any sessions are created
        OrtEnv.Instance().EnvLogLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;

        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", false, true);

        IConfiguration config = builder.Build();

        if (!StartupValidator.Run(config))
        {
            await Log.CloseAndFlushAsync();
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(true);
            return;
        }

        var services = new ServiceCollection();

        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.AddSerilog();
        });

        services.AddMetrics();
        services.AddApp(config);

        var serviceProvider = services.BuildServiceProvider();

        var window = serviceProvider.GetRequiredService<AvatarApp>();
        window.Run();

        await serviceProvider.DisposeAsync();
    }

    private static void CreateLogger()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .MinimumLevel.Override("PersonaEngine.Lib.Core.Conversation", LogEventLevel.Information)
            .MinimumLevel.Override(
                "PersonaEngine.Lib.TTS.Synthesis.Alignment",
                LogEventLevel.Information
            )
            .MinimumLevel.Override(
                "PersonaEngine.Lib.TTS.Synthesis.LipSync",
                LogEventLevel.Information
            )
            .MinimumLevel.Override(
                "PersonaEngine.Lib.TTS.Synthesis.Engine.SentenceProcessor",
                LogEventLevel.Information
            )
            .MinimumLevel.Override("Startup", LogEventLevel.Information)
            .MinimumLevel.Override("llama.cpp", LogEventLevel.Error)
            .Enrich.FromLogContext()
            .Enrich.With<GuidToEmojiEnricher>()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();
    }
}

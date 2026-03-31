using System.Runtime.InteropServices;
using System.Text;
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

    private static async Task Main()
    {
        var nativeDir = Path.Combine(AppContext.BaseDirectory, "native");
        if (Directory.Exists(nativeDir))
        {
            SetDllDirectory(nativeDir);
        }

        Console.OutputEncoding = Encoding.UTF8;

        CreateLogger();

        // Suppress ONNX Runtime warnings globally before any sessions are created
        OrtEnv.Instance().EnvLogLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;

        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", false, true);

        IConfiguration config = builder.Build();

        if (!StartupValidator.Run(config))
        {
            Log.CloseAndFlush();
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
            .MinimumLevel.Override("Startup", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.With<GuidToEmojiEnricher>()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();
    }
}

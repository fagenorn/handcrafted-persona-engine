using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PersonaEngine.Lib;
using PersonaEngine.Lib.Core;

using Serilog;
using Serilog.Events;

namespace PersonaEngine.App;

internal static class Program
{
    private static async Task Main()
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
                     .Enrich.FromLogContext()
                     .Enrich.With<GuidToEmojiEnricher>()
                     .WriteTo.Console(
                                      outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                                     )
                     .CreateLogger();
    }
}
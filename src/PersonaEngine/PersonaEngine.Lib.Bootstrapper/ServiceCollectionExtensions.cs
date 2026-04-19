using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Assets;
using PersonaEngine.Lib.Bootstrapper.Manifest;
using PersonaEngine.Lib.Bootstrapper.Planner;
using PersonaEngine.Lib.Bootstrapper.Sources;
using BootstrapperHttpExtensions = PersonaEngine.Lib.Bootstrapper.Sources.HttpClientFactoryExtensions;

namespace PersonaEngine.Lib.Bootstrapper;

public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the bootstrapper services into the bootstrap-time DI graph.
    ///     The main app uses a separate <see cref="IServiceCollection" /> populated by
    ///     <c>AddApp</c>; both register <see cref="IAssetCatalog" /> via the same
    ///     <see cref="AssetCatalogFactory" /> so the projection logic stays in one place.
    /// </summary>
    public static IServiceCollection AddBootstrapper(
        this IServiceCollection services,
        bool nonInteractive
    )
    {
        services.AddLogging();

        services.AddSingleton<InstallManifest>(_ => ManifestLoader.LoadEmbedded());

        services.AddSingleton<InstallStateLockStore>(sp => new InstallStateLockStore(
            Path.Combine(AppContext.BaseDirectory, "install-state.lock.json"),
            sp.GetService<ILogger<InstallStateLockStore>>()
        ));

        services.AddSingleton<AssetPlanner>();

        // Named HttpClient with retry + transient-error handling.
        services.AddAssetDownloadHttpClient();

        services.AddSingleton<NvidiaRedistClient>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>()
                .CreateClient(BootstrapperHttpExtensions.AssetDownloadHttpClientName);
            return new NvidiaRedistClient(
                http,
                sp.GetRequiredService<ILogger<NvidiaRedistClient>>()
            );
        });
        services.AddSingleton<HuggingFaceClient>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>()
                .CreateClient(BootstrapperHttpExtensions.AssetDownloadHttpClientName);
            return new HuggingFaceClient(
                http,
                HuggingFaceClient.ResolveEndpoint(),
                sp.GetRequiredService<ILogger<HuggingFaceClient>>()
            );
        });

        // AssetDownloader handles raw streaming + SHA verify. PlanItemAssetDownloader
        // is the IAssetDownloader wrapper that resolves URLs via the source clients
        // and computes destination paths under the resource root.
        services.AddSingleton<AssetDownloader>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>()
                .CreateClient(BootstrapperHttpExtensions.AssetDownloadHttpClientName);
            return new AssetDownloader(http, sp.GetRequiredService<ILogger<AssetDownloader>>());
        });
        services.AddSingleton<IAssetDownloader>(sp => new PlanItemAssetDownloader(
            sp.GetRequiredService<AssetDownloader>(),
            sp.GetRequiredService<HuggingFaceClient>(),
            sp.GetRequiredService<NvidiaRedistClient>(),
            Path.Combine(AppContext.BaseDirectory, "Resources"),
            sp.GetService<ILogger<PlanItemAssetDownloader>>()
        ));

        // Bootstrap-time IAssetCatalog. The main app re-registers this in its own DI
        // graph via AddApp so post-bootstrap services see the same projection.
        services.AddSingleton<IAssetCatalog>(sp =>
            AssetCatalogFactory.Build(sp.GetRequiredService<InstallManifest>())
        );

        if (nonInteractive)
        {
            services.AddSingleton<IBootstrapUserInterface, NoOpBootstrapUserInterface>();
        }
        else
        {
            services.AddSingleton<IBootstrapUserInterface, SpectreBootstrapUserInterface>();
        }

        services.AddSingleton<BootstrapRunner>();
        return services;
    }
}

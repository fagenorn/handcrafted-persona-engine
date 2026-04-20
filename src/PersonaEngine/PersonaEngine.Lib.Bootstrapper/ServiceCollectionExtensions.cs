using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Assets;
using PersonaEngine.Lib.Assets.Manifest;
using PersonaEngine.Lib.Bootstrapper.GpuPreflight;
using PersonaEngine.Lib.Bootstrapper.Planner;
using PersonaEngine.Lib.Bootstrapper.Sources;
using BootstrapperHttpExtensions = PersonaEngine.Lib.Bootstrapper.Sources.HttpClientFactoryExtensions;

namespace PersonaEngine.Lib.Bootstrapper;

public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     The on-disk root all bootstrapped assets land under. Used by both
    ///     <see cref="AssetPlanner" /> (to detect already-installed files) and
    ///     <see cref="PlanItemAssetDownloader" /> (to compute destination paths).
    ///     They MUST agree, hence the single source of truth here.
    /// </summary>
    private static string ResourceRoot => Path.Combine(AppContext.BaseDirectory, "Resources");

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
        // The host (Program.cs) is responsible for wiring providers (Serilog, etc.)
        // — AddLogging() here just registers the LoggerFactory infrastructure. If
        // the host adds no provider, ILogger<T> instances no-op silently, and the
        // operator only sees the final "Bootstrap failed" FTL line. The App project
        // calls services.AddLogging(b => b.AddSerilog(...)) after AddBootstrapper.
        services.AddLogging();

        services.AddSingleton<InstallManifest>(_ => ManifestLoader.LoadEmbedded());

        services.AddSingleton<InstallStateLockStore>(sp => new InstallStateLockStore(
            Path.Combine(AppContext.BaseDirectory, "install-state.lock.json"),
            sp.GetService<ILogger<InstallStateLockStore>>()
        ));

        services.AddSingleton<AssetPlanner>(sp => new AssetPlanner(
            ResourceRoot,
            sp.GetService<ILogger<AssetPlanner>>()
        ));

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
            ResourceRoot,
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

        // GPU preflight — bundles nvidia-smi probe + nvcuda.dll fallback into
        // a single IGpuPreflightCheck consumed by the runner. The two probe
        // interfaces are registered separately so tests can substitute them.
        services.AddSingleton<INvidiaSmiRunner, NvidiaSmiRunner>();
        services.AddSingleton<INvcudaProbe, NvcudaProbe>();
        services.AddSingleton<IGpuPreflightCheck>(sp => new NvidiaGpuPreflightCheck(
            sp.GetRequiredService<INvidiaSmiRunner>(),
            sp.GetRequiredService<INvcudaProbe>(),
            sp.GetService<ILogger<NvidiaGpuPreflightCheck>>()
        ));

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<BootstrapRunner>(sp => new BootstrapRunner(
            sp.GetRequiredService<InstallManifest>(),
            sp.GetRequiredService<InstallStateLockStore>(),
            sp.GetRequiredService<AssetPlanner>(),
            sp.GetRequiredService<IAssetDownloader>(),
            sp.GetRequiredService<IAssetCatalog>(),
            sp.GetRequiredService<IBootstrapUserInterface>(),
            sp.GetRequiredService<IGpuPreflightCheck>(),
            ResourceRoot,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<BootstrapRunner>>()
        ));
        return services;
    }
}

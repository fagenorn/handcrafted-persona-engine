using Microsoft.Extensions.DependencyInjection;
using PersonaEngine.Lib.Assets;
using PersonaEngine.Lib.Assets.Manifest;
using PersonaEngine.Lib.Bootstrapper.Planner;
using PersonaEngine.Lib.Bootstrapper.Sources;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests;

/// <summary>
/// Guards the bootstrap-time DI graph against regressions like a service
/// registration that fails to inject a constructor parameter (e.g.
/// <c>AssetPlanner(string installRoot)</c> previously registered as a plain
/// <c>AddSingleton&lt;AssetPlanner&gt;()</c> would throw at first launch).
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBootstrapper_resolves_BootstrapRunner_end_to_end()
    {
        var services = new ServiceCollection();
        services.AddBootstrapper(nonInteractive: true);
        using var sp = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );

        var runner = sp.GetRequiredService<BootstrapRunner>();
        Assert.NotNull(runner);

        // Spot-check transitive deps that have constructor parameters — these
        // are the ones most likely to regress when the DI registration drifts.
        Assert.NotNull(sp.GetRequiredService<AssetPlanner>());
        Assert.NotNull(sp.GetRequiredService<IAssetDownloader>());
        Assert.NotNull(sp.GetRequiredService<IAssetCatalog>());
        Assert.NotNull(sp.GetRequiredService<InstallStateLockStore>());
        Assert.NotNull(sp.GetRequiredService<IBootstrapUserInterface>());
    }

    [Fact]
    public void AddBootstrapper_interactive_resolves_BootstrapRunner_end_to_end()
    {
        var services = new ServiceCollection();
        services.AddBootstrapper(nonInteractive: false);
        using var sp = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );

        Assert.NotNull(sp.GetRequiredService<BootstrapRunner>());
        Assert.IsType<SpectreBootstrapUserInterface>(
            sp.GetRequiredService<IBootstrapUserInterface>()
        );
    }
}

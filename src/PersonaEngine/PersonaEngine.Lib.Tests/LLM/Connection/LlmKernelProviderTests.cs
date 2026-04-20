using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.LLM.Connection;
using Xunit;

namespace PersonaEngine.Lib.Tests.LLM.Connection;

public class LlmKernelProviderTests
{
    private static LlmOptions SeedOpts(string model = "m") =>
        new()
        {
            TextEndpoint = "http://localhost:1234/v1",
            TextModel = model,
            TextApiKey = "",
        };

    private static IOptionsMonitor<LlmOptions> MonitorWith(
        LlmOptions seed,
        out Action<LlmOptions, string?> captureOnChange
    )
    {
        var monitor = Substitute.For<IOptionsMonitor<LlmOptions>>();
        monitor.CurrentValue.Returns(_ => seed);
        Action<LlmOptions, string?> captured = (_, _) => { };
        monitor
            .OnChange(Arg.Do<Action<LlmOptions, string?>>(h => captured = h))
            .Returns((IDisposable?)null);
        captureOnChange = (o, n) => captured(o, n);
        return monitor;
    }

    private sealed class FakeCoordinator : IKernelReloadCoordinator
    {
        public bool IsSafeToReloadNow { get; set; }

        public event Action? SafeToReload;

        public void Fire() => SafeToReload?.Invoke();
    }

    [Fact]
    public void Ctor_BuildsKernelFromInitialOptions_CurrentIsNotNull()
    {
        var opts = SeedOpts();
        var provider = new LlmKernelProvider(
            MonitorWith(opts, out _),
            new FakeCoordinator(),
            configure: null,
            NullLogger<LlmKernelProvider>.Instance
        );

        Assert.NotNull(provider.Current);
    }

    [Fact]
    public void OnChange_WhenSafe_AppliesImmediately_RaisesKernelRebuilt()
    {
        var opts = SeedOpts();
        var coord = new FakeCoordinator { IsSafeToReloadNow = true };
        var provider = new LlmKernelProvider(
            MonitorWith(opts, out var fire),
            coord,
            configure: null,
            NullLogger<LlmKernelProvider>.Instance
        );
        var fired = 0;
        provider.KernelRebuilt += () => fired++;

        var before = provider.Current;
        opts = SeedOpts("m2");
        fire(opts, null);

        Assert.Equal(1, fired);
        Assert.NotSame(before, provider.Current);
    }

    [Fact]
    public void OnChange_WhenBusy_DefersUntilSafeToReload()
    {
        var opts = SeedOpts();
        var coord = new FakeCoordinator { IsSafeToReloadNow = false };
        var provider = new LlmKernelProvider(
            MonitorWith(opts, out var fire),
            coord,
            configure: null,
            NullLogger<LlmKernelProvider>.Instance
        );
        var fired = 0;
        provider.KernelRebuilt += () => fired++;

        var before = provider.Current;
        opts = SeedOpts("m2");
        fire(opts, null);

        Assert.Equal(0, fired);
        Assert.Same(before, provider.Current);

        coord.IsSafeToReloadNow = true;
        coord.Fire();

        Assert.Equal(1, fired);
        Assert.NotSame(before, provider.Current);
    }

    [Fact]
    public void MultipleChangesWhileBusy_CoalesceToLatest()
    {
        var opts = SeedOpts();
        var coord = new FakeCoordinator { IsSafeToReloadNow = false };
        var provider = new LlmKernelProvider(
            MonitorWith(opts, out var fire),
            coord,
            configure: null,
            NullLogger<LlmKernelProvider>.Instance
        );
        var fired = 0;
        provider.KernelRebuilt += () => fired++;

        opts = SeedOpts("m2");
        fire(opts, null);
        opts = SeedOpts("m3");
        fire(opts, null);
        opts = SeedOpts("m4");
        fire(opts, null);

        coord.IsSafeToReloadNow = true;
        coord.Fire();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void BuildFailure_LeavesCurrentIntact()
    {
        var opts = SeedOpts();
        var coord = new FakeCoordinator { IsSafeToReloadNow = true };
        var throwOnBuild = false;
        var provider = new LlmKernelProvider(
            MonitorWith(opts, out var fire),
            coord,
            configure: _ =>
            {
                if (throwOnBuild)
                    throw new InvalidOperationException("boom");
            },
            NullLogger<LlmKernelProvider>.Instance
        );

        var before = provider.Current;
        throwOnBuild = true;
        opts = SeedOpts("m2");
        fire(opts, null);

        Assert.Same(before, provider.Current);
    }
}

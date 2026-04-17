using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.LLM.Connection;

/// <summary>
///     Owns the application's <see cref="Kernel" /> and hot-reloads it whenever
///     <see cref="LlmOptions" /> change, but only once the session is idle as indicated by
///     <see cref="IKernelReloadCoordinator.SafeToReload" />.
///     <para>
///         <see cref="Current" /> is read via <see cref="Volatile.Read{T}" /> — no lock on the
///         hot path. Pending options are coalesced: if multiple changes arrive while a turn is
///         in-flight, only the latest is applied when the session becomes idle.
///     </para>
/// </summary>
public sealed class LlmKernelProvider : ILlmKernelProvider, IDisposable
{
    private static readonly Meter MeterInstance = new("PersonaEngine.Llm");

    private static readonly Counter<long> RebuildsCounter = MeterInstance.CreateCounter<long>(
        "personaengine.llm.kernel.rebuilds.count"
    );

    private readonly Action<IKernelBuilder>? _configure;
    private readonly IKernelReloadCoordinator _coordinator;
    private readonly ILogger<LlmKernelProvider> _log;
    private readonly IOptionsMonitor<LlmOptions> _monitor;
    private readonly IDisposable? _onChangeSub;
    private readonly object _pendingLock = new();

    private Kernel _current;
    private LlmOptions? _pending;

    /// <summary>
    ///     Initialises the provider, builds the initial <see cref="Kernel" /> synchronously,
    ///     and subscribes to future option changes.
    /// </summary>
    /// <param name="monitor">Options monitor for <see cref="LlmOptions" />.</param>
    /// <param name="coordinator">
    ///     Signals when swapping the kernel is safe (i.e. no turn in-flight).
    /// </param>
    /// <param name="configure">
    ///     Optional post-build callback to register additional services on the builder
    ///     (used in tests to inject faults or extra services).
    /// </param>
    /// <param name="log">Logger for build errors and reload events.</param>
    public LlmKernelProvider(
        IOptionsMonitor<LlmOptions> monitor,
        IKernelReloadCoordinator coordinator,
        Action<IKernelBuilder>? configure,
        ILogger<LlmKernelProvider> log
    )
    {
        _monitor = monitor;
        _coordinator = coordinator;
        _configure = configure;
        _log = log;

        _current = Build(monitor.CurrentValue, configure);
        _onChangeSub = monitor.OnChange(OnOptionsChanged);
        _coordinator.SafeToReload += OnSafeToReload;
    }

    /// <inheritdoc />
    public Kernel Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public event Action? KernelRebuilt;

    /// <inheritdoc />
    public void Dispose()
    {
        _onChangeSub?.Dispose();
        _coordinator.SafeToReload -= OnSafeToReload;
    }

    private void OnOptionsChanged(LlmOptions updated, string? _)
    {
        lock (_pendingLock)
        {
            _pending = updated;
        }

        if (_coordinator.IsSafeToReloadNow)
        {
            ApplyPending();
        }
    }

    private void OnSafeToReload() => ApplyPending();

    private void ApplyPending()
    {
        LlmOptions? pending;
        lock (_pendingLock)
        {
            pending = _pending;
            _pending = null;
        }

        if (pending is null)
        {
            return;
        }

        try
        {
            var next = Build(pending, _configure);
            Volatile.Write(ref _current, next);
            RebuildsCounter.Add(1);
            KernelRebuilt?.Invoke();
        }
        catch (Exception ex)
        {
            _log.LogError(
                ex,
                "Failed to rebuild Kernel from updated LlmOptions — keeping previous instance."
            );
        }
    }

    private static Kernel Build(LlmOptions opts, Action<IKernelBuilder>? configure)
    {
        var b = Kernel.CreateBuilder();

        if (
            !string.IsNullOrWhiteSpace(opts.TextEndpoint)
            && Uri.TryCreate(opts.TextEndpoint, UriKind.Absolute, out var textUri)
        )
        {
            b.AddOpenAIChatCompletion(
                modelId: opts.TextModel,
                endpoint: textUri,
                apiKey: opts.TextApiKey,
                serviceId: "text"
            );
        }

        if (
            opts.VisionEnabled
            && !string.IsNullOrWhiteSpace(opts.VisionEndpoint)
            && Uri.TryCreate(opts.VisionEndpoint, UriKind.Absolute, out var visionUri)
        )
        {
            b.AddOpenAIChatCompletion(
                modelId: opts.VisionModel,
                endpoint: visionUri,
                apiKey: opts.VisionApiKey,
                serviceId: "vision"
            );
        }

        configure?.Invoke(b);
        return b.Build();
    }
}

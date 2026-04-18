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

    // Grace period before disposing a replaced Kernel. Covers any in-flight
    // chat operations that captured the old instance via ILlmKernelProvider.Current
    // just before the swap. Overlaps with the LLM Polly timeout (60s) with margin.
    private static readonly TimeSpan DisposeGrace = TimeSpan.FromSeconds(30);

    private readonly Action<IKernelBuilder>? _configure;
    private readonly IKernelReloadCoordinator _coordinator;
    private readonly ILogger<LlmKernelProvider> _log;
    private readonly IDisposable? _onChangeSub;
    private readonly object _pendingLock = new();

    private int _building;
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

        // No grace period on shutdown: the host is tearing down, in-flight
        // chat operations are being cancelled anyway.
        if (Volatile.Read(ref _current)?.Services is IDisposable services)
        {
            services.Dispose();
        }
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
        if (Interlocked.CompareExchange(ref _building, 1, 0) != 0)
        {
            return;
        }

        try
        {
            while (true)
            {
                // Re-check safety at the top of each drain iteration. State may have
                // flipped Idle→Busy between SafeToReload firing (outside the
                // coordinator's lock) and us getting here, or between a successful
                // rebuild and picking up another coalesced change. Leave _pending
                // intact so the next idle transition retries.
                if (!_coordinator.IsSafeToReloadNow)
                {
                    return;
                }

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

                Kernel next;
                try
                {
                    next = Build(pending, _configure);
                }
                catch (Exception ex)
                {
                    _log.LogError(
                        ex,
                        "Failed to rebuild Kernel from updated LlmOptions — keeping previous instance."
                    );
                    continue;
                }

                // Second-gate check: Build() is the expensive step. Between taking
                // _pending and finishing Build() the session may have transitioned
                // Idle→Busy; publishing the new kernel now would land mid-turn.
                // Re-queue the options we just built against and bail — next idle
                // retries (and discards the freshly-built kernel).
                if (!_coordinator.IsSafeToReloadNow)
                {
                    lock (_pendingLock)
                    {
                        // If no newer change has arrived, restore the one we consumed
                        // so the next SafeToReload rebuilds from the same baseline.
                        _pending ??= pending;
                    }

                    ScheduleDispose(next);
                    return;
                }

                var previous = Volatile.Read(ref _current);
                Volatile.Write(ref _current, next);
                RebuildsCounter.Add(1);
                ScheduleDispose(previous);
                KernelRebuilt?.Invoke();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _building, 0);
        }
    }

    /// <summary>
    ///     Schedules the previous <see cref="Kernel" /> for disposal after
    ///     <see cref="DisposeGrace" />. Chat engines that captured it via
    ///     <see cref="ILlmKernelProvider.Current" /> finish their in-flight calls
    ///     against that instance; the grace period covers the Polly-bounded
    ///     worst-case. Multiple overlapping swaps each schedule independently —
    ///     no queue or slot coordination is needed because each replaced kernel
    ///     owns its own services graph.
    /// </summary>
    private static void ScheduleDispose(Kernel? kernel)
    {
        if (kernel is null)
        {
            return;
        }

        // Kernel itself isn't IDisposable, but Kernel.Services is a DI
        // ServiceProvider that owns the registered OpenAIChatCompletionService
        // instances (and their HttpClients). Disposing it releases those sockets.
        if (kernel.Services is not IDisposable disposable)
        {
            return;
        }

        _ = Task.Delay(DisposeGrace)
            .ContinueWith(
                _ => disposable.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
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

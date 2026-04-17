using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel;
using PersonaEngine.Lib.UI.Host;
using PersonaEngine.Lib.UI.Rendering.Spout;

namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     Lifecycle owner for the floating overlay. Launches the overlay on its own
///     dedicated thread so it doesn't share a device / render loop with Live2D —
///     the Live2D loop has ~20 ms/iteration stalls that made drag feel laggy when
///     the overlay rode on the same thread.
///
///     Overlay thread: creates the raw Win32 HWND (no Silk.NET), owns a D3D11
///     device + DirectComposition visual tree, runs the message pump, consumes
///     the avatar frame via Spout's DX11 shared-handle path
///     (<see cref="Rendering.SpoutD3D11Source" />), drives the drag /
///     resize state machine.
///
///     This thread (main): reacts to config changes, starts / stops the overlay
///     thread, persists overlay position and size when the user commits a gesture,
///     mirrors Spout sender enable flags onto <see cref="SpoutRegistry" />.
///
///     External close (Alt+F4, task manager ending the overlay thread's window)
///     flips <see cref="OverlayConfiguration.Enabled" /> to false so the config
///     panel reflects reality and the user can respawn the overlay by toggling
///     Enabled back on.
/// </summary>
public sealed class OverlayHost : IDisposable
{
    private readonly IOptionsMonitor<AvatarAppConfig> _options;
    private readonly IConfigWriter _configWriter;
    private readonly ILogger<OverlayHost> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private readonly object _lifecycleLock = new();

    private SpoutRegistry? _spoutRegistry;
    private IDisposable? _optionsSubscription;
    private bool _started;

    private Thread? _thread;
    private OverlayWindow? _runningOverlay;
    private volatile bool _stopRequested;

    // ── New live-control state (all reads/writes under _lifecycleLock) ─────
    private bool _desiredEnabled;
    private string? _lastError;

    // The Stateless lifecycle machine. Constructed in Start() so callbacks
    // can read instance state (_desiredEnabled).
    private OverlayStateMachine? _machine;

    // Coalesces rapid-fire config writes when the user spams the toggle.
    // The thread lifecycle is NOT debounced — that reacts instantly.
    private CancellationTokenSource? _pendingPersistCts;
    private const int PersistDebounceMs = 200;

    /// <summary>
    ///     The user's intent, updated instantly by <see cref="SetEnabled" />. The
    ///     UI binds to this; <see cref="Status" /> is the actual thread state.
    /// </summary>
    public bool DesiredEnabled
    {
        get
        {
            lock (_lifecycleLock)
                return _desiredEnabled;
        }
    }

    /// <summary>Real-world lifecycle state of the overlay thread.</summary>
    public OverlayStatus Status
    {
        get
        {
            lock (_lifecycleLock)
                return _machine?.State ?? OverlayStatus.Off;
        }
    }

    /// <summary>Populated when <see cref="Status" /> is <see cref="OverlayStatus.Failed" />.</summary>
    public string? LastError
    {
        get
        {
            lock (_lifecycleLock)
                return _lastError;
        }
    }

    /// <summary>Fires on any status or desired-enabled change. Optional for pollers.</summary>
    public event Action? StatusChanged;

    public OverlayHost(
        IOptionsMonitor<AvatarAppConfig> options,
        IConfigWriter configWriter,
        ILogger<OverlayHost> logger,
        ILoggerFactory loggerFactory
    )
    {
        _options = options;
        _configWriter = configWriter;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    ///     Must be called on the main thread once the <see cref="SpoutRegistry" />
    ///     is ready (i.e. after the main window's OnLoad has created the Spout
    ///     senders). Applies the initial configuration and subscribes to hot-reload.
    /// </summary>
    public void Start(SpoutRegistry spoutRegistry)
    {
        lock (_lifecycleLock)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _spoutRegistry = spoutRegistry;
            _desiredEnabled = _options.CurrentValue.Overlay.Enabled;

            _machine = new OverlayStateMachine(
                onStartThread: () =>
                {
                    // Entry action for Starting. _lifecycleLock is held because
                    // Fire() is always called under the lock.
                    _lastError = null;
                    StartThread(_options.CurrentValue.Overlay);
                    RaiseStatusChanged();
                },
                onPostClose: () =>
                {
                    _stopRequested = true;
                    _runningOverlay?.Close();
                    RaiseStatusChanged();
                },
                getDesiredEnabled: () => _desiredEnabled
            );

            _optionsSubscription = _options.OnChange(
                (cfg, _) =>
                {
                    lock (_lifecycleLock)
                    {
                        ApplyConfig(cfg);
                    }
                }
            );

            // Apply Spout sender toggles and drive the initial Enabled intent
            // through the machine (entry action starts the thread if desired).
            ApplySpoutSenderToggles(_options.CurrentValue);
            if (_desiredEnabled)
            {
                _machine.Fire(OverlayTrigger.TurnOn);
            }
        }
    }

    public void Dispose()
    {
        var oldCts = Interlocked.Exchange(ref _pendingPersistCts, null);
        oldCts?.Cancel();
        oldCts?.Dispose();

        lock (_lifecycleLock)
        {
            _optionsSubscription?.Dispose();
            _optionsSubscription = null;
        }

        StopThreadIfRunning();
    }

    /// <summary>
    ///     Flips the user's intent. Drives the overlay thread lifecycle
    ///     immediately (no file-watcher round-trip) and persists the new value
    ///     to config in the background. Safe to call rapidly; the state
    ///     machine reconciles reverse-intent sequences.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        lock (_lifecycleLock)
        {
            if (_machine is null)
            {
                _logger.LogWarning("SetEnabled called before OverlayHost.Start — ignoring.");
                return;
            }

            SetDesired(enabled);
            _machine.Fire(enabled ? OverlayTrigger.TurnOn : OverlayTrigger.TurnOff);
            ApplySpoutSenderToggles(_options.CurrentValue);
        }

        SchedulePersistEnabled(enabled);
    }

    /// <summary>
    ///     Resets the overlay position to a centered default on the primary
    ///     monitor (based on the current overlay size). Persists to config; if
    ///     the overlay is currently running, also moves the live window.
    /// </summary>
    public void ResetPosition()
    {
        var current = _options.CurrentValue;
        var width = current.Overlay.Width;
        var height = current.Overlay.Height;

        var (screenW, screenH) = Win32WindowHelper.GetPrimaryWorkArea();
        var x = Math.Max(0, (screenW - width) / 2);
        var y = Math.Max(0, (screenH - height) / 2);

        _configWriter.Write(current with { Overlay = current.Overlay with { X = x, Y = y } });

        OverlayWindow? live;
        lock (_lifecycleLock)
        {
            live = _runningOverlay;
        }

        live?.MoveTo(x, y);
    }

    /// <summary>
    ///     Resets the overlay size to the <see cref="OverlayConfiguration" />
    ///     record defaults. Persists to config; if the overlay is currently
    ///     running, also resizes the live window. Position is preserved.
    /// </summary>
    public void ResetSize()
    {
        var current = _options.CurrentValue;
        var defaults = new OverlayConfiguration();
        var width = defaults.Width;
        var height = defaults.Height;

        _configWriter.Write(
            current with
            {
                Overlay = current.Overlay with { Width = width, Height = height },
            }
        );

        OverlayWindow? live;
        lock (_lifecycleLock)
        {
            live = _runningOverlay;
        }

        live?.ResizeTo(width, height);
    }

    // Caller holds _lifecycleLock.
    private void RaiseStatusChanged()
    {
        var handler = StatusChanged;
        if (handler is not null)
        {
            Task.Run(handler);
        }
    }

    // Caller holds _lifecycleLock.
    private void SetDesired(bool desired)
    {
        if (_desiredEnabled == desired)
        {
            return;
        }

        _desiredEnabled = desired;
        RaiseStatusChanged();
    }

    private void SchedulePersistEnabled(bool enabled)
    {
        var fresh = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _pendingPersistCts, fresh);
        oldCts?.Cancel();
        oldCts?.Dispose();

        var token = fresh.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(PersistDebounceMs, token).ConfigureAwait(false);
                var current = _options.CurrentValue;
                if (current.Overlay.Enabled == enabled)
                {
                    return;
                }

                _configWriter.Write(
                    current with
                    {
                        Overlay = current.Overlay with { Enabled = enabled },
                    }
                );
            }
            catch (OperationCanceledException)
            {
                // Superseded by a later SetEnabled call.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist overlay Enabled={Enabled}.", enabled);
            }
        });
    }

    // ── Config reaction ─────────────────────────────────────────────────────────

    // Caller holds _lifecycleLock.
    private void ApplyConfig(AvatarAppConfig cfg)
    {
        ApplySpoutSenderToggles(cfg);

        if (_machine is null)
        {
            return;
        }

        // Sync intent with config on external edits (e.g. appsettings.json
        // modified while the app is running). The live UI path uses SetEnabled
        // directly, which persists via SchedulePersistEnabled — when that write
        // lands, this callback fires but the desired-matches-config check
        // makes it a no-op.
        if (_desiredEnabled != cfg.Overlay.Enabled)
        {
            SetDesired(cfg.Overlay.Enabled);
            _machine.Fire(cfg.Overlay.Enabled ? OverlayTrigger.TurnOn : OverlayTrigger.TurnOff);
        }
    }

    private void ApplySpoutSenderToggles(AvatarAppConfig cfg)
    {
        if (_spoutRegistry is null)
        {
            return;
        }

        var overlaySource = cfg.Overlay.Enabled ? cfg.Overlay.Source : null;
        foreach (var spoutCfg in cfg.SpoutConfigs)
        {
            // While the overlay is active, force its Spout source on so the
            // receiver has something to consume — otherwise the overlay would
            // be transparent but empty.
            var forcedOn = overlaySource is not null && spoutCfg.OutputName == overlaySource;
            _spoutRegistry.SetSenderEnabled(spoutCfg.OutputName, spoutCfg.Enabled || forcedOn);
        }
    }

    // ── Thread lifecycle ────────────────────────────────────────────────────────

    // Caller holds _lifecycleLock.
    private void StartThread(OverlayConfiguration cfg)
    {
        _stopRequested = false;
        var snapshot = cfg;
        var thread = new Thread(() => RunOverlayThread(snapshot))
        {
            IsBackground = true,
            Name = "PersonaEngine.Overlay",
        };
        _thread = thread;
        thread.Start();
        _logger.LogInformation(
            "Overlay thread started at {X},{Y} {W}x{H} mirroring '{Source}'.",
            cfg.X,
            cfg.Y,
            cfg.Width,
            cfg.Height,
            cfg.Source
        );
    }

    private void RunOverlayThread(OverlayConfiguration cfg)
    {
        try
        {
            using var overlay = OverlayWindow.Create(cfg, _loggerFactory);
            lock (_lifecycleLock)
            {
                _runningOverlay = overlay;
            }

            overlay.PositionCommitted += OnPositionCommitted;
            overlay.SizeCommitted += OnSizeCommitted;

            // Thread is past init: HWND + D3D11 device + DComp tree constructed
            // and the window is visible. Transition Starting → Active so the UI
            // pill goes green.
            lock (_lifecycleLock)
            {
                _machine?.Fire(OverlayTrigger.Ready);
                RaiseStatusChanged();
            }

            // Run pumps messages + renders on this thread. Host shutdown sends
            // WM_CLOSE cross-thread via overlay.Close() → PostMessage; the
            // overlay thread picks it up on its next pump and the loop exits.
            overlay.Run();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Overlay thread failed.");
            lock (_lifecycleLock)
            {
                _lastError = ex.Message;
                // Clear intent so Off.OnEntry reconcile doesn't respawn on ThreadExited.
                SetDesired(false);
                _machine?.Fire(OverlayTrigger.Failed);
                RaiseStatusChanged();
            }
        }
        finally
        {
            bool wasExternal;
            lock (_lifecycleLock)
            {
                _runningOverlay = null;
                if (_thread == Thread.CurrentThread)
                {
                    _thread = null;
                }

                // External close = user closed via Alt+F4 / task manager. Not
                // host-requested (via Stopping.OnEntry → _stopRequested) and
                // not a Failed exit.
                wasExternal = !_stopRequested && _machine?.State != OverlayStatus.Failed;

                // Fire ThreadExited — machine transitions to Off and Off.OnEntry
                // reconciles if desired flipped back on during Stopping.
                _machine?.Fire(OverlayTrigger.ThreadExited);
                _stopRequested = false;
                RaiseStatusChanged();
            }

            if (wasExternal)
            {
                // Flip desired off so the UI honestly reflects reality and
                // persist to config for the next app launch.
                lock (_lifecycleLock)
                {
                    SetDesired(false);
                }

                PersistEnabled(false);
                _logger.LogInformation(
                    "Overlay window closed externally — overlay disabled in config."
                );
            }
        }
    }

    private void StopThreadIfRunning()
    {
        Thread? toJoin;
        OverlayWindow? toClose;
        lock (_lifecycleLock)
        {
            _stopRequested = true;
            toJoin = _thread;
            toClose = _runningOverlay;
        }

        // OverlayWindow.Close is thread-safe (PostMessage WM_CLOSE); posting it
        // here wakes the overlay thread's message pump so Run() returns promptly
        // rather than waiting for the next input event.
        toClose?.Close();
        toJoin?.Join(TimeSpan.FromSeconds(3));
    }

    // ── Persistence ─────────────────────────────────────────────────────────────

    private void OnPositionCommitted((int X, int Y) position)
    {
        var current = _options.CurrentValue;
        var updated = current with
        {
            Overlay = current.Overlay with { X = position.X, Y = position.Y },
        };
        _configWriter.Write(updated);
    }

    private void OnSizeCommitted((int X, int Y) size)
    {
        var current = _options.CurrentValue;
        var updated = current with
        {
            Overlay = current.Overlay with { Width = size.X, Height = size.Y },
        };
        _configWriter.Write(updated);
    }

    private void PersistEnabled(bool enabled)
    {
        var current = _options.CurrentValue;
        if (current.Overlay.Enabled == enabled)
        {
            return;
        }

        _configWriter.Write(current with { Overlay = current.Overlay with { Enabled = enabled } });
    }
}

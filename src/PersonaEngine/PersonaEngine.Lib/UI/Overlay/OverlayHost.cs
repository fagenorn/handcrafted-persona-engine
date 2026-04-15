using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel;
using PersonaEngine.Lib.UI.Rendering.Spout;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     Lifecycle owner for the floating overlay. Launches the overlay on its own
///     dedicated thread so it doesn't share a GL context with the main app (removes
///     the pixel-format compatibility constraint that was breaking DWM transparency)
///     and doesn't share a render loop with Live2D (removes the ~20 ms/iteration
///     stall that was making drag feel laggy).
///
///     Overlay thread: creates the Silk.NET window, owns its GL context, runs the
///     message pump, consumes the avatar frame via <see cref="SpoutFrameSource" />,
///     drives the drag/resize state machine.
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
    private volatile bool _stopRequested;

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

            _optionsSubscription = _options.OnChange(
                (cfg, _) =>
                {
                    lock (_lifecycleLock)
                    {
                        ApplyConfig(cfg);
                    }
                }
            );

            ApplyConfig(_options.CurrentValue);
        }
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            _optionsSubscription?.Dispose();
            _optionsSubscription = null;
        }

        StopThreadIfRunning();
    }

    // ── Config reaction ─────────────────────────────────────────────────────────

    // Caller holds _lifecycleLock.
    private void ApplyConfig(AvatarAppConfig cfg)
    {
        ApplySpoutSenderToggles(cfg);

        var wantOverlay = cfg.Overlay.Enabled;
        var threadAlive = _thread is { IsAlive: true };

        if (wantOverlay && !threadAlive && _spoutRegistry is not null)
        {
            StartThread(cfg.Overlay);
        }
        else if (!wantOverlay && threadAlive)
        {
            // Signal; actual join happens inside StopThreadIfRunning.
            _stopRequested = true;
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
        var externalClose = true;

        try
        {
            using var overlay = OverlayWindow.Create(cfg, _loggerFactory);

            overlay.PositionCommitted += OnPositionCommitted;
            overlay.SizeCommitted += OnSizeCommitted;

            overlay.Window.Update += _ =>
            {
                if (_stopRequested && !overlay.Window.IsClosing)
                {
                    // Deliberate shutdown from the host — not an external close.
                    externalClose = false;
                    overlay.Window.Close();
                }
            };

            // OverlayWindow.Run pumps both the display window and the hidden GL
            // carrier window on this thread until the display window closes.
            overlay.Run();

            // If we exited Run() without _stopRequested being set by the host,
            // the window was closed by the user / OS — that's an external close.
            if (!_stopRequested)
            {
                externalClose = true;
            }
            else
            {
                externalClose = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Overlay thread failed.");
            // Treat as external close so the user sees the overlay toggle flip off
            // rather than being stuck on with no window.
            externalClose = true;
        }
        finally
        {
            lock (_lifecycleLock)
            {
                if (_thread == Thread.CurrentThread)
                {
                    _thread = null;
                }
            }

            if (externalClose)
            {
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
        lock (_lifecycleLock)
        {
            _stopRequested = true;
            toJoin = _thread;
        }

        toJoin?.Join(TimeSpan.FromSeconds(3));
    }

    // ── Persistence ─────────────────────────────────────────────────────────────

    private void OnPositionCommitted(Vector2D<int> position)
    {
        var current = _options.CurrentValue;
        var updated = current with
        {
            Overlay = current.Overlay with { X = position.X, Y = position.Y },
        };
        _configWriter.Write(updated);
    }

    private void OnSizeCommitted(Vector2D<int> size)
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

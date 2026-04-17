using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.Native;
using PersonaEngine.Lib.UI.Overlay.Native;
using PersonaEngine.Lib.UI.Overlay.Rendering;
using Vortice.Mathematics;

namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     The floating, borderless, per-pixel-alpha, always-on-top overlay window.
///
///     Renders in pure D3D11 with DirectComposition for transparency. No OpenGL,
///     no <c>UpdateLayeredWindow</c>, no GDI DIB — every frame is GPU-resident
///     from Spout receive through the swap chain present. Expected per-frame
///     cost on the overlay thread: sub-millisecond for modest overlay sizes.
///
///     Single-window architecture:
///     Spout sender (main thread, GL, unchanged)
///       → shared DX11 texture (<c>D3D11_RESOURCE_MISC_SHARED</c>)
///       → <see cref="SpoutD3D11Source" /> opens it in our D3D11 device
///       → <see cref="QuadPipeline" /> samples it into the swap chain back buffer
///       → <see cref="ChromeRenderer" /> overlays the hover buttons
///       → <see cref="OverlayD3D11Context.Present" /> → DirectComposition → screen
/// </summary>
public sealed class OverlayWindow : IDisposable
{
    private readonly OverlayConfiguration _initialConfig;
    private readonly ILogger<OverlayWindow> _logger;

    private readonly OverlayNativeWindow _nativeWindow;
    private readonly OverlayWin32Helper _win32;

    private OverlayD3D11Context? _d3d;
    private QuadPipeline? _quad;
    private ChromeRenderer? _chrome;
    private SpoutD3D11Source? _source;
    private OverlayInteractionController? _interaction;

    private readonly Stopwatch _frameStopwatch = Stopwatch.StartNew();
    private readonly Stopwatch _stageStopwatch = new();
    private readonly Stopwatch _fpsStopwatch = Stopwatch.StartNew();
    private long _lastFrameTicks;
    private bool _firstFramePresented;
    private int _frameCounter;
    private int _framesSinceLastFpsLog;

    // First 10 frames go to Debug for startup diagnostics. After that, one every
    // 600 frames (~10 s @ 60 fps) so long-running overlays don't spam the log.
    private const int InitialFrameLogCount = 10;
    private const int SteadyFrameLogInterval = 600;

    // Log an FPS summary every 2 s so we can verify the waitable is pacing us.
    private const double FpsLogIntervalSeconds = 2.0;

    // Hard 60 FPS cap. The Spout sender publishes at whatever rate the main app
    // runs Live2D (60 fps by default), so rendering faster than that just
    // resamples identical content. On a 240 Hz display, letting the waitable +
    // VSync pace us to the monitor's refresh produces 4x the GPU cost with
    // zero visible benefit.
    private const double TargetFrameTimeSeconds = 1.0 / 60.0;
    private long _nextFrameTargetTicks;

    private OverlayWindow(
        OverlayConfiguration config,
        ILogger<OverlayWindow> logger,
        OverlayNativeWindow nativeWindow,
        OverlayWin32Helper win32
    )
    {
        _initialConfig = config;
        _logger = logger;
        _nativeWindow = nativeWindow;
        _win32 = win32;

        _nativeWindow.Resized += OnResized;
        _nativeWindow.Closing += OnClosing;
    }

    /// <summary>Factory: allocates the raw Win32 HWND but defers D3D11 setup to <see cref="Run" />.</summary>
    public static OverlayWindow Create(OverlayConfiguration config, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<OverlayWindow>();

        var width = Math.Max(config.MinWidth, config.Width);
        var height = Math.Max(config.MinHeight, config.Height);

        var nativeWindow = new OverlayNativeWindow(
            "PersonaEngine.Overlay",
            config.X,
            config.Y,
            width,
            height
        );

        // Click-through is the default (WS_EX_TRANSPARENT set at creation) — the
        // interaction controller toggles it off on hover.
        var win32 = new OverlayWin32Helper(nativeWindow.Handle, initialClickThrough: true);

        return new OverlayWindow(config, logger, nativeWindow, win32);
    }

    /// <summary>Native HWND — exposed for test or diagnostic needs only.</summary>
    public nint Handle => _nativeWindow.Handle;

    public bool IsVisible { get; private set; }

    /// <summary>Fires when the user finishes moving the overlay.</summary>
    public event Action<(int X, int Y)>? PositionCommitted;

    /// <summary>Fires when the user finishes resizing the overlay.</summary>
    public event Action<(int X, int Y)>? SizeCommitted;

    /// <summary>
    ///     Fires when the window is closed externally (Alt+F4 / task manager).
    ///     The overlay host uses this to flip the Enabled flag in config.
    /// </summary>
    public event Action? ExternalClosed;

    /// <summary>
    ///     Runs the render + message loop on the calling thread until the
    ///     window closes. Must be invoked on the same thread that will own
    ///     the HWND (Win32 delivers messages to the creating thread).
    /// </summary>
    public void Run()
    {
        try
        {
            Initialize();

            _nextFrameTargetTicks = _frameStopwatch.ElapsedTicks;

            while (!_nativeWindow.IsClosing)
            {
                WaitUntilNextFrameTime();

                // Block on the swap chain's frame-latency waitable too — the
                // stopwatch bounds max FPS, the waitable yields cleanly if DWM
                // is behind (e.g. during heavy compositor load) so we don't
                // over-queue presents.
                _d3d?.WaitForNextFrame();

                _nativeWindow.PumpMessages();

                if (_nativeWindow.IsClosing)
                {
                    break;
                }

                var deltaTime = ComputeDeltaSeconds();
                _interaction?.Update(deltaTime);
                Render();

                _nextFrameTargetTicks += (long)(TargetFrameTimeSeconds * Stopwatch.Frequency);
            }
        }
        finally
        {
            TeardownPipelines();
        }
    }

    /// <summary>Requests the overlay window to close. Safe to call from any thread.</summary>
    public void Close()
    {
        _nativeWindow.Close();
    }

    /// <summary>
    ///     Moves the overlay to the given virtual-screen coordinates. Safe to
    ///     call from any thread — posts a custom message that runs SetWindowPos
    ///     on the overlay's creating thread.
    /// </summary>
    public void MoveTo(int x, int y) => _nativeWindow.PostMove(x, y);

    /// <summary>
    ///     Resizes the overlay to the given client dimensions. Safe to call from
    ///     any thread — posts a custom message that runs SetWindowPos on the
    ///     overlay's creating thread. The swap-chain / DComp visual resize is
    ///     driven from WM_SIZE through the normal path.
    /// </summary>
    public void ResizeTo(int width, int height) => _nativeWindow.PostResize(width, height);

    public void Dispose()
    {
        TeardownPipelines();
        _nativeWindow.Dispose();
    }

    private void Initialize()
    {
        _logger.LogInformation(
            "Overlay init: HWND=0x{Hwnd:X} size={Width}x{Height} source='{Source}'",
            _nativeWindow.Handle,
            _nativeWindow.Width,
            _nativeWindow.Height,
            _initialConfig.Source
        );

        _d3d = new OverlayD3D11Context(
            _nativeWindow.Handle,
            _nativeWindow.Width,
            _nativeWindow.Height
        );
        _logger.LogInformation("Overlay init: D3D11 + DirectComposition ready.");

        _quad = new QuadPipeline(_d3d.Device, _d3d.Context);
        _chrome = new ChromeRenderer(_d3d.Device, _d3d.Context);
        _logger.LogInformation("Overlay init: HLSL pipelines compiled.");

        _source = new SpoutD3D11Source(_d3d.Device, _initialConfig.Source);

        var aspect =
            _initialConfig.Height > 0 ? (double)_initialConfig.Width / _initialConfig.Height : 1.0;

        _interaction = new OverlayInteractionController(
            _nativeWindow,
            _win32,
            _initialConfig.MinWidth,
            _initialConfig.MinHeight,
            aspect
        );

        _interaction.PositionCommitted += pos => PositionCommitted?.Invoke(pos);
        _interaction.SizeCommitted += size => SizeCommitted?.Invoke(size);

        _logger.LogInformation("Overlay init complete — entering render loop.");
    }

    private void Render()
    {
        if (_d3d is null || _quad is null || _chrome is null || _source is null)
        {
            return;
        }

        var w = _nativeWindow.Width;
        var h = _nativeWindow.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        if (w != _d3d.Width || h != _d3d.Height)
        {
            _d3d.Resize(w, h);
        }

        _stageStopwatch.Restart();

        _d3d.Context.OMSetRenderTargets(_d3d.BackBufferRtv, null);
        _d3d.Context.ClearRenderTargetView(_d3d.BackBufferRtv, new Color4(0, 0, 0, 0));

        var acquired = _source.Acquire();
        var acquireMs = _stageStopwatch.Elapsed.TotalMilliseconds;

        if (acquired && _source.ShaderResourceView is not null)
        {
            _quad.Draw(_source.ShaderResourceView, w, h);
        }

        if (_interaction is { ChromeAlpha: > 0f } ic)
        {
            _chrome.Render(w, h, ic.ChromeAlpha, ic.ActiveHandle);
        }

        var drawMs = _stageStopwatch.Elapsed.TotalMilliseconds - acquireMs;

        _d3d.Present();

        var totalMs = _stageStopwatch.Elapsed.TotalMilliseconds;
        var presentMs = totalMs - acquireMs - drawMs;

        LogFrameTiming(w, h, acquireMs, drawMs, presentMs, totalMs);
        LogFpsIfDue();

        if (!_firstFramePresented)
        {
            _firstFramePresented = true;
            _nativeWindow.Show();
            IsVisible = true;
            _logger.LogInformation(
                "Overlay first frame presented — window shown. Spout source connected: {Connected}",
                acquired
            );
        }
    }

    private void LogFrameTiming(
        int w,
        int h,
        double acquireMs,
        double drawMs,
        double presentMs,
        double totalMs
    )
    {
        _frameCounter++;
        var shouldLog =
            _frameCounter <= InitialFrameLogCount || _frameCounter % SteadyFrameLogInterval == 0;
        if (!shouldLog)
        {
            return;
        }

        _logger.LogDebug(
            "Overlay frame {Frame} {Width}x{Height} — acquire {AcquireMs:F2} ms, draw {DrawMs:F2} ms, present {PresentMs:F2} ms, total {TotalMs:F2} ms",
            _frameCounter,
            w,
            h,
            acquireMs,
            drawMs,
            presentMs,
            totalMs
        );
    }

    private void LogFpsIfDue()
    {
        _framesSinceLastFpsLog++;

        if (_fpsStopwatch.Elapsed.TotalSeconds < FpsLogIntervalSeconds)
        {
            return;
        }

        var fps = _framesSinceLastFpsLog / _fpsStopwatch.Elapsed.TotalSeconds;
        _logger.LogInformation("Overlay FPS: {Fps:F1}", fps);

        _framesSinceLastFpsLog = 0;
        _fpsStopwatch.Restart();
    }

    private void WaitUntilNextFrameTime()
    {
        var now = _frameStopwatch.ElapsedTicks;
        var remainingTicks = _nextFrameTargetTicks - now;
        if (remainingTicks <= 0)
        {
            // We're already late (first frame or a stall). Don't try to catch
            // up by rendering back-to-back — just reset the target so we don't
            // build up a backlog.
            _nextFrameTargetTicks = now;
            return;
        }

        var remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;

        // Thread.Sleep on Windows has ~15 ms granularity by default; only sleep
        // when we have clear headroom, then SpinWait the last ~1 ms for
        // sub-tick precision. This keeps CPU near-zero when waiting.
        if (remainingMs > 2.0)
        {
            Thread.Sleep((int)(remainingMs - 1));
        }

        while (_frameStopwatch.ElapsedTicks < _nextFrameTargetTicks)
        {
            Thread.SpinWait(100);
        }
    }

    private float ComputeDeltaSeconds()
    {
        var now = _frameStopwatch.ElapsedTicks;
        if (_lastFrameTicks == 0)
        {
            _lastFrameTicks = now;
            return 0f;
        }

        var delta = (float)((now - _lastFrameTicks) / (double)Stopwatch.Frequency);
        _lastFrameTicks = now;
        // Cap large deltas on the first few frames / after a stall so the chrome
        // fade doesn't jump past its target.
        return Math.Clamp(delta, 0f, 0.25f);
    }

    private void OnResized(int width, int height)
    {
        if (_d3d is null)
        {
            return;
        }

        _d3d.Resize(width, height);
    }

    private void OnClosing()
    {
        // Native window is being destroyed externally — caller will see IsClosing
        // on the next loop iteration and break out. Notify the host so it can
        // flip the Enabled flag.
        ExternalClosed?.Invoke();
    }

    private void TeardownPipelines()
    {
        _interaction?.Dispose();
        _interaction = null;

        _chrome?.Dispose();
        _chrome = null;

        _quad?.Dispose();
        _quad = null;

        _source?.Dispose();
        _source = null;

        _d3d?.Dispose();
        _d3d = null;
    }
}

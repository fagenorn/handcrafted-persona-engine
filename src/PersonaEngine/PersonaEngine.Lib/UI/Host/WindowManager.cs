using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace PersonaEngine.Lib.UI.Host;

/// <summary>
///     Wraps Silk.NET window creation, events, and OpenGL context initialization.
///     Owns the primary application window plus any secondary windows (e.g. the
///     floating overlay) and pumps all of them from a single main-thread loop.
/// </summary>
public class WindowManager
{
    [DllImport("glfw3", EntryPoint = "glfwGetWin32Window")]
    private static extern nint GlfwGetWin32Window(nint glfwWindow);

    private readonly Vector2D<int> _minSize;

    private readonly List<IWindow> _secondaryWindows = new();

    private readonly HashSet<IWindow> _pendingSecondaryInit = new();

    private readonly ConcurrentQueue<Action> _mainThreadActions = new();

    private Win32WindowHelper? _win32Helper;

    public WindowManager(Vector2D<int> size, Vector2D<int> minSize, string title)
    {
        _minSize = minSize;

        var options = WindowOptions.Default;
        options.Size = size;
        options.Title = title;
        options.UpdatesPerSecond = 60;
        options.FramesPerSecond = 30;
        options.WindowBorder = WindowBorder.Hidden;
        MainWindow = Window.Create(options);
        MainWindow.Load += OnLoad;
        MainWindow.Update += OnUpdate;
        MainWindow.Render += OnRender;
        MainWindow.Resize += OnResize;
        MainWindow.Closing += OnClose;
    }

    public IWindow MainWindow { get; }

    public GL GL { get; private set; } = null!;

    public Win32WindowHelper? Win32Helper => _win32Helper;

    public event Action<double>? RenderFrame;

    public event Action? Load;

    public event Action<Vector2D<int>>? Resize;

    public event Action<double>? Update;

    public event Action? Close;

    /// <summary>
    ///     Register a secondary window (e.g. the overlay). Safe to call before or after
    ///     <see cref="Run" /> — windows registered after the main loop has started are
    ///     lazily initialized on the next iteration on the main thread.
    /// </summary>
    public void RegisterSecondaryWindow(IWindow window)
    {
        InvokeOnMainThread(() =>
        {
            if (_secondaryWindows.Contains(window))
            {
                return;
            }

            _secondaryWindows.Add(window);
            _pendingSecondaryInit.Add(window);
        });
    }

    /// <summary>
    ///     Queues an action to run on the main thread at the top of the next loop
    ///     iteration. Use this from background threads (e.g. config hot-reload callbacks)
    ///     when you need to touch GL, GLFW, or a Silk.NET window.
    /// </summary>
    public void InvokeOnMainThread(Action action)
    {
        _mainThreadActions.Enqueue(action);
    }

    private unsafe void OnLoad()
    {
        GL = GL.GetApi(MainWindow);

        var glfw = GlfwProvider.GLFW.Value;
        var handle = (WindowHandle*)MainWindow.Handle;
        glfw.SetWindowSizeLimits(handle, _minSize.X, _minSize.Y, Glfw.DontCare, Glfw.DontCare);

        _win32Helper = new Win32WindowHelper(
            GlfwGetWin32Window(MainWindow.Handle),
            _minSize.X,
            _minSize.Y
        );

        Load?.Invoke();
    }

    private void OnUpdate(double delta)
    {
        Update?.Invoke(delta);
    }

    private void OnRender(double delta)
    {
        RenderFrame?.Invoke(delta);
    }

    private void OnResize(Vector2D<int> size)
    {
        Resize?.Invoke(size);
    }

    private void OnClose()
    {
        _win32Helper?.Dispose();
        Close?.Invoke();
    }

    public void Run()
    {
        MainWindow.Initialize();

        while (!MainWindow.IsClosing)
        {
            DrainMainThreadActions();
            InitializePendingSecondaries();

            MainWindow.DoEvents();
            PumpSecondaryEvents();

            if (MainWindow.IsClosing)
            {
                break;
            }

            // Update secondary windows (overlay drag/resize logic) BEFORE main's
            // expensive render so cursor polling and window-position updates don't
            // wait for Live2D + ONNX inference to finish. The overlay's chrome
            // follows the cursor with only its own ~0.1 ms update cost of latency,
            // not the main window's ~20 ms frame time.
            UpdateSecondaries();

            MainWindow.DoUpdate();
            MainWindow.DoRender();

            RenderSecondaries();
            ReapClosedSecondaries();
        }

        foreach (var window in _secondaryWindows)
        {
            if (!window.IsClosing)
            {
                window.Close();
            }

            window.Reset();
        }

        _secondaryWindows.Clear();

        MainWindow.Reset();
    }

    private void DrainMainThreadActions()
    {
        while (_mainThreadActions.TryDequeue(out var action))
        {
            action();
        }
    }

    private void InitializePendingSecondaries()
    {
        if (_pendingSecondaryInit.Count == 0)
        {
            return;
        }

        foreach (var window in _pendingSecondaryInit)
        {
            window.Initialize();
        }

        _pendingSecondaryInit.Clear();
    }

    private void PumpSecondaryEvents()
    {
        foreach (var window in _secondaryWindows)
        {
            if (!window.IsClosing)
            {
                window.DoEvents();
            }
        }
    }

    private void UpdateSecondaries()
    {
        foreach (var window in _secondaryWindows)
        {
            if (!window.IsClosing)
            {
                window.DoUpdate();
            }
        }
    }

    private void RenderSecondaries()
    {
        foreach (var window in _secondaryWindows)
        {
            if (!window.IsClosing)
            {
                window.DoRender();
            }
        }
    }

    private void ReapClosedSecondaries()
    {
        for (var i = _secondaryWindows.Count - 1; i >= 0; i--)
        {
            var window = _secondaryWindows[i];
            if (!window.IsClosing)
            {
                continue;
            }

            window.Reset();
            _secondaryWindows.RemoveAt(i);
        }
    }
}

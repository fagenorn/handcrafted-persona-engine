using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace PersonaEngine.Lib.UI;

/// <summary>
///     Wraps Silk.NET window creation, events, and OpenGL context initialization.
/// </summary>
public class WindowManager
{
    private readonly Vector2D<int> _minSize;

    public WindowManager(Vector2D<int> size, Vector2D<int> minSize, string title)
    {
        _minSize = minSize;

        var options = WindowOptions.Default;
        options.Size = size;
        options.Title = title;
        options.UpdatesPerSecond = 60;
        options.FramesPerSecond = 30;
        options.WindowBorder = WindowBorder.Resizable;
        MainWindow = Window.Create(options);
        MainWindow.Load += OnLoad;
        MainWindow.Update += OnUpdate;
        MainWindow.Render += OnRender;
        MainWindow.Resize += OnResize;
        MainWindow.Closing += OnClose;
    }

    public IWindow MainWindow { get; }

    public GL GL { get; private set; }

    public event Action<double> RenderFrame;

    public event Action Load;

    public event Action<Vector2D<int>> Resize;

    public event Action<double> Update;

    public event Action Close;

    private unsafe void OnLoad()
    {
        GL = GL.GetApi(MainWindow);

        // Set GLFW minimum window size so the OS prevents resizing below the limit.
        var glfw = GlfwProvider.GLFW.Value;
        var handle = (WindowHandle*)MainWindow.Handle;
        glfw.SetWindowSizeLimits(handle, _minSize.X, _minSize.Y, Glfw.DontCare, Glfw.DontCare);

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
        Close?.Invoke();
    }

    public void Run()
    {
        MainWindow.Run();
    }
}

using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace PersonaEngine.Lib.UI;

/// <summary>
///     Wraps Silk.NET window creation, events, and OpenGL context initialization.
/// </summary>
public class WindowManager
{
    public WindowManager(Vector2D<int> size, string title)
    {
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

    private void OnLoad()
    {
        GL = GL.GetApi(MainWindow);
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

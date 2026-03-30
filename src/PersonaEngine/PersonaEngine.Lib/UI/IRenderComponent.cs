using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace PersonaEngine.Lib.UI;

public interface IRenderComponent : IDisposable
{
    bool UseSpout { get; }

    string SpoutTarget { get; }

    /// <summary>
    ///     Priority of the filter (higher values run first)
    /// </summary>
    int Priority { get; }

    void Update(float deltaTime);

    void Render(float deltaTime);

    void Resize();

    void Initialize(GL gl, IView view, IInputContext input);
}

using PersonaEngine.Lib.UI;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace PersonaEngine.Lib.Live2D;

public class NullLive2DComponent : IRenderComponent
{
    public int DrawOrder => 100;

    public void Dispose() { }
    public void Draw() { }
    public void Initialize() { }
    public void LoadContent() { }
    public void UnloadContent() { }
    public bool UseSpout { get; } = false;
    public string SpoutTarget { get; } = string.Empty;
    public int Priority { get; } = -1;
    public void Update(float deltaTime) { }
    public void Render(float deltaTime) { }
    public void Resize() { }
    public void Initialize(GL gl, IView view, IInputContext input) { }
}
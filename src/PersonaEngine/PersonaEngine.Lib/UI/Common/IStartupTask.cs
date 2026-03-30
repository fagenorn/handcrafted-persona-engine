using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.Common;

public interface IStartupTask
{
    void Execute(GL gl);
}

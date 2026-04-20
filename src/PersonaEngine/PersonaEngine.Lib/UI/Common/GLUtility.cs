using System.Diagnostics.Contracts;
using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.Common;

internal static class GlUtility
{
    [Pure]
    public static float Clamp(float value, float min, float max)
    {
        return value < min ? min
            : value > max ? max
            : value;
    }

    public static void CheckError(this GL gl, string? title = null)
    {
        var error = gl.GetError();
        if (error == GLEnum.NoError)
        {
            return;
        }

        if (title != null)
        {
            throw new Exception($"{title}: {error}");
        }

        throw new Exception($"GL.GetError(): {error}");
    }
}

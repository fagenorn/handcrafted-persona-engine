using PersonaEngine.Lib.UI.Common;
using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.Text.Rendering;

public class VertexArrayObject : IDisposable
{
    private readonly GL _gl;

    private readonly uint _handle;

    private readonly int _stride;

    public VertexArrayObject(GL glApi, int stride)
    {
        _gl = glApi;

        if (stride <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }

        _stride = stride;

        _gl.GenVertexArrays(1, out _handle);
        _gl.CheckError();
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_handle);
        _gl.CheckError();
    }

    public void Bind()
    {
        _gl.BindVertexArray(_handle);
        _gl.CheckError();
    }

    public unsafe void VertexAttribPointer(
        int location,
        int size,
        VertexAttribPointerType type,
        bool normalized,
        int offset
    )
    {
        _gl.EnableVertexAttribArray((uint)location);
        _gl.VertexAttribPointer(
            (uint)location,
            size,
            type,
            normalized,
            (uint)_stride,
            (void*)offset
        );
        _gl.CheckError();
    }
}

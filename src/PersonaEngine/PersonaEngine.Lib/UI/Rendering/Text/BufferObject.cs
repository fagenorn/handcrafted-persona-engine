using System.Runtime.InteropServices;
using PersonaEngine.Lib.UI.Common;
using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.Rendering.Text;

public class BufferObject<T> : IDisposable
    where T : unmanaged
{
    private readonly BufferTargetARB _bufferType;

    private readonly GL _gl;

    private readonly uint _handle;

    private readonly int _size;

    public unsafe BufferObject(GL glApi, int size, BufferTargetARB bufferType, bool isDynamic)
    {
        _gl = glApi;

        _bufferType = bufferType;
        _size = size;

        _handle = _gl.GenBuffer();
        _gl.CheckError();

        Bind();

        var elementSizeInBytes = Marshal.SizeOf<T>();
        _gl.BufferData(
            bufferType,
            (nuint)(size * elementSizeInBytes),
            null,
            isDynamic ? BufferUsageARB.StreamDraw : BufferUsageARB.StaticDraw
        );
        _gl.CheckError();
    }

    public void Dispose()
    {
        // No CheckError: glDeleteBuffers is spec'd to be a safe no-op on unknown or
        // zero handles, and any GL error pending here would just be from teardown
        // (e.g. disposal running after the window's context was already destroyed).
        // Throwing from Dispose would abort the rest of the DI disposal chain.
        _gl.DeleteBuffer(_handle);
    }

    public void Bind()
    {
        _gl.BindBuffer(_bufferType, _handle);
        _gl.CheckError();
    }

    public unsafe void SetData(T[] data, int startIndex, int elementCount)
    {
        Bind();

        fixed (T* dataPtr = &data[startIndex])
        {
            var elementSizeInBytes = sizeof(T);

            _gl.BufferSubData(_bufferType, 0, (nuint)(elementCount * elementSizeInBytes), dataPtr);
            _gl.CheckError();
        }
    }
}

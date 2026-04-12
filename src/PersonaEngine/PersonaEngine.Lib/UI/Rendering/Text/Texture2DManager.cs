using System.Drawing;
using System.Runtime.InteropServices;
using FontStashSharp.Interfaces;
using PersonaEngine.Lib.UI.Common;
using Silk.NET.OpenGL;
using Texture = PersonaEngine.Lib.UI.Common.Texture;

namespace PersonaEngine.Lib.UI.Rendering.Text;

internal class Texture2DManager(GL gl) : ITexture2DManager
{
    public object CreateTexture(int width, int height)
    {
        // Create an empty texture of the specified size
        // Allocate empty memory for the texture (4 bytes per pixel for RGBA format)
        var data = IntPtr.Zero;

        try
        {
            // Allocate unmanaged memory for the texture data
            var size = width * height * 4;
            data = Marshal.AllocHGlobal(size);

            // Initialize with transparent pixels (optional)
            unsafe
            {
                var ptr = (byte*)data.ToPointer();
                for (var i = 0; i < size; i++)
                {
                    ptr[i] = 0;
                }
            }

            // Create the texture with the empty data
            // generateMipmaps is set to false as this is for UI/text rendering
            return new Texture(gl, width, height, data);
        }
        finally
        {
            // Free the unmanaged memory after the texture is created
            if (data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(data);
            }
        }
    }

    public Point GetTextureSize(object texture)
    {
        var t = (Texture)texture;

        return new Point((int)t.Width, (int)t.Height);
    }

    public void SetTextureData(object texture, Rectangle bounds, byte[] data)
    {
        var t = (Texture)texture;
        t.Bind();

        unsafe
        {
            fixed (byte* ptr = data)
            {
                gl.TexSubImage2D(
                    GLEnum.Texture2D,
                    0, // mipmap level
                    bounds.Left, // x offset
                    bounds.Top, // y offset
                    (uint)bounds.Width, // width
                    (uint)bounds.Height, // height
                    PixelFormat.Bgra, // format matching the Texture class
                    PixelType.UnsignedByte, // data type
                    ptr // data pointer
                );

                gl.CheckError();
            }
        }
    }
}

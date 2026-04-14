using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
/// A pre-rendered RGBA radial gradient texture. RGB is white (1,1,1); alpha
/// falls off from 1 at the center to 0 at the edge. Consumers tint/scale it
/// at draw time via ImGui.AddImage.
/// </summary>
public sealed class RadialGradientTexture : IDisposable
{
    private readonly GL _gl;
    private readonly uint _textureId;
    private bool _disposed;

    public uint TextureId => _textureId;

    public int Size { get; }

    private RadialGradientTexture(GL gl, uint textureId, int size)
    {
        _gl = gl;
        _textureId = textureId;
        Size = size;
    }

    /// <summary>
    /// Generates a smooth radial gradient with quadratic falloff and uploads
    /// it as an RGBA8 texture with linear filtering and clamp-to-edge wrap.
    /// </summary>
    public static RadialGradientTexture Create(GL gl, int size = 256)
    {
        var pixels = GenerateGradientPixels(size);

        var texId = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texId);

        unsafe
        {
            fixed (byte* ptr = pixels)
            {
                gl.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    InternalFormat.Rgba8,
                    (uint)size,
                    (uint)size,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    ptr
                );
            }
        }

        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.Linear
        );
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Linear
        );
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS,
            (int)TextureWrapMode.ClampToEdge
        );
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.ClampToEdge
        );

        gl.BindTexture(TextureTarget.Texture2D, 0);

        return new RadialGradientTexture(gl, texId, size);
    }

    private static byte[] GenerateGradientPixels(int size)
    {
        var pixels = new byte[size * size * 4];
        var halfSize = size * 0.5f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = (x - halfSize) / halfSize;
                var dy = (y - halfSize) / halfSize;
                var distSq = dx * dx + dy * dy;

                // Smoothstep-like falloff: stays near 1 at center, smoothly
                // drops to 0 at the edge. (1 - dist²)² clamped ≥ 0.
                var falloff = MathF.Max(0f, 1f - distSq);
                var alpha = falloff * falloff;

                var idx = (y * size + x) * 4;
                pixels[idx + 0] = 255; // R
                pixels[idx + 1] = 255; // G
                pixels[idx + 2] = 255; // B
                pixels[idx + 3] = (byte)(alpha * 255f);
            }
        }

        return pixels;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            _gl.DeleteTexture(_textureId);
        }
        catch (GlfwException)
        {
            // Context may already be destroyed during app shutdown — safe to ignore.
        }
    }
}

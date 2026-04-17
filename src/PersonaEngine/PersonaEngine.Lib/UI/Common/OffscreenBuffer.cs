using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.Common;

/// <summary>
/// A reusable OpenGL framebuffer object (FBO) backed by a single RGBA8 color texture.
/// Callers render into it via <see cref="Bind"/> / <see cref="Unbind"/>, which
/// automatically save and restore the previously bound FBO and viewport so the wrapper
/// can be nested inside any existing render pass without disturbing surrounding state.
/// </summary>
public sealed class OffscreenBuffer : IDisposable
{
    private readonly GL _gl;

    private uint _fbo;
    private uint _colorTexture;
    private bool _disposed;

    private int _prevDrawFbo;
    private int _prevReadFbo;

    // Four-element array {x, y, width, height} populated by GetInteger(Viewport).
    private readonly int[] _prevViewport = new int[4];

    public uint ColorTextureId => _colorTexture;

    public int Width { get; private set; }

    public int Height { get; private set; }

    private OffscreenBuffer(GL gl, uint fbo, uint colorTexture, int width, int height)
    {
        _gl = gl;
        _fbo = fbo;
        _colorTexture = colorTexture;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Allocates an FBO of the given dimensions. Both <paramref name="width"/> and
    /// <paramref name="height"/> are clamped to a minimum of 1.
    /// </summary>
    public static OffscreenBuffer Create(GL gl, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var (fbo, tex) = Allocate(gl, width, height);

        return new OffscreenBuffer(gl, fbo, tex, width, height);
    }

    /// <summary>
    /// Saves the currently bound FBO and viewport, then binds this buffer and sets
    /// the viewport to cover its full dimensions. Call <see cref="Unbind"/> to restore.
    /// </summary>
    public void Bind()
    {
        _gl.GetInteger(GetPName.DrawFramebufferBinding, out _prevDrawFbo);
        _gl.GetInteger(GetPName.ReadFramebufferBinding, out _prevReadFbo);
        _gl.GetInteger(GetPName.Viewport, _prevViewport.AsSpan());

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    /// <summary>
    /// Restores the FBO and viewport that were active when <see cref="Bind"/> was called.
    /// </summary>
    public void Unbind()
    {
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)_prevDrawFbo);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)_prevReadFbo);
        _gl.Viewport(
            _prevViewport[0],
            _prevViewport[1],
            (uint)_prevViewport[2],
            (uint)_prevViewport[3]
        );
    }

    /// <summary>
    /// Destroys the existing FBO and texture and re-creates them at the new size.
    /// No-ops if the dimensions are unchanged. Both values are clamped to a minimum of 1.
    /// </summary>
    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (width == Width && height == Height)
        {
            return;
        }

        _gl.DeleteTexture(_colorTexture);
        _gl.DeleteFramebuffer(_fbo);

        var (fbo, tex) = Allocate(_gl, width, height);
        _fbo = fbo;
        _colorTexture = tex;
        Width = width;
        Height = height;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // glDeleteTextures / glDeleteFramebuffers are spec'd to silently ignore
        // unknown or zero handles. If the context has already been destroyed the
        // calls are no-ops at the driver level; either way, disposal must not throw.
        _gl.DeleteTexture(_colorTexture);
        _gl.DeleteFramebuffer(_fbo);
    }

    private static unsafe (uint fbo, uint tex) Allocate(GL gl, int width, int height)
    {
        var tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.Rgba8,
            (uint)width,
            (uint)height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            null
        );
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

        var fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            tex,
            0
        );
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        return (fbo, tex);
    }
}

using PersonaEngine.Lib.Configuration;

using Silk.NET.OpenGL;

using Spout.Interop;

namespace PersonaEngine.Lib.UI.Spout;

/// <summary>
///     Provides real-time frame sharing via Spout with custom framebuffer support.
/// </summary>
public class SpoutManager : IDisposable
{
    private readonly SpoutConfiguration _config;

    private readonly GL _gl;

    private readonly SpoutSender _spoutSender;

    private uint _colorAttachment;

    private uint _customFbo;

    private bool _customFboInitialized = false;

    private uint _depthAttachment;

    public SpoutManager(GL gl, SpoutConfiguration config)
    {
        _gl          = gl;
        _config      = config;
        _spoutSender = new SpoutSender();

        InitializeCustomFramebuffer();

        if ( !_spoutSender.CreateSender(config.OutputName, (uint)_config.Width, (uint)_config.Height, 0) )
        {
            _spoutSender.Dispose();
            Console.WriteLine($"Failed to create Spout Sender '{config.OutputName}'.");
        }
    }

    public void Dispose()
    {
        if ( _customFboInitialized )
        {
            _gl.DeleteTexture(_colorAttachment);
            _gl.DeleteTexture(_depthAttachment);
            _gl.DeleteFramebuffer(_customFbo);
        }

        _spoutSender?.Dispose();
    }

    private unsafe void InitializeCustomFramebuffer()
    {
        _customFbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(GLEnum.Framebuffer, _customFbo);

        _colorAttachment = _gl.GenTexture();
        _gl.BindTexture(GLEnum.Texture2D, _colorAttachment);
        _gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba8,
                       (uint)_config.Width, (uint)_config.Height, 0, GLEnum.Rgba, GLEnum.UnsignedByte, null);

        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
        _gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0,
                                 GLEnum.Texture2D, _colorAttachment, 0);

        _depthAttachment = _gl.GenTexture();
        _gl.BindTexture(GLEnum.Texture2D, _depthAttachment);
        _gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.DepthComponent,
                       (uint)_config.Width, (uint)_config.Height, 0, GLEnum.DepthComponent, GLEnum.Float, null);

        _gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.DepthAttachment,
                                 GLEnum.Texture2D, _depthAttachment, 0);

        if ( _gl.CheckFramebufferStatus(GLEnum.Framebuffer) != GLEnum.FramebufferComplete )
        {
            Console.WriteLine("Custom framebuffer is not complete!");

            return;
        }

        _gl.BindFramebuffer(GLEnum.Framebuffer, 0);

        _customFboInitialized = true;
    }

    /// <summary>
    ///     Begins rendering to the custom framebuffer
    /// </summary>
    public void BeginFrame()
    {
        if ( _customFboInitialized )
        {
            _gl.BindFramebuffer(GLEnum.Framebuffer, _customFbo);

            _gl.Viewport(0, 0, (uint)_config.Width, (uint)_config.Height);

            var blendEnabled = _gl.IsEnabled(EnableCap.Blend);

            // Enable blending for transparency
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Clear with transparency (RGBA: 0,0,0,0)
            _gl.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
            _gl.Clear((uint)(GLEnum.ColorBufferBit | GLEnum.DepthBufferBit));

            if ( !blendEnabled )
            {
                _gl.Disable(EnableCap.Blend);
            }
        }
    }

    /// <summary>
    ///     Sends the current frame to Spout and returns to the default framebuffer
    /// </summary>
    /// <param name="blitToScreen">Whether to copy the framebuffer to the screen</param>
    /// <param name="windowWidth">Window width if blitting to screen</param>
    /// <param name="windowHeight">Window height if blitting to screen</param>
    public void SendFrame(bool blitToScreen = true, int windowWidth = 0, int windowHeight = 0)
    {
        if ( _customFboInitialized )
        {
            _gl.GetInteger(GetPName.UnpackAlignment, out var previousUnpackAlignment);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

            _spoutSender.SendFbo(_customFbo, (uint)_config.Width, (uint)_config.Height, true);

            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, previousUnpackAlignment);

            if ( blitToScreen && windowWidth > 0 && windowHeight > 0 )
            {
                _gl.BindFramebuffer(GLEnum.ReadFramebuffer, _customFbo);
                _gl.BindFramebuffer(GLEnum.DrawFramebuffer, 0); // Default framebuffer

                var blendEnabled = _gl.IsEnabled(EnableCap.Blend);
                if ( !blendEnabled )
                {
                    _gl.Enable(EnableCap.Blend);
                    _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                }

                _gl.BlitFramebuffer(
                                    0, 0, _config.Width, _config.Height,
                                    0, 0, windowWidth, windowHeight,
                                    (uint)GLEnum.ColorBufferBit,
                                    GLEnum.Linear);

                if ( !blendEnabled )
                {
                    _gl.Disable(EnableCap.Blend);
                }
            }

            _gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        }
        else
        {
            _gl.GetInteger(GetPName.ReadFramebufferBinding, out var fboId);
            _spoutSender.SendFbo((uint)fboId, (uint)_config.Width, (uint)_config.Height, true);
        }
    }

    /// <summary>
    ///     Updates the custom framebuffer dimensions if needed
    /// </summary>
    public void ResizeFramebuffer(int width, int height)
    {
        // if ( _config.Width == width && _config.Height == height )
        // {
        //     return;
        // }

        // _config.Width  = width;
        // _config.Height = height;

        // if ( _customFboInitialized )
        // {
        //     _gl.DeleteTexture(_colorAttachment);
        //     _gl.DeleteTexture(_depthAttachment);
        //     _gl.DeleteFramebuffer(_customFbo);
        //     _customFboInitialized = false;
        // }
        //
        // InitializeCustomFramebuffer();
        //
        // _spoutSender.UpdateSender(_config.OutputName, (uint)width, (uint)height);
    }
}
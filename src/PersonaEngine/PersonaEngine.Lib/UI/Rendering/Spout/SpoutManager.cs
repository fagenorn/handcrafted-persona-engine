using PersonaEngine.Lib.Configuration;
using Silk.NET.OpenGL;
using Spout.Interop;

namespace PersonaEngine.Lib.UI.Rendering.Spout;

/// <summary>
///     Owns the off-screen framebuffer that components render into, and publishes
///     it to other processes via Spout (OBS, and the floating overlay which
///     receives through the Spout sender registry — same process, but now via
///     D3D11 shared-texture handle rather than a GL interop path).
///
///     The Spout sender can be enabled/disabled at runtime without destroying
///     the FBO. When disabled, the FBO keeps rendering but nothing reads it.
/// </summary>
public class SpoutManager : IDisposable
{
    private readonly GL _gl;

    private readonly string _outputName;

    private readonly SpoutSender _spoutSender;

    private uint _colorAttachment;

    private uint _customFbo;

    private bool _customFboInitialized;

    private uint _depthAttachment;

    private bool _senderCreated;

    public SpoutManager(GL gl, SpoutConfiguration config)
    {
        _gl = gl;
        _outputName = config.OutputName;
        Width = config.Width;
        Height = config.Height;
        _spoutSender = new SpoutSender();

        InitializeCustomFramebuffer();
        SetSenderEnabled(config.Enabled);
    }

    public int Width { get; }

    public int Height { get; }

    public string OutputName => _outputName;

    /// <summary>
    ///     Turn the Spout sender on or off without touching the FBO. The in-process
    ///     frame source keeps working either way.
    /// </summary>
    public void SetSenderEnabled(bool enabled)
    {
        if (enabled == _senderCreated)
        {
            return;
        }

        if (enabled)
        {
            if (!_spoutSender.CreateSender(_outputName, (uint)Width, (uint)Height, 0))
            {
                Console.WriteLine($"Failed to create Spout sender '{_outputName}'.");
                return;
            }

            _senderCreated = true;
        }
        else
        {
            _spoutSender.ReleaseSender();
            _senderCreated = false;
        }
    }

    public void Dispose()
    {
        if (_customFboInitialized)
        {
            _gl.DeleteTexture(_colorAttachment);
            _gl.DeleteTexture(_depthAttachment);
            _gl.DeleteFramebuffer(_customFbo);
            _customFboInitialized = false;
        }

        if (_senderCreated)
        {
            _spoutSender.ReleaseSender();
            _senderCreated = false;
        }

        _spoutSender.Dispose();
    }

    private unsafe void InitializeCustomFramebuffer()
    {
        _customFbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(GLEnum.Framebuffer, _customFbo);

        _colorAttachment = _gl.GenTexture();
        _gl.BindTexture(GLEnum.Texture2D, _colorAttachment);
        _gl.TexImage2D(
            GLEnum.Texture2D,
            0,
            (int)GLEnum.Rgba8,
            (uint)Width,
            (uint)Height,
            0,
            GLEnum.Rgba,
            GLEnum.UnsignedByte,
            null
        );

        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.FramebufferTexture2D(
            GLEnum.Framebuffer,
            GLEnum.ColorAttachment0,
            GLEnum.Texture2D,
            _colorAttachment,
            0
        );

        _depthAttachment = _gl.GenTexture();
        _gl.BindTexture(GLEnum.Texture2D, _depthAttachment);
        _gl.TexImage2D(
            GLEnum.Texture2D,
            0,
            (int)GLEnum.DepthComponent,
            (uint)Width,
            (uint)Height,
            0,
            GLEnum.DepthComponent,
            GLEnum.Float,
            null
        );

        _gl.FramebufferTexture2D(
            GLEnum.Framebuffer,
            GLEnum.DepthAttachment,
            GLEnum.Texture2D,
            _depthAttachment,
            0
        );

        if (_gl.CheckFramebufferStatus(GLEnum.Framebuffer) != GLEnum.FramebufferComplete)
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
        if (!_customFboInitialized)
        {
            return;
        }

        _gl.BindFramebuffer(GLEnum.Framebuffer, _customFbo);

        _gl.Viewport(0, 0, (uint)Width, (uint)Height);

        var blendEnabled = _gl.IsEnabled(EnableCap.Blend);

        // Enable blending for transparency
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // Clear with transparency (RGBA: 0,0,0,0)
        _gl.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
        _gl.Clear((uint)(GLEnum.ColorBufferBit | GLEnum.DepthBufferBit));

        if (!blendEnabled)
        {
            _gl.Disable(EnableCap.Blend);
        }
    }

    /// <summary>
    ///     Sends the current frame to Spout (if enabled) and returns to the default framebuffer.
    /// </summary>
    /// <param name="blitToScreen">Whether to copy the framebuffer to the screen</param>
    /// <param name="windowWidth">Window width if blitting to screen</param>
    /// <param name="windowHeight">Window height if blitting to screen</param>
    public void SendFrame(bool blitToScreen = true, int windowWidth = 0, int windowHeight = 0)
    {
        if (_customFboInitialized)
        {
            if (_senderCreated)
            {
                _gl.GetInteger(GetPName.UnpackAlignment, out var previousUnpackAlignment);
                _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

                _spoutSender.SendFbo(_customFbo, (uint)Width, (uint)Height, true);

                _gl.PixelStore(PixelStoreParameter.UnpackAlignment, previousUnpackAlignment);
            }

            if (blitToScreen && windowWidth > 0 && windowHeight > 0)
            {
                _gl.BindFramebuffer(GLEnum.ReadFramebuffer, _customFbo);
                _gl.BindFramebuffer(GLEnum.DrawFramebuffer, 0); // Default framebuffer

                var blendEnabled = _gl.IsEnabled(EnableCap.Blend);
                if (!blendEnabled)
                {
                    _gl.Enable(EnableCap.Blend);
                    _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                }

                _gl.BlitFramebuffer(
                    0,
                    0,
                    Width,
                    Height,
                    0,
                    0,
                    windowWidth,
                    windowHeight,
                    (uint)GLEnum.ColorBufferBit,
                    GLEnum.Linear
                );

                if (!blendEnabled)
                {
                    _gl.Disable(EnableCap.Blend);
                }
            }

            _gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        }
        else if (_senderCreated)
        {
            _gl.GetInteger(GetPName.ReadFramebufferBinding, out var fboId);
            _spoutSender.SendFbo((uint)fboId, (uint)Width, (uint)Height, true);
        }
    }

    /// <summary>
    ///     Updates the custom framebuffer dimensions if needed
    /// </summary>
    public void ResizeFramebuffer(int width, int height)
    {
        // Not implemented — SpoutConfiguration dimensions are fixed at startup.
        // Resizing would require tearing down and rebuilding both the FBO and the
        // Spout sender, which invalidates any existing receivers. Left as a stub
        // to preserve the public surface.
    }
}

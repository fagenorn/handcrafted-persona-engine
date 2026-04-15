using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Configuration;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     A borderless, per-pixel-alpha, always-on-top floating overlay window.
///
///     Two-window architecture, required because OpenGL on Windows has known
///     incompatibilities with <c>WS_EX_LAYERED</c> on the same HWND — the GL
///     driver claims the window's displayable surface and GDI can't composite
///     the layered bitmap over it even when <c>UpdateLayeredWindow</c> reports
///     success.
///
///       * Display window (<see cref="GraphicsAPI.None" />): the visible,
///         layered window. Receives input, is moved/resized, is painted via
///         <c>UpdateLayeredWindow</c> with per-pixel alpha.
///       * GL carrier window (hidden, <see cref="GraphicsAPI.Default" />):
///         exists solely to host a WGL context so we can render the avatar +
///         chrome into an off-screen FBO and <c>glReadPixels</c> into the
///         display window's DIB.
///
///     Both windows run on the overlay thread. The GL carrier is pumped just
///     enough for Silk.NET bookkeeping; its Render event is unused — we drive
///     GL directly from the display window's Render callback after making the
///     carrier's context current.
/// </summary>
public sealed class OverlayWindow : IDisposable
{
    [DllImport("glfw3", EntryPoint = "glfwGetWin32Window")]
    private static extern nint GlfwGetWin32Window(nint glfwWindow);

    private readonly IWindow _displayWindow;
    private readonly IWindow _glWindow;
    private readonly OverlayConfiguration _initialConfig;
    private readonly ILoggerFactory _loggerFactory;

    private GL? _gl;
    private IInputContext? _inputContext;
    private OverlayWin32Helper? _win32;
    private FullscreenQuadRenderer? _quad;
    private OverlayChromeRenderer? _chrome;
    private OverlayInteractionController? _interaction;
    private SpoutFrameSource? _source;
    private OverlayLayeredSurface? _surface;

    private uint _fbo;
    private uint _fboColor;
    private uint _fboDepth;
    private int _fboWidth;
    private int _fboHeight;

    private bool _runCompleted;

    private OverlayWindow(
        IWindow displayWindow,
        IWindow glWindow,
        OverlayConfiguration config,
        ILoggerFactory loggerFactory
    )
    {
        _displayWindow = displayWindow;
        _glWindow = glWindow;
        _initialConfig = config;
        _loggerFactory = loggerFactory;

        // Render/update/load/closing events fire on the display window. We pump
        // the GL window manually inside RunLoop so Silk.NET's bookkeeping stays
        // happy, but its events are no-ops.
        _displayWindow.Load += OnLoad;
        _displayWindow.Update += OnUpdate;
        _displayWindow.Render += OnRender;
        _displayWindow.Closing += OnClosing;
    }

    /// <summary>Builds both windows. The display window is hidden until the first
    /// <c>UpdateLayeredWindow</c> succeeds — layered windows misbehave if shown
    /// before their initial contents are set.</summary>
    public static OverlayWindow Create(OverlayConfiguration config, ILoggerFactory loggerFactory)
    {
        var displayOptions = WindowOptions.Default with
        {
            Size = new Vector2D<int>(
                Math.Max(config.MinWidth, config.Width),
                Math.Max(config.MinHeight, config.Height)
            ),
            Position = new Vector2D<int>(config.X, config.Y),
            Title = "PersonaEngine.Overlay",
            WindowBorder = WindowBorder.Hidden,
            TopMost = true,
            IsVisible = false,
            FramesPerSecond = 60,
            UpdatesPerSecond = 120,
            VSync = false,
            ShouldSwapAutomatically = false,
            // No GL context on this window — GL lives on the hidden carrier.
            API = GraphicsAPI.None,
        };

        var glOptions = WindowOptions.Default with
        {
            // Size is meaningless; we render to FBO, never to the default framebuffer.
            Size = new Vector2D<int>(16, 16),
            Title = "PersonaEngine.OverlayGL",
            WindowBorder = WindowBorder.Hidden,
            IsVisible = false,
            ShouldSwapAutomatically = false,
            VSync = false,
            FramesPerSecond = 0,
            UpdatesPerSecond = 0,
        };

        var display = Silk.NET.Windowing.Window.Create(displayOptions);
        var gl = Silk.NET.Windowing.Window.Create(glOptions);
        return new OverlayWindow(display, gl, config, loggerFactory);
    }

    public IWindow Window => _displayWindow;

    public bool IsVisible
    {
        get => _displayWindow.IsVisible;
        set => _displayWindow.IsVisible = value;
    }

    /// <summary>Fires when the user finishes moving the overlay.</summary>
    public event Action<Vector2D<int>>? PositionCommitted;

    /// <summary>Fires when the user finishes resizing the overlay.</summary>
    public event Action<Vector2D<int>>? SizeCommitted;

    /// <summary>
    ///     Runs the combined event pump for both windows until the display window
    ///     closes. Must be called on the thread that wants to own the windows
    ///     (GLFW requires all window operations on the creating thread).
    /// </summary>
    public void Run()
    {
        try
        {
            _glWindow.Initialize();
            _displayWindow.Initialize();

            while (!_displayWindow.IsClosing)
            {
                _glWindow.DoEvents();
                _displayWindow.DoEvents();

                if (_displayWindow.IsClosing)
                {
                    break;
                }

                _displayWindow.DoUpdate();
                _displayWindow.DoRender();
            }
        }
        finally
        {
            // Reset destroys the native GLFW windows. After this point the
            // Silk.NET handle wrappers point at freed memory and any property
            // access (IsClosing, etc.) crashes. Flag so Dispose doesn't touch
            // them again.
            try
            {
                _displayWindow.Reset();
            }
            catch
            {
                /* best-effort */
            }

            try
            {
                _glWindow.Reset();
            }
            catch
            {
                /* best-effort */
            }

            _runCompleted = true;
        }
    }

    private void OnLoad()
    {
        // GL context lives on the hidden carrier; make it current on this thread.
        _glWindow.MakeCurrent();
        _gl = GL.GetApi(_glWindow);

        var hwnd = GlfwGetWin32Window(_displayWindow.Handle);
        _win32 = new OverlayWin32Helper(hwnd);
        _surface = new OverlayLayeredSurface(
            hwnd,
            _loggerFactory.CreateLogger<OverlayLayeredSurface>()
        );

        _source = new SpoutFrameSource(_initialConfig.Source);
        _quad = new FullscreenQuadRenderer(_gl);
        _chrome = new OverlayChromeRenderer(_gl);

        _inputContext = _displayWindow.CreateInput();

        var aspect =
            _initialConfig.Height > 0 ? (double)_initialConfig.Width / _initialConfig.Height : 1.0;

        _interaction = new OverlayInteractionController(
            _displayWindow,
            _inputContext,
            _win32,
            _initialConfig.MinWidth,
            _initialConfig.MinHeight,
            _initialConfig.ChromeFadeSeconds,
            _initialConfig.LockAspect,
            aspect
        );

        _interaction.PositionCommitted += pos => PositionCommitted?.Invoke(pos);
        _interaction.SizeCommitted += size => SizeCommitted?.Invoke(size);

        EnsureFramebuffer(_displayWindow.Size.X, _displayWindow.Size.Y);
    }

    private void OnUpdate(double deltaTime)
    {
        _interaction?.Update((float)deltaTime);
    }

    private unsafe void OnRender(double deltaTime)
    {
        if (_gl is null || _quad is null || _chrome is null || _source is null || _surface is null)
        {
            return;
        }

        var size = _displayWindow.Size;
        if (size.X <= 0 || size.Y <= 0)
        {
            return;
        }

        // Render happens on the GL carrier's context — switch it current for
        // this thread. Display window has no GL context so nothing to switch
        // away from.
        _glWindow.MakeCurrent();

        EnsureFramebuffer(size.X, size.Y);
        _surface.Resize(size.X, size.Y);

        // Render scene into offscreen FBO.
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, (uint)size.X, (uint)size.Y);

        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear((uint)GLEnum.ColorBufferBit);

        if (_source.Acquire())
        {
            _quad.Draw(_source.ColorTextureHandle, flipV: !_source.OriginBottomLeft);
        }

        if (_interaction is { ChromeAlpha: > 0f } ic)
        {
            _chrome.Render(size.X, size.Y, ic.ChromeAlpha, ic.ActiveHandle);
        }

        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        _gl.ReadBuffer(ReadBufferMode.ColorAttachment0);
        _gl.ReadPixels(
            0,
            0,
            (uint)size.X,
            (uint)size.Y,
            PixelFormat.Bgra,
            PixelType.UnsignedByte,
            (void*)_surface.Pixels
        );

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        var presented = _surface.Present();

        // Show after first successful UpdateLayeredWindow — layered windows
        // misbehave if shown before their contents are set.
        if (presented && !_displayWindow.IsVisible)
        {
            _displayWindow.IsVisible = true;
        }
    }

    private unsafe void EnsureFramebuffer(int width, int height)
    {
        if (_gl is null)
        {
            return;
        }

        if (width == _fboWidth && height == _fboHeight && _fbo != 0)
        {
            return;
        }

        if (_fbo != 0)
        {
            _gl.DeleteFramebuffer(_fbo);
            _gl.DeleteTexture(_fboColor);
            _gl.DeleteRenderbuffer(_fboDepth);
        }

        _fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);

        _fboColor = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _fboColor);
        _gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            (int)InternalFormat.Rgba8,
            (uint)width,
            (uint)height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            (void*)0
        );
        _gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter,
            (int)GLEnum.Linear
        );
        _gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter,
            (int)GLEnum.Linear
        );
        _gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS,
            (int)GLEnum.ClampToEdge
        );
        _gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT,
            (int)GLEnum.ClampToEdge
        );
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            _fboColor,
            0
        );

        _fboDepth = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _fboDepth);
        _gl.RenderbufferStorage(
            RenderbufferTarget.Renderbuffer,
            InternalFormat.DepthComponent24,
            (uint)width,
            (uint)height
        );
        _gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer,
            _fboDepth
        );

        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException(
                $"Overlay FBO incomplete at {width}x{height}: {status}"
            );
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        _fboWidth = width;
        _fboHeight = height;
    }

    private void OnClosing()
    {
        _interaction?.Dispose();
        _interaction = null;

        _chrome?.Dispose();
        _chrome = null;

        _quad?.Dispose();
        _quad = null;

        _source?.Dispose();
        _source = null;

        if (_gl is not null && _fbo != 0)
        {
            _gl.DeleteFramebuffer(_fbo);
            _gl.DeleteTexture(_fboColor);
            _gl.DeleteRenderbuffer(_fboDepth);
            _fbo = 0;
            _fboColor = 0;
            _fboDepth = 0;
        }

        _surface?.Dispose();
        _surface = null;

        _inputContext?.Dispose();
        _inputContext = null;

        _win32 = null;
        _gl = null;

        if (!_glWindow.IsClosing)
        {
            _glWindow.Close();
        }
    }

    public void Dispose()
    {
        // Run() already Resets both windows inside a finally block. Touching
        // the Silk.NET handles after Reset dereferences freed GLFW memory and
        // crashes. If Run completed normally there's nothing left to do.
        if (_runCompleted)
        {
            return;
        }

        try
        {
            if (!_displayWindow.IsClosing)
            {
                _displayWindow.Close();
            }
        }
        catch
        {
            /* handle already gone */
        }
    }
}
